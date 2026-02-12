using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class ThreadRelevanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetThreadOddsThrowsOnNonPositiveEmailCount(int emailCount)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => EmailThreadGenerator.GetThreadOdds(emailCount));

        Assert.Contains("Thread email count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(1.1, 0.5)]
    [InlineData(0.5, -0.1)]
    [InlineData(0.5, 1.1)]
    public void EvaluateThreadRelevanceThrowsOnInvalidRoll(double responsiveRoll, double hotRoll)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailThreadGenerator.EvaluateThreadRelevance(1, responsiveRoll, hotRoll));

        Assert.Contains("between 0.0 and 1.0", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateThreadRelevanceHotOverridesNonResponsive()
    {
        var (responsiveOdds, hotOdds) = EmailThreadGenerator.GetThreadOdds(1);
        var responsiveRoll = 1.0;
        var hotRoll = Math.Max(0.0, hotOdds - 1e-9);

        var (relevance, isHot) = EmailThreadGenerator.EvaluateThreadRelevance(1, responsiveRoll, hotRoll);

        Assert.True(isHot);
        Assert.Equal(EmailThread.ThreadRelevance.Responsive, relevance);
        Assert.True(hotRoll <= hotOdds);
        Assert.True(responsiveRoll > responsiveOdds);
    }

    [Fact]
    public void EvaluateThreadRelevanceNonResponsiveWhenBothRollsAboveOdds()
    {
        var (responsiveOdds, hotOdds) = EmailThreadGenerator.GetThreadOdds(1);
        var responsiveRoll = Math.Min(1.0, responsiveOdds + 0.1);
        var hotRoll = Math.Min(1.0, hotOdds + 0.1);

        var (relevance, isHot) = EmailThreadGenerator.EvaluateThreadRelevance(1, responsiveRoll, hotRoll);

        Assert.False(isHot);
        Assert.Equal(EmailThread.ThreadRelevance.NonResponsive, relevance);
        Assert.True(responsiveRoll > responsiveOdds);
        Assert.True(hotRoll > hotOdds);
    }

    [Fact]
    public void EvaluateThreadRelevanceResponsiveWhenResponsiveRollHitsButHotDoesNot()
    {
        var (responsiveOdds, hotOdds) = EmailThreadGenerator.GetThreadOdds(1);
        var responsiveRoll = Math.Max(0.0, responsiveOdds - 1e-9);
        var hotRoll = Math.Min(1.0, hotOdds + 0.1);

        var (relevance, isHot) = EmailThreadGenerator.EvaluateThreadRelevance(1, responsiveRoll, hotRoll);

        Assert.False(isHot);
        Assert.Equal(EmailThread.ThreadRelevance.Responsive, relevance);
        Assert.True(responsiveRoll <= responsiveOdds);
        Assert.True(hotRoll > hotOdds);
    }
}
