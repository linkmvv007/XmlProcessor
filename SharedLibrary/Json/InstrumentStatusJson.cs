using SharedLibrary.Xml;
using System.Text.Json.Serialization;

namespace SharedLibrary.Json;



public class InstrumentStatusJson
{
    public string SchemaVersion { get; set; }

    public string PackageID { get; set; }

    public List<DeviceStatusJson> DeviceStatuses { get; set; }
}


public class DeviceStatusJson
{
    public string ModuleCategoryID { get; set; } = string.Empty;

    public int IndexWithinRole { get; set; }

    public RapidControlStatusJson RapidControlStatus { get; set; }
}


[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CombinedOvenStatusJson), "CombinedOvenStatusJson")]
[JsonDerivedType(typeof(CombinedPumpStatusJson), "CombinedPumpStatusJson")]
[JsonDerivedType(typeof(CombinedSamplerStatusJson), "CombinedSamplerStatusJson")]
public abstract class RapidControlStatusJson
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModuleStateEnum ModuleState { get; set; }

    public bool IsBusy { get; set; }

    public bool IsReady { get; set; }

    public bool IsError { get; set; }

    public bool KeyLock { get; set; }
}

public class CombinedOvenStatusJson : RapidControlStatusJson
{
    public bool UseTemperatureControl { get; set; }
    public bool OvenOn { get; set; }

    public double Temperature_Actual { get; set; }

    public double Temperature_Room { get; set; }

    public double MaximumTemperatureLimit { get; set; }

    public int Valve_Position { get; set; }

    public int Valve_Rotations { get; set; }

    public bool Buzzer { get; set; }
}

public class CombinedPumpStatusJson : RapidControlStatusJson
{
    public string Mode { get; set; }

    public double Flow { get; set; }

    public double PercentB { get; set; }

    public double PercentC { get; set; }

    public double PercentD { get; set; }

    public double MinimumPressureLimit { get; set; }

    public double MaximumPressureLimit { get; set; }

    public double Pressure { get; set; }

    public bool PumpOn { get; set; }
    public int Channel { get; set; }
}

public class CombinedSamplerStatusJson : RapidControlStatusJson
{
    public int Status { get; set; }
    public string Vial { get; set; }
    public int Volume { get; set; }

    public int MaximumInjectionVolume { get; set; }

    public string RackL { get; set; }

    public string RackR { get; set; }

    public int RackInf { get; set; }

    public bool Buzzer { get; set; }
}