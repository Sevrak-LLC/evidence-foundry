using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvidenceFoundry.Models;

public static class EmailThreadTopicCatalog
{
    private const string ResourceName = "EvidenceFoundry.Resources.EmailThreadTopicCatalog.json";

    private static readonly Lazy<EmailThreadTopicCatalogData> CatalogLazy = new(LoadCatalog);

    public static IReadOnlyList<string> GetTopics(Industry industry, OrganizationType organizationType)
    {
        return GetTopicsInternal(industry, organizationType, null, null);
    }

    public static IReadOnlyList<string> GetTopics(
        Industry industry,
        OrganizationType organizationType,
        DepartmentName department,
        RoleName role)
    {
        return GetTopicsInternal(industry, organizationType, department, role);
    }

    public static IReadOnlyList<string> GetCommonTopics()
    {
        return BuildTopicList(Catalog.Global);
    }

    public static IReadOnlyList<string> GetMasterTopicList()
    {
        var topics = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDistinct(topics, seen, Catalog.Global);

        foreach (var industry in Catalog.Industries.Values)
        {
            AddDistinct(topics, seen, industry.Topics);

            foreach (var orgType in industry.OrganizationTypes.Values)
            {
                AddDistinct(topics, seen, orgType.Topics);

                foreach (var department in orgType.Departments.Values)
                {
                    AddDistinct(topics, seen, department.Topics);

                    foreach (var role in department.Roles.Values)
                    {
                        AddDistinct(topics, seen, role);
                    }
                }
            }
        }

        return topics;
    }

    private static EmailThreadTopicCatalogData Catalog => CatalogLazy.Value;

    private static EmailThreadTopicCatalogData LoadCatalog()
    {
        var assembly = typeof(EmailThreadTopicCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
            throw new InvalidOperationException($"Missing email thread topic catalog resource '{ResourceName}'.");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var catalog = JsonSerializer.Deserialize<EmailThreadTopicCatalogData>(json, options);
        if (catalog == null)
            throw new InvalidOperationException("Email thread topic catalog is empty or invalid.");

        return catalog;
    }

    private static IReadOnlyList<string> GetTopicsInternal(
        Industry industry,
        OrganizationType organizationType,
        DepartmentName? department,
        RoleName? role)
    {
        var topics = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDistinct(topics, seen, Catalog.Global);

        var industryConfig = GetIndustryConfig(industry);
        if (industryConfig == null)
            return topics;

        var orgTypeConfig = GetOrganizationTypeConfig(industryConfig, organizationType);

        if (department.HasValue && role.HasValue && orgTypeConfig != null)
        {
            if (orgTypeConfig.Departments.TryGetValue(department.Value, out var departmentConfig) &&
                departmentConfig.Roles.TryGetValue(role.Value, out var roleTopics))
            {
                AddDistinct(topics, seen, roleTopics);
                return topics;
            }
        }

        AddDistinct(topics, seen, industryConfig.Topics);

        if (orgTypeConfig != null)
        {
            AddDistinct(topics, seen, orgTypeConfig.Topics);

            if (department.HasValue &&
                orgTypeConfig.Departments.TryGetValue(department.Value, out var departmentConfig))
            {
                AddDistinct(topics, seen, departmentConfig.Topics);
            }
        }

        return topics;
    }

    private static IndustryTopicGroup? GetIndustryConfig(Industry industry)
    {
        if (Catalog.Industries.TryGetValue(industry, out var industryConfig))
            return industryConfig;

        return Catalog.Industries.TryGetValue(Industry.Other, out var fallback)
            ? fallback
            : null;
    }

    private static OrganizationTypeTopicGroup? GetOrganizationTypeConfig(
        IndustryTopicGroup industryConfig,
        OrganizationType organizationType)
    {
        return industryConfig.OrganizationTypes.TryGetValue(organizationType, out var orgTypeConfig)
            ? orgTypeConfig
            : null;
    }

    private static IReadOnlyList<string> BuildTopicList(TopicGroup group)
    {
        var topics = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDistinct(topics, seen, group);
        return topics;
    }

    private static void AddDistinct(List<string> target, HashSet<string> seen, TopicGroup group)
    {
        if (group.Scoped == null)
            return;

        AddDistinct(target, seen, group.Scoped.Send);
        AddDistinct(target, seen, group.Scoped.Receive);
    }

    private static void AddDistinct(List<string> target, HashSet<string> seen, ScopedDirectionGroup group)
    {
        AddDistinct(target, seen, group.Internal);
        AddDistinct(target, seen, group.External);
        AddDistinct(target, seen, group.Both);
    }

    private static void AddDistinct(List<string> target, HashSet<string> seen, IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            var normalized = item.Trim();
            if (seen.Add(normalized))
            {
                target.Add(normalized);
            }
        }
    }

    private sealed class EmailThreadTopicCatalogData
    {
        [JsonPropertyName("global")]
        public TopicGroup Global { get; init; } = new();

        [JsonPropertyName("industries")]
        public Dictionary<Industry, IndustryTopicGroup> Industries { get; init; } = new();
    }

    private sealed class IndustryTopicGroup
    {
        [JsonPropertyName("topics")]
        public TopicGroup Topics { get; init; } = new();

        [JsonPropertyName("organizationTypes")]
        public Dictionary<OrganizationType, OrganizationTypeTopicGroup> OrganizationTypes { get; init; } = new();
    }

    private sealed class OrganizationTypeTopicGroup
    {
        [JsonPropertyName("topics")]
        public TopicGroup Topics { get; init; } = new();

        [JsonPropertyName("departments")]
        public Dictionary<DepartmentName, DepartmentTopicGroup> Departments { get; init; } = new();
    }

    private sealed class DepartmentTopicGroup
    {
        [JsonPropertyName("topics")]
        public TopicGroup Topics { get; init; } = new();

        [JsonPropertyName("roles")]
        public Dictionary<RoleName, TopicGroup> Roles { get; init; } = new();
    }

    private sealed class TopicGroup
    {
        [JsonPropertyName("scoped")]
        public ScopedTopicGroup? Scoped { get; init; } = new();
    }

    private sealed class ScopedTopicGroup
    {
        [JsonPropertyName("send")]
        public ScopedDirectionGroup Send { get; init; } = new();

        [JsonPropertyName("receive")]
        public ScopedDirectionGroup Receive { get; init; } = new();
    }

    private sealed class ScopedDirectionGroup
    {
        [JsonPropertyName("internal")]
        public List<string> Internal { get; init; } = new();

        [JsonPropertyName("external")]
        public List<string> External { get; init; } = new();

        [JsonPropertyName("both")]
        public List<string> Both { get; init; } = new();
    }
}
