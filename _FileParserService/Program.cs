using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SharedLibrary.Configuration;

namespace FileParserService;

public class Program
{
    public static void Main(string[] args)
    {

        //// serilog:
        //Log.Logger = new LoggerConfiguration()
        //.ReadFrom.Configuration(
        //    new ConfigurationBuilder()
        //    .AddJsonFile("serilog.json")
        //    .Build()
        //    )
        //.CreateLogger();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("serilog.json")
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                var rabbitMqSection = hostContext.Configuration.GetSection("RabbitMq");
                var XmlFilesSection = hostContext.Configuration.GetSection("XmlFiles");


                services.Configure<RabbitMqConfig>(rabbitMqSection);
                services.Configure<FileStorageConfig>(XmlFilesSection);


                services.AddHostedService<Worker>();
            });
}