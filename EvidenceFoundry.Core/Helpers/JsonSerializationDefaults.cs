using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvidenceFoundry.Helpers;

public static class JsonSerializationDefaults
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions IndentedCamelCaseWithEnums = CreateIndentedCamelCaseWithEnums();

    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions CaseInsensitiveWithEnums = CreateCaseInsensitiveWithEnums();

    private static JsonSerializerOptions CreateIndentedCamelCaseWithEnums()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    private static JsonSerializerOptions CreateCaseInsensitiveWithEnums()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
