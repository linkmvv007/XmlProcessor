using FileParserService;
using FileParserWebService.Interfaces;
using Polly;
using Polly.Registry;
using Serilog;
using SharedLibrary;
using SharedLibrary.Configuration;


var builder = WebApplication.CreateBuilder(args);

// serilog:
Log.Logger = new LoggerConfiguration()
.ReadFrom.Configuration(
    new ConfigurationBuilder()
    .AddJsonFile("serilog.json")
    .Build()
    )
.CreateLogger();

builder.Host.UseSerilog();

var registry = new PolicyRegistry
{
    // Policy for RabbitMQ
    {
        PolicyRegistryConsts.RabbitRetryKey, Policy
            .Handle<RabbitMqReturnException>()
            .Or<Exception>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, ts, count, ctx) =>
                {
                    var logger = ctx[PolicyRegistryConsts.Logger] as Microsoft.Extensions.Logging.ILogger;
                    logger?.LogWarning(ex, $"RabbitMQ attempt {count}");
                })
    },
    // Policy for opening files
    {
        PolicyRegistryConsts.FileOpenRetryKey, Policy
            .Handle<IOException>()
            .Or<UnauthorizedAccessException>()
            .Or<Exception>()
            .WaitAndRetry(5, attempt => TimeSpan.FromMilliseconds(500 * attempt),
                (ex, ts, count, ctx) =>
                {
                    var logger = ctx[PolicyRegistryConsts.Logger] as Microsoft.Extensions.Logging.ILogger;
                    var fileName = ctx[PolicyRegistryConsts.FileName] as string;

                    logger?.LogWarning($"Attempt {count}: error opening the file '{fileName}'. Repeat after {ts} sec.");
                })
    }
};

builder.Services.AddSingleton<IReadOnlyPolicyRegistry<string>>(registry);


builder.Services.AddSingleton<IRabbitMqConnectionManager, RabbitMqConnectionManager>();
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

builder.Services.AddHostedService<Worker>();

// do not stop the host in case of exceptions:
builder.Services.Configure<HostOptions>(hostOptions =>
{
    hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

var rabbitMqSection = builder.Configuration.GetSection("RabbitMq");
var xmlFilesSection = builder.Configuration.GetSection("XmlFiles");


builder.Services.Configure<RabbitMqConfigPublisher>(rabbitMqSection);
builder.Services.Configure<FileStorageConfig>(xmlFilesSection);


var app = builder.Build();


app.UseRouting();
//app.UseStaticFiles();

app.Run();