using DataProcessorService.Db.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataProcessorService;

public class Worker : BackgroundService
{
    private const string TitleProgram = "DataProcessorService";

    private readonly ILogger<Worker> _logger;
    private readonly IRabbitMqConsumerManager _consumerManager;
    private readonly IRepository _repository;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="consumerManager"></param>
    /// <param name="repository"></param>
    /// <param name="lifetime"></param>
    public Worker(
        ILogger<Worker> logger,
        IRabbitMqConsumerManager consumerManager,
        IRepository repository,
        IHostApplicationLifetime lifetime
    )
    {
        _logger = logger;
        _consumerManager = consumerManager;
        _repository = repository;
        _lifetime = lifetime;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"{TitleProgram} is starting.");

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{TitleProgram} is running.");

        if (await InitializeOrTerminateAsync())
        {
            await _consumerManager.InitConsumer(stoppingToken);
        }
    }

    private async Task<bool> InitializeOrTerminateAsync()
    {
        using CancellationTokenSource localCts = new();

        await _repository.InitializeDatabaseAsync(localCts);

        await _consumerManager.SetupQueuesAsync(localCts);

        if (localCts.IsCancellationRequested)
        {
            _logger.LogInformation("Exit background service");
            _lifetime.StopApplication();
            return false;
        }

        return true;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _consumerManager.DisposeAsync();

        await base.StopAsync(cancellationToken);
    }
}