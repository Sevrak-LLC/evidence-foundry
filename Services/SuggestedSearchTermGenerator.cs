namespace EvidenceFoundry.Services;

public class SuggestedSearchTermGenerator
{
    private readonly OpenAIService _openAI;

    public SuggestedSearchTermGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
    }

    internal async Task<List<string>> GenerateSuggestedSearchTermsAsync(
        string exportedEmail,
        string storylineSummary,
        string storyBeatPlot,
        bool isHot,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(exportedEmail))
            return new List<string>();

        var precisionGuidance = isHot
            ? "HIGH PRECISION: Use specific phrases or unique names from the email. Minimize false positives."
            : "MODERATE PRECISION: Use terms likely to find the email but allow some ambiguity or false positives.";

        var systemPrompt = @"You are an eDiscovery search expert who writes dtSearch query strings for email discovery.
Return ONLY valid JSON matching the schema; no markdown or commentary.";

        var userPrompt = $@"Storyline summary:
{storylineSummary}

Story beat plot:
{storyBeatPlot}

Exported email (full content, no attachment binaries):
{exportedEmail}

Thread priority:
{(isHot ? "HOT (highly responsive)" : "RESPONSIVE (not hot)")}

Instructions:
- Generate 2 to 3 dtSearch-formatted query strings.
- Use only terms that appear in the email content.
- {precisionGuidance}
- Prefer quoted phrases for specificity and use AND/OR or w/n when helpful.
- Avoid message-id strings or raw header tokens.

Respond with JSON in this exact format:
{{ ""terms"": [""query1"", ""query2""] }}";

        var response = await _openAI.GetJsonCompletionAsync<SuggestedSearchTermsResponse>(
            systemPrompt,
            userPrompt,
            "Suggested Search Terms",
            ct);

        if (response?.Terms == null || response.Terms.Count == 0)
            return new List<string>();

        var terms = response.Terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return terms;
    }

    private sealed class SuggestedSearchTermsResponse
    {
        public List<string> Terms { get; set; } = new();
    }
}
