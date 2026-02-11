using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EvidenceFoundry.Models;

public partial class AIModelConfig
{
    public const int DefaultMaxOutputTokens = 1200;
    public const int DefaultMaxJsonOutputTokens = 3000;
    private const string DefaultConfigFileName = "model-configs.json";
    private static readonly Regex ModelIdRegex = ModelIdRegexGenerated();
    private static readonly JsonSerializerOptions WriteIndentedJsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions ReadCaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex("^[A-Za-z0-9_.:-]+$", RegexOptions.Compiled)]
    private static partial Regex ModelIdRegexGenerated();

    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal InputTokenPricePerMillion { get; set; }
    public decimal OutputTokenPricePerMillion { get; set; }
    public bool IsDefault { get; set; }
    public int MaxOutputTokens { get; set; } = DefaultMaxOutputTokens;
    public int MaxJsonOutputTokens { get; set; } = DefaultMaxJsonOutputTokens;

    public decimal CalculateInputCost(int tokens) =>
        tokens * InputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateOutputCost(int tokens) =>
        tokens * OutputTokenPricePerMillion / 1_000_000m;

    public decimal CalculateTotalCost(int inputTokens, int outputTokens) =>
        CalculateInputCost(inputTokens) + CalculateOutputCost(outputTokens);

    public override string ToString() => DisplayName;

    public static bool IsValidModelId(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        return ModelIdRegex.IsMatch(modelId.Trim());
    }

    public static List<AIModelConfig> LoadModelConfigs()
    {
        var userPath = GetUserConfigPath();
        var configs = LoadFromPath(userPath);
        if (configs.Count == 0)
        {
            EnsureDefaultConfigOnDisk();
            var appDefaults = GetAppDefaultPath();
            configs = LoadFromPath(appDefaults);
        }

        configs = ValidateAndNormalize(configs);
        if (configs.Count == 0)
        {
            configs = new List<AIModelConfig>
            {
                new()
                {
                    ModelId = "gpt-4o-mini",
                    DisplayName = "GPT-4o Mini",
                    InputTokenPricePerMillion = 0.15m,
                    OutputTokenPricePerMillion = 0.60m,
                    IsDefault = true
                }
            };
        }

        return configs;
    }

    public static List<AIModelConfig> GetDefaultModels()
    {
        EnsureDefaultConfigOnDisk();
        var configs = LoadFromPath(GetAppDefaultPath());
        configs = ValidateAndNormalize(configs);
        return configs.Count > 0 ? configs : LoadModelConfigs();
    }

    public static void SaveModelConfigs(IEnumerable<AIModelConfig> models)
    {
        var validated = ValidateAndNormalize(models);
        var path = GetUserConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(validated, WriteIndentedJsonOptions);

        File.WriteAllText(path, json);
    }

    private static string GetUserConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "EvidenceFoundry");
        return Path.Combine(folder, DefaultConfigFileName);
    }

    private static string GetAppDefaultPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Resources", DefaultConfigFileName);
    }

    private static void EnsureDefaultConfigOnDisk()
    {
        var path = GetAppDefaultPath();
        if (File.Exists(path))
            return;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = GetEmbeddedDefaultConfigStream();
            if (stream == null)
                return;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json))
                return;

            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to extract default model config to '{path}'. Error: {ex.Message}");
        }
    }

    private static Stream? GetEmbeddedDefaultConfigStream()
    {
        var assembly = typeof(AIModelConfig).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.EndsWith("Resources.model-configs.json", StringComparison.OrdinalIgnoreCase));

        return resourceName != null ? assembly.GetManifestResourceStream(resourceName) : null;
    }

    private static List<AIModelConfig> LoadFromPath(string path)
    {
        if (!File.Exists(path))
            return new List<AIModelConfig>();

        try
        {
            var json = File.ReadAllText(path);
            var configs = JsonSerializer.Deserialize<List<AIModelConfig>>(json, ReadCaseInsensitiveJsonOptions);
            return configs ?? new List<AIModelConfig>();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to read model config at '{path}'. Using defaults. Error: {ex.Message}");
            return new List<AIModelConfig>();
        }
    }

    private static List<AIModelConfig> ValidateAndNormalize(IEnumerable<AIModelConfig> configs)
    {
        var result = new List<AIModelConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in configs)
        {
            if (!TryNormalizeConfig(config, seen, out var normalized))
                continue;

            result.Add(normalized);
        }

        if (result.Count == 0)
            return result;

        EnsureSingleDefault(result);

        return result;
    }

    private static bool TryNormalizeConfig(
        AIModelConfig? config,
        HashSet<string> seen,
        out AIModelConfig normalized)
    {
        normalized = default!;
        if (config == null)
            return false;

        var modelId = (config.ModelId ?? string.Empty).Trim();
        if (!IsValidModelId(modelId))
            return false;

        if (config.InputTokenPricePerMillion < 0 || config.OutputTokenPricePerMillion < 0)
            return false;

        if (!seen.Add(modelId))
            return false;

        var displayName = string.IsNullOrWhiteSpace(config.DisplayName)
            ? modelId
            : config.DisplayName.Trim();

        var maxOutputTokens = config.MaxOutputTokens > 0
            ? config.MaxOutputTokens
            : DefaultMaxOutputTokens;
        var maxJsonOutputTokens = config.MaxJsonOutputTokens > 0
            ? config.MaxJsonOutputTokens
            : DefaultMaxJsonOutputTokens;

        normalized = new AIModelConfig
        {
            ModelId = modelId,
            DisplayName = displayName,
            InputTokenPricePerMillion = config.InputTokenPricePerMillion,
            OutputTokenPricePerMillion = config.OutputTokenPricePerMillion,
            IsDefault = config.IsDefault,
            MaxOutputTokens = maxOutputTokens,
            MaxJsonOutputTokens = maxJsonOutputTokens
        };

        return true;
    }

    private static void EnsureSingleDefault(List<AIModelConfig> configs)
    {
        var defaultIndex = -1;
        for (var i = 0; i < configs.Count; i++)
        {
            if (!configs[i].IsDefault)
                continue;

            defaultIndex = i;
            break;
        }
        if (defaultIndex < 0)
        {
            configs[0].IsDefault = true;
            return;
        }

        for (var i = 0; i < configs.Count; i++)
        {
            configs[i].IsDefault = i == defaultIndex;
        }
    }
}
