using DataProcessorService.Db;
using DataProcessorService.Db.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SharedLibrary;
using SharedLibrary.Configuration;
using SharedLibrary.Json;
using SharedLibrary.Xml;
using System.Text.Json;
using System.Xml.Serialization;

namespace TestProject
{
    public class Tests
    {
        private ILogger<Repository> CreateMockLogger()
        {
            var mockLogger = new Mock<ILogger<Repository>>();

            mockLogger
                .Setup(x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)))
                .Callback((LogLevel level, EventId eventId, object state, Exception exception, Delegate formatter) =>
                {
                    var message = formatter.DynamicInvoke(state, exception);

                    Console.WriteLine($"[Mocked Log] {level}: {message}");
                });


            return mockLogger.Object;
        }
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void ParseXmlTest()
        {

            InstrumentStatus modules;
            using (var stream = File.OpenRead(Path.Combine("./Data/status.xml")))
            {
                var serializer = new XmlSerializer(typeof(InstrumentStatus));
                modules = (InstrumentStatus)serializer.Deserialize(stream);
            }


            Assert.IsTrue(modules.DeviceStatuses.Count == 3);

            InstrumentStatusJson json = modules.ToJson(true);

            var options = new JsonSerializerOptions
            {
                IncludeFields = true
            };

            var jsonData = System.Text.Json.JsonSerializer.Serialize(json, json.GetType(), options);
            Console.WriteLine(jsonData);

            var dt = System.Text.Json.JsonSerializer.Deserialize<InstrumentStatusJson>(jsonData);
        }

        [Test]
        public async Task CreateDbSqlite()
        {
            var currentPath = Directory.GetCurrentDirectory();
            string fileName = Path.Combine(currentPath, "testSqlite.db");

            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            var config = new DatabaseConfig()
            {
                ConnectionString = $"Data Source={fileName}"
            };

            IOptions<DatabaseConfig> dbConfig = Options.Create<DatabaseConfig>(config);
            IRepository db = new Repository(
                CreateMockLogger(),
                dbConfig
                );

            var cts = new CancellationTokenSource();
            db.InitializeDatabase(cts);

            await db.ProcessModuleAsync(
                new ModuleInfoJson
                {
                    ModuleCategoryID = "testID",
                    ModuleState = ModuleStateEnum.Offline.ToString(),
                },
                new CancellationToken()
                );
        }

    }
}