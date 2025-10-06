using SharedLibrary.Json;

namespace DataProcessorService.Db.Interfaces;

public interface IRepository
{
    Task InitializeDatabaseAsync(CancellationTokenSource cts);
    Task<bool> ProcessModuleAsync(ModuleInfoJson module, CancellationToken ct);
    Task<bool> ProcessModulesBatchAsync(IEnumerable<ModuleInfoJson> modules, CancellationToken ct);

}
