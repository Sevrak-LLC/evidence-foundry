using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

public static partial class DateHelper
{
    private static readonly string[] IsoDateFormats = { "yyyy-MM-dd" };
    private static readonly string[] AiDateFormats =
    {
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "MM/dd/yyyy",
        "M/d/yyyy",
        "MMM d, yyyy",
        "MMMM d, yyyy",
        "yyyy-MM",
        "yyyy"
    };

    private static readonly string[] AiDateTimeFormats =
    {
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFF",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.fffK",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "o"
    };

    public static bool TryParseIsoDate(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return DateTime.TryParseExact(
            value.Trim(),
            IsoDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out date);
    }

    public static bool TryParseAiDate(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (DateTime.TryParseExact(
                trimmed,
                AiDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out date))
        {
            return true;
        }

        return DateTime.TryParseExact(
            trimmed,
            AiDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out date);
    }

    public static List<DateTime> DistributeDatesForThread(
        int emailCount,
        DateTime threadStart,
        DateTime threadEnd,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var dates = new List<DateTime>();

        if (emailCount <= 0) return dates;
        if (emailCount == 1)
        {
            dates.Add(AdjustToBusinessHours(threadStart, rng));
            return dates;
        }

        var totalMinutes = (threadEnd - threadStart).TotalMinutes;
        var avgGap = totalMinutes / (emailCount - 1);

        var current = threadStart;

        for (int i = 0; i < emailCount; i++)
        {
            // Adjust to business hours 90% of the time
            var adjustedDate = rng.NextDouble() < 0.9
                ? AdjustToBusinessHours(current, rng)
                : current;

            dates.Add(adjustedDate);

            if (i < emailCount - 1)
            {
                // Add some randomness to gaps (0.3x to 1.7x the average)
                var gapVariation = avgGap * (0.3 + rng.NextDouble() * 1.4);
                current = current.AddMinutes(gapVariation);

                // Ensure we don't go past the end date
                if (current > threadEnd)
                    current = threadEnd;
            }
        }

        return dates;
    }

    public static DateTime AdjustToBusinessHours(DateTime dt, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        // Skip weekends
        while (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
        {
            dt = dt.AddDays(1).Date.AddHours(9);
        }

        // Adjust to business hours (8 AM - 7 PM)
        if (dt.Hour < 8)
        {
            dt = dt.Date.AddHours(8).AddMinutes(rng.Next(0, 60));
        }
        else if (dt.Hour >= 19)
        {
            // Move to next business day
            dt = dt.Date.AddDays(1);
            while (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday)
            {
                dt = dt.AddDays(1);
            }
            dt = dt.AddHours(8).AddMinutes(rng.Next(0, 60));
        }

        return dt;
    }

    public static DateTime RandomDateInRange(DateTime start, DateTime end, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var range = (end - start).TotalMinutes;
        var randomMinutes = rng.NextDouble() * range;
        return start.AddMinutes(randomMinutes);
    }

    /// <summary>
    /// Interpolate a date within a range based on a progress fraction (0.0 to 1.0)
    /// </summary>
    public static DateTime InterpolateDateInRange(DateTime start, DateTime end, double fraction)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        var totalMinutes = (end - start).TotalMinutes;
        return start.AddMinutes(totalMinutes * fraction);
    }

    internal static (DateTime start, DateTime end, string? note) NormalizeStorylineDateRange(
        Storyline storyline,
        DateTime start,
        DateTime end)
    {
        ArgumentNullException.ThrowIfNull(storyline);

        var normalizedStart = start.Date;
        var normalizedEnd = end.Date;
        if (normalizedEnd < normalizedStart)
            normalizedEnd = normalizedStart;

        var notes = new List<string>();
        var rng = CreateDeterministicRandom(storyline.Title, storyline.Summary);
        var allowBoundary = HasExplicitBoundaryLanguage(storyline.Title, storyline.Summary);

        if (!allowBoundary)
        {
            normalizedStart = NudgeOffMonthStart(normalizedStart, normalizedEnd, rng, notes);
            normalizedEnd = NudgeOffMonthEnd(normalizedStart, normalizedEnd, rng, notes);
        }

        var startShift = rng.Next(1, 4);
        normalizedStart = ShiftByBusinessDays(ClampToWeekday(normalizedStart, forward: true), startShift, direction: 1);
        notes.Add($"Shifted start forward by {startShift} business day(s).");

        var endShift = rng.Next(1, 8);
        normalizedEnd = ShiftByBusinessDays(ClampToWeekday(normalizedEnd, forward: false), endShift, direction: -1);
        notes.Add($"Shifted end earlier by {endShift} business day(s).");

        var maxEnd = normalizedStart.AddMonths(6);
        if (normalizedEnd > maxEnd)
        {
            normalizedEnd = maxEnd;
            notes.Add("Capped to 6 months after the start date.");
        }

        if (normalizedEnd < normalizedStart)
        {
            normalizedEnd = normalizedStart;
            notes.Add("Adjusted end date to be on or after the start date.");
        }

        var note = string.Join(" ", notes);
        return (normalizedStart, normalizedEnd, note);
    }

    private static DateTime ClampToWeekday(DateTime date, bool forward)
    {
        var current = date.Date;
        if (IsWeekday(current))
            return current;

        var step = forward ? 1 : -1;
        while (!IsWeekday(current))
        {
            current = current.AddDays(step);
        }

        return current;
    }

    private static DateTime ShiftByBusinessDays(DateTime date, int businessDays, int direction)
    {
        if (businessDays <= 0)
            return date.Date;

        var current = date.Date;
        var remaining = businessDays;
        var step = direction >= 0 ? 1 : -1;

        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsWeekday(current))
            {
                remaining--;
            }
        }

        return current;
    }

    private static bool IsWeekday(DateTime date)
    {
        return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
    }

    private static DateTime NudgeOffMonthStart(
        DateTime start,
        DateTime end,
        Random rng,
        List<string> notes)
    {
        if (start.Day != 1)
            return start;

        var available = (end - start).Days;
        if (available < 2)
            return start;

        var maxShift = Math.Min(5, available - 1);
        if (maxShift <= 0)
            return start;

        var shift = rng.Next(1, maxShift + 1);
        notes.Add($"Shifted start off month boundary by {shift} day(s).");
        return start.AddDays(shift);
    }

    private static DateTime NudgeOffMonthEnd(
        DateTime start,
        DateTime end,
        Random rng,
        List<string> notes)
    {
        var monthEnd = DateTime.DaysInMonth(end.Year, end.Month);
        if (end.Day != monthEnd)
            return end;

        var available = (end - start).Days;
        if (available < 2)
            return end;

        var maxShift = Math.Min(5, available - 1);
        if (maxShift <= 0)
            return end;

        var shift = rng.Next(1, maxShift + 1);
        notes.Add($"Shifted end off month boundary by {shift} day(s).");
        return end.AddDays(-shift);
    }

    private static bool HasExplicitBoundaryLanguage(string title, string summary)
    {
        var text = $"{title} {summary}".ToLowerInvariant();

        if (ExplicitBoundaryShortRegex().IsMatch(text))
            return true;

        return ExplicitBoundaryLongRegex().IsMatch(text);
    }

    [GeneratedRegex(@"\b(q[1-4]|h[12]|fy\d{2,4})\b")]
    private static partial Regex ExplicitBoundaryShortRegex();

    [GeneratedRegex(@"\b(quarter|quarterly|fiscal year|fiscal-year|year-end|year end|month-end|month end|annual|annually|calendar year|half-year|half year)\b")]
    private static partial Regex ExplicitBoundaryLongRegex();

    private static Random CreateDeterministicRandom(string title, string summary)
    {
        var seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{title}|{summary}"));
        var seed = BitConverter.ToInt32(seedBytes, 0);
        return new Random(seed);
    }

    internal static bool NormalizeStoryBeats(IList<StoryBeat> beats, DateTime storylineStart, DateTime storylineEnd)
    {
        if (beats.Count == 0)
            return false;

        var changed = false;
        var startDate = storylineStart.Date;
        var endDate = storylineEnd.Date;

        changed |= TryAlignBeatStart(beats[0], startDate);
        changed |= TryAlignBeatEnd(beats[^1], endDate);

        for (var i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            changed |= TryClampStartToRange(beat, startDate);
            changed |= TryClampEndToRange(beat, endDate);
        }

        for (var i = 1; i < beats.Count; i++)
        {
            var previous = beats[i - 1];
            var beat = beats[i];
            changed |= TryAlignStartAfterPrevious(beat, previous.EndDate.Date);
            changed |= TryEnsureEndNotBeforeStart(beat);
        }

        return changed;
    }

    private static bool TryAlignBeatStart(StoryBeat beat, DateTime targetStart)
    {
        var current = beat.StartDate.Date;
        if (Math.Abs((current - targetStart).Days) > 1 || current == targetStart)
            return false;

        beat.StartDate = targetStart;
        return true;
    }

    private static bool TryAlignBeatEnd(StoryBeat beat, DateTime targetEnd)
    {
        var current = beat.EndDate.Date;
        if (Math.Abs((current - targetEnd).Days) > 1 || current == targetEnd)
            return false;

        beat.EndDate = targetEnd;
        return true;
    }

    private static bool TryClampStartToRange(StoryBeat beat, DateTime minStart)
    {
        var current = beat.StartDate.Date;
        if (current >= minStart || (minStart - current).Days > 1)
            return false;

        beat.StartDate = minStart;
        return true;
    }

    private static bool TryClampEndToRange(StoryBeat beat, DateTime maxEnd)
    {
        var current = beat.EndDate.Date;
        if (current <= maxEnd || (current - maxEnd).Days > 1)
            return false;

        beat.EndDate = maxEnd;
        return true;
    }

    private static bool TryAlignStartAfterPrevious(StoryBeat beat, DateTime previousEndDate)
    {
        var minStart = previousEndDate.AddDays(1);
        var current = beat.StartDate.Date;
        if (current >= minStart || (minStart - current).Days > 1)
            return false;

        beat.StartDate = minStart;
        return true;
    }

    private static bool TryEnsureEndNotBeforeStart(StoryBeat beat)
    {
        var start = beat.StartDate.Date;
        var end = beat.EndDate.Date;
        if (end >= start || (start - end).Days > 1)
            return false;

        beat.EndDate = start;
        return true;
    }

    internal static DateTime NormalizeFoundedDate(DateTime? founded, DateTime storylineStartDate)
    {
        var minDate = storylineStartDate.AddYears(-1).Date;
        var normalized = founded?.Date ?? storylineStartDate.AddYears(-5).Date;
        return normalized > minDate ? minDate : normalized;
    }

    public static string FormatForFileName(DateTime date)
    {
        return date.ToString("yyyyMMdd_HHmmss");
    }

    public static (int businessDays, int saturdays, int sundays) CountDayTypesInclusive(DateTime start, DateTime end)
    {
        var startDate = start.Date;
        var endDate = end.Date;
        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(end));

        var businessDays = 0;
        var saturdays = 0;
        var sundays = 0;

        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            switch (current.DayOfWeek)
            {
                case DayOfWeek.Saturday:
                    saturdays++;
                    break;
                case DayOfWeek.Sunday:
                    sundays++;
                    break;
                default:
                    businessDays++;
                    break;
            }
        }

        return (businessDays, saturdays, sundays);
    }

    public static (int low, int high) GetBusinessDayEmailRange(int keyRoleCount)
    {
        if (keyRoleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(keyRoleCount), "Key role count must be positive.");

        const double s = 24;
        const double pMax = 0.65;
        const double k = 12;
        const double kappa0 = 3;
        const double z = 1.645;

        var p = pMax * (1 - Math.Exp(-(keyRoleCount - 1) / k));
        var mu = keyRoleCount * s * p;
        var sigma = Math.Sqrt(mu + (mu * mu) / (keyRoleCount * kappa0));
        var pm = z * sigma;

        var low = Math.Max(0, mu - pm);
        var high = mu + pm;

        return (CeilToInt(low), CeilToInt(high));
    }

    public static (int low, int high) GetWeekendEmailRange(int keyRoleCount, DayOfWeek dayType)
    {
        if (dayType != DayOfWeek.Saturday && dayType != DayOfWeek.Sunday)
            throw new ArgumentOutOfRangeException(nameof(dayType), "Day type must be Saturday or Sunday.");

        if (keyRoleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(keyRoleCount), "Key role count must be positive.");

        const double s = 24;
        const double pMax = 0.65;
        const double k = 12;
        const double kappa0 = 2;
        const double z = 1.645;
        const double mSat = 0.146;
        const double mSun = 0.136;

        var p = pMax * (1 - Math.Exp(-(keyRoleCount - 1) / k));
        var muBd = keyRoleCount * s * p;
        var m = dayType == DayOfWeek.Saturday ? mSat : mSun;
        var mu = m * muBd;

        var sigma = Math.Sqrt(mu + (mu * mu) / (keyRoleCount * kappa0));
        var pm = z * sigma;

        var low = Math.Max(0, mu - pm);
        var high = mu + pm;

        return (CeilToInt(low), CeilToInt(high));
    }

    public static int CalculateEmailCountForRange(DateTime start, DateTime end, int keyRoleCount, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (keyRoleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(keyRoleCount), "Key role count must be positive.");

        var startDate = start.Date;
        var endDate = end.Date;
        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(end));

        var (businessLow, businessHigh) = GetBusinessDayEmailRange(keyRoleCount);
        var (satLow, satHigh) = GetWeekendEmailRange(keyRoleCount, DayOfWeek.Saturday);
        var (sunLow, sunHigh) = GetWeekendEmailRange(keyRoleCount, DayOfWeek.Sunday);

        var total = 0;
        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            switch (current.DayOfWeek)
            {
                case DayOfWeek.Saturday:
                    total += SampleDailyCount(satLow, satHigh, rng);
                    break;
                case DayOfWeek.Sunday:
                    total += SampleDailyCount(sunLow, sunHigh, rng);
                    break;
                default:
                    total += SampleDailyCount(businessLow, businessHigh, rng);
                    break;
            }
        }

        return total;
    }

    internal static List<int> BuildThreadSizePlan(int totalEmails, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (totalEmails < 0)
            throw new ArgumentOutOfRangeException(nameof(totalEmails), "Total email count must be non-negative.");
        if (totalEmails == 0) return new List<int>();

        var sizes = new List<int>();
        var remaining = totalEmails;

        while (remaining > 0)
        {
            var eligible = ThreadSizeBuckets.Where(b => b.Min <= remaining).ToList();
            if (eligible.Count == 0)
            {
                sizes.Add(1);
                remaining -= 1;
                continue;
            }

            var chosen = WeightedChoice(eligible, rng);
            var hi = Math.Min(Math.Min(chosen.Max, remaining), ThreadSizeCap);
            var lo = chosen.Min;

            int size;
            if (hi < lo)
            {
                size = 1;
            }
            else if (hi == lo)
            {
                size = lo;
            }
            else
            {
                size = SampleLowerBiased(lo, hi, rng);
            }

            sizes.Add(size);
            remaining -= size;
        }

        return sizes;
    }

    private static int SampleDailyCount(int low, int high, Random rng)
    {
        if (low < 0) low = 0;
        if (high < low) high = low;
        return rng.Next(low, high + 1);
    }

    private const int ThreadSizeCap = 50;

    private sealed class ThreadSizeBucket
    {
        public ThreadSizeBucket(int min, int max, double weight)
        {
            Min = min;
            Max = max;
            Weight = weight;
        }

        public int Min { get; }
        public int Max { get; }
        public double Weight { get; }
    }

    private static readonly ThreadSizeBucket[] ThreadSizeBuckets =
    {
        new(1, 1, 0.35),
        new(2, 2, 0.25),
        new(3, 3, 0.12),
        new(4, 4, 0.07),
        new(5, 5, 0.05),
        new(6, 10, 0.10),
        new(11, 15, 0.03),
        new(16, 20, 0.02),
        new(21, 30, 0.007),
        new(31, 40, 0.002),
        new(41, 50, 0.001)
    };

    private static ThreadSizeBucket WeightedChoice(IReadOnlyList<ThreadSizeBucket> buckets, Random rng)
    {
        var totalWeight = buckets.Sum(b => b.Weight);
        var roll = rng.NextDouble() * totalWeight;
        var acc = 0.0;

        foreach (var bucket in buckets)
        {
            acc += bucket.Weight;
            if (roll <= acc)
                return bucket;
        }

        return buckets[^1];
    }

    private static int SampleLowerBiased(int min, int max, Random rng)
    {
        var x = rng.Next(min, max + 1);
        var y = rng.Next(min, max + 1);
        return Math.Min(x, y);
    }

    private static int CeilToInt(double value)
    {
        if (value <= 0) return 0;
        if (value >= int.MaxValue) return int.MaxValue;
        return (int)Math.Ceiling(value);
    }
}
