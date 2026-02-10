using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class EmailThreadTopicCatalogTests
{
    [Fact]
    public void GetTopics_ReturnsNonEmptyForAllEnumCombinations()
    {
        foreach (var industry in Enum.GetValues<Industry>())
        {
            foreach (var organizationType in Enum.GetValues<OrganizationType>())
            {
                var topics = EmailThreadTopicCatalog.GetTopics(industry, organizationType);

                Assert.NotEmpty(topics);
            }
        }
    }

    [Fact]
    public void GetTopics_IsDeterministicAndDistinct()
    {
        var first = EmailThreadTopicCatalog.GetTopics(Industry.InformationTechnology, OrganizationType.CCorporation);
        var second = EmailThreadTopicCatalog.GetTopics(Industry.InformationTechnology, OrganizationType.CCorporation);

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GetTopics_AvoidsDisputeLanguage()
    {
        var blocked = new[]
        {
            "dispute",
            "litigation",
            "lawsuit",
            "breach",
            "fraud",
            "collusion",
            "monopolization",
            "antitrust",
            "infringement",
            "trade secret",
            "non-compete",
            "noncompete",
            "non-solicit",
            "nonsolicit",
            "bad faith",
            "bankruptcy",
            "creditor",
            "class action",
            "claim",
            "copyright",
            "trademark",
            "patent"
        };

        foreach (var industry in Enum.GetValues<Industry>())
        {
            foreach (var organizationType in Enum.GetValues<OrganizationType>())
            {
                var topics = EmailThreadTopicCatalog.GetTopics(industry, organizationType);

                foreach (var topic in topics)
                {
                    foreach (var term in blocked)
                    {
                        Assert.DoesNotContain(term, topic, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
    }
}
