using FileParserWebService.Interfaces;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SharedLibrary;
using SharedLibrary.Configuration;

namespace FileParserWebService;

public class RabbitMQPublisher : IRabbitMQPublisher
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly ILogger<RabbitMQPublisher> _logger;

    private readonly RabbitMqConfig _rabbitMqConfig;

    private readonly ConnectionFactory _factory;

    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger, IOptions<RabbitMqConfig> rabbitMqConfig)
    {
        _logger = logger;
        _rabbitMqConfig = rabbitMqConfig.Value;

        _factory = new ConnectionFactory()
        {
            HostName = _rabbitMqConfig.HostName,
            VirtualHost = _rabbitMqConfig.VirtualHost,
            UserName = _rabbitMqConfig.UserName,
            Password = _rabbitMqConfig.Password,
            Port = _rabbitMqConfig.Port,

            AutomaticRecoveryEnabled = _rabbitMqConfig.AutomaticRecoveryEnabled, // Автоматическое восстановление соединения
            NetworkRecoveryInterval = TimeSpan.FromSeconds(_rabbitMqConfig.NetworkRecoveryInterval)
        };
    }

    async Task IRabbitMQPublisher.SendMessageToRabbitMQ(string messageBody, CancellationToken ts)
    {
        await _semaphore.WaitAsync(ts);

        try
        {
            using var connection = await _factory.CreateConnectionAsync(cancellationToken: ts);
            using var channel = await connection.CreateChannelAsync(cancellationToken: ts);

            // Объявляем очередь (если не существует)
            await channel.QueueDeclareAsync(
                queue: _rabbitMqConfig.QueueName,
                durable: true, // Сохранять очередь после рестарта
                exclusive: false,
                autoDelete: false,
                //arguments:null
                arguments: new Dictionary<string, object?>
                 {
                { RabbitMqConsts.xDeadLetterExchange, _rabbitMqConfig.XDeadLetterExchange },
                { RabbitMqConsts.xDeadLetterRoutingKey, _rabbitMqConfig.XDeadLetterRoutingKey }
                 },
                cancellationToken: ts);


            var body = System.Text.Encoding.UTF8.GetBytes(messageBody);

            // Публикуем сообщение
            var properties = new BasicProperties
            {
                DeliveryMode = DeliveryModes.Persistent,
                ContentType = "application/json"
            };


            // Публикуем сообщение (с обязательными mandatory)
            await channel.BasicPublishAsync(
                  exchange: "",
                  routingKey: _rabbitMqConfig.QueueName,
                  mandatory: true,
                  basicProperties: properties,
                  body: body,
                  ts);


            // не отправленные сообщения:
            channel.BasicReturnAsync += async (sender, args) =>
            {
                _logger.LogWarning($"Message returned: {args.ReplyText} ; routingKey = {args.RoutingKey}");

                //var body = args.Body.ToArray();
                //var message = Encoding.UTF8.GetString(body);
                //todo: save to storage... that later send again

                await Task.CompletedTask;
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception with RabbitMQ : {ex.Message},\n\r message :'{messageBody}'");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }

    }
}