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
    public static OrganizationStructureCatalogData Catalog => CatalogResourceLoader.OrganizationStructureCatalog;
}
