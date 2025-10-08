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

public class RabbitMqConsumerManager : IRabbitMqConsumerManager
{
    private int _reinitInProgress = 0;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<RabbitMqConsumerManager> _logger;
    private readonly SemaphoreSlim _sqliteLock = new(1);

    private readonly RabbitMqConsumerConfig _rabbitMqConsumerConfig;
    private IConnection? _connection;
    private IChannel? _channel;
    private AsyncEventingBasicConsumer? _consumer;

    private AsyncEventHandler<BasicDeliverEventArgs>? _handlerBasicDeliver; // consumer

    private AsyncEventHandler<CallbackExceptionEventArgs>? _handlerCallbackException;
    private AsyncEventHandler<ConnectionRecoveryErrorEventArgs>? _handlerConnectionRecoveryError;
    private readonly IAsyncPolicy _rabbitMqPolicy;
    private readonly IAsyncPolicy _repositoryPolicy;
    private readonly IRepository _repository;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="rabbitMqOptions"></param>
    /// <param name="policyRegistry"></param>
    /// <param name="repository"></param>
    /// <param name="lifetime"></param>
    public RabbitMqConsumerManager(
        ILogger<RabbitMqConsumerManager> logger,
        IOptionsMonitor<RabbitMqConsumerConfig> rabbitMqOptions,
        IReadOnlyPolicyRegistry<string> policyRegistry,
        IRepository repository,
        IHostApplicationLifetime lifetime
    )
    {
        _logger = logger;
        _rabbitMqConsumerConfig = rabbitMqOptions.CurrentValue;
        _rabbitMqPolicy = policyRegistry.Get<IAsyncPolicy>(PolicyRegistryConsts.RabbitRetryKey);
        _repositoryPolicy = policyRegistry.Get<IAsyncPolicy>(PolicyRegistryConsts.DbPollyKey);
        _repository = repository;
        _lifetime = lifetime;
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

    public async Task SetupQueuesAsync(CancellationTokenSource cts)
    {
        if (cts.Token.IsCancellationRequested)
            return;

        try
        {
            await _rabbitMqPolicy.ExecuteAsync(async (_) => await InitializeConsumerAsync(cts.Token), new Context
            {
                [PolicyRegistryConsts.Logger] = _logger
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup Rabbit");
            cts.Cancel();
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
        catch (OperationCanceledException)
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


    public async Task InitConsumer(CancellationToken stoppingToken)
    {
        _consumer = new AsyncEventingBasicConsumer(_channel);

        _handlerBasicDeliver = async (sender, ea) => await HandleMessageAsync(ea, stoppingToken);
        if (_handlerBasicDeliver != null)
            _consumer.ReceivedAsync += _handlerBasicDeliver;

        await _channel.BasicConsumeAsync(
            queue: _rabbitMqConsumerConfig.QueueName,
            autoAck: false, // Manually confirming receipt
            consumer: _consumer,
            cancellationToken: stoppingToken
        );
    }

    private async Task InitializeConsumerAsync(CancellationToken ct)
    {
        await CreateConnectionAsync(ct);

        _channel = await _connection.CreateChannelAsync();

        await DeclareQueuesAsync(ct);
    }

    private async Task CreateConnectionAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqConsumerConfig.HostName,
            VirtualHost = _rabbitMqConsumerConfig.VirtualHost,
            Port = _rabbitMqConsumerConfig.Port,
            UserName = _rabbitMqConsumerConfig.UserName,
            Password = _rabbitMqConsumerConfig.Password,

            AutomaticRecoveryEnabled = _rabbitMqConsumerConfig.AutomaticRecoveryEnabled,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(_rabbitMqConsumerConfig.NetworkRecoveryInterval)
        };

        _connection = await factory.CreateConnectionAsync(ct);
        if (_connection is null || !_connection.IsOpen)
            throw new InvalidOperationException("RabbitMQ connection is not open");

        _handlerConnectionRecoveryError = async (_, args) =>
        {
            _logger.LogWarning(args.Exception, "RabbitMQ connection recovery error");

            await ReinitializeConsumerAsync(args.CancellationToken);
        };

        _connection.ConnectionRecoveryErrorAsync += _handlerConnectionRecoveryError;
    }

    private async Task DeclareQueuesAsync(CancellationToken stoppedToken)
    {
        // main fifo
        await _channel.QueueDeclareAsync(_rabbitMqConsumerConfig.QueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppedToken,
            arguments: new Dictionary<string, object?>
            {
                { RabbitMqConsts.xDeadLetterExchange, _rabbitMqConsumerConfig.XDeadLetterExchange },
                { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConsumerConfig.XDeadLetterRoutingKey }
            });

        // retry fifo
        await _channel.QueueDeclareAsync(_rabbitMqConsumerConfig.XDeadLetterQueueName, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: stoppedToken,
            arguments: new Dictionary<string, object?>
            {
                { RabbitMqConsts.xDeadLetterExchange, "" }, // default exchange
                { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConsumerConfig.QueueName },
                { RabbitMqConsts.xMessageTtl, _rabbitMqConsumerConfig.TtlDelay }, // delay in milliseconds
                { RabbitMqConsts.xMaxLength, _rabbitMqConsumerConfig.XMaxLength } // overflow protection
            });

        // exchange:
        await _channel.ExchangeDeclareAsync(
            exchange: _rabbitMqConsumerConfig.XDeadLetterExchange,
            type: ExchangeType.Direct,
            cancellationToken: stoppedToken);

        await _channel.QueueBindAsync(
            queue: _rabbitMqConsumerConfig.XDeadLetterQueueName,
            exchange: _rabbitMqConsumerConfig.XDeadLetterExchange,
            routingKey: _rabbitMqConsumerConfig.XDeadLetterRoutingKey,
            cancellationToken: stoppedToken);

        await _channel.BasicQosAsync(
            _rabbitMqConsumerConfig.Consumer.PrefetchSize,
            _rabbitMqConsumerConfig.Consumer.PrefetchCount,
            _rabbitMqConsumerConfig.Consumer.Global,
            stoppedToken
        );

        _handlerCallbackException = async (_, args) =>
        {
            _logger.LogWarning(args.Exception, "RabbitMQ channel callback exception occurred.");
            await ReinitializeConsumerAsync(args.CancellationToken);
        };
        _channel.CallbackExceptionAsync += _handlerCallbackException;
    }

    private void ClearHandlers()
    {
        if (_consumer is not null && _handlerBasicDeliver is not null)
            _consumer.ReceivedAsync -= _handlerBasicDeliver;

        if (_channel is not null && _handlerCallbackException is not null)
            _channel.CallbackExceptionAsync -= _handlerCallbackException;

        if (_connection is not null && _handlerConnectionRecoveryError is not null)
            _connection.ConnectionRecoveryErrorAsync -= _handlerConnectionRecoveryError;

        _consumer = null;
        _handlerBasicDeliver = null;
        _handlerCallbackException = null;
        _handlerConnectionRecoveryError = null;
    }

    private void AttachChannelEventHandlers()
    {
        _handlerCallbackException = async (_, args) =>
        {
            _logger.LogWarning(args.Exception, "CallbackException occurred.");
            await ReinitializeConsumerAsync(args.CancellationToken);
        };

        _channel.CallbackExceptionAsync += _handlerCallbackException;
    }

    private async Task ReinitializeConsumerAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _reinitInProgress, 1) == 1)
            return;
        try
        {
            await ConnectAndChannelDispose(ct);

            if (_connection is null || !_connection.IsOpen)
            {
                await _rabbitMqPolicy.ExecuteAsync(async (_) => await CreateConnectionAsync(ct), new Context
                {
                    [PolicyRegistryConsts.Logger] = _logger
                });
            }

            _logger.LogWarning("Channel is closed. Recreating...");

            _channel = await _connection.CreateChannelAsync();
            await DeclareQueuesAsync(ct);

            AttachChannelEventHandlers();

            await InitConsumer(ct);

            _logger.LogInformation("RabbitMQ consumer reinitialized.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reinitialize RabbitMQ consumer. Stopping host...");
            _lifetime.StopApplication();
        }
        finally
        {
            Interlocked.Exchange(ref _reinitInProgress, 0);
        }
    }

    private async Task ConnectAndChannelDispose(CancellationToken ct)
    {
        ClearHandlers();

        if (_channel is not null && _channel.IsOpen)
        {
            await _channel.CloseAsync(ct);
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null && _connection.IsOpen)
        {
            await _connection.CloseAsync(ct);
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }


    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await ConnectAndChannelDispose(default);
    }
}