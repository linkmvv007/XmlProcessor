using System.Diagnostics;
using Microsoft.Extensions.Options;
using Polly;
using RabbitMQ.Client;
using SharedLibrary;
using SharedLibrary.Configuration;

namespace FileParserService;

public class RabbitMqConnectionManager : IRabbitMqConnectionManager, IDisposable
{
    private readonly RabbitMqConfigPublisher _rabbitMqConfig;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection; // Shared connection
    private bool _queueDeclared;
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly IAsyncPolicy _rabbitPolicy;
    
    public RabbitMqConnectionManager(
        ILogger<RabbitMqConnectionManager> logger,
        IOptions<RabbitMqConfigPublisher> rabbitMqConfig,
        IAsyncPolicy rabbitPolicy
        )
    {
        _rabbitMqConfig = rabbitMqConfig.Value;
        _rabbitPolicy = rabbitPolicy;
        _logger = logger;
 
        _factory = new ConnectionFactory
        {
            HostName = _rabbitMqConfig.HostName,
            VirtualHost = _rabbitMqConfig.VirtualHost,
            UserName = _rabbitMqConfig.UserName,
            Password = _rabbitMqConfig.Password,
            Port = _rabbitMqConfig.Port,
 
            AutomaticRecoveryEnabled = _rabbitMqConfig.AutomaticRecoveryEnabled,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(_rabbitMqConfig.NetworkRecoveryInterval)
        };
    }

    private async Task DeclareQueueAsync(IChannel channel, CancellationToken ts)
    {
        var result = await channel.QueueDeclareAsync(
            queue: _rabbitMqConfig.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                { RabbitMqConsts.xDeadLetterExchange, _rabbitMqConfig.XDeadLetterExchange },
                { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConfig.XDeadLetterRoutingKey }
            },
            cancellationToken: ts);

        _logger.LogDebug("Queue '{QueueName}' declared: messages={Messages}, consumers={Consumers}",
            _rabbitMqConfig.QueueName, result.MessageCount, result.ConsumerCount);
    }

    private bool IsReadyConnection =>
         _connection is { IsOpen: true } && _queueDeclared;
    
    private bool IsQueueDeclared =>
        _connection is { IsOpen: true } && !_queueDeclared;
    
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

            if (_connection is null)
            {
                await _rabbitPolicy.ExecuteAsync(
                    async (_) => await CreateConnection(ct), new Context
                    {
                        [PolicyRegistryConsts.Logger] = _logger
                    });
            }
            
            if (!IsQueueDeclared)
            {
                await DeclareChannel(ct);
                
                Debug.Assert(_connection != null);
                return _connection;
            }
            
            await ClearRabbitMqConnection(ct);
           
            return await _rabbitPolicy.ExecuteAsync(
                async (_) => await CreateConnection(ct),  new Context
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
            _logger.LogInformation("Queue '{QueueName}' declared on connection init", _rabbitMqConfig.QueueName);
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