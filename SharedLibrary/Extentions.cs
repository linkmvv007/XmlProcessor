using FileParserService.Xml;
using SharedLibrary.Json;
using SharedLibrary.Xml;

namespace SharedLibrary;

public static class Extentions
{
    public static InstrumentStatusJson ToJson(this InstrumentStatus modules, bool generateRandomModuleState = false)
    {

        var json = new InstrumentStatusJson
        {
            PackageID = modules.PackageID,
            SchemaVersion = modules.SchemaVersion,
            DeviceStatuses = new List<DeviceStatusJson>()
        };

        foreach (var module in modules.DeviceStatuses)
        {
            switch (module.ModuleCategoryID)
            {
                case "SAMPLER":
                    {
                        var status = XmlHelper.Deserialize<CombinedSamplerStatus>(module.RapidControlStatus);

                        var data = new CombinedSamplerStatusJson()
                        {
                            Buzzer = status.Buzzer,
                            IsBusy = status.IsBusy,
                            IsError = status.IsError,
                            IsReady = status.IsReady,
                            KeyLock = status.KeyLock,
                            MaximumInjectionVolume = status.MaximumInjectionVolume,
                            ModuleState = generateRandomModuleState ? ModuleStateGenerator.GetRandomState() : status.ModuleState,
                            RackInf = status.RackInf,
                            RackL = status.RackL,
                            RackR = status.RackR,
                            Status = status.Status,
                            Vial = status.Vial,
                            Volume = status.Volume
                        };

                        var item = new DeviceStatusJson
                        {
                            IndexWithinRole = module.IndexWithinRole,
                            ModuleCategoryID = module.ModuleCategoryID,
                            RapidControlStatus = data
                        };

                        json.DeviceStatuses.Add(item);
                    }
                    break;

                case "QUATPUMP":
                    {
                        var status = XmlHelper.Deserialize<CombinedPumpStatus>(module.RapidControlStatus);

                        var data = new CombinedPumpStatusJson()
                        {
                            IsBusy = status.IsBusy,
                            IsError = status.IsError,
                            IsReady = status.IsReady,
                            KeyLock = status.KeyLock,
                            ModuleState = generateRandomModuleState ? ModuleStateGenerator.GetRandomState() : status.ModuleState,
                            Channel = status.Channel,
                            Flow = status.Flow,
                            MaximumPressureLimit = status.MaximumPressureLimit,
                            MinimumPressureLimit = status.MinimumPressureLimit,
                            Mode = status.Mode,
                            PercentB = status.PercentB,
                            PercentC = status.PercentC,
                            PercentD = status.PercentD,
                            Pressure = status.Pressure,
                            PumpOn = status.PumpOn

                        };

                        var item = new DeviceStatusJson
                        {
                            IndexWithinRole = module.IndexWithinRole,
                            ModuleCategoryID = module.ModuleCategoryID,
                            RapidControlStatus = data
                        };

                        json.DeviceStatuses.Add(item);
                    }
                    break;

                case "COLCOMP":
                    {
                        var status = XmlHelper.Deserialize<CombinedOvenStatus>(module.RapidControlStatus);

                        var data = new CombinedOvenStatusJson()
                        {
                            IsBusy = status.IsBusy,
                            IsError = status.IsError,
                            IsReady = status.IsReady,
                            KeyLock = status.KeyLock,
                            ModuleState = generateRandomModuleState ? ModuleStateGenerator.GetRandomState() : status.ModuleState,
                            Buzzer = status.Buzzer,
                            MaximumTemperatureLimit = status.MaximumTemperatureLimit,
                            OvenOn = status.OvenOn,
                            Temperature_Actual = status.Temperature_Actual,
                            Temperature_Room = status.Temperature_Room,
                            UseTemperatureControl = status.UseTemperatureControl,
                            Valve_Position = status.Valve_Position,
                            Valve_Rotations = status.Valve_Rotations,

                        };

                        var item = new DeviceStatusJson
                        {
                            IndexWithinRole = module.IndexWithinRole,
                            ModuleCategoryID = module.ModuleCategoryID,
                            RapidControlStatus = data
                        };

                        json.DeviceStatuses.Add(item);
                    }
                    break;

                default:
                    throw new ApplicationException($"Unknown ModuleCategoryID value: {module.ModuleCategoryID}");
            }
        }

        return json;
    }
}
