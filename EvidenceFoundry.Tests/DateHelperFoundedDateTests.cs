using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class DateHelperFoundedDateTests
{
    [Fact]
    public void NormalizeFoundedDate_UsesDefaultWhenMissing()
    {
        var start = new DateTime(2025, 5, 10);

        var normalized = DateHelper.NormalizeFoundedDate(null, start);

        Assert.Equal(start.AddYears(-5).Date, normalized);
    }

    [Fact]
    public void NormalizeFoundedDate_CapsToOneYearBeforeStoryline()
    {
        var start = new DateTime(2025, 5, 10);
        var founded = start.AddMonths(-6);

        var normalized = DateHelper.NormalizeFoundedDate(founded, start);

        Assert.Equal(start.AddYears(-1).Date, normalized);
    }
}
