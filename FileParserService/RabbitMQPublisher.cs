using FileParserWebService.Interfaces;
 using Microsoft.Extensions.Options;
 using RabbitMQ.Client;
 using RabbitMQ.Client.Events;
 using SharedLibrary.Configuration;
 
 namespace FileParserService;
 
 public class RabbitMqPublisher : IRabbitMqPublisher
 {
     private readonly ILogger<RabbitMqPublisher> _logger;
     private readonly RabbitMqConfigPublisher _rabbitMqConfig;
     private readonly IRabbitMqConnectionManager _rabbitConnectionManager;

     /// <summary>
     /// </summary>
     /// <param name="logger"></param>
     /// <param name="rabbitMqConfig"></param>
     /// <param name="rabbitConnectionManager"></param>
     public RabbitMqPublisher(
         ILogger<RabbitMqPublisher> logger, 
         IOptionsMonitor<RabbitMqConfigPublisher> rabbitMqConfig,
         IRabbitMqConnectionManager rabbitConnectionManager
         )
     {
         _logger = logger;
            
         _rabbitMqConfig = rabbitMqConfig.CurrentValue;
         _rabbitConnectionManager = rabbitConnectionManager;
     }
    
     async Task<bool> MessagePublishing(IChannel channel, byte[] body, CancellationToken ts)
     {
         var properties = new BasicProperties
         {
             MessageId = Guid.NewGuid().ToString("N")[..8],
             DeliveryMode = DeliveryModes.Persistent,
             ContentType = "application/json"
         };
         
         AsyncEventHandler<BasicReturnEventArgs>returnHandler = (_, args) =>
         {
             _logger.LogWarning(" Message returned: {ReplyText}, routingKey = {RoutingKey}, messageId = {MessageId}"
                 , args.ReplyText
                 , args.RoutingKey
                 , args.BasicProperties.MessageId
             );
 
             // Try to send it again
             throw new RabbitMqReturnException(
                 args, $"Raise BasicReturnAsync: MessageId: {args.BasicProperties.MessageId}"
                 );
         };
         
         // unsent messages:
         channel.BasicReturnAsync += returnHandler;
 
         try
         {
             //  The publishing the message
             await channel.BasicPublishAsync(
                 exchange: "",
                 routingKey: _rabbitMqConfig.QueueName,
                 mandatory: true,
                 basicProperties: properties,
                 body: body,
                 ts);
 
             _logger.LogInformation("Message sent successfully to queue: {QueueName}; messageId = {MessageId}",
                 _rabbitMqConfig.QueueName, properties.MessageId);
             
             return true;
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Message publishing failed. MessageId = {MessageId}",properties.MessageId);
             return false;
         }
         finally
         {
             channel.BasicReturnAsync -= returnHandler;
         }
     }
     async Task<bool> IRabbitMqPublisher.SendMessageToRabbitMq(string messageBody, CancellationToken ts)
     {
         var body = System.Text.Encoding.UTF8.GetBytes(messageBody);
 
         try
         {
             var connection = await _rabbitConnectionManager.GetConnectionAsync(ts);
             await using var channel = await connection.CreateChannelAsync(cancellationToken: ts);
             
            return  await MessagePublishing(channel, body, ts);
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Exception with RabbitMQ - message :'{messageBody}'", messageBody);
             throw;
         }
 
     }
 }