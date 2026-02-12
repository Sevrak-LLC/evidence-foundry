using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class DateHelperEmailCountTests
{
    [Fact]
    public void CountDayTypesInclusiveReturnsExpectedCounts()
    {
        var start = new DateTime(2024, 1, 1); // Monday
        var end = new DateTime(2024, 1, 7);   // Sunday

        var (businessDays, saturdays, sundays) = DateHelper.CountDayTypesInclusive(start, end);

        Assert.Equal(5, businessDays);
        Assert.Equal(1, saturdays);
        Assert.Equal(1, sundays);
    }

    [Fact]
    public void GetBusinessDayEmailRangeUsesCeilingAndMatchesFormula()
    {
        const int n = 6;
        var (low, high) = DateHelper.GetBusinessDayEmailRange(n);

        const double s = 24;
        const double pMax = 0.65;
        const double k = 12;
        const double kappa0 = 3;
        const double z = 1.645;

        var p = pMax * (1 - Math.Exp(-(n - 1) / k));
        var mu = n * s * p;
        var sigma = Math.Sqrt(mu + (mu * mu) / (n * kappa0));
        var pm = z * sigma;
        var expectedLow = (int)Math.Ceiling(Math.Max(0, mu - pm));
        var expectedHigh = (int)Math.Ceiling(mu + pm);

        Assert.Equal(expectedLow, low);
        Assert.Equal(expectedHigh, high);
        Assert.True(high >= low);
    }

    [Fact]
    public void GetWeekendEmailRangeReflectsSaturdayVsSundayMultipliers()
    {
        var (satLow, satHigh) = DateHelper.GetWeekendEmailRange(8, DayOfWeek.Saturday);
        var (sunLow, sunHigh) = DateHelper.GetWeekendEmailRange(8, DayOfWeek.Sunday);

        Assert.True(satHigh >= sunHigh);
        Assert.True(satLow >= sunLow);
    }

    [Fact]
    public void CalculateEmailCountForRangeIsDeterministicForSeedAndInclusive()
    {
        var start = new DateTime(2024, 1, 5); // Friday
        var end = new DateTime(2024, 1, 7);   // Sunday
        const int n = 5;

        var rng1 = new Random(123);
        var rng2 = new Random(123);

        var total1 = DateHelper.CalculateEmailCountForRange(start, end, n, rng1);
        var total2 = DateHelper.CalculateEmailCountForRange(start, end, n, rng2);

        Assert.Equal(total1, total2);

        var (bdLow, bdHigh) = DateHelper.GetBusinessDayEmailRange(n);
        var (satLow, satHigh) = DateHelper.GetWeekendEmailRange(n, DayOfWeek.Saturday);
        var (sunLow, sunHigh) = DateHelper.GetWeekendEmailRange(n, DayOfWeek.Sunday);

        var minTotal = bdLow + satLow + sunLow;
        var maxTotal = bdHigh + satHigh + sunHigh;

        Assert.InRange(total1, minTotal, maxTotal);
    }

    [Fact]
    public void CalculateEmailCountForRangeThrowsOnInvalidInputs()
    {
        var rng = new Random(1);
        var start = new DateTime(2024, 2, 1);
        var end = new DateTime(2024, 1, 31);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DateHelper.CalculateEmailCountForRange(start, start, 0, rng));

        Assert.Throws<ArgumentException>(() =>
            DateHelper.CalculateEmailCountForRange(start, end, 3, rng));
    }
}
