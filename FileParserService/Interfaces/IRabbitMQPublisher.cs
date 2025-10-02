namespace FileParserWebService.Interfaces;

public interface IRabbitMqPublisher
{
    Task SendMessageToRabbitMq(string messageBody, CancellationToken cancellationToken);
}