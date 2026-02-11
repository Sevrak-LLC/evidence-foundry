using EvidenceFoundry.Helpers;

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
        return EmbeddedResourceLoader.LoadJsonResource<OrganizationStructureCatalogData>(
            assembly,
            ResourceName,
            JsonSerializationDefaults.CaseInsensitiveWithEnums,
            $"Missing organization structure config resource '{ResourceName}'.",
            "Organization structure config is empty or invalid.");
    }
}
