using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceFoundry.Services;

public static class RoleGenerator
{
    private static ILogger GetLogger(ILogger? logger)
        => logger ?? NullLogger.Instance;

    internal static readonly HashSet<RoleName> SingleOccupantRoles = new()
    {
        RoleName.ChiefExecutiveOfficer,
        RoleName.ChiefOperatingOfficer,
        RoleName.ChiefFinancialOfficer,
        RoleName.ChiefTechnologyOfficer,
        RoleName.ChiefInformationOfficer,
        RoleName.ChiefSecurityOfficer,
        RoleName.ChiefMarketingOfficer,
        RoleName.ChiefSalesOfficer,
        RoleName.ChiefHumanResourcesOfficer,
        RoleName.ChiefComplianceOfficer,
        RoleName.ChiefProductOfficer,
        RoleName.ExecutiveDirector
    };

    internal static void EnsureSingleOccupantRolesInExecutive(
        Organization organization,
        ILogger? logger = null)
    {
        var log = GetLogger(logger);
        log.LogDebug("Ensuring single-occupant roles are in the executive department.");

        if (organization.Departments.Count == 0)
            return;

        var executive = GetOrCreateExecutiveDepartment(organization);
        var executiveRoles = executive.Roles.ToDictionary(r => r.Name, r => r);
        var executiveCharacterIds = new HashSet<Guid>(
            executive.Roles.SelectMany(r => r.Characters).Select(c => c.Id));

        foreach (var department in organization.Departments.Where(d => d.Name != DepartmentName.Executive))
        {
            MoveSingleOccupantRoles(
                department,
                executive,
                executiveRoles,
                executiveCharacterIds,
                organization.Id);
        }
    }

    private static Department GetOrCreateExecutiveDepartment(Organization organization)
    {
        var executive = organization.Departments.FirstOrDefault(d => d.Name == DepartmentName.Executive);
        if (executive != null)
            return executive;

        executive = new Department { Name = DepartmentName.Executive };
        organization.InsertDepartment(0, executive);
        return executive;
    }

    private static void MoveSingleOccupantRoles(
        Department department,
        Department executive,
        Dictionary<RoleName, Role> executiveRoles,
        ISet<Guid> executiveCharacterIds,
        Guid organizationId)
    {
        var toMove = department.Roles
            .Where(r => SingleOccupantRoles.Contains(r.Name))
            .ToList();

        foreach (var role in toMove)
        {
            department.RemoveRole(role);
            if (executiveRoles.TryGetValue(role.Name, out var target))
            {
                MoveCharactersToRole(role, target, executive, executiveCharacterIds, organizationId);
                continue;
            }

            AttachRoleToExecutive(role, executive, executiveRoles, executiveCharacterIds, organizationId);
        }
    }

    private static void MoveCharactersToRole(
        Role sourceRole,
        Role targetRole,
        Department executive,
        ISet<Guid> executiveCharacterIds,
        Guid organizationId)
    {
        foreach (var character in sourceRole.Characters)
        {
            if (!executiveCharacterIds.Add(character.Id))
                continue;

            character.RoleId = targetRole.Id;
            character.DepartmentId = executive.Id;
            character.OrganizationId = organizationId;
            targetRole.AddCharacter(character);
        }
    }

    private static void AttachRoleToExecutive(
        Role role,
        Department executive,
        Dictionary<RoleName, Role> executiveRoles,
        ISet<Guid> executiveCharacterIds,
        Guid organizationId)
    {
        role.DepartmentId = executive.Id;
        role.OrganizationId = organizationId;

        foreach (var character in role.Characters)
        {
            character.RoleId = role.Id;
            character.DepartmentId = executive.Id;
            character.OrganizationId = organizationId;
            executiveCharacterIds.Add(character.Id);
        }

        executive.AddRole(role);
        executiveRoles[role.Name] = role;
    }

    internal static (Role Role, Department Department) SelectRoleAssignment(
        RoleName roleName,
        List<(Role Role, Department Department)> roleAssignments)
    {
        if (roleAssignments.Count == 1)
            return roleAssignments[0];

        if (SingleOccupantRoles.Contains(roleName))
        {
            var executive = roleAssignments.FirstOrDefault(r => r.Department.Name == DepartmentName.Executive);
            if (executive.Role != null)
                return executive;
        }

        return roleAssignments
            .OrderBy(r => r.Department.Name)
            .First();
    }

    internal static string BuildRoleDepartmentLegend(Organization organization)
    {
        var lines = new List<string>();

        foreach (var department in organization.Departments)
        {
            var deptRaw = department.Name.ToString();
            var deptHuman = EnumHelper.HumanizeEnumName(deptRaw);
            lines.Add($"{deptRaw} -> {deptHuman}");

            foreach (var role in department.Roles)
            {
                var roleRaw = role.Name.ToString();
                var roleHuman = EnumHelper.HumanizeEnumName(roleRaw);
                lines.Add($"  {roleRaw} -> {roleHuman}");
            }
        }

        return lines.Count == 0 ? "(none)" : string.Join("\n", lines);
    }
}
