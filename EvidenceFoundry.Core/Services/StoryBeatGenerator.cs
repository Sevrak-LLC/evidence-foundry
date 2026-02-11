using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class StoryBeatGenerator
{
    private readonly OpenAIService _openAI;

    public StoryBeatGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
    }

    public async Task<List<StoryBeat>> GenerateStoryBeatsAsync(
        string topic,
        Storyline storyline,
        IReadOnlyList<Organization> organizations,
        IReadOnlyList<Character> characters,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        ValidateGenerationInputs(storyline, organizations, characters);

        var (startDate, endDate) = GetStorylineDateRange(storyline);

        progress?.Report("Generating story beats...");

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Narrative Enricher.

Your job: expand a storyline summary into a structured, ordered list of story beats suitable for generating realistic fictional emails.

Rules (strict):
- Do NOT write dialog or email content.
- Use only fictional people and organizations provided in the context.
- Use realistic corporate narrative beats with ambiguity and plausible motivations.
- Do NOT fully resolve the core dispute; the final beat should leave open questions, pending decisions, or ongoing conflict.
- Each beat's plot MUST include newline characters (use \n) to create readable paragraphs.
- Beats must be chronological and non-overlapping.
- The first beat MUST start on the storyline start date.
- The last beat MUST end on the storyline end date.
- The end date of a beat MUST be strictly before the next beat's start date.
- Choose the number of beats based on story complexity and date range; keep it reasonable.
 - Include characters from the provided pool, especially those named in the summary, but do NOT force every character into the story.");

        var orgJson = PromptPayloadSerializer.SerializeOrganizations(organizations);
        var characterJson = PromptPayloadSerializer.SerializeCharacters(organizations);

        var schema = """
{
  "beats": [
    {
      "name": "string (friendly beat name)",
      "plot": "string (2-4 short paragraphs separated by \\n; no dialog or email content)",
      "startDate": "YYYY-MM-DD",
      "endDate": "YYYY-MM-DD"
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}

Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Storyline date range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}

Organizations (JSON):
{orgJson}

Character pool (JSON):
{characterJson}", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<StoryBeatApiResponse>(
            systemPrompt,
            userPrompt,
            "Story Beat Generation",
            ct);

        var beats = BuildBeatsFromResponse(response);
        await NormalizeRepairAndValidateBeatsAsync(
            topic,
            storyline,
            beats,
            startDate,
            endDate,
            ct);

        return beats;
    }

    private static void ValidateGenerationInputs(
        Storyline storyline,
        IReadOnlyList<Organization> organizations,
        IReadOnlyList<Character> characters)
    {
        if (storyline == null)
            throw new ArgumentNullException(nameof(storyline));
        if (string.IsNullOrWhiteSpace(storyline.Summary))
            throw new InvalidOperationException("Storyline summary is required before generating story beats.");
        if (!storyline.StartDate.HasValue || !storyline.EndDate.HasValue)
            throw new InvalidOperationException("Storyline must have a start and end date before generating story beats.");
        if (organizations == null || organizations.Count == 0)
            throw new InvalidOperationException("At least one organization is required before generating story beats.");
        if (characters == null || characters.Count < 2)
            throw new InvalidOperationException("At least two characters are required before generating story beats.");
    }

    private static (DateTime startDate, DateTime endDate) GetStorylineDateRange(Storyline storyline)
    {
        var startDate = storyline.StartDate!.Value.Date;
        var endDate = storyline.EndDate!.Value.Date;
        if (endDate < startDate)
            throw new InvalidOperationException("Storyline end date must be on or after the start date.");

        return (startDate, endDate);
    }

    private static List<StoryBeat> BuildBeatsFromResponse(StoryBeatApiResponse? response)
    {
        if (response?.Beats == null || response.Beats.Count == 0)
            throw new InvalidOperationException("Failed to generate story beats.");

        var beats = new List<StoryBeat>();
        for (var i = 0; i < response.Beats.Count; i++)
        {
            var beat = response.Beats[i];
            if (string.IsNullOrWhiteSpace(beat.Name))
                throw new InvalidOperationException($"Story beat {i + 1} is missing a name.");
            if (string.IsNullOrWhiteSpace(beat.Plot))
                throw new InvalidOperationException($"Story beat '{beat.Name}' is missing plot details.");
            if (!beat.Plot.Contains('\n'))
                throw new InvalidOperationException($"Story beat '{beat.Name}' must include newline characters in the plot text.");

            if (!DateTime.TryParse(beat.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var beatStart))
                throw new InvalidOperationException($"Story beat '{beat.Name}' has an invalid startDate.");
            if (!DateTime.TryParse(beat.EndDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var beatEnd))
                throw new InvalidOperationException($"Story beat '{beat.Name}' has an invalid endDate.");

            beats.Add(new StoryBeat
            {
                Name = beat.Name.Trim(),
                Plot = beat.Plot.Trim(),
                StartDate = beatStart.Date,
                EndDate = beatEnd.Date
            });
        }

        return beats;
    }

    private async Task NormalizeRepairAndValidateBeatsAsync(
        string topic,
        Storyline storyline,
        List<StoryBeat> beats,
        DateTime startDate,
        DateTime endDate,
        CancellationToken ct)
    {
        DateHelper.NormalizeStoryBeats(beats, startDate, endDate);
        var invalidIndex = FindFirstInvalidBeatIndex(beats, startDate, endDate);
        if (invalidIndex.HasValue)
        {
            var repaired = await RepairStoryBeatDatesAsync(
                topic,
                storyline,
                beats,
                invalidIndex.Value,
                startDate,
                endDate,
                ct);

            if (repaired)
            {
                DateHelper.NormalizeStoryBeats(beats, startDate, endDate);
            }
        }

        try
        {
            ValidateStoryBeats(beats, startDate, endDate);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"{ex.Message} Options: regenerate story beats, allow deterministic redistribution across the date range, or relax strict non-overlap/same-day boundaries.");
        }
    }

    internal static void ValidateStoryBeats(IReadOnlyList<StoryBeat> beats, DateTime storylineStart, DateTime storylineEnd)
    {
        if (beats.Count == 0)
            throw new InvalidOperationException("At least one story beat is required.");

        if (beats[0].StartDate.Date != storylineStart.Date)
            throw new InvalidOperationException("The first story beat must start on the storyline start date.");

        if (beats[^1].EndDate.Date != storylineEnd.Date)
            throw new InvalidOperationException("The last story beat must end on the storyline end date.");

        for (var i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            if (beat.EndDate.Date < beat.StartDate.Date)
                throw new InvalidOperationException($"Story beat '{beat.Name}' has an end date before its start date.");

            if (beat.StartDate.Date < storylineStart.Date || beat.EndDate.Date > storylineEnd.Date)
                throw new InvalidOperationException($"Story beat '{beat.Name}' falls outside the storyline date range.");

            if (i == 0)
                continue;

            var previous = beats[i - 1];
            if (previous.EndDate.Date >= beat.StartDate.Date)
                throw new InvalidOperationException($"Story beat '{beat.Name}' must start after the previous beat ends.");
        }
    }

    internal static int? FindFirstInvalidBeatIndex(IReadOnlyList<StoryBeat> beats, DateTime storylineStart, DateTime storylineEnd)
    {
        if (beats.Count == 0)
            return 0;

        if (beats[0].StartDate.Date != storylineStart.Date)
            return 0;

        if (beats[^1].EndDate.Date != storylineEnd.Date)
            return beats.Count - 1;

        for (var i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            if (beat.EndDate.Date < beat.StartDate.Date)
                return i;

            if (beat.StartDate.Date < storylineStart.Date || beat.EndDate.Date > storylineEnd.Date)
                return i;

            if (i == 0)
                continue;

            var previous = beats[i - 1];
            if (previous.EndDate.Date >= beat.StartDate.Date)
                return i;
        }

        return null;
    }

    private async Task<bool> RepairStoryBeatDatesAsync(
        string topic,
        Storyline storyline,
        IReadOnlyList<StoryBeat> beats,
        int startIndex,
        DateTime storylineStart,
        DateTime storylineEnd,
        CancellationToken ct)
    {
        if (startIndex < 0 || startIndex >= beats.Count)
            return false;

        var fixedBeats = beats.Take(startIndex).ToList();
        var tailBeats = beats.Skip(startIndex).ToList();
        var previousEnd = fixedBeats.Count > 0 ? fixedBeats[^1].EndDate.Date : (DateTime?)null;

        var fixedJson = JsonSerializer.Serialize(fixedBeats.Select(b => new
        {
            name = b.Name,
            startDate = b.StartDate.ToString("yyyy-MM-dd"),
            endDate = b.EndDate.ToString("yyyy-MM-dd")
        }), JsonSerializationDefaults.Indented);

        var tailJson = JsonSerializer.Serialize(tailBeats.Select(b => new
        {
            name = b.Name,
            plot = b.Plot,
            startDate = b.StartDate.ToString("yyyy-MM-dd"),
            endDate = b.EndDate.ToString("yyyy-MM-dd")
        }), JsonSerializationDefaults.Indented);

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Timeline Repair Agent.

Your job: repair ONLY the dates for the story beats provided, keeping their order, names, and plots unchanged.

Rules (strict):
- Do NOT change beat names or plots.
- Dates must be chronological and non-overlapping.
- If a previous beat end date is provided, the first repaired beat must start AFTER that date (strictly).
- The last repaired beat MUST end on the storyline end date.
 - Keep durations realistic based on each beat's plot.");

        var previousEndText = previousEnd.HasValue ? previousEnd.Value.ToString("yyyy-MM-dd") : "(none)";

        var repairSchema = """
{
  "beats": [
    {
      "name": "string (same as input)",
      "startDate": "YYYY-MM-DD",
      "endDate": "YYYY-MM-DD"
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}

Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Storyline date range: {storylineStart:yyyy-MM-dd} to {storylineEnd:yyyy-MM-dd}
Previous beat end date (must start AFTER this): {previousEndText}

Fixed beats (do NOT change):
{fixedJson}

Beats to re-date (preserve order, names, plots):
{tailJson}", PromptScaffolding.JsonSchemaSection(repairSchema, "Beats to re-date ONLY."));

        var response = await _openAI.GetJsonCompletionAsync<StoryBeatDateRepairResponse>(
            systemPrompt,
            userPrompt,
            "Story Beat Date Repair",
            ct);

        if (response?.Beats == null || response.Beats.Count != tailBeats.Count)
            return false;

        for (var i = 0; i < response.Beats.Count; i++)
        {
            var repaired = response.Beats[i];
            if (!DateTime.TryParse(repaired.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !DateTime.TryParse(repaired.EndDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                return false;
            }

            var target = tailBeats[i];
            target.StartDate = start.Date;
            target.EndDate = end.Date;
        }

        return true;
    }


    private class StoryBeatApiResponse
    {
        [JsonPropertyName("beats")]
        public List<StoryBeatDto> Beats { get; set; } = new();
    }

    private class StoryBeatDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("plot")]
        public string Plot { get; set; } = string.Empty;

        [JsonPropertyName("startDate")]
        public string StartDate { get; set; } = string.Empty;

        [JsonPropertyName("endDate")]
        public string EndDate { get; set; } = string.Empty;
    }

    private class StoryBeatDateRepairResponse
    {
        [JsonPropertyName("beats")]
        public List<StoryBeatDateRepairDto> Beats { get; set; } = new();
    }

    private class StoryBeatDateRepairDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("startDate")]
        public string StartDate { get; set; } = string.Empty;

        [JsonPropertyName("endDate")]
        public string EndDate { get; set; } = string.Empty;
    }
}
