using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

public static class TokenUsageFormatter
{
    public static string FormatCompact(TokenUsageSummary summary)
    {
        return $"Cost: {FormatCost(summary.TotalCost)} | Tokens: {FormatTokenCount(summary.TotalTokens)} ({FormatTokenCount(summary.TotalInputTokens)} in / {FormatTokenCount(summary.TotalOutputTokens)} out)";
    }

    public static string FormatTokenAndCost(TokenUsageSummary summary)
    {
        return $"Tokens: {FormatTokenCount(summary.TotalTokens)} | Cost: {FormatCost(summary.TotalCost)}";
    }

    public static string FormatTokenCount(int tokens) => tokens.ToString("N0");

    public static string FormatCost(decimal cost) => $"${cost:F4}";
}
