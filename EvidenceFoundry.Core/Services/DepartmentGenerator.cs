using System.Text.Json;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public static class DepartmentGenerator
{

    private static OrganizationStructureCatalogData.OrganizationTypeStructure? GetOrganizationTypeStructure(
        Industry industry,
        OrganizationType organizationType)
    {
        var config = OrganizationStructureCatalog.Catalog;
        if (!config.Industries.TryGetValue(industry, out var industryConfig))
            config.Industries.TryGetValue(Industry.Other, out industryConfig);

        if (industryConfig == null || industryConfig.OrganizationTypes.Count == 0)
            return null;

        if (industryConfig.OrganizationTypes.TryGetValue(organizationType, out var orgConfig))
            return orgConfig;

        return industryConfig.OrganizationTypes.Values.FirstOrDefault();
    }

    internal static IReadOnlyList<DepartmentName> GetAllowedDepartments(
        Industry industry,
        OrganizationType organizationType)
    {
        var orgConfig = GetOrganizationTypeStructure(industry, organizationType);
        if (orgConfig == null)
            return Array.Empty<DepartmentName>();

        return orgConfig.Departments.Keys.ToList();
    }

    internal static IReadOnlyList<RoleName> GetAllowedRoles(
        Industry industry,
        OrganizationType organizationType,
        DepartmentName department)
    {
        var orgConfig = GetOrganizationTypeStructure(industry, organizationType);
        if (orgConfig == null)
            return Array.Empty<RoleName>();

        return orgConfig.Departments.TryGetValue(department, out var roles)
            ? roles
            : Array.Empty<RoleName>();
    }

    internal static void ApplyDepartmentRoleConstraints(Organization organization)
    {
        var allowedDepartments = GetAllowedDepartments(organization.Industry, organization.OrganizationType);
        var allowedDepartmentSet = new HashSet<DepartmentName>(allowedDepartments);

        organization.SetDepartments(organization.Departments
            .Where(d => allowedDepartmentSet.Contains(d.Name))
            .ToList());

        foreach (var department in organization.Departments)
        {
            var allowedRoles = GetAllowedRoles(organization.Industry, organization.OrganizationType, department.Name);
            if (allowedRoles.Count == 0)
            {
                department.ClearRoles();
                continue;
            }

            var allowedRoleSet = new HashSet<RoleName>(allowedRoles);
            department.SetRoles(department.Roles
                .Where(r => allowedRoleSet.Contains(r.Name))
                .ToList());
        }
    }

    internal static string BuildAllowedDepartmentsJson(Industry industry, OrganizationType organizationType)
    {
        var departments = GetAllowedDepartments(industry, organizationType)
            .Select(d => $"{d} ({EnumHelper.HumanizeEnumName(d.ToString())})")
            .ToArray();

        return JsonSerializer.Serialize(departments, JsonSerializationDefaults.Indented);
    }

    internal static string BuildAllowedDepartmentRoleMapJson(Industry industry, OrganizationType organizationType)
    {
        var departments = GetAllowedDepartments(industry, organizationType);
        var map = departments.ToDictionary(
            d => $"{d} ({EnumHelper.HumanizeEnumName(d.ToString())})",
            d => GetAllowedRoles(industry, organizationType, d)
                .Select(r => $"{r} ({EnumHelper.HumanizeEnumName(r.ToString())})")
                .ToArray());

        return JsonSerializer.Serialize(map, JsonSerializationDefaults.Indented);
    }

    internal static string BuildIndustryOrganizationRoleCatalogJson(IEnumerable<Industry> industries)
    {
        var catalog = OrganizationStructureCatalog.Catalog;
        var filteredIndustries = new Dictionary<Industry, OrganizationStructureCatalogData.IndustryStructure>();

        foreach (var industry in industries.Distinct())
        {
            if (catalog.Industries.TryGetValue(industry, out var industryStructure))
            {
                filteredIndustries[industry] = industryStructure;
            }
        }

        var filteredCatalog = new OrganizationStructureCatalogData
        {
            Industries = filteredIndustries
        };

        return JsonSerializer.Serialize(filteredCatalog, JsonSerializationDefaults.IndentedCamelCaseWithEnums);
    }
}
