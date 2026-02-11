using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Models;

public class TokenUsageTracker
{
    private readonly object _lock = new();
    private readonly List<TokenUsageEntry> _entries = new();

    public int TotalInputTokens { get; private set; }
    public int TotalOutputTokens { get; private set; }
    public decimal TotalCost { get; private set; }

    public IReadOnlyList<TokenUsageEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList().AsReadOnly();
            }
        }
    }

    public void RecordUsage(string operation, AIModelConfig model, int inputTokens, int outputTokens)
    {
        var cost = model.CalculateTotalCost(inputTokens, outputTokens);

        lock (_lock)
        {
            TotalInputTokens += inputTokens;
            TotalOutputTokens += outputTokens;
            TotalCost += cost;

            _entries.Add(new TokenUsageEntry
            {
                Timestamp = Clock.LocalNowDateTime,
                Operation = operation,
                ModelId = model.ModelId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Cost = cost
            });
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            TotalInputTokens = 0;
            TotalOutputTokens = 0;
            TotalCost = 0;
            _entries.Clear();
        }
    }

    public TokenUsageSummary GetSummary()
    {
        lock (_lock)
        {
            return new TokenUsageSummary(
                TotalInputTokens,
                TotalOutputTokens,
                TotalInputTokens + TotalOutputTokens,
                TotalCost);
        }
    }

    public TokenUsageDetailedSummary GetDetailedSummary()
    {
        lock (_lock)
        {
            var totals = new TokenUsageSummary(
                TotalInputTokens,
                TotalOutputTokens,
                TotalInputTokens + TotalOutputTokens,
                TotalCost);

            var byOperation = _entries
                .GroupBy(e => e.Operation)
                .Select(g => new TokenUsageOperationSummary(
                    g.Key,
                    g.Count(),
                    g.Sum(e => e.InputTokens),
                    g.Sum(e => e.OutputTokens),
                    g.Sum(e => e.Cost)))
                .OrderByDescending(x => x.Cost)
                .ToList()
                .AsReadOnly();

            return new TokenUsageDetailedSummary(totals, byOperation);
        }
    }
}

public class TokenUsageEntry
{
    public DateTime Timestamp { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal Cost { get; set; }
}

public sealed record TokenUsageSummary(
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalTokens,
    decimal TotalCost);

public sealed record TokenUsageOperationSummary(
    string Operation,
    int Count,
    int InputTokens,
    int OutputTokens,
    decimal Cost);

public sealed record TokenUsageDetailedSummary(
    TokenUsageSummary Totals,
    IReadOnlyList<TokenUsageOperationSummary> ByOperation);
