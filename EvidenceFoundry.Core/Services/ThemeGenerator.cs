using System.Text.Json.Serialization;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class ThemeGenerator
{
    private readonly OpenAIService _openAI;

    public ThemeGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
    }

    /// <summary>
    /// Generates organization themes for all unique domains found in the organization list.
    /// </summary>
    public async Task<Dictionary<string, OrganizationTheme>> GenerateThemesForOrganizationsAsync(
        string topic,
        List<Organization> organizations,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var themes = new Dictionary<string, OrganizationTheme>(StringComparer.OrdinalIgnoreCase);

        // Get unique domains and their organization names
        var domainOrgs = organizations
            .Where(o => !string.IsNullOrWhiteSpace(o.Domain))
            .GroupBy(o => o.Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First().Name,
                StringComparer.OrdinalIgnoreCase);

        if (domainOrgs.Count == 0)
            return themes;

        progress?.Report($"Generating organization themes for {domainOrgs.Count} organizations...");


        var systemPrompt = @"You are a brand designer creating professional color schemes and typography for fictional corporate documents.
Given fictional organizations, create appropriate themes that match each organization's industry and personality.

Guidelines:
- Colors should be professional, readable, and high-contrast
- The primary color is used for title backgrounds and header bars
- The accent color is used for emphasis lines and highlights
- Do not mimic or reference real-world brand palettes
- Keep results plausible for corporate use (no neon or overly saturated palettes)

Font Guidelines - choose fonts that match the organization's personality:
- Law firms, banks, traditional companies: Serif fonts like 'Georgia', 'Times New Roman', 'Garamond'
- Tech companies, startups, modern firms: Clean sans-serif like 'Segoe UI', 'Arial', 'Calibri'
- Creative agencies, media companies: Modern fonts like 'Century Gothic', 'Trebuchet MS'
- Government, formal institutions: Traditional like 'Times New Roman', 'Book Antiqua'
- Healthcare, scientific: Clean professional like 'Calibri', 'Arial'
- The heading font should be bolder/more distinctive than the body font

Respond with valid JSON only.";

        var orgList = string.Join("\n", domainOrgs.Select(kv => $"- {kv.Value} ({kv.Key})"));


        var userPrompt = $@"Topic: {topic}

Generate unique color themes for the following fictional organizations:
{orgList}

Each theme should reflect the organization's industry and personality. Do not reference real brands.

Available fonts (use ONLY these - they are commonly installed):
Serif: Georgia, Times New Roman, Garamond, Book Antiqua, Palatino Linotype
Sans-serif: Segoe UI, Arial, Calibri, Trebuchet MS, Century Gothic, Verdana, Tahoma

Respond with JSON:
{{
  ""themes"": [
    {{
      ""domain"": ""string (the email domain)"",
      ""organizationName"": ""string"",
      ""themeName"": ""string (descriptive name like 'Corporate Navy' or 'Tech Vibrant')"",
      ""primaryColor"": ""RRGGBB (hex without #, main brand color)"",
      ""secondaryColor"": ""RRGGBB (complementary color)"",
      ""accentColor"": ""RRGGBB (highlight/emphasis color)"",
      ""headingFont"": ""string (font for titles/headings)"",
      ""bodyFont"": ""string (font for body text)""
    }}
  ]
}}";

        var response = await _openAI.GetJsonCompletionAsync<ThemeApiResponse>(systemPrompt, userPrompt, "Theme Generation", ct);

        if (response?.Themes != null)
        {
            foreach (var t in response.Themes)
            {
                if (string.IsNullOrEmpty(t.Domain))
                    continue;

                themes[t.Domain] = new OrganizationTheme
                {
                    Domain = t.Domain,
                    OrganizationName = t.OrganizationName ?? domainOrgs.GetValueOrDefault(t.Domain, "Organization"),
                    ThemeName = t.ThemeName ?? "Corporate",
                    PrimaryColor = SanitizeHexColor(t.PrimaryColor) ?? "2B579A",
                    SecondaryColor = SanitizeHexColor(t.SecondaryColor) ?? "5B9BD5",
                    AccentColor = SanitizeHexColor(t.AccentColor) ?? "ED7D31",
                    HeadingFont = SanitizeFont(t.HeadingFont) ?? "Segoe UI Semibold",
                    BodyFont = SanitizeFont(t.BodyFont) ?? "Segoe UI"
                };
            }
        }

        // Ensure all domains have a theme (use defaults for any missing)
        foreach (var domain in domainOrgs.Keys)
        {
            if (!themes.ContainsKey(domain))
            {
                themes[domain] = new OrganizationTheme
                {
                    Domain = domain,
                    OrganizationName = domainOrgs[domain],
                    ThemeName = "Default Corporate"
                };
            }
        }

        progress?.Report($"Generated {themes.Count} presentation themes.");
        return themes;
    }

    /// <summary>
    /// Validates and sanitizes a hex color string (removes # if present, validates length).
    /// </summary>
    private static string? SanitizeHexColor(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return null;

        color = color.Trim().TrimStart('#');

        // Must be exactly 6 hex characters
        if (color.Length != 6)
            return null;

        // Validate hex characters
        if (!color.All(c => "0123456789ABCDEFabcdef".Contains(c)))
            return null;

        return color.ToUpperInvariant();
    }

    private class ThemeApiResponse
    {
        [JsonPropertyName("themes")]
        public List<ThemeDto> Themes { get; set; } = new();
    }

    private class ThemeDto
    {
        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("organizationName")]
        public string OrganizationName { get; set; } = string.Empty;

        [JsonPropertyName("themeName")]
        public string ThemeName { get; set; } = string.Empty;

        [JsonPropertyName("primaryColor")]
        public string PrimaryColor { get; set; } = string.Empty;

        [JsonPropertyName("secondaryColor")]
        public string SecondaryColor { get; set; } = string.Empty;

        [JsonPropertyName("accentColor")]
        public string AccentColor { get; set; } = string.Empty;

        [JsonPropertyName("headingFont")]
        public string HeadingFont { get; set; } = string.Empty;

        [JsonPropertyName("bodyFont")]
        public string BodyFont { get; set; } = string.Empty;
    }

    // Allowed fonts that are commonly installed on Windows
    private static readonly HashSet<string> AllowedFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Serif
        "Georgia", "Times New Roman", "Garamond", "Book Antiqua", "Palatino Linotype",
        // Sans-serif
        "Segoe UI", "Segoe UI Semibold", "Segoe UI Light", "Arial", "Arial Black",
        "Calibri", "Calibri Light", "Trebuchet MS", "Century Gothic", "Verdana", "Tahoma"
    };

    /// <summary>
    /// Validates font name against allowed list, returns null if invalid.
    /// </summary>
    private static string? SanitizeFont(string? font)
    {
        if (string.IsNullOrWhiteSpace(font))
            return null;

        font = font.Trim();

        // Check if it's in our allowed list
        if (AllowedFonts.Contains(font))
            return font;

        // Try to find a close match (case-insensitive)
        var match = AllowedFonts.FirstOrDefault(f => f.Equals(font, StringComparison.OrdinalIgnoreCase));
        return match;
    }
}
