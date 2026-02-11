namespace EvidenceFoundry.Helpers;

public static class GenerationRequestNormalizer
{
    public const string RandomIndustryPreference = "Random";

    public static string NormalizeIndustryPreference(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference))
        {
            return RandomIndustryPreference;
        }

        var trimmed = preference.Trim();
        return IsRandomIndustry(trimmed) ? RandomIndustryPreference : trimmed;
    }

    public static int NormalizePartyCount(int value)
    {
        if (value < 1)
            return 1;
        if (value > 3)
            return 3;
        return value;
    }

    public static bool IsRandomIndustry(string? industry)
    {
        return string.Equals(industry, RandomIndustryPreference, StringComparison.OrdinalIgnoreCase);
    }
}
