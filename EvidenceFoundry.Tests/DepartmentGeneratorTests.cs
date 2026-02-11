using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class DepartmentGeneratorTests
{
    [Fact]
    public void ApplyDepartmentRoleConstraints_RemovesInvalidDepartmentsAndRoles()
    {
        var organization = new Organization
        {
            Name = "Acme",
            Industry = Industry.Other,
            OrganizationType = OrganizationType.LLC
        };

        var engineering = new Department { Name = DepartmentName.Engineering };
        engineering.AddRole(new Role { Name = RoleName.SoftwareEngineer });

        var hr = new Department { Name = DepartmentName.HumanResources };
        hr.AddRole(new Role { Name = RoleName.SoftwareEngineer });

        organization.AddDepartment(engineering);
        organization.AddDepartment(hr);

        DepartmentGenerator.ApplyDepartmentRoleConstraints(organization);

        Assert.DoesNotContain(organization.Departments, d => d.Name == DepartmentName.Engineering);
        var hrDepartment = organization.Departments.Single(d => d.Name == DepartmentName.HumanResources);
        Assert.Empty(hrDepartment.Roles);
    }

    [Fact]
    public void BuildIndustryOrganizationRoleCatalogJson_FiltersIndustries()
    {
        var json = DepartmentGenerator.BuildIndustryOrganizationRoleCatalogJson(
            new[] { Industry.InformationTechnology, Industry.HealthCareAndSocialAssistance });

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var industries = doc.RootElement.GetProperty("industries");

        Assert.True(industries.TryGetProperty(nameof(Industry.InformationTechnology), out _));
        Assert.True(industries.TryGetProperty(nameof(Industry.HealthCareAndSocialAssistance), out _));
        Assert.False(industries.TryGetProperty(nameof(Industry.Manufacturing), out _));
    }
}
