using System.Text.RegularExpressions;

namespace EvidenceFoundry.Models;

public partial class AIModelConfig
{
    public const int DefaultMaxOutputTokens = 1200;
    public const int DefaultMaxJsonOutputTokens = 3000;
    private static readonly Regex ModelIdRegex = ModelIdRegexGenerated();

    [GeneratedRegex("^[A-Za-z0-9_.:-]+$", RegexOptions.Compiled)]
    private static partial Regex ModelIdRegexGenerated();

    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal InputTokenPricePerMillion { get; set; }
    public decimal OutputTokenPricePerMillion { get; set; }
    public bool IsDefault { get; set; }
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;
    public int MaxJsonOutputTokens { get; set; } = DefaultMaxJsonOutputTokens;

    public decimal CalculateInputCost(int tokens) =>
        tokens * InputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateOutputCost(int tokens) =>
        tokens * OutputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateTotalCost(int inputTokens, int outputTokens) =>
        CalculateInputCost(inputTokens) + CalculateOutputCost(outputTokens);

    public override string ToString() => DisplayName;

    public static bool IsValidModelId(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        return ModelIdRegex.IsMatch(modelId.Trim());
    }

}
