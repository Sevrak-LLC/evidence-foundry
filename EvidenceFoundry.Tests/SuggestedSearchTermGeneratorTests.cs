using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class SuggestedSearchTermGeneratorTests
{
    [Fact]
    public void FilterTermsAgainstEmailDropsOutOfScopeTerms()
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
        Assert.Contains(filtered, item => string.Equals(item, "\"budget review\"", StringComparison.Ordinal));
        Assert.Contains(filtered, item => string.Equals(item, "Q3 AND forecast", StringComparison.Ordinal));
        Assert.DoesNotContain(filtered, item => string.Equals(item, "\"customer churn\"", StringComparison.Ordinal));
        Assert.DoesNotContain(filtered, item => string.Equals(item, "review AND invoice", StringComparison.Ordinal));
    }

    [Fact]
    public void FilterTermsAgainstEmailRespectsMaxAndDedupes()
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
        Assert.DoesNotContain(filtered, item => string.Equals(item, "gamma", StringComparison.Ordinal));
    }

    [Fact]
    public void FilterTermsAgainstEmailRejectsTermsWithoutLiterals()
    {
        var email = "Budget approved.";
        var terms = new List<string> { "AND OR NOT" };

        var filtered = SuggestedSearchTermGenerator.FilterTermsAgainstEmail(terms, email, 3);

        Assert.Empty(filtered);
    }
}
