using System.Xml.Serialization;

namespace SharedLibrary.Xml;

/// <summary>
///  Класс для десериализации внутреннего XML для значения QUATPUMP в ModuleCategoryID
/// </summary>
[XmlRoot("CombinedPumpStatus")]
public class CombinedPumpStatus
{
    [XmlElement("ModuleState")]
    public ModuleStateEnum ModuleState { get; set; }

    [XmlElement("IsBusy")]
    public bool IsBusy { get; set; }

    [XmlElement("IsReady")]
    public bool IsReady { get; set; }

    [XmlElement("IsError")]
    public bool IsError { get; set; }

    [XmlElement("KeyLock")]
    public bool KeyLock { get; set; }

    [XmlElement("Mode")]
    public string? Mode { get; set; }

    [XmlElement("Flow")]
    public double Flow { get; set; }

    [XmlElement("PercentB")]
    public double PercentB { get; set; }

    [XmlElement("PercentC")]
    public double PercentC { get; set; }

    [XmlElement("PercentD")]
    public double PercentD { get; set; }

    [XmlElement("MinimumPressureLimit")]
    public double MinimumPressureLimit { get; set; }

    [XmlElement("MaximumPressureLimit")]
    public double MaximumPressureLimit { get; set; }

    [XmlElement("Pressure")]
    public double Pressure { get; set; }

    [XmlElement("PumpOn")]
    public bool PumpOn { get; set; }

    [XmlElement("Channel")]
    public int Channel { get; set; }
}