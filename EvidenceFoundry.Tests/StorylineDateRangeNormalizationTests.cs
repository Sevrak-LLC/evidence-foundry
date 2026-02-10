using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class StorylineDateRangeNormalizationTests
{
    [Fact]
    public void NormalizeStorylineDateRange_NudgesMonthBoundaries()
    {
        var storyline = new Storyline
        {
            Title = "Vendor rollout",
            Summary = "A short rollout with coordination, miscommunication, and a quick escalation."
        };

        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 3, 31);

        var normalized = DateHelper.NormalizeStorylineDateRange(storyline, start, end);

        Assert.NotEqual(1, normalized.start.Day);
        Assert.NotEqual(DateTime.DaysInMonth(normalized.end.Year, normalized.end.Month), normalized.end.Day);
        Assert.True(IsWeekday(normalized.start));
        Assert.True(IsWeekday(normalized.end));
        Assert.True(normalized.start.Date >= start.Date.AddDays(1));
        Assert.True(normalized.end.Date <= end.Date.AddDays(-1));
    }

    [Fact]
    public void NormalizeStorylineDateRange_AdjustsExplicitQuarterBoundaries()
    {
        var storyline = new Storyline
        {
            Title = "Q1 closeout coordination",
            Summary = "Teams scramble to finalize Q1 numbers with a quarter-end push."
        };

        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 3, 31);

        var normalized = DateHelper.NormalizeStorylineDateRange(storyline, start, end);

        Assert.True(IsWeekday(normalized.start));
        Assert.True(IsWeekday(normalized.end));
        Assert.True(normalized.start.Date >= start.Date.AddDays(1));
        Assert.True(normalized.end.Date <= end.Date.AddDays(-1));
    }

    [Fact]
    public void NormalizeStorylineDateRange_CapsToSixMonths()
    {
        var storyline = new Storyline
        {
            Title = "Extended integration effort",
            Summary = "A prolonged integration effort with shifting timelines and vendor friction."
        };

        var start = new DateTime(2024, 1, 5);
        var end = new DateTime(2024, 12, 20);

        var normalized = DateHelper.NormalizeStorylineDateRange(storyline, start, end);

        Assert.Equal(normalized.start.AddMonths(6).Date, normalized.end);
        Assert.Contains("Capped to 6 months", normalized.note);
    }

    private static bool IsWeekday(DateTime date)
    {
        return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
    }
}
