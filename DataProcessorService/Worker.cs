using DataProcessorService.Db.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedLibrary;
using SharedLibrary.Configuration;
using SharedLibrary.Json;
using System.Text;

namespace DataProcessorService;


public class Worker : BackgroundService
{
    private const string TitleProgram = "DataProcessorService";

    private readonly ILogger<Worker> _logger;
    private readonly RabbitMqConfig _rabbitMqConfig;
    private readonly IRepository _repository;

    private IConnection? _connection;
    private IChannel? _channel;

    private readonly SemaphoreSlim _sqliteLock = new(1);
    private readonly IHostApplicationLifetime _lifetime;

    public Worker(
        IHostApplicationLifetime lifetime,
        ILogger<Worker> logger,
        IOptions<RabbitMqConfig> rabbitMqOptions,
        IRepository repository
       )
    {
        _logger = logger;
        _lifetime = lifetime;
        _rabbitMqConfig = rabbitMqOptions.Value;
        _repository = repository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{TitleProgram} is starting.");

        await InitializeAsync();

        var consumer = new AsyncEventingBasicConsumer(_channel!);

        consumer.ReceivedAsync += async (_, ea) => await HandleMessageAsync(ea, stoppingToken);

        await _channel!.BasicConsumeAsync(
                             queue: _rabbitMqConfig.QueueName,
                             autoAck: false, // Вручную подтверждаем получение
                             consumer: consumer,
                             cancellationToken: stoppingToken
                             );

        // Ждем токен отмены
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(10000, stoppingToken);
        }

        _logger.LogInformation($"{TitleProgram} is stopping.");
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        await _sqliteLock.WaitAsync(stoppingToken);

        string message = string.Empty;
        try
        {
            message = Encoding.UTF8.GetString(ea.Body.ToArray());
            var modules = System.Text.Json.JsonSerializer.Deserialize<InstrumentStatusJson>(message);

            if (modules?.DeviceStatuses is null)
            {
                _logger.LogWarning("Received invalid message: {Message}", message);
                return;
            }

            foreach (var module in modules.DeviceStatuses)
            {
                var json = new ModuleInfoJson
                {
                    ModuleCategoryID = module.ModuleCategoryID,
                    ModuleState = module.RapidControlStatus.ModuleState.ToString()
                };

                if (await _repository.ProcessModuleAsync(json, stoppingToken))
                {
                    await _channel!.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false, stoppingToken);

                    //todo: error write to db => не подтверждать получение | хранить в др. месте (redis, fifo rabit)'
                    // Dead Letter Queue
                    return;
                }
            }

            await _channel!.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, stoppingToken); // confirm receipt

            _logger.LogInformation("Processed message from RabbitMQ.");

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from RabbitMQ: {Message}", message);
            // В реальном приложении может потребоваться Dead Letter Queue
        }
        finally
        {
            _sqliteLock.Release();
        }
    }

    private async Task InitializeAsync()
    {
        CancellationTokenSource localCts = new();

        _repository.InitializeDatabase(localCts);

        //await InitializeRabbitMqAsync(localCts);
        await SetupQueuesAsync(localCts);

        if (localCts.IsCancellationRequested)
        {
            _logger.LogInformation("Exit background service");

            _lifetime.StopApplication(); // exit
        }
    }

    private async Task InitializeRabbitMqAsync(CancellationTokenSource cts)
    {
        try
        {
            var factory = new ConnectionFactory()
            {
                HostName = _rabbitMqConfig.HostName,
                VirtualHost = _rabbitMqConfig.VirtualHost,
                Port = _rabbitMqConfig.Port,
                UserName = _rabbitMqConfig.UserName,
                Password = _rabbitMqConfig.Password
            };

            _connection = await factory.CreateConnectionAsync(cts.Token);
            _channel = await _connection.CreateChannelAsync();



            await _channel.ExchangeDeclareAsync(_rabbitMqConfig.XDeadLetterExchange, ExchangeType.Direct, durable: true);
            await _channel.QueueDeclareAsync(_rabbitMqConfig.XDeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
            await _channel.QueueBindAsync(_rabbitMqConfig.XDeadLetterQueueName, _rabbitMqConfig.XDeadLetterExchange, _rabbitMqConfig.XDeadLetterQueueName);



            var queue = await _channel.QueueDeclareAsync(
                                  queue: _rabbitMqConfig.QueueName,
                                  durable: true,
                                  exclusive: false,
                                  autoDelete: false,
                                  cancellationToken: cts.Token,
                                  arguments: new Dictionary<string, object?>  // !!!
            {
                { RabbitMqConsts.xDeadLetterExchange, _rabbitMqConfig.XDeadLetterExchange },
                { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConfig.XDeadLetterQueueName }
            }
                                  );

            _logger.LogInformation("Connected to RabbitMQ.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ.");
            cts.Cancel(); // exit
        }
    }

    private async Task SetupQueuesAsync(CancellationTokenSource cts)
    {
        var factory = new ConnectionFactory()
        {
            HostName = _rabbitMqConfig.HostName,
            VirtualHost = _rabbitMqConfig.VirtualHost,
            Port = _rabbitMqConfig.Port,
            UserName = _rabbitMqConfig.UserName,
            Password = _rabbitMqConfig.Password
        };

        _connection = await factory.CreateConnectionAsync(cts.Token);
        _channel = await _connection.CreateChannelAsync();

        try
        {
            // main fifo
            await _channel.QueueDeclareAsync(_rabbitMqConfig.QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: cts.Token,
                 arguments: new Dictionary<string, object?>
                 {
                    { RabbitMqConsts.xDeadLetterExchange, _rabbitMqConfig.XDeadLetterExchange  },
                    { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConfig.XDeadLetterRoutingKey }
                 });

            // retry fifo
            await _channel.QueueDeclareAsync(_rabbitMqConfig.XDeadLetterQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: cts.Token,
                arguments: new Dictionary<string, object?>
                {
                { RabbitMqConsts.xDeadLetterExchange, "" }, // default exchange
                { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConfig.QueueName },
                { RabbitMqConsts.xMessageTtl, _rabbitMqConfig.TtlDelay } // delay in milliseconds
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup Rabbit");
            throw;
        }
    }
    public async override void Dispose()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        base.Dispose();
    }
}