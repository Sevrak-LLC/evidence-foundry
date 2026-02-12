using System.Text.Json.Serialization;

namespace EvidenceFoundry.Models;

internal sealed class TopicGenerationModel
{
    [JsonPropertyName("tags")]
    public List<TopicTag> Tags { get; init; } = new();

    [JsonPropertyName("department_default_tag_multipliers")]
    public Dictionary<string, Dictionary<string, double>> DepartmentDefaultTagMultipliers { get; init; } = new();

    [JsonPropertyName("role_default_tag_multipliers")]
    public Dictionary<string, Dictionary<string, double>> RoleDefaultTagMultipliers { get; init; } = new();

    [JsonPropertyName("industry_multipliers")]
    public Dictionary<string, IndustryMultipliers> IndustryMultipliers { get; init; } = new();

    [JsonPropertyName("archetypes")]
    public List<TopicArchetype> Archetypes { get; init; } = new();
}

internal sealed class TopicTag
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

internal sealed class IndustryMultipliers
{
    [JsonPropertyName("tag_multipliers")]
    public Dictionary<string, double> TagMultipliers { get; init; } = new();

    [JsonPropertyName("category_multipliers")]
    public Dictionary<string, double> CategoryMultipliers { get; init; } = new();

    [JsonPropertyName("intent_multipliers")]
    public Dictionary<string, double> IntentMultipliers { get; init; } = new();

    [JsonPropertyName("archetype_id_overrides")]
    public Dictionary<string, double> ArchetypeIdOverrides { get; init; } = new();
}

internal sealed class TopicArchetype
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("intent")]
    public string Intent { get; init; } = string.Empty;

    [JsonPropertyName("base_weight")]
    public double BaseWeight { get; init; }

    [JsonPropertyName("archetype_tags")]
    public List<string> ArchetypeTags { get; init; } = new();

    [JsonPropertyName("constraints")]
    public ArchetypeConstraints Constraints { get; init; } = new();

    [JsonPropertyName("entities_required")]
    public List<string> EntitiesRequired { get; init; } = new();

    [JsonPropertyName("entities_optional")]
    public List<string> EntitiesOptional { get; init; } = new();

    [JsonPropertyName("seasonality")]
    public ArchetypeSeasonality? Seasonality { get; init; }

    [JsonPropertyName("archetype_subject_prompt")]
    public string ArchetypeSubjectPrompt { get; init; } = string.Empty;

    [JsonPropertyName("archetype_body_prompt")]
    public string ArchetypeBodyPrompt { get; init; } = string.Empty;
}

internal sealed class ArchetypeConstraints
{
    [JsonPropertyName("sender_tags_any")]
    public List<string>? SenderTagsAny { get; init; }

    [JsonPropertyName("recipient_tags_any")]
    public List<string>? RecipientTagsAny { get; init; }

    [JsonPropertyName("cc_tags_any")]
    public List<string>? CcTagsAny { get; init; }

    [JsonPropertyName("relationship_modifiers")]
    public Dictionary<string, double> RelationshipModifiers { get; init; } = new();
}

internal sealed class ArchetypeSeasonality
{
    [JsonPropertyName("months_boost")]
    public Dictionary<string, double> MonthsBoost { get; init; } = new();
}
