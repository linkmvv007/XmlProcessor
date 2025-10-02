using FileParserService;
using FileParserWebService;
using FileParserWebService.Interfaces;
using Polly;
using Polly.Registry;
using Serilog;
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


var registry = new PolicyRegistry();

// Политика для RabbitMQ
registry.Add("RabbitRetry", Policy
    .Handle<Exception>()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
        (ex, ts, count, ctx) =>
        {
            var logger = ctx["Logger"] as Microsoft.Extensions.Logging.ILogger;
            logger?.LogWarning($"RabbitMQ попытка {count}: {ex.Message}");
        }));

// Политика для открытия файлов
registry.Add("FileOpenRetry", Policy
    .Handle<IOException>()
    .Or<UnauthorizedAccessException>()
    .Or<Exception>()
    .WaitAndRetry(5, attempt => TimeSpan.FromMilliseconds(500 * attempt),
        (ex, ts, count, ctx) =>
        {
            var logger = ctx["Logger"] as Microsoft.Extensions.Logging.ILogger;
            var fileName = ctx["FileName"] as string;
            //  logger?.LogWarning($"FileOpen попытка {count}: {ex.Message}");
            logger?.LogWarning($"Попытка {count}: ошибка открытия файла '{fileName}'. Повтор через {ts} сек.");
        }));

builder.Services.AddSingleton<IReadOnlyPolicyRegistry<string>>(registry);




builder.Services.AddSingleton<IRabbitMQPublisher, RabbitMQPublisher>();

builder.Services.AddHostedService<Worker>();
// do not stop the host in case of exceptions:
builder.Services.Configure<HostOptions>(hostOptions =>
{
    hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

var rabbitMqSection = builder.Configuration.GetSection("RabbitMq");
var XmlFilesSection = builder.Configuration.GetSection("XmlFiles");


builder.Services.Configure<RabbitMqConfig>(rabbitMqSection);
builder.Services.Configure<FileStorageConfig>(XmlFilesSection);


var app = builder.Build();


app.UseRouting();
app.UseStaticFiles();

//   CreateHostBuilder(args).Build().Run();

//private static IHostBuilder CreateHostBuilder(string[] args)
//{
//    return Host.CreateDefaultBuilder(args)
//        .ConfigureAppConfiguration((context, configBuilder) =>
//        {
//            //var env = context.HostingEnvironment.EnvironmentName;

//            //configBuilder.AddConfigurationFiles(env, args);
//        })
//        //.ConfigureSerilog()
//        .ConfigureWebHostDefaults(webBuilder =>
//        {
//            webBuilder.UseStartup<Startup>();
//        }).ConfigureServices(services =>
//        {
//            services.AddHostedService<Worker>();
//            // do not stop the host in case of exceptions:
//            services.Configure<HostOptions>(hostOptions =>
//            {
//                hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
//            });
//        });
//}

app.Run();