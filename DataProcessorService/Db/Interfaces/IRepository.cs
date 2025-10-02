using SharedLibrary.Json;

namespace DataProcessorService.Db.Interfaces;

public interface IRepository
{
    void InitializeDatabase(CancellationTokenSource cts);
    Task<bool> ProcessModuleAsync(ModuleInfoJson module, CancellationToken ct);

}
