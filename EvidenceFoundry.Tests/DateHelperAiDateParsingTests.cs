using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class DateHelperAiDateParsingTests
{
    [Fact]
    public void TryParseIsoDateAcceptsIsoOnly()
    {
        var parsed = DateHelper.TryParseIsoDate("2025-04-15", out var date);

        Assert.True(parsed);
        Assert.Equal(new DateTime(2025, 4, 15), date.Date);
        Assert.False(DateHelper.TryParseIsoDate("04/15/2025", out _));
        Assert.False(DateHelper.TryParseIsoDate("2025-04-15T00:00:00", out _));
    }

    [Theory]
    [InlineData("2025-04-15", 2025, 4, 15)]
    [InlineData("2025/04/15", 2025, 4, 15)]
    [InlineData("04/15/2025", 2025, 4, 15)]
    [InlineData("4/5/2025", 2025, 4, 5)]
    [InlineData("Apr 5, 2025", 2025, 4, 5)]
    [InlineData("April 5, 2025", 2025, 4, 5)]
    [InlineData("2025-04", 2025, 4, 1)]
    [InlineData("2025", 2025, 1, 1)]
    public void TryParseAiDateAcceptsSupportedDates(string value, int year, int month, int day)
    {
        var parsed = DateHelper.TryParseAiDate(value, out var date);

        Assert.True(parsed);
        Assert.Equal(new DateTime(year, month, day), date.Date);
    }

    [Theory]
    [InlineData("2025-04-15T13:45:00")]
    [InlineData("2025-04-15T13:45:00Z")]
    [InlineData("2025-04-15 13:45")]
    public void TryParseAiDateAcceptsSupportedDateTimes(string value)
    {
        var parsed = DateHelper.TryParseAiDate(value, out var date);

        Assert.True(parsed);
        Assert.Equal(2025, date.Year);
        Assert.Equal(4, date.Month);
        Assert.Equal(15, date.Day);
    }

    [Fact]
    public void TryParseAiDateRejectsUnsupportedFormats()
    {
        Assert.False(DateHelper.TryParseAiDate("15/04/2025", out _));
        Assert.False(DateHelper.TryParseAiDate(null, out _));
        Assert.False(DateHelper.TryParseAiDate(" ", out _));
    }
}
