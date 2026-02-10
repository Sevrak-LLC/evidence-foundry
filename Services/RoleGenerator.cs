using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class RoleGenerator
{
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

    internal static void EnsureSingleOccupantRolesInExecutive(Organization organization)
    {
        if (organization.Departments.Count == 0)
            return;

        var executive = organization.Departments.FirstOrDefault(d => d.Name == DepartmentName.Executive);
        if (executive == null)
        {
            executive = new Department { Name = DepartmentName.Executive };
            organization.Departments.Insert(0, executive);
        }

        var executiveRoles = executive.Roles.ToDictionary(r => r.Name, r => r);
        var executiveCharacterIds = new HashSet<Guid>(
            executive.Roles.SelectMany(r => r.Characters).Select(c => c.Id));

        foreach (var department in organization.Departments.Where(d => d.Name != DepartmentName.Executive))
        {
            var toMove = department.Roles
                .Where(r => SingleOccupantRoles.Contains(r.Name))
                .ToList();

            foreach (var role in toMove)
            {
                department.Roles.Remove(role);
                if (executiveRoles.TryGetValue(role.Name, out var target))
                {
                    foreach (var character in role.Characters)
                    {
                        if (executiveCharacterIds.Add(character.Id))
                        {
                            character.RoleId = target.Id;
                            character.DepartmentId = executive.Id;
                            character.OrganizationId = organization.Id;
                            target.Characters.Add(character);
                        }
                    }
                }
                else
                {
                    role.DepartmentId = executive.Id;
                    role.OrganizationId = organization.Id;
                    foreach (var character in role.Characters)
                    {
                        character.RoleId = role.Id;
                        character.DepartmentId = executive.Id;
                        character.OrganizationId = organization.Id;
                    }
                    executive.Roles.Add(role);
                    executiveRoles[role.Name] = role;
                    foreach (var character in role.Characters)
                        executiveCharacterIds.Add(character.Id);
                }
            }
        }
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
