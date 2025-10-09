using System.Diagnostics;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;
using SharedLibrary;
using SharedLibrary.Configuration;

namespace FileParserService;

public class RabbitMqConnectionManager : IRabbitMqConnectionManager, IDisposable
{
    private readonly RabbitMqPublisherConfig _rabbitMqPublisherConfig;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection; // Shared connection
    private bool _queueDeclared;
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly IAsyncPolicy _rabbitPolicy;

    public RabbitMqConnectionManager(
        ILogger<RabbitMqConnectionManager> logger,
        IOptionsMonitor<RabbitMqPublisherConfig> rabbitMqConfig,
        IAsyncPolicy rabbitPolicy
    )
    {
        _rabbitMqPublisherConfig = rabbitMqConfig.CurrentValue;
        _rabbitPolicy = rabbitPolicy;
        _logger = logger;

        _factory = new ConnectionFactory
        {
            HostName = _rabbitMqPublisherConfig.HostName,
            VirtualHost = _rabbitMqPublisherConfig.VirtualHost,
            UserName = _rabbitMqPublisherConfig.UserName,
            Password = _rabbitMqPublisherConfig.Password,
            Port = _rabbitMqPublisherConfig.Port,

            AutomaticRecoveryEnabled = _rabbitMqPublisherConfig.AutomaticRecoveryEnabled,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(_rabbitMqPublisherConfig.NetworkRecoveryInterval)
        };
        
        _logger.LogInformation("Rabbit Connection: Host: {HostName}, VirtualHost:{VirtualHost}, UserName:{UserName}, Password:{Password}, Port: {Port}", 
            _factory.HostName,
            _factory.VirtualHost, 
            _factory.UserName, 
            _factory.Password,
            _factory.Port
            );
    }

    private async Task DeclareQueueAsync(IChannel channel, CancellationToken ts)
    {
        var result = await channel.QueueDeclareAsync(
            queue: _rabbitMqPublisherConfig.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                { RabbitMqConsts.xDeadLetterExchange, _rabbitMqPublisherConfig.XDeadLetterExchange },
                { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqPublisherConfig.XDeadLetterRoutingKey }
            },
            cancellationToken: ts);

        _logger.LogDebug("Queue '{QueueName}' declared: messages={Messages}, consumers={Consumers}",
            _rabbitMqPublisherConfig.QueueName, result.MessageCount, result.ConsumerCount);
    }

    private bool IsReadyConnection =>
        _connection is { IsOpen: true } && _queueDeclared;

    async Task<IConnection> IRabbitMqConnectionManager.GetConnectionAsync(CancellationToken ct)
    {
        if (IsReadyConnection)
        {
            Debug.Assert(_connection != null);
            return _connection;
        }

        try
        {
            await _connectionLock.WaitAsync(ct);

            if (IsReadyConnection)
            {
                Debug.Assert(_connection != null);
                return _connection;
            }

            if (_connection is { IsOpen: true })
            {
                if (!_queueDeclared)
                {
                    await DeclareChannel(ct);

                    Debug.Assert(_connection != null);
                    return _connection;
                }
            }

            await ClearRabbitMqConnection(ct);

            return await _rabbitPolicy.ExecuteAsync(
                async (_) => await CreateConnection(ct), new Context
                {
                    [PolicyRegistryConsts.Logger] = _logger
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create connection or declare queue");
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<IConnection> CreateConnection(CancellationToken ct)
    {
        _connection = await _factory.CreateConnectionAsync(cancellationToken: ct);
        _logger.LogInformation("RabbitMQ connection created/recreated");

        await DeclareChannel(ct);

        return _connection;
    }

    private async Task DeclareChannel(CancellationToken ct)
    {
        if (!_queueDeclared)
        {
            Debug.Assert(_connection != null);

            await using var tempChannel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await DeclareQueueAsync(tempChannel, ct);

            _queueDeclared = true;
            _logger.LogInformation("Queue '{QueueName}' declared on connection init",
                _rabbitMqPublisherConfig.QueueName);
        }
    }

    private async Task ClearRabbitMqConnection(CancellationToken ct)
    {
        if (_connection is not null)
        {
            try
            {
                if (!_connection.IsOpen)
                {
                    try
                    {
                        await _connection.CloseAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not close connection");
                    }
                }

                await _connection.DisposeAsync();
                _connection = null;
                _logger.LogDebug("Old connection disposed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing/disposing old connection (ignoring for recreate)");
            }
        }

        _queueDeclared = false;
    }

    public void Dispose()
    {
        _connectionLock.Dispose();
        _connection?.Dispose();

        _connection = null;
    }
}