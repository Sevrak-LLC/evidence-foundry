using System.Reflection;
using System.Text.Json;

namespace EvidenceFoundry.Helpers;

public static class EmbeddedResourceLoader
{
    public static T LoadJsonResource<T>(
        Assembly assembly,
        string resourceName,
        JsonSerializerOptions options,
        string missingResourceMessage,
        string invalidResourceMessage)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(options);

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException(missingResourceMessage);

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var result = JsonSerializer.Deserialize<T>(json, options);
        if (result == null)
            throw new InvalidOperationException(invalidResourceMessage);

        return result;
    }
}
