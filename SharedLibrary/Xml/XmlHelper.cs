using System.Xml.Serialization;

namespace FileParserService.Xml;

public static class XmlHelper
{
    public static T? Deserialize<T>(string xmlString)
        where T : class
    {
        if (string.IsNullOrEmpty(xmlString))
            return default(T);

        var serializer = new XmlSerializer(typeof(T));
        using var reader = new StringReader(xmlString);
        return (T?)serializer.Deserialize(reader);
    }

    public static T? Deserialize<T>(FileStream fs)
    where T : class
    {
        try
        {
            var serializer = new XmlSerializer(typeof(T));
            return (T?)serializer.Deserialize(fs);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
