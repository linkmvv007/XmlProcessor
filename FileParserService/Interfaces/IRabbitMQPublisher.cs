namespace FileParserWebService.Interfaces;

public interface IRabbitMqPublisher
{
    Task<bool> SendMessageToRabbitMq(string messageBody, CancellationToken cancellationToken);
}