namespace FileParserWebService.Interfaces;

public interface IRabbitMQPublisher
{
    Task SendMessageToRabbitMQ(string messageBody, CancellationToken cancellationToken);
}