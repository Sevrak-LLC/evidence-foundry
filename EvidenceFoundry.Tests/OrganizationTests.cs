using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class OrganizationTests
{
    [Fact]
    public void EnumerateCharactersReturnsAllAssignments()
    {
        var org = new Organization { Name = "Ardent Ridge" };
        var legal = new Department { Name = DepartmentName.Legal };
        var gc = new Role { Name = RoleName.GeneralCounsel };
        var counsel = new Role { Name = RoleName.LegalCounsel };

        var alex = new Character { FirstName = "Alex", LastName = "Smith", Email = "alex@ardent.com" };
        var jamie = new Character { FirstName = "Jamie", LastName = "Lee", Email = "jamie@ardent.com" };

        gc.AddCharacter(alex);
        counsel.AddCharacter(jamie);
        legal.AddRole(gc);
        legal.AddRole(counsel);
        org.AddDepartment(legal);

        var assignments = org.EnumerateCharacters().ToList();

        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, a => a.Character == alex && a.Role.Name == RoleName.GeneralCounsel && a.Department.Name == DepartmentName.Legal && a.Organization == org);
        Assert.Contains(assignments, a => a.Character == jamie && a.Role.Name == RoleName.LegalCounsel && a.Department.Name == DepartmentName.Legal && a.Organization == org);
    }

    [Fact]
    public void EnumerateCharactersNoDepartmentsReturnsEmpty()
    {
        var org = new Organization { Name = "Solo Corp" };

        var assignments = org.EnumerateCharacters().ToList();

        Assert.Empty(assignments);
    }
}
