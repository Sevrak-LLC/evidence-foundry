using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Models;

public static class CaseIssueCatalog
{
    private const string ResourceName = "EvidenceFoundry.Resources.CaseIssueCatalog.json";
    private static readonly Lazy<CaseIssueCatalogConfig> ConfigLazy = new(LoadConfig);
    private static readonly Lazy<Dictionary<string, CaseAreaDefinition>> CaseAreaLookupLazy =
        new(() => Config.CaseAreas.ToDictionary(area => area.Name, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> GetCaseAreas()
    {
        return Config.CaseAreas.Select(area => area.Name).ToList();
    }

    public static IReadOnlyList<string> GetMatterTypes(string caseArea)
    {
        var area = GetCaseArea(caseArea);
        return area.MatterTypes.Select(type => type.Name).ToList();
    }

    public static IReadOnlyList<string> GetIssues(string caseArea, string matterType)
    {
        var type = GetMatterType(caseArea, matterType);
        return type.Issues.Select(issue => issue.Name).ToList();
    }

    public static string GetIssueDescription(string caseArea, string matterType, string issue)
    {
        var issueDefinition = GetIssue(caseArea, matterType, issue);
        return issueDefinition.Description;
    }

    private static CaseIssueCatalogConfig Config => ConfigLazy.Value;
    private static Dictionary<string, CaseAreaDefinition> CaseAreaLookup => CaseAreaLookupLazy.Value;

    private static CaseIssueCatalogConfig LoadConfig()
    {
        var assembly = typeof(CaseIssueCatalog).Assembly;
        return EmbeddedResourceLoader.LoadJsonResource<CaseIssueCatalogConfig>(
            assembly,
            ResourceName,
            JsonSerializationDefaults.CaseInsensitive,
            $"Missing case issue catalog resource '{ResourceName}'.",
            "Case issue catalog config is empty or invalid.");
    }

    private static CaseAreaDefinition GetCaseArea(string caseArea)
    {
        if (string.IsNullOrWhiteSpace(caseArea))
            throw new ArgumentException("Case area is required.", nameof(caseArea));

        if (!CaseAreaLookup.TryGetValue(caseArea.Trim(), out var area))
            throw new ArgumentException($"Unknown case area '{caseArea}'.", nameof(caseArea));

        return area;
    }

    private static MatterTypeDefinition GetMatterType(string caseArea, string matterType)
    {
        var area = GetCaseArea(caseArea);
        if (string.IsNullOrWhiteSpace(matterType))
            throw new ArgumentException("Matter type is required.", nameof(matterType));

        var match = area.MatterTypes.FirstOrDefault(type =>
            string.Equals(type.Name, matterType.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new ArgumentException($"Unknown matter type '{matterType}' for case area '{caseArea}'.", nameof(matterType));

        return match;
    }

    private static IssueDefinition GetIssue(string caseArea, string matterType, string issue)
    {
        var type = GetMatterType(caseArea, matterType);
        if (string.IsNullOrWhiteSpace(issue))
            throw new ArgumentException("Issue is required.", nameof(issue));

        var match = type.Issues.FirstOrDefault(definition =>
            string.Equals(definition.Name, issue.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new ArgumentException($"Unknown issue '{issue}' for matter type '{matterType}'.", nameof(issue));

        return match;
    }

    private sealed class CaseIssueCatalogConfig
    {
        [JsonPropertyName("caseAreas")]
        public List<CaseAreaDefinition> CaseAreas { get; init; } = new();
    }

    private sealed class CaseAreaDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("matterTypes")]
        public List<MatterTypeDefinition> MatterTypes { get; init; } = new();
    }

    private sealed class MatterTypeDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("issues")]
        public List<IssueDefinition> Issues { get; init; } = new();
    }

    private sealed class IssueDefinition
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; init; } = string.Empty;
    }
}
