using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

internal static class CatalogResourceLoader
{
    private const string CaseIssueCatalogResourceName = "EvidenceFoundry.Resources.CaseIssueCatalog.json";
    private const string OrganizationStructureCatalogResourceName =
        "EvidenceFoundry.Resources.OrganizationStructureCatalog.json";

    private static readonly Lazy<CaseIssueCatalog.CaseIssueCatalogConfig> CaseIssueCatalogLazy =
        new(LoadCaseIssueCatalog);
    private static readonly Lazy<OrganizationStructureCatalogData> OrganizationStructureCatalogLazy =
        new(LoadOrganizationStructureCatalog);

    internal static CaseIssueCatalog.CaseIssueCatalogConfig CaseIssueCatalog => CaseIssueCatalogLazy.Value;
    internal static OrganizationStructureCatalogData OrganizationStructureCatalog =>
        OrganizationStructureCatalogLazy.Value;

    private static CaseIssueCatalog.CaseIssueCatalogConfig LoadCaseIssueCatalog()
    {
        var assembly = typeof(CatalogResourceLoader).Assembly;
        return EmbeddedResourceLoader.LoadJsonResource<CaseIssueCatalog.CaseIssueCatalogConfig>(
            assembly,
            CaseIssueCatalogResourceName,
            JsonSerializationDefaults.CaseInsensitive,
            $"Missing case issue catalog resource '{CaseIssueCatalogResourceName}'.",
            "Case issue catalog config is empty or invalid.");
    }

    private static OrganizationStructureCatalogData LoadOrganizationStructureCatalog()
    {
        var assembly = typeof(CatalogResourceLoader).Assembly;
        return EmbeddedResourceLoader.LoadJsonResource<OrganizationStructureCatalogData>(
            assembly,
            OrganizationStructureCatalogResourceName,
            JsonSerializationDefaults.CaseInsensitiveWithEnums,
            $"Missing organization structure config resource '{OrganizationStructureCatalogResourceName}'.",
            "Organization structure config is empty or invalid.");
    }
}
