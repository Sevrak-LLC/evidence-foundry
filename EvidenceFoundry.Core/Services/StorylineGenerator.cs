using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using Serilog;
using Serilog.Context;

namespace EvidenceFoundry.Services;

public partial class StorylineGenerator
{
    private readonly OpenAIService _openAI;
    private readonly StoryBeatGenerator _beatGenerator;
    private readonly EmailThreadGenerator _threadGenerator;
    private readonly Random _rng;
    private readonly ILogger _logger;

    public StorylineGenerator(
        OpenAIService openAI,
        Random rng,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(openAI);
        ArgumentNullException.ThrowIfNull(rng);
        _openAI = openAI;
        _rng = rng;
        var baseLogger = logger ?? Serilog.Log.Logger;
        _logger = baseLogger.ForContext<StorylineGenerator>();
        _beatGenerator = new StoryBeatGenerator(openAI, baseLogger.ForContext<StoryBeatGenerator>());
        _threadGenerator = new EmailThreadGenerator(baseLogger.ForContext<EmailThreadGenerator>());
        Log.StorylineGeneratorInitialized(_logger);
    }

    public async Task<StorylineGenerationResult> GenerateStorylineAsync(
        StorylineGenerationRequest request,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Topic))
            throw new ArgumentException("Topic is required.", nameof(request));

        var topic = request.Topic;
        var issueDescription = request.IssueDescription;
        var additionalInstructions = request.AdditionalInstructions ?? string.Empty;
        var mediaHints = BuildMediaHints(request.WantsDocuments, request.WantsImages, request.WantsVoicemails);
        var normalizedPlaintiffCount = GenerationRequestNormalizer.NormalizePartyCount(request.PlaintiffOrganizationCount);
        var normalizedDefendantCount = GenerationRequestNormalizer.NormalizePartyCount(request.DefendantOrganizationCount);

        using var topicScope = LogContext.PushProperty("Topic", topic);
        using var plaintiffCountScope = LogContext.PushProperty("PlaintiffCount", normalizedPlaintiffCount);
        using var defendantCountScope = LogContext.PushProperty("DefendantCount", normalizedDefendantCount);

        var stopwatch = Stopwatch.StartNew();
        Log.GeneratingStoryline(_logger, normalizedPlaintiffCount, normalizedDefendantCount);

        try
        {
            progress?.Report("Generating storyline...");
            var issueContext = string.IsNullOrWhiteSpace(issueDescription) ? topic : issueDescription;
            var normalizedPlaintiffIndustry = GenerationRequestNormalizer.NormalizeIndustryPreference(request.PlaintiffIndustry);
            var normalizedDefendantIndustry = GenerationRequestNormalizer.NormalizeIndustryPreference(request.DefendantIndustry);
            var includeOtherIndustry = string.Equals(normalizedPlaintiffIndustry, nameof(Industry.Other), StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedDefendantIndustry, nameof(Industry.Other), StringComparison.OrdinalIgnoreCase);

            var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Narrative Generator.

Your job: produce fictional, narrative-driven pre-dispute setups that can be expanded into realistic corporate email corpora for eDiscovery testing.

CORE RULES (NON-NEGOTIABLE)
- Entirely fictional case: invent all companies, domains, people, teams, products, internal project names, and events. Do NOT use real company names, real people, or identifiable real-world incidents.
- Realistic workplace communication: mundane work mixed with tension. Allow minor typos, shorthand (FYI/pls), occasional slang, and imperfect memory.
- eDiscovery usefulness: narratives should naturally create discoverable relevance and evidence trails without mentioning disputes, litigation, or discovery.
- Organization roles required: include at least two distinct organizations and explicitly identify which organization is the likely plaintiff and which is the likely defendant in the eventual dispute (they must be different organizations). Do not describe filings or legal proceedings.
- Ambiguity is required: include innocent explanations, red herrings, conflicting statements, miscommunication, and ""could be nothing"" details. Do not make every clue conclusive.
- No tidy conclusions: the core tensions MUST remain unresolved with open questions, pending decisions, or ongoing conflict. Minor sub-issues may resolve.
- No edgy content: avoid explicit sexual content, graphic violence, hate/harassment/slurs, self-harm, or shock content. If the topic touches HR/retaliation, keep summaries professional and non-explicit.

SETTING
- Each narrative must indicate where it takes place (e.g., city/state/country or region). Keep it generic; do not reference real events tied to that place.

TOPIC HANDLING
- If the issue description or label contains any real entity names or looks like a real incident, replace them with fictional equivalents while preserving the underlying scenario type.

OPTIONAL eDISCOVERY HOOKS (USE WHEN THEY FIT)
You may include any of the following if they make sense for the topic and narrative beats:
- Confidentiality: NDAs, ""need-to-know,"" internal-only markings, restricted distribution.
- Policy/compliance: procurement rules, gifts/conflicts, retention, security, HR policies.
- Data/record handling: ""clean up,"" deletion, personal email, off-channel comms, device resets, vague ""don't write this down"" hints.
Keep these plausible and non-instructional (do not provide step-by-step wrongdoing guidance). Do not frame them as litigation or disputes.

SMOKING GUN (about 30% of narratives)
- Sometimes include a subtle but decisive ""smoking gun"" clue that would materially resolve the eventual dispute if found later (e.g., a forwarded email, attachment filename, invoice pattern, calendar note, file path reference, vendor quote mismatch).
- If the user explicitly requests a different rate or explicitly requires/forbids a smoking gun, follow the user.

NARRATIVE QUALITY BAR
Each narrative should be rich enough to generate many realistic emails without feeling repetitive:
- Multiple departments/roles with competing incentives (at least leadership, management, IC/SME, HR/compliance, IT/security, and legal; plus an external if relevant).
- A clear sequence of beats.
- Plausible motives (fear, ambition, resentment, loyalty, confusion, burnout) and believable misunderstandings.

OUTPUT REQUIREMENTS (STRICT)
- Output must match the specified schema exactly.
- Ensure internal consistency of names/titles/relationships/sequence of events within each narrative.
- The summary field MUST clearly state the core pre-dispute situation, who is involved, and the tensions/risks that later lead to a dispute. Do NOT mention any dispute, claim, lawsuit, litigation, arbitration, investigation, subpoena, enforcement action, discovery process, or legal proceedings.");

            var industryOptions = FormatIndustryOptionsForStorylines(includeOtherIndustry);
            var worldContext = BuildWorldModelContext(request.WorldModel);
            var worldRules = request.WorldModel != null
                ? @"WORLD MODEL CONSTRAINTS:
- Use ONLY the provided organizations and their domains. Do NOT invent or rename organizations.
- Explicitly label the provided organizations as plaintiffs/defendants as given in the world model.
- Use the provided key people by name. Do NOT invent new named people.
- You may reference additional unnamed roles/teams if needed, but do not add named individuals beyond the world model."
                : string.Empty;

            var schema = """
{
  "storylines": [
    {
      "title": "string",
      "logline": "string (1-2 sentences)",
      "summary": "string (10-15 sentences: the pre-dispute situation, who is involved, and any ambiguity)",
      "plotOutline": ["string (3-7 bullets, short sentences)"]],
      "tensionDrivers": ["string (3-6 items)"],
      "ambiguities": ["string (3-6 items)"],
      "redHerrings": ["string (2-4 items)"],
      "evidenceThemes": ["string (3-6 items)"]
    }
  ]
}
""";

            var userPrompt = PromptScaffolding.JoinSections($@"Selected Case Issue: {topic}
Issue Description: {issueContext}

Additional Instructions: {(string.IsNullOrWhiteSpace(additionalInstructions) ? "None" : additionalInstructions)}
Plaintiff Industry: {normalizedPlaintiffIndustry}
Defendant Industry: {normalizedDefendantIndustry}
Plaintiff Organization Count: {normalizedPlaintiffCount}
Defendant Organization Count: {normalizedDefendantCount}
{mediaHints}
{(string.IsNullOrWhiteSpace(worldRules) ? "" : $"\n{worldRules}")}
{(string.IsNullOrWhiteSpace(worldContext) ? "" : $"\nWORLD MODEL (JSON):\n{worldContext}")}

Generate exactly 1 fictional corporate pre-dispute narrative for eDiscovery testing.

Allowed industries (use exact enum values only):
{industryOptions}

REQUIREMENTS:
- Entirely fictional: invent all companies, domains, people, teams, products, internal project names, and events. If the issue description or label contains real entities, replace them with fictional equivalents.
- Each narrative must include where it takes place (city/state/country or region).
- Build in ambiguity: include innocent explanations, red herrings, conflicting statements, miscommunication, and ""could be nothing"" details.
- Include multiple departments/roles with competing incentives (leadership, management, IC/SME, HR/compliance, IT/security, legal; plus external entities or people if relevant).
- Explicitly name at least two distinct organizations in each narrative. The summary MUST clearly label the likely plaintiff organization(s) and the likely defendant organization(s).
- Include exactly {normalizedPlaintiffCount} distinct plaintiff organization(s) and exactly {normalizedDefendantCount} distinct defendant organization(s), all explicitly named in the summary.
- If a plaintiff/defendant industry is not ""Random"", that organization MUST use that exact industry enum value.
- If an industry is ""Random"", choose any allowed industry value. Use ""Other"" only when an industry explicitly requests it.
- In the summary, include a final sentence starting with ""Organizations:"" that lists each organization and its industry using the allowed industry enum values.
- Organizations generated as plaintiffs must not be also defendants and vice versa.
- Provide clear narrative beats (setup, escalation, unresolved ending with open questions or pending action) that can expand into many emails.
- Use any applicable media hints to create natural opportunities for documents, images, or voicemails.
- Keep content professional and non-explicit; avoid edgy content.
- The narrative MUST describe only the pre-dispute events and communications that lead up to the issue described above.
- Do NOT mention any dispute, claim, lawsuit, litigation, arbitration, investigation, regulator, subpoena, enforcement action, discovery process, preservation, or legal proceedings (other than the required plaintiff/defendant role labels).", PromptScaffolding.JsonSchemaSection(schema));

            var response = await _openAI.GetJsonCompletionAsync<StorylineApiResponse>(systemPrompt, userPrompt, "Storyline Generation", ct);

            if (response == null || response.Storylines.Count == 0)
                throw new InvalidOperationException("Failed to generate a storyline.");

            var result = new StorylineGenerationResult();

            if (response.Storylines.Count > 1)
            {
                result.StorylineFilterSummary = $"Received {response.Storylines.Count} storylines; using the first one only.";
                Log.ReceivedMultipleStorylines(_logger, response.Storylines.Count);
            }

            var storyline = response.Storylines[0];
            var plotOutline = storyline.PlotOutline?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new StoryOutline { Point = p.Trim() })
                .ToList() ?? new List<StoryOutline>();
            var tensionDrivers = FilterAndTrimList(storyline.TensionDrivers);
            var ambiguities = FilterAndTrimList(storyline.Ambiguities);
            var redHerrings = FilterAndTrimList(storyline.RedHerrings);
            var evidenceThemes = FilterAndTrimList(storyline.EvidenceThemes);

            result.Storyline = new Storyline
            {
                Title = storyline.Title,
                Logline = storyline.Logline,
                Summary = storyline.Summary
            };
            result.Storyline.SetPlotOutline(plotOutline);
            result.Storyline.SetTensionDrivers(tensionDrivers);
            result.Storyline.SetAmbiguities(ambiguities);
            result.Storyline.SetRedHerrings(redHerrings);
            result.Storyline.SetEvidenceThemes(evidenceThemes);
            result.Storyline.Id = DeterministicIdHelper.CreateGuid(
                "storyline",
                result.Storyline.Title,
                result.Storyline.Summary,
                result.Storyline.Logline);

            await ApplyStorylineDateRangeAsync(result, topic, additionalInstructions, progress, ct);
            ValidateStoryline(result);
            SetMasterDateRangeFromStoryline(result);

            Log.StorylineGenerated(_logger, stopwatch.ElapsedMilliseconds, plotOutline.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            Log.StorylineGenerationCanceled(_logger, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Log.StorylineGenerationFailed(_logger, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<StoryBeat>> GenerateStoryBeatsAsync(
        string topic,
        Storyline storyline,
        IReadOnlyList<Organization> organizations,
        IReadOnlyList<Character> characters,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic is required.", nameof(topic));
        ArgumentNullException.ThrowIfNull(storyline);
        ArgumentNullException.ThrowIfNull(organizations);
        ArgumentNullException.ThrowIfNull(characters);

        using var storylineIdScope = LogContext.PushProperty("StorylineId", storyline.Id);
        using var storylineTitleScope = LogContext.PushProperty("StorylineTitle", storyline.Title);

        var stopwatch = Stopwatch.StartNew();
        Log.GeneratingStoryBeats(_logger, organizations.Count, characters.Count);

        try
        {
            var beats = await _beatGenerator.GenerateStoryBeatsAsync(
                topic,
                storyline,
                organizations,
                characters,
                progress,
                ct);

            foreach (var beat in beats)
            {
                beat.StorylineId = storyline.Id;
                if (beat.Id == Guid.Empty)
                {
                    beat.Id = DeterministicIdHelper.CreateGuid(
                        "story-beat",
                        storyline.Id.ToString("N"),
                        beat.Name,
                        beat.StartDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                        beat.EndDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                }
            }

            _threadGenerator.PlanEmailThreadsForBeats(beats, characters.Count, _rng);

            storyline.SetBeats(beats);
            Log.StoryBeatsPlanned(_logger, beats.Count, stopwatch.ElapsedMilliseconds);
            return beats;
        }
        catch (OperationCanceledException)
        {
            Log.StoryBeatPlanningCanceled(_logger, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Log.StoryBeatPlanningFailed(_logger, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
    }

    private static string BuildMediaHints(bool wantsDocuments, bool wantsImages, bool wantsVoicemails)
    {
        var hints = new List<string>();

        if (wantsDocuments)
        {
            hints.Add("DOCUMENTS: Create storylines where characters would naturally share reports, proposals, spreadsheets, or presentations (e.g., 'Attached is the quarterly report', 'See the attached budget breakdown')");
        }

        if (wantsImages)
        {
            hints.Add("IMAGES: Include storylines with visual moments where characters would share photos or images (e.g., 'Here's a photo from the ceremony', 'Look at this - I captured the moment', 'Attached: evidence from the scene')");
        }

        if (wantsVoicemails)
        {
            hints.Add("VOICEMAILS: Create scenarios where characters might leave urgent voice messages (e.g., important calls, time-sensitive situations, emotional moments that warrant a personal voice message)");
        }

        if (hints.Count == 0)
        {
            return "";
        }

        return "\n\nMEDIA OPPORTUNITIES - Design storylines that naturally include:\n" + string.Join("\n", hints.Select(h => $"• {h}"));
    }

    private static string FormatIndustryOptionsForStorylines(bool includeOther)
    {
        var options = Enum.GetNames<Industry>()
            .Where(name => includeOther || !string.Equals(name, nameof(Industry.Other), StringComparison.OrdinalIgnoreCase))
            .Select(name => $"{name} ({EnumHelper.HumanizeEnumName(name)})");

        return string.Join(", ", options);
    }

    private static List<string> FilterAndTrimList(IEnumerable<string>? values)
    {
        if (values == null)
            return new List<string>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
    }

    private static string BuildWorldModelContext(World? worldModel)
    {
        if (worldModel == null)
            return string.Empty;

        var organizations = worldModel.Plaintiffs.Select(org => new
        {
            name = org.Name,
            domain = org.Domain,
            description = org.Description,
            organizationType = org.OrganizationType.ToString(),
            industry = org.Industry.ToString(),
            state = org.State.ToString(),
            founded = org.Founded?.Year,
            side = "Plaintiff",
            keyPeople = org.EnumerateCharacters().Select(a => new
            {
                firstName = a.Character.FirstName,
                lastName = a.Character.LastName,
                email = a.Character.Email,
                role = a.Role.Name.ToString(),
                department = a.Department.Name.ToString(),
                personality = a.Character.Personality,
                communicationStyle = a.Character.CommunicationStyle,
                involvement = a.Character.Involvement,
                involvementSummary = a.Character.InvolvementSummary
            })
        })
        .Concat(worldModel.Defendants.Select(org => new
        {
            name = org.Name,
            domain = org.Domain,
            description = org.Description,
            organizationType = org.OrganizationType.ToString(),
            industry = org.Industry.ToString(),
            state = org.State.ToString(),
            founded = org.Founded?.Year,
            side = "Defendant",
            keyPeople = org.EnumerateCharacters().Select(a => new
            {
                firstName = a.Character.FirstName,
                lastName = a.Character.LastName,
                email = a.Character.Email,
                role = a.Role.Name.ToString(),
                department = a.Department.Name.ToString(),
                personality = a.Character.Personality,
                communicationStyle = a.Character.CommunicationStyle,
                involvement = a.Character.Involvement,
                involvementSummary = a.Character.InvolvementSummary
            })
        }))
        .ToList();

        var context = new
        {
            caseContext = new
            {
                caseArea = worldModel.CaseContext.CaseArea,
                matterType = worldModel.CaseContext.MatterType,
                issue = worldModel.CaseContext.Issue,
                issueDescription = worldModel.CaseContext.IssueDescription
            },
            organizations
        };

        return JsonSerializer.Serialize(context, JsonSerializationDefaults.Indented);
    }

    private async Task ApplyStorylineDateRangeAsync(
        StorylineGenerationResult result,
        string topic,
        string additionalInstructions,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (result.Storyline == null)
            return;

        progress?.Report("Suggesting storyline date range...");

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Date Range Assistant.

Your job: assign a reasonable start and end date for the storyline based on the narrative summary provided.

RULES (STRICT)
- Use YYYY-MM-DD format for dates.
- End date must be on or after start date.
- If the narratives imply a time period, align to it; otherwise choose a plausible range within the past 2-4 years.
- Prefer the shortest reasonable range that the storyline could plausibly occur within.
- Avoid starting on the 1st of a month or ending on the last day of a month unless explicitly implied (e.g., quarter-end, fiscal year).
- Do NOT default to January 1 through December 31 ranges.
 - Keep spans under 6 months unless the narrative explicitly requires longer (still keep it as short as plausible).");

        ct.ThrowIfCancellationRequested();

        var storyline = result.Storyline;

        var dateSchema = """
{
  "startDate": "YYYY-MM-DD",
  "endDate": "YYYY-MM-DD"
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}
Additional Instructions: {(string.IsNullOrWhiteSpace(additionalInstructions) ? "None" : additionalInstructions)}

Storyline:
Title: {storyline.Title}
Summary: {storyline.Summary}
", PromptScaffolding.JsonSchemaSection(dateSchema));

        var response = await _openAI.GetJsonCompletionAsync<StorylineDateRangeSingleResponse>(
            systemPrompt,
            userPrompt,
            "Storyline Date Range",
            ct);

        if (response == null)
            throw new InvalidOperationException($"Storyline date range generation failed for '{storyline.Title}'.");

        if (!DateHelper.TryParseIsoDate(response.StartDate, out var start) ||
            !DateHelper.TryParseIsoDate(response.EndDate, out var end))
        {
            throw new InvalidOperationException($"Storyline date range response contained invalid dates for '{storyline.Title}'.");
        }

        var normalized = DateHelper.NormalizeStorylineDateRange(storyline, start, end);
        storyline.StartDate = normalized.start;
        storyline.EndDate = normalized.end;

        Log.StorylineDateRangeSet(_logger, storyline.StartDate, storyline.EndDate);
    }

    private static class Log
    {
        public static void StorylineGeneratorInitialized(ILogger logger)
            => logger.Debug("StorylineGenerator initialized.");

        public static void GeneratingStoryline(ILogger logger, int plaintiffCount, int defendantCount)
            => logger.Information(
                "Generating storyline with {PlaintiffCount} plaintiff(s) and {DefendantCount} defendant(s).",
                plaintiffCount,
                defendantCount);

        public static void ReceivedMultipleStorylines(ILogger logger, int storylineCount)
            => logger.Warning("Received {StorylineCount} storylines; using only the first.", storylineCount);

        public static void StorylineGenerated(ILogger logger, long durationMs, int plotPointCount)
            => logger.Information(
                "Storyline generated in {DurationMs} ms with {PlotPointCount} plot points.",
                durationMs,
                plotPointCount);

        public static void StorylineGenerationCanceled(ILogger logger, long durationMs)
            => logger.Warning("Storyline generation canceled after {DurationMs} ms.", durationMs);

        public static void StorylineGenerationFailed(ILogger logger, long durationMs, Exception exception)
            => logger.Error(exception, "Storyline generation failed after {DurationMs} ms.", durationMs);

        public static void GeneratingStoryBeats(ILogger logger, int organizationCount, int characterCount)
            => logger.Information(
                "Generating story beats for {OrganizationCount} organizations and {CharacterCount} characters.",
                organizationCount,
                characterCount);

        public static void StoryBeatsPlanned(ILogger logger, int beatCount, long durationMs)
            => logger.Information(
                "Story beats planned with {BeatCount} beats in {DurationMs} ms.",
                beatCount,
                durationMs);

        public static void StoryBeatPlanningCanceled(ILogger logger, long durationMs)
            => logger.Warning("Story beat planning canceled after {DurationMs} ms.", durationMs);

        public static void StoryBeatPlanningFailed(ILogger logger, long durationMs, Exception exception)
            => logger.Error(exception, "Story beat planning failed after {DurationMs} ms.", durationMs);

        public static void StorylineDateRangeSet(ILogger logger, DateTime? startDate, DateTime? endDate)
            => logger.Information("Storyline date range set to {StartDate} - {EndDate}.", startDate, endDate);
    }

    private static void SetMasterDateRangeFromStoryline(StorylineGenerationResult result)
    {
        var storyline = result.Storyline;
        if (storyline?.StartDate == null || storyline.EndDate == null)
            return;

        result.SuggestedStartDate = storyline.StartDate.Value;
        result.SuggestedEndDate = storyline.EndDate.Value;
    }

    private static void ValidateStoryline(StorylineGenerationResult result)
    {
        var storyline = result.Storyline;
        if (storyline == null)
            throw new InvalidOperationException("No storyline was generated.");

        if (!storyline.StartDate.HasValue || !storyline.EndDate.HasValue)
            throw new InvalidOperationException("Storyline is missing a valid date range.");

        if (storyline.EndDate.Value.Date < storyline.StartDate.Value.Date)
            throw new InvalidOperationException("Storyline has an invalid date range.");
    }

    // API Response DTOs
    private sealed class StorylineApiResponse
    {
        [JsonPropertyName("storylines")]
        public List<StorylineDto> Storylines { get; set; } = new();
    }

    private sealed class StorylineDto
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("logline")]
        public string Logline { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("plotOutline")]
        public List<string>? PlotOutline { get; set; }

        [JsonPropertyName("tensionDrivers")]
        public List<string>? TensionDrivers { get; set; }

        [JsonPropertyName("ambiguities")]
        public List<string>? Ambiguities { get; set; }

        [JsonPropertyName("redHerrings")]
        public List<string>? RedHerrings { get; set; }

        [JsonPropertyName("evidenceThemes")]
        public List<string>? EvidenceThemes { get; set; }
    }

    private sealed class StorylineDateRangeSingleResponse
    {
        [JsonPropertyName("startDate")]
        public string StartDate { get; set; } = string.Empty;

        [JsonPropertyName("endDate")]
        public string EndDate { get; set; } = string.Empty;
    }
}

public sealed class StorylineGenerationRequest
{
    public string? Topic { get; set; }
    public string? IssueDescription { get; set; }
    public string? AdditionalInstructions { get; set; }
    public string? PlaintiffIndustry { get; set; }
    public string? DefendantIndustry { get; set; }
    public int PlaintiffOrganizationCount { get; set; }
    public int DefendantOrganizationCount { get; set; }
    public World? WorldModel { get; set; }
    public bool WantsDocuments { get; set; } = true;
    public bool WantsImages { get; set; }
    public bool WantsVoicemails { get; set; }
}

public class StorylineGenerationResult
{
    public Storyline? Storyline { get; set; }
    public DateTime? SuggestedStartDate { get; set; }
    public DateTime? SuggestedEndDate { get; set; }
    public string? StorylineFilterSummary { get; set; }
}
