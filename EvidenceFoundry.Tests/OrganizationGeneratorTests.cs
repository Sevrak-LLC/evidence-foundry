using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class OrganizationGeneratorTests
{
    [Fact]
    public void GenerateDomainFromName_StripsPunctuationAndAddsSuffix()
    {
        var domain = OrganizationGenerator.GenerateDomainFromName("ACME, Inc.");

        Assert.Equal("acme-inc.com", domain);
    }

    [Fact]
    public void EnsureCaseParties_AssignsPlaintiffAndDefendantWhenMissing()
    {
        var organizations = new List<Organization>
        {
            new() { Name = "Alpha" },
            new() { Name = "Beta" }
        };

        OrganizationGenerator.EnsureCaseParties(organizations);

        Assert.True(organizations[0].IsPlaintiff);
        Assert.True(organizations[1].IsDefendant);
    }
}
