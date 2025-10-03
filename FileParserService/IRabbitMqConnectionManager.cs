using RabbitMQ.Client;

namespace FileParserService;

public interface IRabbitMqConnectionManager
{
    Task<IConnection> GetConnectionAsync(CancellationToken ct);
}