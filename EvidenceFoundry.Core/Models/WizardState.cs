using System.Security.Cryptography;
using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Models;

public class WizardState
{
    private int _generationSeed = RandomNumberGenerator.GetInt32(int.MaxValue);
    private ThreadSafeRandom? _generationRandom;

    // Step 1 - API Configuration
    public string ApiKey { get; set; } = string.Empty;
    public string SelectedModel { get; set; } = "gpt-4o-mini";
    public bool ConnectionTested { get; set; }

    // Model Configurations
    public List<AIModelConfig> AvailableModelConfigs { get; set; } = AIModelConfigStore.LoadModelConfigs();
    public AIModelConfig? SelectedModelConfig => AvailableModelConfigs.FirstOrDefault(m => m.ModelId == SelectedModel);

    // Token Usage Tracking
    public TokenUsageTracker UsageTracker { get; } = new();

    public int GenerationSeed
    {
        get => _generationSeed;
        set
        {
            _generationSeed = value;
            _generationRandom = null;
        }
    }

    public Random GenerationRandom => _generationRandom ??= new ThreadSafeRandom(_generationSeed);

    // Step 2 - Case Issue Selection
    public string CaseArea { get; set; } = string.Empty;
    public string MatterType { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string IssueDescription { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string AdditionalInstructions { get; set; } = string.Empty;
    public string PlaintiffIndustry { get; set; } = "Random";
    public string DefendantIndustry { get; set; } = "Random";
    public int PlaintiffOrganizationCount { get; set; } = 1;
    public int DefendantOrganizationCount { get; set; } = 1;

    // Media type hints (inform storyline generation)
    public bool WantsDocuments { get; set; } = true;
    public bool WantsImages { get; set; } = false;
    public bool WantsVoicemails { get; set; } = false;

    // Step 3 - World Model
    public World? WorldModel { get; set; }

    // Step 4 - Storyline
    public Storyline? Storyline { get; set; }

    // Step 5 - Characters
    public List<Organization> Organizations { get; set; } = new();
    public List<Character> Characters { get; set; } = new();
    public string CompanyDomain { get; set; } = string.Empty;

    // Organization themes by domain (used for emails and documents)
    public Dictionary<string, OrganizationTheme> DomainThemes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Step 6 - Story Beats

    // Step 7 - Generation Config
    public GenerationConfig Config { get; set; } = new();

    // Step 8-9 - Results
    public List<EmailThread> GeneratedThreads { get; set; } = new();
    public GenerationResult? Result { get; set; }

    // Helper to create OpenAI service with tracking
    public Services.OpenAIService CreateOpenAIService()
    {
        var modelConfig = SelectedModelConfig;
        if (modelConfig != null)
        {
            return new Services.OpenAIService(ApiKey, modelConfig, UsageTracker, GenerationRandom);
        }
        return new Services.OpenAIService(ApiKey, SelectedModel, GenerationRandom);
    }

    public IEnumerable<Storyline> GetActiveStorylines()
    {
        return Storyline != null ? new[] { Storyline } : Enumerable.Empty<Storyline>();
    }

    public string TopicDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Topic))
                return Topic;

            if (string.IsNullOrWhiteSpace(CaseArea)
                || string.IsNullOrWhiteSpace(MatterType)
                || string.IsNullOrWhiteSpace(Issue))
                return string.Empty;

            return $"{CaseArea} > {MatterType} > {Issue}";
        }
    }

    public string StorylineIssueDescription =>
        !string.IsNullOrWhiteSpace(IssueDescription) ? IssueDescription : Topic;

    public (int BeatCount,
        int ThreadCount,
        int HotThreadCount,
        int RelevantThreadCount,
        int NonRelevantThreadCount,
        int EmailCount,
        int EstimatedDocumentAttachments,
        int EstimatedImageAttachments,
        int EstimatedVoicemailAttachments,
        int EstimatedCalendarInviteChecks,
        DateTime? StartDate,
        DateTime? EndDate) GetGenerationSummary()
    {
        var storyline = Storyline;
        if (storyline == null)
        {
            return (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
        }

        var beatCount = storyline.Beats?.Count ?? 0;
        var threadCount = storyline.ThreadCount;
        var threads = storyline.Beats?.SelectMany(b => b.Threads ?? new List<EmailThread>()).ToList()
            ?? new List<EmailThread>();
        var hotThreadCount = threads.Count(t => t.IsHot);
        var responsiveThreadCount = threads.Count(t =>
            t.Relevance == EmailThread.ThreadRelevance.Responsive || t.IsHot);
        var relevantThreadCount = Math.Max(0, responsiveThreadCount - hotThreadCount);
        var nonRelevantThreadCount = Math.Max(0, threads.Count - responsiveThreadCount);
        var emailCount = storyline.EmailCount;
        var startDate = storyline.StartDate;
        var endDate = storyline.EndDate;

        if (emailCount <= 0)
        {
            return (
                beatCount,
                threadCount,
                hotThreadCount,
                relevantThreadCount,
                nonRelevantThreadCount,
                emailCount,
                0,
                0,
                0,
                0,
                startDate,
                endDate);
        }

        var config = Config;
        var estimatedDocs = config.AttachmentPercentage > 0 && config.EnabledAttachmentTypes.Count > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.AttachmentPercentage / 100.0))
            : 0;
        var estimatedImages = config.IncludeImages && config.ImagePercentage > 0
            ? Math.Max(1, (int)Math.Round(emailCount * config.ImagePercentage / 100.0))
            : 0;
        var estimatedVoicemails = config.IncludeVoicemails && config.VoicemailPercentage > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.VoicemailPercentage / 100.0))
            : 0;
        var estimatedCalendarChecks = config.IncludeCalendarInvites && config.CalendarInvitePercentage > 0
            ? Math.Max(1, (int)Math.Round(emailCount * config.CalendarInvitePercentage / 100.0))
            : 0;

        return (
            beatCount,
            threadCount,
            hotThreadCount,
            relevantThreadCount,
            nonRelevantThreadCount,
            emailCount,
            estimatedDocs,
            estimatedImages,
            estimatedVoicemails,
            estimatedCalendarChecks,
            startDate,
            endDate);
    }

    // Legacy - Available OpenAI models (for backward compatibility)
    public static readonly string[] AvailableModels = new[]
    {
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-4-turbo",
        "gpt-4",
        "gpt-3.5-turbo"
    };
}
