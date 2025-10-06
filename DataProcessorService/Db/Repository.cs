using DataProcessorService.Db.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedLibrary.Configuration;
using SharedLibrary.Json;

namespace DataProcessorService.Db;

public class Repository : IRepository
{
    private readonly DatabaseConfig _databaseConfig;
    private readonly ILogger<Repository> _logger;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="databaseConfig"></param>
    public Repository(ILogger<Repository> logger, IOptions<DatabaseConfig> databaseConfig)
    {
        _logger = logger;

        _databaseConfig = databaseConfig.Value;
    }

    async Task IRepository.InitializeDatabaseAsync(CancellationTokenSource cts)
    {
        try
        {
            using var connection = new SqliteConnection(_databaseConfig.ConnectionString);
            connection.Open();

            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS Modules (
                    ModuleCategoryID TEXT PRIMARY KEY,
                    ModuleState TEXT NOT NULL
                )";

            using (var command = new SqliteCommand(createTableSql, connection))
            {
                await command.ExecuteNonQueryAsync();

                _logger.LogInformation("Database and table initialized.");
            }
            
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database and table is not initialized");
        }
        
        await cts.CancelAsync(); // exit
    }

    async Task<bool> IRepository.ProcessModuleAsync(ModuleInfoJson module, CancellationToken ct)
    {
        var sql =
            @"INSERT OR REPLACE INTO Modules (ModuleCategoryID, ModuleState) VALUES (@ModuleCategoryID, @ModuleState)";

        try
        {
            using var connection = new SqliteConnection(_databaseConfig.ConnectionString);
            await connection.OpenAsync(ct);

            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@ModuleCategoryID", module.ModuleCategoryID);
            command.Parameters.AddWithValue("@ModuleState", module.ModuleState);

            await command.ExecuteNonQueryAsync();

            _logger.LogInformation(
                "Updated/Inserted ModuleCategoryID: {ModuleCategoryID} with ModuleState: {ModuleState}",
                module.ModuleCategoryID, module.ModuleState);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"Update table is failed ModuleCategoryID = ({module.ModuleCategoryID}, ModuleState = {module.ModuleState})");
        }

        return false;
    }
    
    async Task<bool> IRepository.ProcessModulesBatchAsync(IEnumerable<ModuleInfoJson> modules, CancellationToken ct)
    {
        const string sql = @"INSERT OR REPLACE INTO Modules (ModuleCategoryID, ModuleState) VALUES (@ModuleCategoryID, @ModuleState)";

        try
        {
            using var connection = new  SqliteConnection(_databaseConfig.ConnectionString);
            await connection.OpenAsync(ct);

            using var transaction = connection.BeginTransaction();

            foreach (var module in modules)
            {
                using var command = new SqliteCommand(sql, connection, transaction);
                
                command.Parameters.AddWithValue("@ModuleCategoryID", module.ModuleCategoryID);
                command.Parameters.AddWithValue("@ModuleState", module.ModuleState);

                await command.ExecuteNonQueryAsync(ct);

                _logger.LogInformation(
                    "Updated/Inserted ModuleCategoryID: {ModuleCategoryID} with ModuleState: {ModuleState}",
                    module.ModuleCategoryID, module.ModuleState);
            }

            await transaction.CommitAsync(ct);
            
            return true; // success
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch update failed for Modules collection.");
            return false;
        }
    }
}