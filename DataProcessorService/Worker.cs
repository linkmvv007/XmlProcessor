using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataProcessorService.Db.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedLibrary;
using SharedLibrary.Configuration;
using SharedLibrary.Json;

namespace DataProcessorService;

public class Worker : BackgroundService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string TitleProgram = "DataProcessorService";

    private readonly ILogger<Worker> _logger;
    private readonly RabbitMqConfigConsumer _rabbitMqConfig;
    private readonly IRepository _repository;

    private IConnection? _connection;
    private IChannel? _channel;

    private readonly SemaphoreSlim _sqliteLock = new(1);
    private readonly IHostApplicationLifetime _lifetime;

    private readonly IAsyncPolicy _repositoryPolicy;
    private readonly IAsyncPolicy _rabbitMqPolicy;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="lifetime"></param>
    /// <param name="logger"></param>
    /// <param name="rabbitMqOptions"></param>
    /// <param name="repository"></param>
    /// <param name="policyRegistry"></param>
    public Worker(
        IHostApplicationLifetime lifetime,
        ILogger<Worker> logger,
        IOptions<RabbitMqConfigConsumer> rabbitMqOptions,
        IRepository repository,
        IReadOnlyPolicyRegistry<string> policyRegistry
    )
    {
        _logger = logger;
        _lifetime = lifetime;
        _rabbitMqConfig = rabbitMqOptions.Value;
        _repository = repository;

        _repositoryPolicy = policyRegistry.Get<IAsyncPolicy>(PolicyRegistryConsts.DbPollyKey);
        _rabbitMqPolicy = policyRegistry.Get<IAsyncPolicy>(PolicyRegistryConsts.RabbitRetryKey);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"{TitleProgram} is starting.");


        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{TitleProgram} is running.");

        await InitializeOrTerminateAsync();

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (_, ea) => await HandleMessageAsync(ea, stoppingToken);

        await _channel.BasicConsumeAsync(
            queue: _rabbitMqConfig.QueueName,
            autoAck: false, // Manually confirming receipt
            consumer: consumer,
            cancellationToken: stoppingToken
        );
    }

    private void ChannelIsNullThrow()
    {
        const string rabbitMqChannelMsg = @"RabbitMq channel is null";
        if (_channel is null)
        {
            _logger.LogError(rabbitMqChannelMsg);
            throw new InvalidOperationException(rabbitMqChannelMsg);
        }
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        try
        {
            var modules = JsonSerializer.Deserialize<InstrumentStatusJson>(
                Encoding.UTF8.GetString(ea.Body.Span
                ),
                _jsonOptions
            );

            if (modules?.DeviceStatuses is null)
            {
                _logger.LogWarning("Received invalid message: {MessageId}",
                    ea.BasicProperties?.MessageId ?? "null");
                return;
            }

            ChannelIsNullThrow();

            if (await ProcessingMsg(ea, stoppingToken, modules))
            {
                await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false,
                    stoppingToken); // confirm receipt

                _logger.LogInformation("Processed message from RabbitMQ. {deliveryTag}, MessageId: {MessageId}",
                    ea.DeliveryTag, ea.BasicProperties?.MessageId ?? "null");
            }
        }
        catch (OperationCanceledException oce)
        {
            _logger.LogWarning(oce, "RabbitMq operation cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from RabbitMQ: {MessageId}",
                ea.BasicProperties?.MessageId ?? "null");
        }
    }

    private async Task<bool> ProcessingMsg(BasicDeliverEventArgs ea, CancellationToken stoppingToken,
        InstrumentStatusJson modules)
    {
        var ret = false;
        var parameters = new Collection<ModuleInfoJson>();
        foreach (var module in modules.DeviceStatuses)
        {
            parameters.Add(new ModuleInfoJson
            {
                ModuleCategoryID = module.ModuleCategoryID,
                ModuleState = module.RapidControlStatus.ModuleState.ToString()
            });
        }

        await _sqliteLock.WaitAsync(stoppingToken);
        try
        {
            var context = new Context
            {
                [PolicyRegistryConsts.Logger] = _logger
            };

            await _repositoryPolicy.ExecuteAsync(
                async (_) => { ret = await _repository.ProcessModulesBatchAsync(parameters, stoppingToken); }, context
            );
            return ret;
        }
        catch (OperationCanceledException )
        {
            _logger.LogInformation("ProcessingMsg: Operation Canceled Exception");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessingMsg errors: DeliveryTag: {DeliveryTag},  MessageId: {MessageId}",
                ea.DeliveryTag, ea.BasicProperties?.MessageId ?? "null");
            throw;
        }
        finally
        {
            _sqliteLock.Release();
            if (!ret)
            {
                // do not confirm receipt, the message is moved to dlx (Dead Letter Queue)
                await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false,
                    stoppingToken);
            }
        }
    }

    private async Task InitializeOrTerminateAsync()
    {
        using CancellationTokenSource localCts = new();

        await _repository.InitializeDatabaseAsync(localCts);

        await SetupQueuesAsync(localCts);

        if (localCts.IsCancellationRequested)
        {
            _logger.LogInformation("Exit background service");

            _lifetime.StopApplication(); // exit
        }
    }

    private async Task SetupQueuesAsync(CancellationTokenSource cts)
    {
        if (cts.Token.IsCancellationRequested)
            return;

        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqConfig.HostName,
            VirtualHost = _rabbitMqConfig.VirtualHost,
            Port = _rabbitMqConfig.Port,
            UserName = _rabbitMqConfig.UserName,
            Password = _rabbitMqConfig.Password,

            AutomaticRecoveryEnabled = _rabbitMqConfig.AutomaticRecoveryEnabled,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(_rabbitMqConfig.NetworkRecoveryInterval)
        };

        try
        {
            _connection = await factory.CreateConnectionAsync(cts.Token);
            _channel = await _connection.CreateChannelAsync();
            
            // main fifo
            await _channel.QueueDeclareAsync(_rabbitMqConfig.QueueName, durable: true, exclusive: false,
                autoDelete: false, cancellationToken: cts.Token,
                arguments: new Dictionary<string, object?>
                {
                    { RabbitMqConsts.xDeadLetterExchange, _rabbitMqConfig.XDeadLetterExchange },
                    { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConfig.XDeadLetterRoutingKey }
                });

            // retry fifo
            await _channel.QueueDeclareAsync(_rabbitMqConfig.XDeadLetterQueueName, durable: true, exclusive: false,
                autoDelete: false, cancellationToken: cts.Token,
                arguments: new Dictionary<string, object?>
                {
                    { RabbitMqConsts.xDeadLetterExchange, "" }, // default exchange
                    { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConfig.QueueName },
                    { RabbitMqConsts.xMessageTtl, _rabbitMqConfig.TtlDelay } // delay in milliseconds
                    //, {"x-max-length", 1000} // защита от переполнения
                });

            // exchange:
            await _channel.ExchangeDeclareAsync(
                exchange: _rabbitMqConfig.XDeadLetterExchange,
                type: ExchangeType.Direct,
                cancellationToken: cts.Token);

            await _channel.QueueBindAsync(
                queue: _rabbitMqConfig.XDeadLetterQueueName,
                exchange: _rabbitMqConfig.XDeadLetterExchange,
                routingKey: _rabbitMqConfig.XDeadLetterRoutingKey,
                cancellationToken: cts.Token);

            await _channel.BasicQosAsync(
                _rabbitMqConfig.Consumer.PrefetchSize,
                _rabbitMqConfig.Consumer.PrefetchCount,
                _rabbitMqConfig.Consumer.Global,
                cts.Token
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup Rabbit");
            cts.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing RabbitMQ channel.");
            }

            _channel.Dispose();
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing RabbitMQ connection.");
            }

            _connection.Dispose();
        }

        _sqliteLock.Dispose();
    }
}