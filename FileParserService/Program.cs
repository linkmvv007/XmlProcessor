using FileParserService;
using FileParserWebService.Interfaces;
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
                    .Handle<RabbitMqReturnException>()
                    .Or<Exception>(ex => ex is not OperationCanceledException)
                    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                        (ex, ts, count, ctx) =>
                        {
                            var logger = ctx[PolicyRegistryConsts.Logger] as Microsoft.Extensions.Logging.ILogger;
                            logger?.LogWarning(ex, $"RabbitMQ attempt {count}. Repeat after {ts} sec.");
                        })
            },
            {
                PolicyRegistryConsts.FileOpenRetryKey, Policy
                    .Handle<IOException>()
                    .Or<UnauthorizedAccessException>()
                    .Or<Exception>(ex => ex is not OperationCanceledException)
                    .WaitAndRetry(5, attempt => TimeSpan.FromMilliseconds(500 * attempt),
                        (ex, ts, count, ctx) =>
                        {
                            var logger = ctx[PolicyRegistryConsts.Logger] as Microsoft.Extensions.Logging.ILogger;
                            var fileName = ctx[PolicyRegistryConsts.FileName] as string;
                            logger?.LogWarning(
                                $"Attempt {count}: error opening the file '{fileName}'. Repeat after {ts} sec.");
                        })
            }
        };

        // services.AddSingleton<IReadOnlyPolicyRegistry<string>>(registry);
        services.AddSingleton<IAsyncPolicy>(registry.Get<IAsyncPolicy>(PolicyRegistryConsts.RabbitRetryKey));
        services.AddSingleton<ISyncPolicy>(registry.Get<ISyncPolicy>(PolicyRegistryConsts.FileOpenRetryKey));

        // RabbitMQ
        services.AddSingleton<IRabbitMqConnectionManager, RabbitMqConnectionManager>();
        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

        // Hosted service
        services.AddHostedService<Worker>();

        // BackgroundService exception behavior
        services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
        });

        // Configuration binding
        services.Configure<RabbitMqPublisherConfig>(configuration.GetSection("RabbitMq"));
        services.Configure<FileStorageConfig>(configuration.GetSection("XmlFiles"));
    })
    .Build()
    .Run();