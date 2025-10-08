using DataProcessorService;
using DataProcessorService.Db;
using DataProcessorService.Db.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;
using Serilog;
using SharedLibrary;
using SharedLibrary.Configuration;

Host.CreateDefaultBuilder(args)
    .UseDefaultServiceProvider(options =>
    {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true; 
    })
    .UseSerilog((_, config) =>
    {
        config.ReadFrom.Configuration(
            new ConfigurationBuilder()
                .AddJsonFile("serilog.json")
                .Build());
    })
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;
        
        // Polly registry
        var registry = new PolicyRegistry
        {
            {
                PolicyRegistryConsts.RabbitRetryKey, Policy
                    .Handle<Exception>(ex => ex is not OperationCanceledException)
                    .WaitAndRetryAsync(7, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                        (ex, ts, count, ctx) =>
                        {
                            var logger = ctx[PolicyRegistryConsts.Logger] as Microsoft.Extensions.Logging.ILogger;
                            logger?.LogWarning(ex, $"RabbitMQ attempt {count}. Repeat after {ts} sec.");
                        })
            },
            {
                PolicyRegistryConsts.DbPollyKey, Policy
                    .Handle<Exception>(ex => ex is not OperationCanceledException)
                    .WaitAndRetryAsync(5, attempt => TimeSpan.FromMilliseconds(500 * attempt),
                        (ex, ts, count, ctx) =>
                        {
                            var logger = ctx[PolicyRegistryConsts.Logger] as Microsoft.Extensions.Logging.ILogger;
                            logger?.LogWarning(
                                $"Attempt {count}: Database error operation. Repeat after {ts} sec.");
                        })
            }
        };

        // Polly
        services.AddSingleton<IReadOnlyPolicyRegistry<string>>(registry);
        
        services.AddSingleton<IRepository, Repository>();
        services.AddSingleton<IRabbitMqConsumerManager,RabbitMqConsumerManager>();

        // Hosted service
        services.AddHostedService<Worker>();

        // BackgroundService exception behavior
        services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        });

        // Configuration binding
        services.Configure<RabbitMqConsumerConfig>(configuration.GetSection("RabbitMq"));
        services.Configure<DatabaseConfig>(configuration.GetSection("Database"));
    })
    .Build()
    .Run();