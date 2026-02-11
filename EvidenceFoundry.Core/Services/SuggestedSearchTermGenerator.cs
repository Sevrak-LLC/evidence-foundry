using System.Text.RegularExpressions;

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

        return FilterTermsAgainstEmail(response.Terms, exportedEmail, 3);
    }

    private sealed class SuggestedSearchTermsResponse
    {
        public List<string> Terms { get; set; } = new();
    }

    private static readonly Regex QuotedPhraseRegex = new("\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new("[A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly HashSet<string> OperatorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "near", "w", "pre", "within", "adj", "xof"
    };

    internal static List<string> FilterTermsAgainstEmail(
        IEnumerable<string> terms,
        string exportedEmail,
        int maxTerms)
    {
        if (string.IsNullOrWhiteSpace(exportedEmail) || terms == null)
            return new List<string>();

        var normalizedEmail = NormalizeWhitespace(exportedEmail).ToLowerInvariant();
        var emailTokens = ExtractTokens(exportedEmail);

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
                continue;

            var trimmed = term.Trim();
            if (!seen.Add(trimmed))
                continue;

            if (!TermReferencesEmail(trimmed, emailTokens, normalizedEmail))
                continue;

            results.Add(trimmed);
            if (results.Count >= maxTerms)
                break;
        }

        return results;
    }

    private static bool TermReferencesEmail(string term, HashSet<string> emailTokens, string normalizedEmail)
    {
        var hasLiteral = false;

        foreach (Match match in QuotedPhraseRegex.Matches(term))
        {
            var phrase = NormalizeWhitespace(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            hasLiteral = true;
            var normalizedPhrase = phrase.ToLowerInvariant();
            if (!normalizedEmail.Contains(normalizedPhrase, StringComparison.Ordinal))
                return false;
        }

        var unquoted = QuotedPhraseRegex.Replace(term, " ");
        foreach (Match match in TokenRegex.Matches(unquoted))
        {
            var token = match.Value;
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var lower = token.ToLowerInvariant();
            if (lower.Length < 2)
                continue;
            if (OperatorTokens.Contains(lower))
                continue;
            if (int.TryParse(lower, out _))
                continue;

            hasLiteral = true;
            if (!emailTokens.Contains(lower))
                return false;
        }

        return hasLiteral;
    }

    private static HashSet<string> ExtractTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TokenRegex.Matches(text))
        {
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                tokens.Add(match.Value.ToLowerInvariant());
            }
        }

        return tokens;
    }

    private static string NormalizeWhitespace(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        return WhitespaceRegex.Replace(trimmed, " ");
    }
}
