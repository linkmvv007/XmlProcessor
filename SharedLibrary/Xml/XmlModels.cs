using System.Xml.Serialization;

namespace SharedLibrary.Xml;

public enum ModuleStateEnum { Online, Run, NotReady, Offline }


[XmlRoot("InstrumentStatus")]
public class InstrumentStatus
{
    [XmlAttribute("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    [XmlElement("PackageID")]
    public string PackageID { get; set; } = string.Empty;

    [XmlElement("DeviceStatus")]
    public List<DeviceStatus> DeviceStatuses { get; set; } = new List<DeviceStatus>();
}

public class DeviceStatus
{
    [XmlElement("ModuleCategoryID")]
    public string ModuleCategoryID { get; set; } = string.Empty;

    [XmlElement("IndexWithinRole")]
    public int IndexWithinRole { get; set; }

    [XmlElement("RapidControlStatus")]
    public string RapidControlStatus { get; set; } = string.Empty; // XML как строка
}



