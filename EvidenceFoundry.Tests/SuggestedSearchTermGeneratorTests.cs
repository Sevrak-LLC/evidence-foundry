using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class SuggestedSearchTermGeneratorTests
{
    [Fact]
    public void FilterTermsAgainstEmail_DropsOutOfScopeTerms()
    {
        var email = "Please review the Q3 budget forecast before Friday. The budget review meeting is next week.";
        var terms = new List<string>
        {
            "\"budget review\"",
            "\"customer churn\"",
            "Q3 AND forecast",
            "review AND invoice"
        };

        var filtered = SuggestedSearchTermGenerator.FilterTermsAgainstEmail(terms, email, 3);

        Assert.Equal(2, filtered.Count);
        Assert.Contains("\"budget review\"", filtered);
        Assert.Contains("Q3 AND forecast", filtered);
        Assert.DoesNotContain("\"customer churn\"", filtered);
        Assert.DoesNotContain("review AND invoice", filtered);
    }

    [Fact]
    public void FilterTermsAgainstEmail_RespectsMaxAndDedupes()
    {
        var email = "Alpha beta gamma. Alpha beta delta. Alpha beta.";
        var terms = new List<string>
        {
            "Alpha AND beta",
            "alpha AND beta",
            "\"alpha beta\"",
            "gamma"
        };

        var filtered = SuggestedSearchTermGenerator.FilterTermsAgainstEmail(terms, email, 2);

        Assert.Equal(2, filtered.Count);
        Assert.Equal("Alpha AND beta", filtered[0]);
        Assert.Equal("\"alpha beta\"", filtered[1]);
        Assert.DoesNotContain("gamma", filtered);
    }

    [Fact]
    public void FilterTermsAgainstEmail_RejectsTermsWithoutLiterals()
    {
        var email = "Budget approved.";
        var terms = new List<string> { "AND OR NOT" };

        var filtered = SuggestedSearchTermGenerator.FilterTermsAgainstEmail(terms, email, 3);

        Assert.Empty(filtered);
    }
}
