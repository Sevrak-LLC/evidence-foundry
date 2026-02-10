using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvidenceFoundry.Models;

public sealed class OrganizationStructureCatalogData
{
    public Dictionary<Industry, IndustryStructure> Industries { get; init; } = new();

    public sealed class IndustryStructure
    {
        public Dictionary<OrganizationType, OrganizationTypeStructure> OrganizationTypes { get; init; } = new();
    }

    public sealed class OrganizationTypeStructure
    {
        public Dictionary<DepartmentName, List<RoleName>> Departments { get; init; } = new();
    }
}

public static class OrganizationStructureCatalog
{
    private const string ResourceName = "EvidenceFoundry.Resources.OrganizationStructureCatalog.json";

    private static readonly Lazy<OrganizationStructureCatalogData> CatalogLazy = new(LoadConfig);

    public static OrganizationStructureCatalogData Catalog => CatalogLazy.Value;

    private static OrganizationStructureCatalogData LoadConfig()
    {
        var assembly = typeof(OrganizationStructureCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            throw new InvalidOperationException($"Missing organization structure config resource '{ResourceName}'.");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var catalog = JsonSerializer.Deserialize<OrganizationStructureCatalogData>(json, options);
        if (catalog == null)
            throw new InvalidOperationException("Organization structure config is empty or invalid.");

        return catalog;
    }
}
