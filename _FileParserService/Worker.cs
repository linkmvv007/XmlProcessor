using FileParserService.Xml;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Configuration;
using SharedLibrary.Xml;
using System.Xml.Serialization;

namespace FileParserService;

public class Worker : IHostedService
{
    private readonly ILogger<Worker> _logger;
    private readonly RabbitMqConfig _rabbitMqConfig;
    private readonly FileStorageConfig _fileStorageConfig;

    public Worker(
        ILogger<Worker> logger,
        IOptions<RabbitMqConfig> rabbitMqOptions,
        FileStorageConfig fileStorageConfig)
    {
        _logger = logger;
        _rabbitMqConfig = rabbitMqOptions.Value;
        _fileStorageConfig = fileStorageConfig;
    }


    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FileParserServiceis starting");

        await DoWorkAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FileParserServiceis stop");
    }

    protected async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FileParserServicework process is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var folderPath = _fileStorageConfig.XmlFolder;
                var xmlFiles = Directory.GetFiles(folderPath, "*.xml");

                Parallel.ForEach(
                    xmlFiles,
                    new ParallelOptions
                    {
                        CancellationToken = stoppingToken,
                        MaxDegreeOfParallelism = _fileStorageConfig.MaxThreadsCount
                    },
                    async file =>
                        {

                            await ProcessFile(file);
                        }
                );


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{ex.Message}");
                throw;
            }

        }


        _logger.LogInformation("FileParserServiceis stopping.");
    }

    private async Task ProcessFile(string fileName)
    {


        InstrumentStatus modules;
        using (var stream = File.OpenRead(Path.Combine(_fileStorageConfig.XmlFolder, fileName)))
        {
            var serializer = new XmlSerializer(typeof(InstrumentStatus));
            modules = (InstrumentStatus)serializer.Deserialize(stream);
        }

        foreach (var module in modules.DeviceStatuses)
        {
            switch (module.ModuleCategoryID)
            {
                case "SAMPLER":
                    {
                        var status = XmlHelper.Deserialize<CombinedSamplerStatus>(module.RapidControlStatus);

                        // status.ModuleState;

                    }
                    break;

                case "QUATPUMP":
                    {
                        var status = XmlHelper.Deserialize<CombinedPumpStatus>(module.RapidControlStatus);

                    }
                    break;

                case "COLCOMP":
                    {
                        var status = XmlHelper.Deserialize<CombinedOvenStatus>(module.RapidControlStatus);

                    }
                    break;

                default:
                    break;
            }
        }

        //throw new NotImplementedException();
    }
}