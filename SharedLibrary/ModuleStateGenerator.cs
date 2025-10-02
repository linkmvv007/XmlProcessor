using SharedLibrary.Xml;

namespace SharedLibrary;


public static class ModuleStateGenerator
{
    private static readonly Random _random = new();

    public static ModuleStateEnum GetRandomState()
    {
        var values = Enum.GetValues<ModuleStateEnum>();
        return values[_random.Next(values.Length)];
    }
}
