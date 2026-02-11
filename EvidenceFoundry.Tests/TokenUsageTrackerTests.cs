using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class TokenUsageTrackerTests
{
    [Fact]
    public void GetSummary_ReturnsTotals()
    {
        var tracker = new TokenUsageTracker();
        var model = new AIModelConfig
        {
            ModelId = "test",
            InputTokenPricePerMillion = 1m,
            OutputTokenPricePerMillion = 2m
        };

        tracker.RecordUsage("Plan", model, 1000, 500);

        var summary = tracker.GetSummary();

        Assert.Equal(1000, summary.TotalInputTokens);
        Assert.Equal(500, summary.TotalOutputTokens);
        Assert.Equal(1500, summary.TotalTokens);
        Assert.Equal(0.002m, summary.TotalCost);
    }

    [Fact]
    public void GetDetailedSummary_GroupsByOperationAndOrdersByCost()
    {
        var tracker = new TokenUsageTracker();
        var model = new AIModelConfig
        {
            ModelId = "test",
            InputTokenPricePerMillion = 1m,
            OutputTokenPricePerMillion = 1m
        };

        tracker.RecordUsage("Generate", model, 1000, 0);
        tracker.RecordUsage("Generate", model, 2000, 0);
        tracker.RecordUsage("Validate", model, 500, 0);

        var detailed = tracker.GetDetailedSummary();

        Assert.Equal(3500, detailed.Totals.TotalInputTokens);
        Assert.Equal(0, detailed.Totals.TotalOutputTokens);
        Assert.Equal(3500, detailed.Totals.TotalTokens);
        Assert.Equal(0.0035m, detailed.Totals.TotalCost);

        Assert.Collection(
            detailed.ByOperation,
            op =>
            {
                Assert.Equal("Generate", op.Operation);
                Assert.Equal(2, op.Count);
                Assert.Equal(3000, op.InputTokens);
                Assert.Equal(0, op.OutputTokens);
                Assert.Equal(0.003m, op.Cost);
            },
            op =>
            {
                Assert.Equal("Validate", op.Operation);
                Assert.Equal(1, op.Count);
                Assert.Equal(500, op.InputTokens);
                Assert.Equal(0, op.OutputTokens);
                Assert.Equal(0.0005m, op.Cost);
            });
    }

    [Fact]
    public void GetDetailedSummary_ReturnsEmptyBreakdownWhenNoEntries()
    {
        var tracker = new TokenUsageTracker();

        var detailed = tracker.GetDetailedSummary();

        Assert.Empty(detailed.ByOperation);
        Assert.Equal(0, detailed.Totals.TotalInputTokens);
        Assert.Equal(0, detailed.Totals.TotalOutputTokens);
        Assert.Equal(0, detailed.Totals.TotalTokens);
        Assert.Equal(0m, detailed.Totals.TotalCost);
    }
}
