using FileParserService.Xml;
using FileParserWebService.Interfaces;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using SharedLibrary;
using SharedLibrary.Configuration;
using SharedLibrary.Json;
using SharedLibrary.Xml;
using System.Text.Json;

namespace FileParserService;

public class Worker : IHostedService
{
    private const string TitleProgram = "FileParserService";
    private readonly ILogger<Worker> _logger;
    private readonly FileStorageConfig _fileStorageConfig;
    private readonly IWebHostEnvironment _env;
    private readonly IRabbitMQPublisher _publisher;
    private readonly IAsyncPolicy _rabbitPolicy;
    private readonly ISyncPolicy _fileOpenPolicy;

    public Worker(
        IReadOnlyPolicyRegistry<string> registry,
        ILogger<Worker> logger,
        IOptions<FileStorageConfig> fileStorageConfig,
        IRabbitMQPublisher publisher,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _rabbitPolicy = registry.Get<IAsyncPolicy>("RabbitRetry");
        _fileOpenPolicy = registry.Get<ISyncPolicy>("FileOpenRetry");

        _fileStorageConfig = fileStorageConfig.Value;
        _env = env;

        _publisher = publisher;
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"{TitleProgram} is starting");

        _ = DoWorkAsync(cancellationToken);

        return Task.CompletedTask;
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {

        _logger.LogInformation($"{TitleProgram} is stop");

        await Task.CompletedTask;
    }

    protected async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{TitleProgram}  work process is starting.");


        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {


                try
                {
                    var folderPath = Path.Combine(_env.ContentRootPath, _fileStorageConfig.XmlFolder);
                    if (!Directory.Exists(folderPath))
                    {
                        _logger.LogWarning($"Directory '{folderPath}' not found");

                        await Task.Delay(10000);
                        continue;
                    }

                    var folderBadFilesPath = Path.Combine(folderPath, _fileStorageConfig.ErrorFolder);
                    if (!Directory.Exists(folderBadFilesPath))
                    {
                        Directory.CreateDirectory(folderBadFilesPath);
                    }


                    var xmlFiles = Directory.GetFiles(folderPath, _fileStorageConfig.Ext);
                    if (xmlFiles.Length > 0)
                    {
                        await Parallel.ForEachAsync(
                           xmlFiles,
                           new ParallelOptions
                           {
                               CancellationToken = stoppingToken,
                               MaxDegreeOfParallelism = _fileStorageConfig.MaxThreadsCount
                           },
                           async (file, ct) =>
                               {
                                   await ProcessFile(folderBadFilesPath, file, stoppingToken);
                               }
                       );
                    }

                }

                catch (Exception ex)
                {
                    _logger.LogError(ex, $"{ex.Message}");
                    throw;
                }

                await Task.Delay(1000);
            }

        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation($"{TitleProgram} Cancellation Token  is raising & processing");
        }

        _logger.LogInformation($"{TitleProgram}  work process is stopping.");
    }

    private async Task ProcessFile(string folderBadFilesPath, string fileName, CancellationToken stoppingToken)
    {
        InstrumentStatus? modules = null;

        if (ReadXmlFile(fileName, ref modules)) // file open is error
            return;

        try
        {
            if (modules is null) // xml is bad format
            {
                _logger.LogError($"Bad xml file format '{fileName}'");
                File.Move(fileName, GetBadFileName(folderBadFilesPath, fileName));
                return;
            }

            InstrumentStatusJson? json = XmlToJson(modules);
            if (json is null) // invalid values in xml
            {
                File.Move(fileName, GetBadFileName(folderBadFilesPath, fileName));
                return;
            }

            var jsonData = JsonSerializer.Serialize(
                json,
                json.GetType(),
                new JsonSerializerOptions
                {
                    IncludeFields = true
                }
                );

            var context = new Context
            {
                ["Logger"] = _logger
            };
            await _rabbitPolicy.ExecuteAsync((context) => _publisher.SendMessageToRabbitMQ(jsonData, stoppingToken), context);

            File.Delete(fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError($"{ex.Message}");
        }
    }

    private static string GetBadFileName(string folderBadFilesPath, string fileName) =>
         Path.Combine(folderBadFilesPath, $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}.xml");


    private InstrumentStatusJson? XmlToJson(InstrumentStatus modules)
    {
        try
        {
            return modules.ToJson(true);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error convert to json: {ex.Message}");

            return null;
        }
    }

    private bool ReadXmlFile(string fileName, ref InstrumentStatus? modules)
    {
        bool errorOpenFile = false;

        try
        {
            var context = new Context
            {
                ["FileName"] = fileName,
                ["Logger"] = _logger
            };
            using var stream = _fileOpenPolicy.Execute((context) => File.OpenRead(fileName), context);
            {
                modules = XmlHelper.Deserialize<InstrumentStatus>(stream);
            }
        }
        catch (Exception ex)
        {
            errorOpenFile = true;
            _logger.LogError($"Bad xml file format '{fileName}' :{ex.Message}");
        }

        return errorOpenFile;
    }


}