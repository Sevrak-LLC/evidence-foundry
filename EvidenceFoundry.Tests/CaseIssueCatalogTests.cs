using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class CaseIssueCatalogTests
{
    [Fact]
    public void GetIssueDescription_ReturnsExpectedDescription()
    {
        var description = CaseIssueCatalog.GetIssueDescription("Employment", "Restrictive Covenants", "Non-Compete");

        Assert.Equal(
            "A dispute between businesses (often involving a competitor as a defendant) over whether post-employment restrictions are being violated through competitive work, competitive hiring, or use of a former employee to gain an unfair market advantage.",
            description);
    }

    [Fact]
    public void GetIssueDescription_IsCaseInsensitive()
    {
        var description = CaseIssueCatalog.GetIssueDescription("commercial", "contracts", "breach");

        Assert.Equal(
            "A dispute where one business claims the other failed to perform a material obligation under a commercial agreement (deliverables, service levels, warranties, exclusivity, etc.), resulting in damages or specific performance demands.",
            description);
    }

    [Fact]
    public void GetIssueDescription_ThrowsForUnknownCaseArea()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CaseIssueCatalog.GetIssueDescription("Unknown", "Contracts", "Breach"));

        Assert.Contains("Unknown case area", exception.Message);
    }
}
