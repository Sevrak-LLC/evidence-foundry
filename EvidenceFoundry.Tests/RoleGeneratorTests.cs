using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class RoleGeneratorTests
{
    [Fact]
    public void EnsureSingleOccupantRolesInExecutive_MovesRolesToExecutive()
    {
        var organization = new Organization { Name = "Acme" };
        var finance = new Department { Name = DepartmentName.Finance };
        finance.AddRole(new Role { Name = RoleName.ChiefFinancialOfficer });
        organization.AddDepartment(finance);

        RoleGenerator.EnsureSingleOccupantRolesInExecutive(organization);

        var executive = organization.Departments.First(d => d.Name == DepartmentName.Executive);
        Assert.Contains(executive.Roles, r => r.Name == RoleName.ChiefFinancialOfficer);
        Assert.DoesNotContain(finance.Roles, r => r.Name == RoleName.ChiefFinancialOfficer);
    }

    [Fact]
    public void EnsureSingleOccupantRolesInExecutive_PreservesCharacters()
    {
        var organization = new Organization { Name = "Acme" };
        var finance = new Department { Name = DepartmentName.Finance };
        var cfoRole = new Role { Name = RoleName.ChiefFinancialOfficer };
        var character = new Character { FirstName = "Alex", LastName = "Park", Email = "alex@acme.com" };
        cfoRole.AddCharacter(character);
        finance.AddRole(cfoRole);
        organization.AddDepartment(finance);

        RoleGenerator.EnsureSingleOccupantRolesInExecutive(organization);

        var executive = organization.Departments.First(d => d.Name == DepartmentName.Executive);
        var execRole = executive.Roles.First(r => r.Name == RoleName.ChiefFinancialOfficer);
        Assert.Contains(execRole.Characters, c => c.Id == character.Id);
    }
}
