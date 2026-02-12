using System.Text.Json;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class PromptPayloadSerializerTests
{
    [Fact]
    public void SerializeOrganizationWithoutCharactersUsesNullCharactersAndFounded()
    {
        var organization = BuildOrganization(includeCharacter: true);

        var json = PromptPayloadSerializer.SerializeOrganization(organization, includeCharacters: false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("2020-01-15", root.GetProperty("founded").GetString());

        var role = root.GetProperty("departments")[0].GetProperty("roles")[0];
        Assert.Equal(JsonValueKind.Null, role.GetProperty("characters").ValueKind);
    }

    [Fact]
    public void SerializeOrganizationWithCharactersWritesCharacterEntries()
    {
        var organization = BuildOrganization(includeCharacter: true);

        var json = PromptPayloadSerializer.SerializeOrganization(organization, includeCharacters: true);

        using var doc = JsonDocument.Parse(json);
        var characters = doc.RootElement
            .GetProperty("departments")[0]
            .GetProperty("roles")[0]
            .GetProperty("characters");

        Assert.Equal(JsonValueKind.Array, characters.ValueKind);
        Assert.Equal("Ava", characters[0].GetProperty("firstName").GetString());
        Assert.Equal("Lane", characters[0].GetProperty("lastName").GetString());
        Assert.Equal("ava@acme.test", characters[0].GetProperty("email").GetString());
    }

    [Fact]
    public void SerializeCharactersDefaultsToRawEnumValues()
    {
        var organization = BuildOrganization(includeCharacter: true);

        var json = PromptPayloadSerializer.SerializeCharacters(organization);

        using var doc = JsonDocument.Parse(json);
        var character = doc.RootElement[0];

        Assert.Equal("HumanResourcesManager", character.GetProperty("role").GetString());
        Assert.Equal("HumanResources", character.GetProperty("department").GetString());
    }

    [Fact]
    public void SerializeCharactersWhenHumanizedUsesHumanizedEnumValues()
    {
        var organization = BuildOrganization(includeCharacter: true);

        var json = PromptPayloadSerializer.SerializeCharacters(organization, humanizeRoleDepartment: true);

        using var doc = JsonDocument.Parse(json);
        var character = doc.RootElement[0];

        Assert.Equal("Human Resources Manager", character.GetProperty("role").GetString());
        Assert.Equal("Human Resources", character.GetProperty("department").GetString());
    }

    private static Organization BuildOrganization(bool includeCharacter)
    {
        var organizationId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var organization = new Organization
        {
            Id = organizationId,
            Name = "Acme",
            Domain = "acme.test",
            Description = "Test organization",
            OrganizationType = OrganizationType.LLC,
            Industry = Industry.InformationTechnology,
            State = UsState.California,
            Founded = new DateTime(2020, 1, 15),
            IsPlaintiff = true,
            IsDefendant = false
        };

        var role = new Role
        {
            Id = roleId,
            DepartmentId = departmentId,
            OrganizationId = organizationId,
            Name = RoleName.HumanResourcesManager,
            ReportsToRole = RoleName.ChiefHumanResourcesOfficer
        };

        if (includeCharacter)
        {
            role.AddCharacter(new Character
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                DepartmentId = departmentId,
                OrganizationId = organizationId,
                FirstName = "Ava",
                LastName = "Lane",
                Email = "ava@acme.test"
            });
        }

        var department = new Department
        {
            Id = departmentId,
            OrganizationId = organizationId,
            Name = DepartmentName.HumanResources
        };
        department.SetRoles(new List<Role> { role });

        organization.AddDepartment(department);

        return organization;
    }
}
