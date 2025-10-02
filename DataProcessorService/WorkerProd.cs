using DataProcessorService.Db.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedLibrary.Configuration;
using SharedLibrary.Json;
using System.Text;
using System.Text.Json;

/*
public class WorkerProd : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly RabbitMqConfig _config;
    private readonly IRepository _repository;

    private IConnection? _connection;
    private IChannel? _channel;

    public WorkerProd(
        ILogger<Worker> logger,
        IOptions<RabbitMqConfig> rabbitMqOptions,
        IRepository repository)
    {
        _logger = logger;
        _config = rabbitMqOptions.Value;
        _repository = repository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeRabbitMqAsync();

        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (_, ea) => await HandleMessageAsync(ea, stoppingToken);

        await _channel!.BasicConsumeAsync(_config.QueueName, autoAck: false, consumer);

        var dlqConsumer = new AsyncEventingBasicConsumer(_channel);
        dlqConsumer.ReceivedAsync += async (_, ea) => await HandleDlqMessageAsync(ea, stoppingToken);

        await _channel.BasicConsumeAsync(_config.DeadLetterQueueName, autoAck: false, dlqConsumer);

        _logger.LogInformation("Worker consuming messages from main and DLQ queues.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task InitializeRabbitMqAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = _config.HostName,
            VirtualHost = _config.VirtualHost,
            Port = _config.Port,
            UserName = _config.UserName,
            Password = _config.Password
        };

        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        await _channel.ExchangeDeclareAsync(_config.DeadLetterExchange, ExchangeType.Direct, durable: true);

        await _channel.QueueDeclareAsync(_config.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
        await _channel.QueueBindAsync(_config.DeadLetterQueueName, _config.DeadLetterExchange, _config.DeadLetterQueueName);

        await _channel.QueueDeclareAsync(_config.QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", _config.DeadLetterExchange },
                { "x-dead-letter-routing-key", _config.DeadLetterQueueName }
            });

        _logger.LogInformation("RabbitMQ queues and exchanges initialized.");
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        string message = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            var envelope = JsonSerializer.Deserialize<RetryEnvelope>(message);
            var status = envelope?.Payload;

            if (status?.DeviceStatuses is null)
                throw new InvalidDataException("DeviceStatuses is null");

            foreach (var module in status.DeviceStatuses)
            {
                var info = new ModuleInfoJson
                {
                    ModuleCategoryID = module.ModuleCategoryID,
                    ModuleState = module.RapidControlStatus.ModuleState.ToString()
                };

                await _repository.ProcessModuleAsync(info, cancellationToken);
            }

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message. Sending to DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task HandleDlqMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        string message = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            var envelope = JsonSerializer.Deserialize<RetryEnvelope>(message);
            if (envelope is null || envelope.Payload is null)
            {
                _logger.LogWarning("Invalid DLQ message format.");
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            if (envelope.RetryCount >= _config.MaxRetryCount)
            {
                _logger.LogWarning("Message exceeded max retry count: {Message}", message);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            envelope.RetryCount += 1;
            var updatedMessage = JsonSerializer.Serialize(envelope);
            var body = Encoding.UTF8.GetBytes(updatedMessage);

            var props = new RabbitMQ.Client.BasicProperties();
            await _channel!.BasicPublishAsync(
                exchange: "",
                routingKey: _config.QueueName,
                mandatory: true,
                basicProperties: props,
                body: body,
                ct);

            await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            _logger.LogInformation("Requeued message from DLQ (attempt {RetryCount})", envelope.RetryCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to requeue message from DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override async void Dispose()
    {
        try
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal.");
        }

        base.Dispose();
    }
}

*/
public class RetryEnvelope
{
    public int RetryCount { get; set; }
    public InstrumentStatusJson Payload { get; set; } = default!;
}

public class WorkerProd : BackgroundService
{
    private readonly ILogger<WorkerProd> _logger;
    private readonly RabbitMqConfig _config;
    private readonly IRepository _repository;

    private IConnection? _connection;
    private IChannel? _channel;

    public WorkerProd(
        ILogger<WorkerProd> logger,
        IOptions<RabbitMqConfig> rabbitMqOptions,
        IRepository repository)
    {
        _logger = logger;
        _config = rabbitMqOptions.Value;
        _repository = repository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeRabbitMqAsync();

        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += async (_, ea) => await HandleMessageAsync(ea, stoppingToken);
        await _channel!.BasicConsumeAsync(_config.QueueName, autoAck: false, consumer);


        // !!!
        var dlqConsumer = new AsyncEventingBasicConsumer(_channel);
        dlqConsumer.ReceivedAsync += async (_, ea) => await HandleDlqMessageAsync(ea, stoppingToken);
        await _channel.BasicConsumeAsync(_config.DeadLetterQueueName, autoAck: false, dlqConsumer);


        _logger.LogInformation("Worker consuming messages from main and DLQ queues.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task InitializeRabbitMqAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = _config.HostName,
            VirtualHost = _config.VirtualHost,
            Port = _config.Port,
            UserName = _config.UserName,
            Password = _config.Password
        };

        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();


        // !!!
        await _channel.ExchangeDeclareAsync(_config.DeadLetterExchange, ExchangeType.Direct, durable: true);
        await _channel.QueueDeclareAsync(_config.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
        await _channel.QueueBindAsync(_config.DeadLetterQueueName, _config.DeadLetterExchange, _config.DeadLetterQueueName);



        await _channel.QueueDeclareAsync(_config.QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>  // !!!
            {
                { "x-dead-letter-exchange", _config.DeadLetterExchange },
                { "x-dead-letter-routing-key", _config.DeadLetterQueueName }
            });

        _logger.LogInformation("RabbitMQ queues and exchanges initialized.");
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        string message = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            var envelope = JsonSerializer.Deserialize<RetryEnvelope>(message);
            var status = envelope?.Payload;

            if (status?.DeviceStatuses is null)
                throw new InvalidDataException("DeviceStatuses is null");

            foreach (var module in status.DeviceStatuses)
            {
                var info = new ModuleInfoJson
                {
                    ModuleCategoryID = module.ModuleCategoryID,
                    ModuleState = module.RapidControlStatus.ModuleState.ToString()
                };

                await _repository.ProcessModuleAsync(info, cancellationToken);
            }

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message. Sending to DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);  // !!!
        }
    }

    // !!!
    private async Task HandleDlqMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        string message = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            var envelope = JsonSerializer.Deserialize<RetryEnvelope>(message);
            if (envelope is null || envelope.Payload is null)
            {
                _logger.LogWarning("Invalid DLQ message format.");
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            if (envelope.RetryCount >= _config.MaxRetryCount)
            {
                _logger.LogWarning("Message exceeded max retry count: {Message}", message);
                await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                return;
            }

            envelope.RetryCount += 1;
            var updatedMessage = JsonSerializer.Serialize(envelope);
            var body = Encoding.UTF8.GetBytes(updatedMessage);

            var props = new RabbitMQ.Client.BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent // persistent
            };

            await _channel!.BasicPublishAsync(
                exchange: "",
                routingKey: _config.QueueName,
                mandatory: true,
                basicProperties: props,
                body: body,
                ct);

            await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            _logger.LogInformation("Requeued message from DLQ (attempt {RetryCount})", envelope.RetryCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to requeue message from DLQ.");
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override async void Dispose()
    {
        try
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal.");
        }

        base.Dispose();
    }
}
