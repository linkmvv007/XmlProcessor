namespace DataProcessorService;

public interface IRabbitMqConsumerManager: IAsyncDisposable
{
    Task SetupQueuesAsync(CancellationTokenSource cts);
    
    Task InitConsumer(CancellationToken stoppingToken);
}