using DataProcessorService.Db.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Configuration;
using SharedLibrary.Json;

namespace DataProcessorService.Db;

public class Repository : IRepository
{
    private DatabaseConfig _databaseConfig;
    private ILogger<Repository> _logger;

    public Repository(ILogger<Repository> logger, IOptions<DatabaseConfig> databaseConfig)
    {
        _logger = logger;

        _databaseConfig = databaseConfig.Value;
    }

    void IRepository.InitializeDatabase(CancellationTokenSource cts)
    {
        try
        {
            using var connection = new SqliteConnection(_databaseConfig.ConnectionString);
            connection.Open();

            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS Modules (
                    ModuleCategoryID TEXT PRIMARY KEY,
                    ModuleState TEXT NOT NULL
                )";

            using (var command = new SqliteCommand(createTableSql, connection))
            {
                command.ExecuteNonQuery();

                _logger.LogInformation("Database and table initialized.");
            }

            return;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Database and table is not initialized: {ex.Message}");

            cts.Cancel(); // exit
        }

    }

    async Task<bool> IRepository.ProcessModuleAsync(ModuleInfoJson module, CancellationToken ct)
    {
        var sql = @"
                INSERT OR REPLACE INTO Modules (ModuleCategoryID, ModuleState)
                VALUES (@ModuleCategoryID, @ModuleState)";

        try
        {
            using var connection = new SqliteConnection(_databaseConfig.ConnectionString);
            await connection.OpenAsync(ct);

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@ModuleCategoryID", module.ModuleCategoryID);
            command.Parameters.AddWithValue("@ModuleState", module.ModuleState);

            await command.ExecuteNonQueryAsync();

            _logger.LogInformation("Updated/Inserted ModuleCategoryID: {ModuleCategoryID} with ModuleState: {ModuleState}", module.ModuleCategoryID, module.ModuleState);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Update table is failed ModuleCategoryID = ({module.ModuleCategoryID}, ModuleState = {module.ModuleState}): {ex.Message}");
        }

        return true;
    }
}


