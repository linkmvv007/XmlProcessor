namespace SharedLibrary.Configuration;

public class FileStorageConfig
{
    public required string ErrorFolder { get; set; }
    public required string XmlFolder { get; set; }
    public int MaxThreadsCount { get; set; } = 5;
    public required string Ext { get; set; }
}
