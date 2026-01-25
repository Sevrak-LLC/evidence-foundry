namespace ReelDiscovery.Models;

public class AIModelConfig
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal InputTokenPricePerMillion { get; set; }
    public decimal OutputTokenPricePerMillion { get; set; }
    public bool IsDefault { get; set; }

    public decimal CalculateInputCost(int tokens) =>
        tokens * InputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateOutputCost(int tokens) =>
        tokens * OutputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateTotalCost(int inputTokens, int outputTokens) =>
        CalculateInputCost(inputTokens) + CalculateOutputCost(outputTokens);

    public override string ToString() => DisplayName;

    // Common model presets
    public static List<AIModelConfig> GetDefaultModels() => new()
    {
        new AIModelConfig
        {
            ModelId = "gpt-4o",
            DisplayName = "GPT-4o",
            InputTokenPricePerMillion = 2.50m,
            OutputTokenPricePerMillion = 10.00m,
            IsDefault = true
        },
        new AIModelConfig
        {
            ModelId = "gpt-4o-mini",
            DisplayName = "GPT-4o Mini",
            InputTokenPricePerMillion = 0.15m,
            OutputTokenPricePerMillion = 0.60m
        },
        new AIModelConfig
        {
            ModelId = "gpt-4-turbo",
            DisplayName = "GPT-4 Turbo",
            InputTokenPricePerMillion = 10.00m,
            OutputTokenPricePerMillion = 30.00m
        },
        new AIModelConfig
        {
            ModelId = "gpt-3.5-turbo",
            DisplayName = "GPT-3.5 Turbo",
            InputTokenPricePerMillion = 0.50m,
            OutputTokenPricePerMillion = 1.50m
        },
        new AIModelConfig
        {
            ModelId = "o1-preview",
            DisplayName = "O1 Preview",
            InputTokenPricePerMillion = 15.00m,
            OutputTokenPricePerMillion = 60.00m
        },
        new AIModelConfig
        {
            ModelId = "o1-mini",
            DisplayName = "O1 Mini",
            InputTokenPricePerMillion = 3.00m,
            OutputTokenPricePerMillion = 12.00m
        }
    };
}
