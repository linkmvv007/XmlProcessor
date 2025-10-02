using DataProcessorService.Db;
using DataProcessorService.Db.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SharedLibrary.Configuration;


namespace DataProcessorService;

public class Program
{
    public static void Main(string[] args)
    {
        // serilog:
        Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(
            new ConfigurationBuilder()
            .AddJsonFile("serilog.json")
            .Build()
            )
        .CreateLogger();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddSerilog();


        var rabbitMqSection = builder.Configuration.GetSection("RabbitMq");
        var databaseSection = builder.Configuration.GetSection("Database");

        builder.Services.Configure<RabbitMqConfigConsumer>(rabbitMqSection);
        builder.Services.Configure<DatabaseConfig>(databaseSection);

        builder.Services.AddSingleton<IRepository, Repository>();

        builder.Services.AddHostedService<Worker>();

        builder.Build().Run();
    }
}