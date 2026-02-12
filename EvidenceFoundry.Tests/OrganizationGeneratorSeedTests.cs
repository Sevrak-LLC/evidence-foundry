using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class OrganizationGeneratorSeedTests
{
    [Fact]
    public void ParseSeedOrganizationsParsesOrganizationWithDepartments()
    {
        var response = new OrganizationGenerator.OrganizationSeedResponse
        {
            Organizations =
            [
                new OrganizationGenerator.OrganizationSeedDto
                {
                    Type = "Organization",
                    Name = "Acme",
                    Domain = "acme.com",
                    OrganizationType = OrganizationType.LLC.ToString(),
                    Industry = Industry.Other.ToString(),
                    State = UsState.California.ToString(),
                    Plaintiff = true,
                    Defendant = false,
                    Departments =
                    [
                        new OrganizationGenerator.DepartmentDto
                        {
                            Name = DepartmentName.Executive.ToString(),
                            Roles =
                            [
                                new OrganizationGenerator.RoleDto
                                {
                                    Name = RoleName.ChiefExecutiveOfficer.ToString(),
                                    ReportsToRole = null
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var organizations = OrganizationGenerator.ParseSeedOrganizations(response);

        var organization = Assert.Single(organizations);
        Assert.Equal("Acme", organization.Name);
        Assert.True(organization.IsPlaintiff);
        Assert.Equal(DepartmentName.Executive, organization.Departments.Single().Name);
        Assert.Equal(RoleName.ChiefExecutiveOfficer, organization.Departments.Single().Roles.Single().Name);
    }

    [Fact]
    public void ParseSeedOrganizationsThrowsWhenPlaintiffAndDefendant()
    {
        var response = new OrganizationGenerator.OrganizationSeedResponse
        {
            Organizations =
            [
                new OrganizationGenerator.OrganizationSeedDto
                {
                    Type = "Organization",
                    Name = "Acme",
                    Plaintiff = true,
                    Defendant = true
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() => OrganizationGenerator.ParseSeedOrganizations(response));
    }
}
