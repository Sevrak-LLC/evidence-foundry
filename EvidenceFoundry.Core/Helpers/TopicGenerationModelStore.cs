using System.Text.Json;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

internal static class TopicGenerationModelStore
{
    private const string ResourceName = "EvidenceFoundry.Resources.TopicGenerationModel.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Lazy<TopicGenerationModel> ModelLazy = new(() =>
        EmbeddedResourceLoader.LoadJsonResource<TopicGenerationModel>(
            typeof(TopicGenerationModelStore).Assembly,
            ResourceName,
            JsonOptions,
            $"Missing topic generation model resource '{ResourceName}'.",
            $"Invalid topic generation model resource '{ResourceName}'."));

    public static TopicGenerationModel Model => ModelLazy.Value;
}
