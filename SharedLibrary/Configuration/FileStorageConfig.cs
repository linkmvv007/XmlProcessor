namespace SharedLibrary.Configuration;

public class FileStorageConfig
{
    public string ErrorFolder { get; set; }
    public string XmlFolder { get; set; }
    public int MaxThreadsCount { get; set; } = 5;
    public string Ext { get; set; } = "*.xml";
}
