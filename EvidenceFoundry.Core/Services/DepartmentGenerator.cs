using System.Text.Json;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using Serilog;

namespace EvidenceFoundry.Services;

public static partial class DepartmentGenerator
{
    private static ILogger GetLogger(ILogger? logger)
        => logger ?? Serilog.Log.Logger;

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
        Log.ResolvingAllowedDepartments(log, industry, organizationType);

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
        Log.ResolvingAllowedRoles(log, industry, organizationType, department);

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
        Log.ApplyingDepartmentRoleConstraints(log);

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
        Log.BuildingAllowedDepartmentsJson(log, industry, organizationType);

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
        Log.BuildingAllowedDepartmentRoleMapJson(log, industry, organizationType);

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
        Log.BuildingIndustryOrganizationRoleCatalogJson(log);

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

    private static class Log
    {
        public static void ResolvingAllowedDepartments(ILogger logger, Industry industry, OrganizationType organizationType)
            => logger.Debug("Resolving allowed departments for {Industry} / {OrganizationType}.", industry, organizationType);

        public static void ResolvingAllowedRoles(
            ILogger logger,
            Industry industry,
            OrganizationType organizationType,
            DepartmentName department)
            => logger.Debug(
                "Resolving allowed roles for {Industry} / {OrganizationType} / {Department}.",
                industry,
                organizationType,
                department);

        public static void ApplyingDepartmentRoleConstraints(ILogger logger)
            => logger.Debug("Applying department role constraints.");

        public static void BuildingAllowedDepartmentsJson(ILogger logger, Industry industry, OrganizationType organizationType)
            => logger.Debug(
                "Building allowed departments JSON for {Industry} / {OrganizationType}.",
                industry,
                organizationType);

        public static void BuildingAllowedDepartmentRoleMapJson(
            ILogger logger,
            Industry industry,
            OrganizationType organizationType)
            => logger.Debug(
                "Building allowed department role map JSON for {Industry} / {OrganizationType}.",
                industry,
                organizationType);

        public static void BuildingIndustryOrganizationRoleCatalogJson(ILogger logger)
            => logger.Debug("Building industry organization role catalog JSON.");
    }
}
