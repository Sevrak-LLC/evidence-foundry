using System.Text.Json;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceFoundry.Services;

public static class DepartmentGenerator
{
    private static ILogger GetLogger(ILogger? logger)
        => logger ?? NullLogger.Instance;

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
        OrganizationType organizationType,
        ILogger? logger = null)
    {
        var log = GetLogger(logger);
        log.LogDebug(
            "Resolving allowed departments for {Industry} / {OrganizationType}.",
            industry,
            organizationType);

        var orgConfig = GetOrganizationTypeStructure(industry, organizationType);
        if (orgConfig == null)
            return Array.Empty<DepartmentName>();

        return orgConfig.Departments.Keys.ToList();
    }

    internal static IReadOnlyList<RoleName> GetAllowedRoles(
        Industry industry,
        OrganizationType organizationType,
        DepartmentName department,
        ILogger? logger = null)
    {
        var log = GetLogger(logger);
        log.LogDebug(
            "Resolving allowed roles for {Industry} / {OrganizationType} / {Department}.",
            industry,
            organizationType,
            department);

        var orgConfig = GetOrganizationTypeStructure(industry, organizationType);
        if (orgConfig == null)
            return Array.Empty<RoleName>();

        return orgConfig.Departments.TryGetValue(department, out var roles)
            ? roles
            : Array.Empty<RoleName>();
    }

    internal static void ApplyDepartmentRoleConstraints(
        Organization organization,
        ILogger? logger = null)
    {
        var log = GetLogger(logger);
        log.LogDebug("Applying department role constraints.");

        var allowedDepartments = GetAllowedDepartments(
            organization.Industry,
            organization.OrganizationType,
            log);
        var allowedDepartmentSet = new HashSet<DepartmentName>(allowedDepartments);

        organization.SetDepartments(organization.Departments
            .Where(d => allowedDepartmentSet.Contains(d.Name))
            .ToList());

        foreach (var department in organization.Departments)
        {
            var allowedRoles = GetAllowedRoles(
                organization.Industry,
                organization.OrganizationType,
                department.Name,
                log);
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

    internal static string BuildAllowedDepartmentsJson(
        Industry industry,
        OrganizationType organizationType,
        ILogger? logger = null)
    {
        var log = GetLogger(logger);
        log.LogDebug(
            "Building allowed departments JSON for {Industry} / {OrganizationType}.",
            industry,
            organizationType);

        var departments = GetAllowedDepartments(industry, organizationType, log)
            .Select(d => $"{d} ({EnumHelper.HumanizeEnumName(d.ToString())})")
            .ToArray();

        return JsonSerializer.Serialize(departments, JsonSerializationDefaults.Indented);
    }

    internal static string BuildAllowedDepartmentRoleMapJson(
        Industry industry,
        OrganizationType organizationType,
        ILogger? logger = null)
    {
        var log = GetLogger(logger);
        log.LogDebug(
            "Building allowed department role map JSON for {Industry} / {OrganizationType}.",
            industry,
            organizationType);

        var departments = GetAllowedDepartments(industry, organizationType, log);
        var map = departments.ToDictionary(
            d => $"{d} ({EnumHelper.HumanizeEnumName(d.ToString())})",
            d => GetAllowedRoles(industry, organizationType, d, log)
                .Select(r => $"{r} ({EnumHelper.HumanizeEnumName(r.ToString())})")
                .ToArray());

        return JsonSerializer.Serialize(map, JsonSerializationDefaults.Indented);
    }

    internal static string BuildIndustryOrganizationRoleCatalogJson(
        IEnumerable<Industry> industries,
        ILogger? logger = null)
    {
        var log = GetLogger(logger);
        log.LogDebug("Building industry organization role catalog JSON.");

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
