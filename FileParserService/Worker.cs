using System.Text.Json;
using System.Text.Json.Serialization;
using FileParserService.Xml;
using FileParserWebService.Interfaces;
using Microsoft.Extensions.Options;
using Polly;
using SharedLibrary;
using SharedLibrary.Configuration;
using SharedLibrary.Json;
using SharedLibrary.Xml;

namespace FileParserService;

public class Worker : IHostedService
{
    private const string TitleProgram = "FileParserService";
    private readonly ILogger<Worker> _logger;
    private readonly FileStorageConfig _fileStorageConfig;
    private readonly IHostEnvironment _env;
    private readonly IRabbitMqPublisher _publisher;
    private readonly IAsyncPolicy _rabbitPolicy;
    private readonly ISyncPolicy _fileOpenPolicy;
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="fileStorageConfig"></param>
    /// <param name="publisher"></param>
    /// <param name="env"></param>
    /// <param name="lifetime"></param>
    /// <param name="rabbitPolicy"></param>
    /// <param name="fileOpenPolicy"></param>
    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<FileStorageConfig> fileStorageConfig,
        IRabbitMqPublisher publisher,
        IHostEnvironment env,
        IHostApplicationLifetime lifetime,
        IAsyncPolicy rabbitPolicy,
        ISyncPolicy fileOpenPolicy
    )
    {
        _logger = logger;
        _rabbitPolicy = rabbitPolicy;
        _fileOpenPolicy = fileOpenPolicy;

        _fileStorageConfig = fileStorageConfig.CurrentValue;
        _env = env;

        _publisher = publisher;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"{TitleProgram} is starting");

        var executeTask = DoWorkAsync(cancellationToken);

        return executeTask.IsCompleted ? executeTask : Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"{TitleProgram} is stop");

        await Task.CompletedTask;
    }

    private async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation($"{TitleProgram}  work process is starting.");

        try
        {
            SetupFolders(out var folderPath, out var folderBadFilesPath);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
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
                            async (file, ct) => { await ProcessFile(folderBadFilesPath!, file, ct); }
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"1. Failed to DoWorkAsync()");
                    throw;
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation($"{TitleProgram} Cancellation Token  is raising & processing");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2. Failed to DoWorkAsync");
        }

        _logger.LogInformation($"{TitleProgram}  work process is stopping.");
    }

    private void SetupFolders(out string folderPath, out string? folderBadFilesPath)
    {
        (var isExistPaths, (folderPath, folderBadFilesPath)) = CheckDirectories();
        if (!isExistPaths)
        {
            _lifetime.StopApplication();
        }
    }

    private (bool isExistPaths, (string folderPath, string? folderBadFilesPath) value) CheckDirectories()
    {
        var folderPath = Path.Combine(_env.ContentRootPath, _fileStorageConfig.XmlFolder);

        if (!Directory.Exists(folderPath))
        {
            _logger.LogWarning("Directory for xml files '{folderPath}' not found", folderPath);

            return (isExistPaths: false, value: (folderPath, null));
        }

        _logger.LogInformation("XML-file folder: {folderPath}", folderPath);

        var folderBadFilesPath = Path.Combine(folderPath, _fileStorageConfig.ErrorFolder);
        if (!Directory.Exists(folderBadFilesPath))
        {
            Directory.CreateDirectory(folderBadFilesPath);
        }
        else
        {
            _logger.LogInformation("Error folder already exists: {folderBadFilesPath}", folderBadFilesPath);
        }

        return (isExistPaths: true, value: (folderPath, folderBadFilesPath));
    }

    private async Task ProcessFile(string folderBadFilesPath, string fileName, CancellationToken stoppingToken)
    {
        InstrumentStatus? modules = null;

        _logger.LogInformation("{fileName} processing ...", fileName);

        var ret = ReadXmlFile(fileName, ref modules);
        if (!ret.openedFile) // file open is error
            return;

        try
        {
            if (!ret.readedXml)
            {
                _logger.LogError("Bad xml file format '{fileName}'", fileName);

                File.Move(fileName, GetBadFileName(folderBadFilesPath, fileName));
                return;
            }

            InstrumentStatusJson? json = XmlToJson(modules!);
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
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }
            );

            var context = new Context
            {
                [PolicyRegistryConsts.Logger] = _logger
            };
            await _rabbitPolicy.ExecuteAsync(
                async (_) =>
                {
                    var success = await _publisher.SendMessageToRabbitMq(jsonData, stoppingToken);
                    if (!success)
                    {
                        throw new InvalidOperationException("Publish failed without exception");
                    }
                }, context);


            File.Delete(fileName);
            _logger.LogInformation("{fileName} processing finished", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ProcessFile: {fileName}, {folderBadFilesPat}",
                fileName, folderBadFilesPath);
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
            _logger.LogError(ex, $"Error convert to json");

            return null;
        }
    }

    private (bool openedFile, bool readedXml) ReadXmlFile(string fileName, ref InstrumentStatus? modules)
    {
        var openedFile = false;
        var readedXml = false;
        try
        {
            var context = new Context
            {
                [PolicyRegistryConsts.FileName] = fileName,
                [PolicyRegistryConsts.Logger] = _logger
            };
            using var stream = _fileOpenPolicy.Execute((_) => File.OpenRead(fileName), context);
            openedFile = true;

            modules = XmlHelper.Deserialize<InstrumentStatus>(stream);
            readedXml = modules is not null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read xml file '{fileName}'", fileName);
        }

        return (openedFile, readedXml);
    }
}