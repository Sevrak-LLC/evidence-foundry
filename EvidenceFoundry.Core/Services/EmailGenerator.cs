using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Serialization;
using EvidenceFoundry.Models;
using EvidenceFoundry.Helpers;
using Serilog;
using Serilog.Context;

namespace EvidenceFoundry.Services;

public partial class EmailGenerator
{
    private readonly OpenAIService _openAI;
    private readonly OfficeDocumentService _officeService;
    private readonly EmailThreadGenerator _threadGenerator;
    private readonly SuggestedSearchTermGenerator _searchTermGenerator;
    private readonly Random _rng;
    private readonly ILogger _logger;

    // Track document chains across threads for versioning
    private readonly ConcurrentDictionary<string, DocumentChainState> _documentChains = new();

    // Legacy batch sizing retained for compatibility with older helpers.
    private const int MaxEmailsPerBatch = 15;
    private const int MaxThreadGenerationAttempts = 1;
    private const int MaxAttachmentFileNameLength = 160;
    private static readonly string[] SignOffPatterns =
    {
        "Best,",
        "Best regards,",
        "Regards,",
        "Thanks,",
        "Thank you,",
        "Sincerely,",
        "Cheers,",
        "Kind regards,",
        "Warm regards,",
        "All the best,",
        "Best wishes,",
        "Thanks!",
        "Thank you!",
        "Respectfully,",
        "Cordially,"
    };

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true
    };

    public EmailGenerator(
        OpenAIService openAI,
        Random rng,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(openAI);
        ArgumentNullException.ThrowIfNull(rng);
        _openAI = openAI;
        _rng = rng;
        var baseLogger = logger ?? Serilog.Log.Logger;
        _logger = baseLogger.ForContext<EmailGenerator>();
        _officeService = new OfficeDocumentService(baseLogger.ForContext<OfficeDocumentService>());
        _threadGenerator = new EmailThreadGenerator(baseLogger.ForContext<EmailThreadGenerator>());
        _searchTermGenerator = new SuggestedSearchTermGenerator(
            openAI,
            baseLogger.ForContext<SuggestedSearchTermGenerator>());
        Log.EmailGeneratorInitialized(_logger);
    }

    private sealed class DocumentChainState
    {
        public string ChainId { get; set; } = string.Empty;
        public string BaseTitle { get; set; } = "";
        public AttachmentType Type { get; set; }
        public int VersionNumber { get; set; } = 1;
        public string LastContent { get; set; } = "";
        public object SyncRoot { get; } = new();
    }

    public async Task<GenerationResult> GenerateEmailsAsync(
        WizardState state,
        IProgress<GenerationProgress> progress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(progress);

        var result = new GenerationResult
        {
            OutputFolder = state.Config.OutputFolder
        };

        var runId = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var activeStorylines = state.GetActiveStorylines().ToList();
        var plannedTotals = CalculatePlannedTotals(activeStorylines, state.Config);
        var progressData = new GenerationProgress
        {
            TotalEmails = plannedTotals.TotalEmails,
            TotalAttachments = plannedTotals.TotalAttachments,
            TotalImages = plannedTotals.TotalImages,
            CurrentOperation = "Initializing..."
        };

        using var runIdScope = LogContext.PushProperty("RunId", runId);
        using var outputFolderScope = LogContext.PushProperty("OutputFolder", state.Config.OutputFolder);

        Log.StartingEmailGeneration(_logger, activeStorylines.Count, progressData.TotalEmails);

        try
        {
            if (activeStorylines.Count == 0)
            {
                Log.NoActiveStorylines(_logger);
                throw new InvalidOperationException("No storyline available for email generation.");
            }
            var characterContexts = BuildCharacterContextMap(state.Organizations);
            var characterRoutingContexts = BuildCharacterRoutingMap(state.Organizations);

            var threads = new ConcurrentBag<EmailThread>();
            var progressLock = new object();
            var savedThreads = new ConcurrentDictionary<Guid, bool>();
            var saveSemaphore = new SemaphoreSlim(1, 1);
            result.PlannedEmails = plannedTotals.TotalEmails;
            result.PlannedThreads = plannedTotals.TotalThreads;
            result.PlannedAttachments = plannedTotals.TotalAttachments;

            ReportProgress(progress, progressData, progressLock, p => p.CurrentOperation = "Initializing...");

            // EML service for incremental saving
            var emlService = new EmlFileService(_logger.ForContext<EmlFileService>());
            Directory.CreateDirectory(state.Config.OutputFolder);

            var processContext = new ProcessStorylineContext(
                characterContexts,
                characterRoutingContexts,
                threads,
                result,
                progressData,
                progress,
                progressLock,
                emlService,
                saveSemaphore,
                savedThreads);

            foreach (var storyline in activeStorylines)
            {
                await ProcessStorylineAsync(
                    storyline,
                    state,
                    processContext,
                    ct);
            }

            // Convert to list for saving and results
            var threadsList = threads.ToList();

            // Save any remaining EML files (threads that weren't saved incrementally)
            var unsavedCount = threadsList.Count(t => !savedThreads.ContainsKey(t.Id));
            if (unsavedCount > 0)
            {
                Log.SavingRemainingEmlFiles(_logger, unsavedCount);
            }
            await SaveRemainingEmlAsync(
                threadsList,
                state,
                processContext,
                result,
                ct);

            var suggestedSearchContext = new SuggestedSearchTermsContext(
                result,
                progressData,
                progress,
                progressLock);

            await GenerateSuggestedSearchTermsAsync(
                threadsList,
                activeStorylines,
                state.Config,
                suggestedSearchContext,
                ct);

            // Finalize results
            result.TotalEmailsGenerated = result.SucceededEmails;
            result.TotalThreadsGenerated = result.SucceededThreads;
            result.TotalAttachmentsGenerated = result.WordDocumentsGenerated
                                               + result.ExcelDocumentsGenerated
                                               + result.PowerPointDocumentsGenerated
                                               + result.ImagesGenerated
                                               + result.CalendarInvitesGenerated
                                               + result.VoicemailsGenerated;
            result.ElapsedTime = stopwatch.Elapsed;

            state.GeneratedThreads = threadsList;
            state.Result = result;

            ReportProgress(progress, progressData, progressLock, p => p.CurrentOperation = "Complete!");

            Log.EmailGenerationCompleted(
                _logger,
                result.TotalThreadsGenerated,
                result.TotalEmailsGenerated,
                result.TotalAttachmentsGenerated,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            result.WasCancelled = true;
            result.ElapsedTime = stopwatch.Elapsed;
            Log.EmailGenerationCanceled(_logger, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            Log.EmailGenerationFailed(_logger, stopwatch.ElapsedMilliseconds, ex);
            result.ElapsedTime = stopwatch.Elapsed;
            result.AddError($"Email generation failed ({ex.GetType().Name}): {ex.Message}");
            if (ex.InnerException != null)
                result.AddError($"Inner exception ({ex.InnerException.GetType().Name}): {ex.InnerException.Message}");
            return result;
        }
    }

    private sealed class ProcessStorylineContext
    {
        public ProcessStorylineContext(
            Dictionary<Guid, CharacterContext> characterContexts,
            Dictionary<Guid, CharacterRoutingContext> characterRoutingContexts,
            ConcurrentBag<EmailThread> threads,
            GenerationResult result,
            GenerationProgress progressData,
            IProgress<GenerationProgress> progress,
            object progressLock,
            EmlFileService emlService,
            SemaphoreSlim saveSemaphore,
            ConcurrentDictionary<Guid, bool> savedThreads)
        {
            CharacterContexts = characterContexts;
            CharacterRoutingContexts = characterRoutingContexts;
            Threads = threads;
            Result = result;
            ProgressData = progressData;
            Progress = progress;
            ProgressLock = progressLock;
            EmlService = emlService;
            SaveSemaphore = saveSemaphore;
            SavedThreads = savedThreads;
        }

        public Dictionary<Guid, CharacterContext> CharacterContexts { get; }
        public Dictionary<Guid, CharacterRoutingContext> CharacterRoutingContexts { get; }
        public ConcurrentBag<EmailThread> Threads { get; }
        public GenerationResult Result { get; }
        public GenerationProgress ProgressData { get; }
        public IProgress<GenerationProgress> Progress { get; }
        public object ProgressLock { get; }
        public EmlFileService EmlService { get; }
        public SemaphoreSlim SaveSemaphore { get; }
        public ConcurrentDictionary<Guid, bool> SavedThreads { get; }
    }

    private async Task ProcessStorylineAsync(
        Storyline storyline,
        WizardState state,
        ProcessStorylineContext context,
        CancellationToken ct)
    {
        var emailCount = storyline.EmailCount;
        var beats = storyline.Beats ?? new List<StoryBeat>();
        var completedAtStart = 0;

        using var storylineIdScope = LogContext.PushProperty("StorylineId", storyline.Id);
        using var storylineTitleScope = LogContext.PushProperty("StorylineTitle", storyline.Title);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            ct.ThrowIfCancellationRequested();

            Log.ProcessingStoryline(_logger, emailCount, beats.Count);

            // Report that we're starting this storyline
            ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, p =>
            {
                p.CurrentStoryline = storyline.Title;
                p.CurrentOperation = $"Processing storyline: {storyline.Title}";
            });

            lock (context.ProgressLock)
            {
                completedAtStart = context.ProgressData.CompletedEmails;
            }

            // Generate thread(s) for this storyline
            var storylineThreads = await GenerateThreadsForStorylineAsync(
                storyline,
                beats,
                state,
                context,
                ct);

            // Add to concurrent collection
            foreach (var thread in storylineThreads)
            {
                context.Threads.Add(thread);
            }

            Log.StorylineProcessingGeneratedThreads(
                _logger,
                storylineThreads.Count,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (Exception ex)
        {
            Log.StorylineProcessingFailed(_logger, storyline.Title, ex);
            context.Result.AddError($"Storyline '{storyline.Title}' failed during email generation: {ex.Message}");
        }
    }

    private async Task SaveRemainingEmlAsync(
        List<EmailThread> threadsList,
        WizardState state,
        ProcessStorylineContext context,
        GenerationResult result,
        CancellationToken ct)
    {
        var unsavedThreads = threadsList.Where(t => !context.SavedThreads.ContainsKey(t.Id)).ToList();
        if (unsavedThreads.Count == 0)
        {
            return;
        }

        ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, p => p.CurrentOperation = "Saving EML files...");

        try
        {
            var emlProgress = new Progress<(int completed, int total, string currentFile)>(p =>
            {
                ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, pd =>
                {
                    pd.CurrentOperation = $"Saving: {p.currentFile}";
                });
            });

            await context.EmlService.SaveAllEmailsAsync(
                unsavedThreads,
                state.Config.OutputFolder,
                state.Config.OrganizeBySender,
                emlProgress,
                state.Config.ParallelThreads,
                releaseAttachmentContent: true,
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.AddError($"Failed to save remaining EML files: {ex.Message}");
            Log.FailedToSaveRemainingEmlFiles(_logger, unsavedThreads.Count, ex);
        }
    }

    private static string IndentSignature(string signature)
    {
        if (string.IsNullOrEmpty(signature)) return "    (no signature)";
        var lines = signature.Replace("\\n", "\n").Split('\n');
        var indentedLines = Array.ConvertAll(lines, line => $"    {line}");
        return string.Join("\n", indentedLines);
    }

    private static string GetThreadSubject(EmailThread thread)
    {
        var subject = thread.EmailMessages.Count > 0 ? thread.EmailMessages[0].Subject : null;
        return string.IsNullOrWhiteSpace(subject) ? "Untitled thread" : subject;
    }

    private sealed class SuggestedSearchTermResult
    {
        public Guid ThreadId { get; init; }
        public string Subject { get; init; } = string.Empty;
        public bool IsHot { get; init; }
        public List<string> Terms { get; init; } = new();
    }

    private static EmailMessage? GetLargestEmailInThread(EmailThread thread)
    {
        if (thread.EmailMessages.Count == 0)
            return null;

        return thread.EmailMessages
            .OrderByDescending(m => m.BodyPlain?.Length ?? 0)
            .ThenByDescending(m => m.Attachments.Count)
            .ThenByDescending(m => m.BodyHtml?.Length ?? 0)
            .ThenBy(m => m.SequenceInThread)
            .FirstOrDefault();
    }

    private static string BuildExportedEmailForPrompt(EmailMessage email)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Subject: {email.Subject}");
        if (email.From != null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"From: {email.From.FullName} <{email.From.Email}>");
        }

        if (email.To.Count > 0)
        {
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"To: {string.Join("; ", email.To.Select(c => $"{c.FullName} <{c.Email}>"))}");
        }

        if (email.Cc.Count > 0)
        {
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"Cc: {string.Join("; ", email.Cc.Select(c => $"{c.FullName} <{c.Email}>"))}");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Date: {email.SentDate:yyyy-MM-dd HH:mm}");

        if (email.Attachments.Count > 0)
        {
            sb.AppendLine("Attachments:");
            foreach (var attachment in email.Attachments)
            {
                var description = string.IsNullOrWhiteSpace(attachment.ContentDescription)
                    ? ""
                    : $" - {attachment.ContentDescription}";
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {attachment.Type} {attachment.FileName}{description}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(email.BodyPlain ?? string.Empty);
        return sb.ToString();
    }

    private async Task GenerateSuggestedSearchTermsAsync(
        List<EmailThread> threads,
        IReadOnlyList<Storyline> storylines,
        GenerationConfig config,
        SuggestedSearchTermsContext context,
        CancellationToken ct)
    {
        if (threads.Count == 0)
        {
            Log.SkippingSuggestedSearchTermsNoThreads(_logger);
            return;
        }

        var responsiveThreads = threads
            .Where(t => t.Relevance == EmailThread.ThreadRelevance.Responsive || t.IsHot)
            .ToList();

        if (responsiveThreads.Count == 0)
        {
            Log.SkippingSuggestedSearchTermsNoResponsiveThreads(_logger);
            return;
        }

        Log.GeneratingSuggestedSearchTerms(_logger, responsiveThreads.Count);

        var beatLookup = storylines
            .SelectMany(s => s.Beats ?? new List<StoryBeat>())
            .GroupBy(b => b.Id)
            .Select(g => g.First())
            .ToDictionary(b => b.Id);

        var storylineLookup = storylines
            .GroupBy(s => s.Id)
            .Select(g => g.First())
            .ToDictionary(s => s.Id);

        var results = new List<SuggestedSearchTermResult>(responsiveThreads.Count);

        foreach (var thread in responsiveThreads.OrderBy(t => GetThreadSubject(t), StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            var contextOptions = new SuggestedSearchContextOptions(
                beatLookup,
                storylineLookup);

            try
            {
                if (!TryGetSuggestedSearchContext(
                        thread,
                        contextOptions,
                        out var beat,
                        out var storyline,
                        out var largestEmail))
                {
                    continue;
                }

                var subject = GetThreadSubject(thread);
                ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, p =>
                {
                    p.CurrentOperation = $"Generating suggested search terms: {subject}";
                });

                var terms = await GenerateSuggestedSearchTermsForThreadAsync(
                    new SuggestedSearchTermsRequest(subject, largestEmail, storyline, beat, thread.IsHot),
                    ct);

                results.Add(new SuggestedSearchTermResult
                {
                    ThreadId = thread.Id,
                    Subject = subject,
                    IsHot = thread.IsHot,
                    Terms = terms
                });
            }
            catch (Exception ex)
            {
                lock (context.ProgressLock)
                {
                    context.Result.AddError(
                        $"Suggested search terms failed for thread {thread.Id} ({GetThreadSubject(thread)}): {ex.Message}");
                }
                Log.SuggestedSearchTermsFailed(_logger, thread.Id, ex);
            }
        }

        if (results.Count == 0)
            return;

        ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, p =>
        {
            p.CurrentOperation = "Writing suggested search terms markdown...";
        });

        var markdown = BuildSuggestedSearchTermsMarkdown(results);
        var outputPath = Path.Combine(config.OutputFolder, "suggested-search-terms.md");
        try
        {
            Directory.CreateDirectory(config.OutputFolder);
            await File.WriteAllTextAsync(outputPath, markdown, ct);
            Log.WroteSuggestedSearchTermsMarkdown(_logger, results.Count, outputPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to write suggested search terms markdown: {ex.Message}", ex);
        }
    }

    private async Task<List<string>> GenerateSuggestedSearchTermsForThreadAsync(
        SuggestedSearchTermsRequest request,
        CancellationToken ct)
    {
        var exportedEmail = BuildExportedEmailForPrompt(request.LargestEmail);
        var terms = await _searchTermGenerator.GenerateSuggestedSearchTermsAsync(
            exportedEmail,
            request.Storyline.Summary,
            request.Beat.Plot,
            request.IsHot,
            ct);
        if (terms.Count < 2)
        {
            throw new InvalidOperationException(
                $"Suggested terms returned fewer than 2 entries for thread '{request.Subject}'.");
        }

        return terms;
    }

    private sealed record SuggestedSearchTermsRequest(
        string Subject,
        EmailMessage LargestEmail,
        Storyline Storyline,
        StoryBeat Beat,
        bool IsHot);

    private sealed record SuggestedSearchTermsContext(
        GenerationResult Result,
        GenerationProgress ProgressData,
        IProgress<GenerationProgress> Progress,
        object ProgressLock);

    private sealed record SuggestedSearchContextOptions(
        IReadOnlyDictionary<Guid, StoryBeat> BeatLookup,
        IReadOnlyDictionary<Guid, Storyline> StorylineLookup);

    private static bool TryGetSuggestedSearchContext(
        EmailThread thread,
        SuggestedSearchContextOptions options,
        [NotNullWhen(true)] out StoryBeat? beat,
        [NotNullWhen(true)] out Storyline? storyline,
        [NotNullWhen(true)] out EmailMessage? largestEmail)
    {
        beat = null;
        storyline = null;
        largestEmail = null;

        if (!options.BeatLookup.TryGetValue(thread.StoryBeatId, out var resolvedBeat))
        {
            throw new InvalidOperationException($"Suggested terms missing story beat for thread {thread.Id}.");
        }

        if (!options.StorylineLookup.TryGetValue(thread.StorylineId, out var resolvedStoryline))
        {
            throw new InvalidOperationException($"Suggested terms missing storyline for thread {thread.Id}.");
        }

        var resolvedEmail = GetLargestEmailInThread(thread);
        if (resolvedEmail == null)
        {
            throw new InvalidOperationException($"Suggested terms skipped: thread {thread.Id} has no emails.");
        }

        beat = resolvedBeat;
        storyline = resolvedStoryline;
        largestEmail = resolvedEmail;
        return true;
    }

    private static string BuildSuggestedSearchTermsMarkdown(IReadOnlyList<SuggestedSearchTermResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Suggested Search Terms");
        sb.AppendLine();
        sb.AppendLine("dtSearch-formatted queries generated from responsive threads.");
        sb.AppendLine();

        WriteSection(sb, "Responsive (Not Hot)", results.Where(r => !r.IsHot).ToList());
        WriteSection(sb, "Hot (IsHot = true)", results.Where(r => r.IsHot).ToList());

        return sb.ToString();
    }

    private static void WriteSection(StringBuilder sb, string title, List<SuggestedSearchTermResult> items)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {title}");
        if (items.Count == 0)
        {
            WriteEmptySection(sb);
            return;
        }

        var aggregated = AggregateTerms(items);
        WriteAggregatedTerms(sb, aggregated);
        WriteThreadTerms(sb, items);
        sb.AppendLine();
    }

    private static void WriteEmptySection(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("_None_");
        sb.AppendLine();
    }

    private static List<string> AggregateTerms(IEnumerable<SuggestedSearchTermResult> items)
    {
        var aggregated = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var term in item.Terms)
            {
                if (seen.Add(term))
                {
                    aggregated.Add(term);
                }
            }
        }

        return aggregated;
    }

    private static void WriteAggregatedTerms(StringBuilder sb, List<string> aggregated)
    {
        sb.AppendLine();
        sb.AppendLine("### Aggregated Terms");
        if (aggregated.Count == 0)
        {
            sb.AppendLine("- (none generated)");
            return;
        }

        foreach (var term in aggregated)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {term}");
        }
    }

    private static void WriteThreadTerms(StringBuilder sb, List<SuggestedSearchTermResult> items)
    {
        sb.AppendLine();
        sb.AppendLine("### Thread Terms");
        foreach (var item in items.OrderBy(i => i.Subject, StringComparer.OrdinalIgnoreCase))
        {
            WriteThreadItem(sb, item);
        }
    }

    private static void WriteThreadItem(StringBuilder sb, SuggestedSearchTermResult item)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Subject: {item.Subject}");
        if (item.Terms.Count == 0)
        {
            sb.AppendLine("  - (no terms generated)");
            return;
        }

        foreach (var term in item.Terms)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  - {term}");
        }
    }

    internal readonly record struct CharacterContext(string Role, string Department, string Organization);
    internal readonly record struct CharacterRoutingContext(
        DepartmentName? Department,
        RoleName? Role,
        Guid OrganizationId);

    internal readonly record struct ThreadPlan(
        int Index,
        EmailThread Thread,
        int EmailCount,
        DateTime Start,
        DateTime End,
        string BeatName,
        List<Character> Participants,
        Dictionary<string, Character> ParticipantLookup,
        string ParticipantList,
        ThreadStructurePlan StructurePlan,
        int ThreadSeed);

    private static Dictionary<Guid, CharacterContext> BuildCharacterContextMap(List<Organization> organizations)
    {
        var map = new Dictionary<Guid, CharacterContext>();

        foreach (var assignment in organizations.SelectMany(o => o.EnumerateCharacters()))
        {
            if (map.ContainsKey(assignment.Character.Id))
                throw new InvalidOperationException($"Character '{assignment.Character.FullName}' appears in multiple roles.");

            map[assignment.Character.Id] = new CharacterContext(
                EnumHelper.HumanizeEnumName(assignment.Role.Name.ToString()),
                EnumHelper.HumanizeEnumName(assignment.Department.Name.ToString()),
                assignment.Organization.Name);
        }

        return map;
    }

    private static Dictionary<Guid, CharacterRoutingContext> BuildCharacterRoutingMap(List<Organization> organizations)
    {
        var map = new Dictionary<Guid, CharacterRoutingContext>();

        foreach (var assignment in organizations.SelectMany(o => o.EnumerateCharacters()))
        {
            if (map.ContainsKey(assignment.Character.Id))
                throw new InvalidOperationException($"Character '{assignment.Character.FullName}' appears in multiple roles.");

            map[assignment.Character.Id] = new CharacterRoutingContext(
                assignment.Department.Name,
                assignment.Role.Name,
                assignment.Organization.Id);
        }

        return map;
    }

    private static string BuildCharacterList(IEnumerable<Character> characters, Dictionary<Guid, CharacterContext> contexts)
    {
        return string.Join("\n\n", characters.Select(c =>
        {
            if (!contexts.TryGetValue(c.Id, out var context))
                throw new InvalidOperationException($"Character '{c.FullName}' has no organization assignment.");

            return $"- {c.FullName} ({c.Email})\n  Role: {context.Role}, {context.Department} @ {context.Organization}";
        }));
    }

    private static string BuildSenderProfileSection(
        Character sender,
        Dictionary<Guid, CharacterContext> contexts)
    {
        var roleLine = contexts.TryGetValue(sender.Id, out var context)
            ? $"Role: {context.Role}, {context.Department} @ {context.Organization}"
            : "Role: Unknown";

        return $@"SENDER PROFILE:
This email is being written by ""{sender.FullName}"" with the personality ""{sender.Personality}"" and the communication style ""{sender.CommunicationStyle}"".
{roleLine}
Email: {sender.Email}
Signature:
{IndentSignature(sender.SignatureBlock)}";
    }

    private static Random CreateThreadRandom(int generationSeed, string scope, Guid threadId)
    {
        var seed = DeterministicSeedHelper.CreateSeed(
            scope,
            generationSeed.ToString(CultureInfo.InvariantCulture),
            threadId.ToString("N"));
        return new Random(seed);
    }

    private static int CountBranches(ThreadStructurePlan plan)
    {
        if (plan.Slots.Count <= 1)
            return 0;

        var branches = 0;
        for (var i = 1; i < plan.Slots.Count; i++)
        {
            var slot = plan.Slots[i];
            var previous = plan.Slots[i - 1];
            if (slot.ParentEmailId.HasValue && slot.ParentEmailId != previous.EmailId)
            {
                branches++;
            }
        }

        return branches;
    }

    private static void ReportProgress(
        IProgress<GenerationProgress> progress,
        GenerationProgress progressData,
        object progressLock,
        Action<GenerationProgress> update)
    {
        GenerationProgress snapshot;
        lock (progressLock)
        {
            update(progressData);
            snapshot = progressData.Snapshot();
        }

        progress.Report(snapshot);
    }

    private void RecordThreadCompletion(
        ThreadPlanContext context,
        ThreadPlan plan,
        EmailThread thread,
        bool success,
        string stage)
    {
        var subject = GetThreadSubject(thread);
        GenerationProgress snapshot;
        lock (context.ProgressLock)
        {
            if (success)
            {
                context.Result.SucceededThreads++;
            }
            else
            {
                context.Result.FailedThreads++;
            }

            context.ProgressData.CurrentOperation = success
                ? $"Completed thread: {subject}"
                : $"Failed thread: {subject}";

            snapshot = context.ProgressData.Snapshot();
        }

        context.Progress.Report(snapshot);

        if (success)
        {
            Log.ThreadCompletedSuccessfully(_logger, stage, subject);
        }
    }

    private void RecordThreadFailure(
        ThreadPlanContext context,
        ThreadPlan plan,
        string stage,
        Exception ex)
    {
        var subject = GetThreadSubject(plan.Thread);
        lock (context.ProgressLock)
        {
            context.Result.AddError(
                $"Thread '{subject}' (ThreadId {plan.Thread.Id}) failed during {stage}: {ex.Message}");
        }

        Log.ThreadFailedDuringStage(_logger, stage, ex);

        RecordThreadCompletion(context, plan, plan.Thread, success: false, stage: stage);
    }

    private void RecordEmailCompletion(
        ThreadPlanContext context,
        EmailThread thread,
        EmailMessage email,
        bool success,
        string stage)
    {
        GenerationProgress snapshot;
        lock (context.ProgressLock)
        {
            context.ProgressData.CompletedEmails = Math.Min(
                context.ProgressData.TotalEmails,
                context.ProgressData.CompletedEmails + 1);

            if (success)
            {
                context.Result.SucceededEmails++;
            }
            else
            {
                context.Result.FailedEmails++;
            }

            var subject = string.IsNullOrWhiteSpace(email.Subject) ? GetThreadSubject(thread) : email.Subject;
            context.ProgressData.CurrentOperation = success
                ? $"Generated email: {subject}"
                : $"Failed email: {subject}";

            snapshot = context.ProgressData.Snapshot();
        }

        context.Progress.Report(snapshot);

        if (!success)
        {
            var subject = string.IsNullOrWhiteSpace(email.Subject)
                ? "<no subject>"
                : email.Subject;
            var reason = string.IsNullOrWhiteSpace(email.GenerationFailureReason)
                ? "No failure reason recorded."
                : email.GenerationFailureReason;
            Log.EmailFailedDuringStage(
                _logger,
                stage,
                thread.Id,
                email.SequenceInThread + 1,
                subject,
                reason);
        }
    }

    private static class Log
    {
        public static void EmailGeneratorInitialized(ILogger logger)
            => logger.Debug("EmailGenerator initialized.");

        public static void StartingEmailGeneration(ILogger logger, int storylineCount, int totalEmailCount)
            => logger.Information(
                "Starting email generation for {StorylineCount} storylines and {TotalEmailCount} planned emails.",
                storylineCount,
                totalEmailCount);

        public static void NoActiveStorylines(ILogger logger)
            => logger.Warning("No active storylines found; email generation cannot proceed.");

        public static void SavingRemainingEmlFiles(ILogger logger, int threadCount)
            => logger.Information("Saving remaining EML files for {ThreadCount} threads.", threadCount);

        public static void EmailGenerationCompleted(
            ILogger logger,
            int threadCount,
            int emailCount,
            int attachmentCount,
            long durationMs)
            => logger.Information(
                "Email generation completed with {ThreadCount} threads, {EmailCount} emails, and {AttachmentCount} attachments in {DurationMs} ms.",
                threadCount,
                emailCount,
                attachmentCount,
                durationMs);

        public static void EmailGenerationCanceled(ILogger logger, long durationMs)
            => logger.Warning("Email generation canceled after {DurationMs} ms.", durationMs);

        public static void EmailGenerationFailed(ILogger logger, long durationMs, Exception exception)
            => logger.Error(exception, "Email generation failed after {DurationMs} ms.", durationMs);

        public static void ProcessingStoryline(ILogger logger, int emailCount, int beatCount)
            => logger.Information(
                "Processing storyline with {EmailCount} planned emails across {BeatCount} beats.",
                emailCount,
                beatCount);

        public static void StorylineProcessingGeneratedThreads(ILogger logger, int threadCount, long durationMs)
            => logger.Information(
                "Storyline processing generated {ThreadCount} threads in {DurationMs} ms.",
                threadCount,
                durationMs);

        public static void StorylineProcessingFailed(ILogger logger, string storylineTitle, Exception exception)
            => logger.Error(
                exception,
                "Storyline '{StorylineTitle}' failed during email generation.",
                storylineTitle);

        public static void FailedToSaveRemainingEmlFiles(ILogger logger, int threadCount, Exception exception)
            => logger.Error(
                exception,
                "Failed to save remaining EML files for {ThreadCount} threads.",
                threadCount);

        public static void SkippingSuggestedSearchTermsNoThreads(ILogger logger)
            => logger.Information("Skipping suggested search terms; no threads available.");

        public static void SkippingSuggestedSearchTermsNoResponsiveThreads(ILogger logger)
            => logger.Information("Skipping suggested search terms; no responsive or hot threads found.");

        public static void GeneratingSuggestedSearchTerms(ILogger logger, int threadCount)
            => logger.Information(
                "Generating suggested search terms for {ThreadCount} responsive/hot threads.",
                threadCount);

        public static void SuggestedSearchTermsFailed(ILogger logger, Guid threadId, Exception exception)
            => logger.Error(exception, "Suggested search terms failed for thread {ThreadId}.", threadId);

        public static void WroteSuggestedSearchTermsMarkdown(ILogger logger, int threadCount, string outputPath)
            => logger.Information(
                "Wrote suggested search terms markdown for {ThreadCount} threads to {OutputPath}.",
                threadCount,
                outputPath);

        public static void ThreadCompletedSuccessfully(ILogger logger, string stage, string subject)
            => logger.Information("Thread completed successfully in stage {Stage}: {Subject}.", stage, subject);

        public static void ThreadFailedDuringStage(ILogger logger, string stage, Exception exception)
            => logger.Error(exception, "Thread failed during {Stage}.", stage);

        public static void EmailFailedDuringStage(
            ILogger logger,
            string stage,
            Guid threadId,
            int slot,
            string subject,
            string reason)
            => logger.Warning(
                "Email failed during {Stage} in thread {ThreadId} (slot {Slot}): {Subject}. Reason: {Reason}",
                stage,
                threadId,
                slot,
                subject,
                reason);

        public static void NoStoryBeatsForStoryline(ILogger logger, string storylineTitle)
            => logger.Warning(
                "No story beats available for storyline {StorylineTitle}; skipping thread generation.",
                storylineTitle);

        public static void NoThreadPlansGenerated(ILogger logger, string storylineTitle)
            => logger.Warning("No thread plans generated for storyline {StorylineTitle}.", storylineTitle);

        public static void GeneratingThreadsForStoryline(ILogger logger, int threadPlanCount, string storylineTitle)
            => logger.Information(
                "Generating {ThreadPlanCount} threads for storyline {StorylineTitle}.",
                threadPlanCount,
                storylineTitle);

        public static void CreatedThreadStructurePlan(
            ILogger logger,
            Guid threadId,
            int emailCount,
            int branchCount)
            => logger.Information(
                "Created thread structure plan for {ThreadId} with {EmailCount} emails and {BranchCount} branch(es).",
                threadId,
                emailCount,
                branchCount);

        public static void GeneratingThread(ILogger logger, Guid threadId, int emailCount)
            => logger.Information("Generating thread {ThreadId} with {EmailCount} planned emails.", threadId, emailCount);

        public static void ResolvedNonResponsiveThreadTopic(
            ILogger logger,
            string topic,
            Guid threadId,
            string source)
            => logger.Information(
                "Resolved non-responsive thread topic '{Topic}' for thread {ThreadId} (source: {Source}).",
                topic,
                threadId,
                source);

        public static void GeneratingNonResponsiveSubject(
            ILogger logger,
            Guid threadId,
            string audience,
            string topic)
            => logger.Debug(
                "Generating non-responsive subject for thread {ThreadId} (audience: {Audience}) with topic '{Topic}'.",
                threadId,
                audience,
                topic);

        public static void FailedToGenerateSubject(ILogger logger, Guid threadId, Exception exception)
            => logger.Warning(exception, "Failed to generate subject for thread {ThreadId}; using fallback.", threadId);

        public static void NonResponsiveThreadUsingTopic(ILogger logger, Guid threadId, string topic)
            => logger.Debug(
                "Non-responsive thread {ThreadId} using topic '{Topic}' for initial email generation.",
                threadId,
                topic);

        public static void ThreadCompletedWithPendingAttachments(
            ILogger logger,
            Guid threadId,
            int docs,
            int images,
            int voicemails)
            => logger.Warning(
                "Thread {ThreadId} completed with {Docs} document(s), {Images} image(s), {Voicemails} voicemail(s) still pending attachment placement.",
                threadId,
                docs,
                images,
                voicemails);
    }

    private async Task<List<EmailThread>> GenerateThreadsForStorylineAsync(
        Storyline storyline,
        IReadOnlyList<StoryBeat> beats,
        WizardState state,
        ProcessStorylineContext processContext,
        CancellationToken ct)
    {
        if (beats == null || beats.Count == 0)
        {
            Log.NoStoryBeatsForStoryline(_logger, storyline.Title);
            return new List<EmailThread>();
        }

        var threadPlans = BuildThreadPlans(storyline, state.Characters, beats, processContext.CharacterContexts, state);
        if (threadPlans.Count == 0)
        {
            Log.NoThreadPlansGenerated(_logger, storyline.Title);
            return new List<EmailThread>();
        }

        Log.GeneratingThreadsForStoryline(_logger, threadPlans.Count, storyline.Title);

        var threads = new EmailThread?[threadPlans.Count];
        var systemPrompt = BuildEmailSystemPrompt();
        var context = new ThreadPlanContext(
            storyline,
            state.CompanyDomain,
            state.Config,
            state.DomainThemes,
            systemPrompt,
            state,
            processContext.CharacterContexts,
            processContext.CharacterRoutingContexts,
            processContext.Result,
            processContext.ProgressData,
            processContext.Progress,
            processContext.ProgressLock,
            processContext.EmlService,
            processContext.SaveSemaphore,
            processContext.SavedThreads);
        var degree = Math.Max(1, state.Config.ParallelThreads);

        if (degree == 1)
        {
            foreach (var plan in threadPlans)
            {
                ct.ThrowIfCancellationRequested();
                await GenerateThreadPlanAsync(
                    plan,
                    threads,
                    context,
                    ct);
            }
        }
        else
        {
            await Parallel.ForEachAsync(threadPlans, new ParallelOptions
            {
                MaxDegreeOfParallelism = degree,
                CancellationToken = ct
            }, async (plan, token) =>
            {
                await GenerateThreadPlanAsync(
                    plan,
                    threads,
                    context,
                    token);
            });
        }

        return threads.Where(t => t != null).Select(t => t!).ToList();
    }

    internal sealed class ThreadPlanContext(
        Storyline storyline,
        string domain,
        GenerationConfig config,
        Dictionary<string, OrganizationTheme> domainThemes,
        string systemPrompt,
        WizardState state,
        Dictionary<Guid, CharacterContext> characterContexts,
        Dictionary<Guid, CharacterRoutingContext> characterRoutingContexts,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        EmlFileService emlService,
        SemaphoreSlim saveSemaphore,
        ConcurrentDictionary<Guid, bool> savedThreads)
    {
        public Storyline Storyline { get; } = storyline;
        public string Domain { get; } = domain;
        public GenerationConfig Config { get; } = config;
        public Dictionary<string, OrganizationTheme> DomainThemes { get; } = domainThemes;
        public string SystemPrompt { get; } = systemPrompt;
        public WizardState State { get; } = state;
        public Dictionary<Guid, CharacterContext> CharacterContexts { get; } = characterContexts;
        public Dictionary<Guid, CharacterRoutingContext> CharacterRoutingContexts { get; } = characterRoutingContexts;
        public GenerationResult Result { get; } = result;
        public GenerationProgress ProgressData { get; } = progressData;
        public IProgress<GenerationProgress> Progress { get; } = progress;
        public object ProgressLock { get; } = progressLock;
        public EmlFileService EmlService { get; } = emlService;
        public SemaphoreSlim SaveSemaphore { get; } = saveSemaphore;
        public ConcurrentDictionary<Guid, bool> SavedThreads { get; } = savedThreads;
    }

    private sealed class ThreadExecutionState
    {
        public ThreadExecutionState(ThreadPlan plan, ThreadPlanContext context, Random rng)
        {
            Plan = plan;
            Context = context;
            Rng = rng;
            TargetLookup = plan.Thread.EmailMessages.ToDictionary(m => m.Id);
        }

        public ThreadPlan Plan { get; }
        public ThreadPlanContext Context { get; }
        public Random Rng { get; }
        public string ThreadTopic { get; set; } = string.Empty;
        public string ThreadSubject { get; set; } = string.Empty;
        public bool ThreadSubjectGenerated { get; set; }
        public NonResponsiveArchetypeSelection? NonResponsiveArchetypeSelection { get; set; }
        public Dictionary<Guid, EmailMessage> TargetLookup { get; }
        public Dictionary<Guid, EmailMessage> GeneratedLookup { get; } = new();
        public List<EmailMessage> Chronological { get; } = new();
        public ThreadFactTable FactTable { get; } = new();
        public AttachmentCarryoverState AttachmentCarryover { get; } = new();
        public int FailedEmails { get; set; }
    }

    private sealed class ThreadFactTable
    {
        public HashSet<string> Participants { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Events { get; } = new();
        public List<string> Decisions { get; } = new();
        public List<string> Conflicts { get; } = new();
        public List<string> OpenQuestions { get; } = new();
    }

    private sealed class AttachmentCarryoverState
    {
        public Queue<AttachmentType> PendingDocuments { get; } = new();
        public int PendingImages { get; set; }
        public int PendingVoicemails { get; set; }
    }

    private sealed record AttachmentRequirement(
        bool RequiresDocument,
        AttachmentType? DocumentType,
        bool DocumentFromPending,
        bool RequiresImage,
        bool ImageFromPending,
        bool IsImageInline,
        bool RequiresVoicemail,
        bool VoicemailFromPending,
        bool IsFinalSlot);

    private sealed record AttachmentPlanDetails(
        string? DocumentDescription,
        string? ImageDescription,
        string? VoicemailContext);

    private sealed record ResolvedEmailParticipants(
        Character From,
        List<Character> To,
        List<Character> Cc);

    private sealed record ParticipantScoringProfile(
        Character Character,
        string IndustryKey,
        Dictionary<string, double> TagPresence);

    private sealed record NonResponsiveArchetypeSelection(
        TopicArchetype Archetype,
        string Subject,
        Dictionary<string, string> EntityValues);

    private sealed class EmailValidationResult
    {
        public List<string> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;
    }

    private sealed record EmailDraft(string BodyPlain);

    private sealed record EmailDraftResult(bool Success, EmailDraft? Draft, List<string> Errors);

    private List<ThreadPlan> BuildThreadPlans(
        Storyline storyline,
        List<Character> characters,
        IReadOnlyList<StoryBeat> beats,
        Dictionary<Guid, CharacterContext> characterContexts,
        WizardState state)
    {
        var threadPlans = new List<ThreadPlan>();
        var planIndex = 0;

        foreach (var beat in beats)
        {
            if (!TryPrepareBeatForPlanning(beat))
                continue;

            var emailsAssigned = 0;
            foreach (var thread in beat.Threads)
            {
                EnsureThreadIsValidForBeat(thread, beat, storyline);

                var threadEmailCount = thread.EmailMessages.Count;
                var threadStartDate = DateHelper.InterpolateDateInRange(beat.StartDate, beat.EndDate, (double)emailsAssigned / beat.EmailCount);
                var threadEndDate = DateHelper.InterpolateDateInRange(beat.StartDate, beat.EndDate, (double)(emailsAssigned + threadEmailCount) / beat.EmailCount);
                _threadGenerator.AssignThreadParticipants(thread, state.Organizations, _rng);
                EmailThreadGenerator.EnsurePlaceholderMessages(thread, threadEmailCount);

                var participants = ResolveThreadParticipants(thread, characters);
                var participantLookup = participants.ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);
                var participantList = BuildCharacterList(participants, characterContexts);
                var threadSeed = DeterministicSeedHelper.CreateSeed(
                    "thread-gen",
                    state.GenerationSeed.ToString(CultureInfo.InvariantCulture),
                    thread.Id.ToString("N"));
                var structurePlan = ThreadStructurePlanner.BuildPlan(
                    thread,
                    threadEmailCount,
                    threadStartDate,
                    threadEndDate,
                    state.Config,
                    state.GenerationSeed);
                var branchCount = CountBranches(structurePlan);

                Log.CreatedThreadStructurePlan(_logger, thread.Id, threadEmailCount, branchCount);

                threadPlans.Add(new ThreadPlan(
                    planIndex++,
                    thread,
                    threadEmailCount,
                    threadStartDate,
                    threadEndDate,
                    beat.Name,
                    participants,
                    participantLookup,
                    participantList,
                    structurePlan,
                    threadSeed));

                emailsAssigned += threadEmailCount;
            }

            EnsureBeatEmailCountMatches(beat, emailsAssigned);
        }

        return threadPlans;
    }

    private sealed record PlannedTotals(int TotalThreads, int TotalEmails, int TotalAttachments, int TotalImages);

    private static PlannedTotals CalculatePlannedTotals(
        IReadOnlyList<Storyline> storylines,
        GenerationConfig config)
    {
        var totalThreads = 0;
        var totalEmails = 0;
        var totalAttachments = 0;
        var totalImages = 0;

        foreach (var storyline in storylines)
        {
            totalThreads += storyline.ThreadCount;
            totalEmails += storyline.EmailCount;

            foreach (var beat in storyline.Beats ?? Array.Empty<StoryBeat>())
            {
                foreach (var thread in beat.Threads ?? Array.Empty<EmailThread>())
                {
                    var emailCount = thread.EmailMessages.Count;
                    if (emailCount <= 0)
                        continue;

                    var (docCount, imageCount, voicemailCount) = CalculateAttachmentTotals(config, emailCount);
                    var calendarChecks = CalculateCalendarInviteChecks(config, emailCount);

                    totalAttachments += docCount + imageCount + voicemailCount + calendarChecks;
                    totalImages += imageCount;
                }
            }
        }

        return new PlannedTotals(totalThreads, totalEmails, totalAttachments, totalImages);
    }

    private static int CalculateCalendarInviteChecks(GenerationConfig config, int emailCount)
    {
        if (!config.IncludeCalendarInvites || config.CalendarInvitePercentage <= 0 || emailCount <= 0)
            return 0;

        return Math.Max(1, (int)Math.Round(emailCount * config.CalendarInvitePercentage / 100.0));
    }

    private static bool TryPrepareBeatForPlanning(StoryBeat beat)
    {
        if (beat.EmailCount <= 0)
        {
            if (beat.Threads.Count > 0)
                throw new InvalidOperationException($"Story beat '{beat.Name}' has threads but zero planned emails.");
            return false;
        }

        if (beat.Threads.Count == 0)
            throw new InvalidOperationException($"Story beat '{beat.Name}' has no planned threads. Regenerate story beats.");

        return true;
    }

    private static void EnsureThreadIsValidForBeat(EmailThread thread, StoryBeat beat, Storyline storyline)
    {
        if (thread.EmailMessages.Count == 0)
            throw new InvalidOperationException($"Story beat '{beat.Name}' has a thread with no planned emails.");
        if (thread.StoryBeatId != beat.Id)
            throw new InvalidOperationException($"Story beat '{beat.Name}' has a thread with an unexpected StoryBeatId.");
        if (thread.StorylineId != beat.StorylineId || thread.StorylineId != storyline.Id)
            throw new InvalidOperationException($"Story beat '{beat.Name}' has a thread with an unexpected StorylineId.");
    }

    private static List<Character> ResolveThreadParticipants(EmailThread thread, List<Character> characters)
    {
        var participants = thread.CharacterParticipants
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => c.Id)
            .Select(g => g.First())
            .ToList();

        if (participants.Count == 0)
        {
            participants = characters
                .Where(c => !string.IsNullOrWhiteSpace(c.Email))
                .ToList();
        }

        return participants;
    }

    private static void EnsureBeatEmailCountMatches(StoryBeat beat, int emailsAssigned)
    {
        if (emailsAssigned != beat.EmailCount)
            throw new InvalidOperationException($"Story beat '{beat.Name}' planned emails ({emailsAssigned}) do not match beat email count ({beat.EmailCount}).");
    }

    private async Task GenerateThreadPlanAsync(
        ThreadPlan plan,
        EmailThread?[] threads,
        ThreadPlanContext context,
        CancellationToken ct)
    {
        var stage = "thread-generation";

        using var threadIdScope = LogContext.PushProperty("ThreadId", plan.Thread.Id);
        using var storylineIdScope = LogContext.PushProperty("StorylineId", context.Storyline.Id);
        using var beatNameScope = LogContext.PushProperty("BeatName", plan.BeatName);
        using var plannedEmailCountScope = LogContext.PushProperty("PlannedEmailCount", plan.EmailCount);

        try
        {
            var thread = await GenerateThreadWithRetriesAsync(
                plan,
                context,
                ct);

            stage = "attachment-generation";
            var attachmentRng = CreateThreadRandom(
                context.State.GenerationSeed,
                "thread-assets",
                thread.Id);
            await GenerateThreadAssetsAsync(
                thread,
                context.State,
                context.Result,
                context.ProgressData,
                context.Progress,
                context.ProgressLock,
                attachmentRng,
                ct);

            stage = "save-eml";
            await SaveThreadAsync(thread, context, ct);

            threads[plan.Index] = thread;

            var threadSucceeded = thread.EmailMessages.All(m => !m.GenerationFailed);
            RecordThreadCompletion(context, plan, thread, success: threadSucceeded, stage: stage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordThreadFailure(context, plan, stage, ex);
        }
    }

    private async Task GenerateThreadAssetsAsync(
        EmailThread thread,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        Random rng,
        CancellationToken ct)
    {
        var emails = thread.EmailMessages;
        var threadTopic = ResolveThreadTopicForAssets(thread, state);

        await GeneratePlannedDocumentsAsync(emails, state, result, progressData, progress, progressLock, rng, ct);
        await GeneratePlannedImagesAsync(emails, state, result, progressData, progress, progressLock, threadTopic, ct);
        await GenerateCalendarInvitesAsync(emails, state, result, progressData, progress, progressLock, rng, ct);
        await GeneratePlannedVoicemailsAsync(emails, state, result, progressData, progress, progressLock, threadTopic, ct);
    }

    private static string ResolveThreadTopicForAssets(EmailThread thread, WizardState state)
    {
        if (!string.IsNullOrWhiteSpace(thread.Topic))
            return thread.Topic;

        var subjectSource = thread.EmailMessages.Count > 0 ? thread.EmailMessages[0].Subject : string.Empty;
        var subject = ThreadingHelper.GetCleanSubject(subjectSource);
        if (!string.IsNullOrWhiteSpace(subject))
            return subject;

        if (!string.IsNullOrWhiteSpace(state.Topic))
            return state.Topic;

        return "Project update";
    }

    private static string ResolveEmailTopic(EmailMessage email, WizardState state)
    {
        var subject = ThreadingHelper.GetCleanSubject(email.Subject);
        if (!string.IsNullOrWhiteSpace(subject))
            return subject;
        if (!string.IsNullOrWhiteSpace(state.Topic))
            return state.Topic;
        return "Project update";
    }

    private async Task GeneratePlannedDocumentsAsync(
        IReadOnlyList<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        Random rng,
        CancellationToken ct)
    {
        var emailsWithPlannedDocuments = emails.Where(e => e.PlannedHasDocument).ToList();
        if (emailsWithPlannedDocuments.Count == 0)
            return;

        foreach (var email in emailsWithPlannedDocuments)
        {
            ct.ThrowIfCancellationRequested();

            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Creating attachment for: {email.Subject}";
            });

            await GeneratePlannedDocumentAsync(email, state, rng, ct);

            var attachment = email.Attachments.FirstOrDefault(a =>
                a.Type == AttachmentType.Word ||
                a.Type == AttachmentType.Excel ||
                a.Type == AttachmentType.PowerPoint);

            GenerationProgress snapshot;
            lock (progressLock)
            {
                progressData.CompletedAttachments++;

                if (attachment != null)
                {
                    switch (attachment.Type)
                    {
                        case AttachmentType.Word:
                            result.WordDocumentsGenerated++;
                            break;
                        case AttachmentType.Excel:
                            result.ExcelDocumentsGenerated++;
                            break;
                        case AttachmentType.PowerPoint:
                            result.PowerPointDocumentsGenerated++;
                            break;
                    }
                }

                snapshot = progressData.Snapshot();
            }

            progress.Report(snapshot);
        }
    }

    private async Task GeneratePlannedImagesAsync(
        IReadOnlyList<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        string threadTopic,
        CancellationToken ct)
    {
        if (!state.Config.IncludeImages)
            return;

        var emailsWithPlannedImages = emails.Where(e => e.PlannedHasImage).ToList();
        if (emailsWithPlannedImages.Count == 0)
            return;

        foreach (var email in emailsWithPlannedImages)
        {
            ct.ThrowIfCancellationRequested();

            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Generating image for: {email.Subject}";
            });

            await GeneratePlannedImageAsync(email, state, threadTopic, ct);

            var hasImage = email.Attachments.Any(a => a.Type == AttachmentType.Image);

            GenerationProgress snapshot;
            lock (progressLock)
            {
                progressData.CompletedAttachments++;
                progressData.CompletedImages++;
                if (hasImage)
                {
                    result.ImagesGenerated++;
                }

                snapshot = progressData.Snapshot();
            }

            progress.Report(snapshot);
        }
    }

    private async Task GenerateCalendarInvitesAsync(
        IReadOnlyList<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        Random rng,
        CancellationToken ct)
    {
        if (!state.Config.IncludeCalendarInvites || state.Config.CalendarInvitePercentage <= 0)
            return;

        var maxCalendarEmails = Math.Max(1, (int)Math.Round(emails.Count * state.Config.CalendarInvitePercentage / 100.0));
        var emailsToCheckForCalendar = emails
            // Randomized ordering (seeded RNG) to sample calendar-invite candidates.
            .OrderBy(_ => rng.Next())
            .Take(maxCalendarEmails)
            .ToList();

        foreach (var email in emailsToCheckForCalendar)
        {
            ct.ThrowIfCancellationRequested();
            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Checking calendar invite for: {email.Subject}";
            });
            await DetectAndAddCalendarInviteAsync(email, state.Characters, ct);

            var hasInvite = email.Attachments.Any(a => a.Type == AttachmentType.CalendarInvite);
            GenerationProgress snapshot;
            lock (progressLock)
            {
                progressData.CompletedAttachments++;
                if (hasInvite)
                {
                    result.CalendarInvitesGenerated++;
                }

                snapshot = progressData.Snapshot();
            }

            progress.Report(snapshot);
        }
    }

    private async Task GeneratePlannedVoicemailsAsync(
        IReadOnlyList<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        string threadTopic,
        CancellationToken ct)
    {
        if (!state.Config.IncludeVoicemails)
            return;

        var emailsWithPlannedVoicemails = emails.Where(e => e.PlannedHasVoicemail).ToList();
        if (emailsWithPlannedVoicemails.Count == 0)
            return;

        foreach (var email in emailsWithPlannedVoicemails)
        {
            ct.ThrowIfCancellationRequested();

            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Generating voicemail for: {email.From.FullName}";
            });

            await GeneratePlannedVoicemailAsync(email, state, threadTopic, ct);

            var hasVoicemail = email.Attachments.Any(a => a.Type == AttachmentType.Voicemail);
            GenerationProgress snapshot;
            lock (progressLock)
            {
                progressData.CompletedAttachments++;
                if (hasVoicemail)
                {
                    result.VoicemailsGenerated++;
                }

                snapshot = progressData.Snapshot();
            }

            progress.Report(snapshot);
        }
    }

    private static async Task SaveThreadAsync(
        EmailThread thread,
        ThreadPlanContext context,
        CancellationToken ct)
    {
        var subject = GetThreadSubject(thread);

        await context.SaveSemaphore.WaitAsync(ct);
        try
        {
            ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, p =>
            {
                p.CurrentOperation = $"Saving EML files for thread: {subject}";
            });

            var emlProgress = new Progress<(int completed, int total, string currentFile)>(p =>
            {
                ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, pd =>
                {
                    pd.CurrentOperation = $"Saving: {p.currentFile}";
                });
            });

            await context.EmlService.SaveThreadEmailsAsync(
                thread,
                context.Config.OutputFolder,
                context.Config.OrganizeBySender,
                emlProgress,
                releaseAttachmentContent: true,
                ct: ct);

            context.SavedThreads.TryAdd(thread.Id, true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save EML files for thread '{subject}': {ex.Message}", ex);
        }
        finally
        {
            context.SaveSemaphore.Release();
        }
    }

    internal static (int totalDocAttachments, int totalImageAttachments, int totalVoicemailAttachments) CalculateAttachmentTotals(
        GenerationConfig config,
        int emailCount)
    {
        ArgumentNullException.ThrowIfNull(config);

        var totalDocAttachments = config.AttachmentPercentage > 0 && config.EnabledAttachmentTypes.Count > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.AttachmentPercentage / 100.0))
            : 0;
        var totalImageAttachments = config.IncludeImages && config.ImagePercentage > 0
            ? Math.Max(1, (int)Math.Round(emailCount * config.ImagePercentage / 100.0))
            : 0;
        var totalVoicemailAttachments = config.IncludeVoicemails && config.VoicemailPercentage > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.VoicemailPercentage / 100.0))
            : 0;

        totalDocAttachments = Math.Min(totalDocAttachments, emailCount);
        totalImageAttachments = Math.Min(totalImageAttachments, emailCount);
        totalVoicemailAttachments = Math.Min(totalVoicemailAttachments, emailCount);

        return (totalDocAttachments, totalImageAttachments, totalVoicemailAttachments);
    }

    internal async Task<EmailThread> GenerateThreadWithRetriesAsync(
        ThreadPlan plan,
        ThreadPlanContext context,
        CancellationToken ct)
    {
        var thread = plan.Thread;
        EmailThreadGenerator.ResetThreadForRetry(thread, plan.EmailCount);
        var rng = new Random(plan.ThreadSeed);
        var executionState = new ThreadExecutionState(plan, context, rng);

        InitializeFactTable(executionState);
        InitializeThreadMetadata(executionState);

        Log.GeneratingThread(_logger, thread.Id, plan.EmailCount);

        foreach (var slot in plan.StructurePlan.Slots)
        {
            ct.ThrowIfCancellationRequested();
            await GenerateEmailForSlotAsync(slot, executionState, ct);
        }

        ThreadingHelper.SetupThreading(thread, context.Domain);
        return thread;
    }

    private static void InitializeFactTable(ThreadExecutionState state)
    {
        foreach (var participant in state.Plan.Participants)
        {
            if (!string.IsNullOrWhiteSpace(participant.Email))
                state.FactTable.Participants.Add(participant.Email);
        }
    }

    private static void InitializeThreadMetadata(ThreadExecutionState state)
    {
        var isResponsive = state.Plan.Thread.Relevance == EmailThread.ThreadRelevance.Responsive || state.Plan.Thread.IsHot;
        if (!isResponsive)
            return;

        if (string.IsNullOrWhiteSpace(state.ThreadTopic))
            state.ThreadTopic = ResolveThreadTopic(state);
        if (string.IsNullOrWhiteSpace(state.ThreadSubject) && !string.IsNullOrWhiteSpace(state.ThreadTopic))
            state.ThreadSubject = ResolveThreadSubjectFromTopic(state);
        if (string.IsNullOrWhiteSpace(state.Plan.Thread.Topic) && !string.IsNullOrWhiteSpace(state.ThreadTopic))
            state.Plan.Thread.Topic = state.ThreadTopic;
    }

    private static string ResolveThreadTopic(ThreadExecutionState state)
    {
        if (!string.IsNullOrWhiteSpace(state.Plan.Thread.Topic))
            return state.Plan.Thread.Topic;

        if (state.Plan.Thread.Relevance != EmailThread.ThreadRelevance.NonResponsive
            && !string.IsNullOrWhiteSpace(state.Plan.BeatName))
        {
            return state.Plan.BeatName;
        }

        if (state.Plan.Thread.Relevance == EmailThread.ThreadRelevance.NonResponsive)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(state.Context.Storyline.Title))
            return state.Context.Storyline.Title;

        if (!string.IsNullOrWhiteSpace(state.Context.State.Topic))
            return state.Context.State.Topic;

        return "Project update";
    }

    private static string ResolveThreadSubjectFromTopic(ThreadExecutionState state)
    {
        var topic = string.IsNullOrWhiteSpace(state.ThreadTopic) ? "Project update" : state.ThreadTopic;
        var subject = topic;
        subject = ThreadingHelper.GetCleanSubject(subject);
        return string.IsNullOrWhiteSpace(subject) ? "Project update" : subject;
    }

    private async Task EnsureThreadSubjectAsync(
        ThreadEmailSlotPlan slot,
        ResolvedEmailParticipants participants,
        ThreadExecutionState state,
        CancellationToken ct)
    {
        if (state.ThreadSubjectGenerated || slot.Index != 0)
            return;

        state.ThreadSubjectGenerated = true;

        var isResponsive = state.Plan.Thread.Relevance == EmailThread.ThreadRelevance.Responsive || state.Plan.Thread.IsHot;
        if (isResponsive)
        {
            if (string.IsNullOrWhiteSpace(state.ThreadTopic))
                state.ThreadTopic = ResolveThreadTopic(state);

            var fallback = string.IsNullOrWhiteSpace(state.ThreadSubject)
                ? ResolveThreadSubjectFromTopic(state)
                : state.ThreadSubject;

            try
            {
                var prompt = BuildThreadSubjectPrompt(participants, state);
                var response = await GetEmailSubjectResponseAsync(
                    BuildEmailSubjectSystemPrompt(),
                    prompt,
                    $"Email Subject (thread {state.Plan.Thread.Id})",
                    ct);

                var resolved = NormalizeSubject(response?.Subject, fallback);
                state.ThreadSubject = resolved;
            }
            catch (Exception ex)
            {
                Log.FailedToGenerateSubject(_logger, state.Plan.Thread.Id, ex);
                state.ThreadSubject = NormalizeSubject(state.ThreadSubject, fallback);
            }

            return;
        }

        var audience = IsExternalAudience(participants) ? "external" : "internal";
        Log.GeneratingNonResponsiveSubject(
            _logger,
            state.Plan.Thread.Id,
            audience,
            state.ThreadTopic);

        var nonResponsiveFallback = string.IsNullOrWhiteSpace(state.ThreadSubject)
            ? "Project update"
            : state.ThreadSubject;

        TopicArchetype? archetype = null;
        try
        {
            archetype = SelectNonResponsiveArchetype(participants, state, slot.SentDate)
                        ?? TopicGenerationModelStore.Model.Archetypes.FirstOrDefault();

            if (archetype == null)
                throw new InvalidOperationException("No archetypes available for non-responsive subject generation.");

            var subjectPrompt = BuildNonResponsiveSubjectPrompt(participants, state, archetype);
            var response = await GetNonResponsiveSubjectResponseAsync(
                BuildEmailSubjectSystemPrompt(),
                subjectPrompt,
                $"Non-responsive Subject (thread {state.Plan.Thread.Id})",
                ct);

            var entityValues = NormalizeEntityValues(archetype, response?.EntityValues, state.Rng, slot.SentDate, participants);
            var subject = NormalizeNonResponsiveSubject(response?.Subject, nonResponsiveFallback, archetype);

            state.ThreadSubject = subject;
            state.NonResponsiveArchetypeSelection = new NonResponsiveArchetypeSelection(archetype, subject, entityValues);
        }
        catch (Exception ex)
        {
            Log.FailedToGenerateSubject(_logger, state.Plan.Thread.Id, ex);
            var fallbackArchetype = archetype ?? TopicGenerationModelStore.Model.Archetypes.FirstOrDefault();
            if (fallbackArchetype != null)
            {
                state.ThreadSubject = NormalizeNonResponsiveSubject(state.ThreadSubject, nonResponsiveFallback, fallbackArchetype);
                var entityValues = NormalizeEntityValues(fallbackArchetype, null, state.Rng, slot.SentDate, participants);
                state.NonResponsiveArchetypeSelection =
                    new NonResponsiveArchetypeSelection(fallbackArchetype, state.ThreadSubject, entityValues);
            }
            else
            {
                state.ThreadSubject = NormalizeSubject(state.ThreadSubject, nonResponsiveFallback);
            }
        }
    }

    private enum TopicRoutingDirection
    {
        Send,
        Receive
    }

    private enum TopicRoutingAudience
    {
        Internal,
        External
    }

    internal enum TopicTier
    {
        Core = 0,
        Department = 1,
        Role = 2
    }

    private sealed class TopicTieredSet
    {
        public HashSet<int> Topics { get; } = new();
        public Dictionary<int, TopicTier> Tiers { get; } = new();
    }

    private static List<Character> CollectRecipients(ResolvedEmailParticipants participants)
    {
        var recipients = new List<Character>();
        var seen = new HashSet<Guid>();

        foreach (var recipient in participants.To.Concat(participants.Cc))
        {
            if (seen.Add(recipient.Id))
                recipients.Add(recipient);
        }

        return recipients;
    }

    private static CharacterRoutingContext ResolveRoutingContext(Character character, ThreadExecutionState state)
    {
        if (state.Context.CharacterRoutingContexts.TryGetValue(character.Id, out var context))
            return context;

        return new CharacterRoutingContext(null, null, character.OrganizationId);
    }

    private static TopicTieredSet BuildParticipantTopicSet(
        CharacterRoutingContext context,
        TopicRoutingDirection direction,
        TopicRoutingAudience audience,
        TopicRoutingCatalog routing)
    {
        var result = new TopicTieredSet();

        AddTierTopics(result, routing.Core, TopicTier.Core, direction, audience);

        if (context.Department.HasValue && routing.TryGetDepartmentSlug(context.Department.Value, out var slug))
        {
            var departmentTier = routing.GetDepartmentRouting(slug);
            AddTierTopics(result, departmentTier, TopicTier.Department, direction, audience);
        }

        if (context.Department.HasValue && context.Role.HasValue
            && routing.TryGetRoleFileId(context.Department.Value, context.Role.Value, out var fileId))
        {
            var roleTier = routing.GetRoleRouting(fileId);
            AddTierTopics(result, roleTier, TopicTier.Role, direction, audience);
        }

        return result;
    }

    private static void AddTierTopics(
        TopicTieredSet target,
        TopicRoutingCatalog.TopicRoutingTier? tier,
        TopicTier tierType,
        TopicRoutingDirection direction,
        TopicRoutingAudience audience)
    {
        if (tier == null)
            return;

        var buckets = direction == TopicRoutingDirection.Send ? tier.Send : tier.Receive;
        if (buckets == null)
            return;

        var scoped = audience == TopicRoutingAudience.Internal ? buckets.Internal : buckets.External;
        AddTopics(target, scoped, tierType);
        AddTopics(target, buckets.Both, tierType);
    }

    private static void AddTopics(TopicTieredSet target, List<int>? topics, TopicTier tier)
    {
        if (topics == null || topics.Count == 0)
            return;

        foreach (var topicId in topics)
        {
            if (target.Topics.Add(topicId))
            {
                target.Tiers[topicId] = tier;
            }
            else if (target.Tiers.TryGetValue(topicId, out var existing) && tier > existing)
            {
                target.Tiers[topicId] = tier;
            }
        }
    }

    internal static List<int> SampleWeightedTopics(
        List<int> candidates,
        Dictionary<int, TopicTier> tiers,
        int sampleCount,
        Random rng)
    {
        if (candidates.Count <= sampleCount)
            return new List<int>(candidates);

        var remaining = new List<int>(candidates);
        var sampled = new List<int>(sampleCount);

        for (var i = 0; i < sampleCount; i++)
        {
            var totalWeight = 0.0;
            foreach (var topicId in remaining)
                totalWeight += GetTierWeight(tiers[topicId]);

            var pick = rng.NextDouble() * totalWeight;
            var cumulative = 0.0;
            var chosenIndex = remaining.Count - 1;

            for (var j = 0; j < remaining.Count; j++)
            {
                cumulative += GetTierWeight(tiers[remaining[j]]);
                if (pick <= cumulative)
                {
                    chosenIndex = j;
                    break;
                }
            }

            sampled.Add(remaining[chosenIndex]);
            remaining.RemoveAt(chosenIndex);
        }

        return sampled;
    }

    private static double GetTierWeight(TopicTier tier)
    {
        return tier switch
        {
            TopicTier.Role => 1.0,
            TopicTier.Department => 0.5,
            _ => 0.2
        };
    }

    private static string? TrySelectTopicText(
        List<int> sampled,
        TopicRoutingCatalog routing,
        Random rng)
    {
        var available = new List<int>(sampled);
        while (available.Count > 0)
        {
            var index = rng.Next(available.Count);
            var topicId = available[index];
            available.RemoveAt(index);

            if (routing.TryGetTopicText(topicId, out var topicText))
                return topicText;
        }

        return null;
    }

    private async Task GenerateEmailForSlotAsync(
        ThreadEmailSlotPlan slot,
        ThreadExecutionState state,
        CancellationToken ct)
    {
        var plan = state.Plan;
        var context = state.Context;
        var thread = plan.Thread;
        var isResponsive = thread.Relevance == EmailThread.ThreadRelevance.Responsive || thread.IsHot;

        state.TargetLookup.TryGetValue(slot.EmailId, out var target);
        if (target == null)
            throw new InvalidOperationException($"Missing email placeholder for slot {slot.Index} in thread {thread.Id}.");

        var parentEmail = slot.ParentEmailId != null && state.GeneratedLookup.TryGetValue(slot.ParentEmailId.Value, out var parent)
            ? parent
            : null;

        var requirement = BuildAttachmentRequirement(
            slot,
            state.AttachmentCarryover,
            slot.Index == state.Plan.EmailCount - 1);

        var participants = ResolveParticipantsForSlot(slot, state, parentEmail);
        await EnsureThreadSubjectAsync(slot, participants, state, ct);
        var attachmentDetails = BuildAttachmentPlanDetails(requirement, state, slot);
        var draftResult = await GenerateValidatedEmailDraftAsync(
            slot,
            parentEmail,
            requirement,
            attachmentDetails,
            participants,
            state,
            ct);

        if (draftResult.Success && draftResult.Draft != null)
        {
            ApplyDraftToEmail(
                draftResult.Draft,
                target,
                state,
                slot,
                requirement,
                attachmentDetails,
                parentEmail,
                participants);

            state.GeneratedLookup[target.Id] = target;
            state.Chronological.Add(target);
            UpdateFactTable(state, target);
            UpdateAttachmentCarryover(state.AttachmentCarryover, slot, requirement, success: true);

            RecordEmailCompletion(context, thread, target, success: true, stage: "email-generation");
        }
        else
        {
            var failureReason = draftResult.Errors.Count == 0
                ? "Unknown email generation failure."
                : string.Join("; ", draftResult.Errors);

            PopulateFailureEmail(target, state, slot, parentEmail, failureReason);
            state.GeneratedLookup[target.Id] = target;
            state.Chronological.Add(target);
            state.FailedEmails++;
            UpdateAttachmentCarryover(state.AttachmentCarryover, slot, requirement, success: false);

            lock (context.ProgressLock)
            {
                context.Result.AddError(
                    $"Email slot {slot.Index + 1} failed for thread {thread.Id}: {failureReason}");
            }

            RecordEmailCompletion(context, thread, target, success: false, stage: "email-generation");
        }

        if (slot.Index == state.Plan.EmailCount - 1)
        {
            if (state.AttachmentCarryover.PendingDocuments.Count > 0
                || state.AttachmentCarryover.PendingImages > 0
                || state.AttachmentCarryover.PendingVoicemails > 0)
            {
                Log.ThreadCompletedWithPendingAttachments(
                    _logger,
                    thread.Id,
                    state.AttachmentCarryover.PendingDocuments.Count,
                    state.AttachmentCarryover.PendingImages,
                    state.AttachmentCarryover.PendingVoicemails);
            }
        }
    }

    private static AttachmentRequirement BuildAttachmentRequirement(
        ThreadEmailSlotPlan slot,
        AttachmentCarryoverState carryover,
        bool isFinalSlot)
    {
        var requiresDoc = slot.Attachments.HasDocument;
        var docFromPending = false;
        var docType = slot.Attachments.DocumentType;

        if (!requiresDoc && carryover.PendingDocuments.Count > 0)
        {
            requiresDoc = true;
            docFromPending = true;
            docType = carryover.PendingDocuments.Peek();
        }

        var requiresImage = slot.Attachments.HasImage;
        var imageFromPending = false;
        var isInline = slot.Attachments.IsImageInline;

        if (!requiresImage && carryover.PendingImages > 0)
        {
            requiresImage = true;
            imageFromPending = true;
            isInline = isInline || carryover.PendingImages > 0;
        }

        var requiresVoicemail = slot.Attachments.HasVoicemail;
        var voicemailFromPending = false;

        if (!requiresVoicemail && carryover.PendingVoicemails > 0)
        {
            requiresVoicemail = true;
            voicemailFromPending = true;
        }

        return new AttachmentRequirement(
            requiresDoc,
            docType,
            docFromPending,
            requiresImage,
            imageFromPending,
            isInline,
            requiresVoicemail,
            voicemailFromPending,
            isFinalSlot);
    }

    private static AttachmentType ResolvePlannedDocumentType(AttachmentRequirement requirement, GenerationConfig config)
    {
        if (requirement.DocumentType.HasValue)
            return requirement.DocumentType.Value;
        if (config.EnabledAttachmentTypes.Count > 0)
            return config.EnabledAttachmentTypes[0];
        return AttachmentType.Word;
    }

    private static AttachmentPlanDetails BuildAttachmentPlanDetails(
        AttachmentRequirement requirement,
        ThreadExecutionState state,
        ThreadEmailSlotPlan slot)
    {
        var topic = string.IsNullOrWhiteSpace(state.ThreadTopic) ? "Project update" : state.ThreadTopic;
        var phase = string.IsNullOrWhiteSpace(slot.NarrativePhase) ? "update" : slot.NarrativePhase.ToLowerInvariant();

        string? documentDescription = null;
        if (requirement.RequiresDocument)
        {
            var docType = ResolvePlannedDocumentType(requirement, state.Context.Config);
            documentDescription = docType switch
            {
                AttachmentType.Excel => $"{topic} tracker ({phase})",
                AttachmentType.PowerPoint => $"{topic} slides ({phase})",
                _ => $"{topic} summary ({phase})"
            };
        }

        var imageDescription = requirement.RequiresImage
            ? $"Screenshot related to {topic} ({phase})"
            : null;

        var voicemailContext = requirement.RequiresVoicemail
            ? $"Follow-up on {topic} ({phase})"
            : null;

        return new AttachmentPlanDetails(documentDescription, imageDescription, voicemailContext);
    }

    private static ResolvedEmailParticipants ResolveParticipantsForSlot(
        ThreadEmailSlotPlan slot,
        ThreadExecutionState state,
        EmailMessage? parentEmail)
    {
        var participants = state.Plan.Participants;
        if (participants.Count == 0)
            throw new InvalidOperationException("No participants available for thread.");

        var rng = state.Rng;
        Character fromChar;
        var toChars = new List<Character>();
        var ccChars = new List<Character>();

        if (slot.Intent == ThreadEmailIntent.New || parentEmail == null)
        {
            fromChar = participants[rng.Next(participants.Count)];
            var toPool = participants.Where(c => c.Id != fromChar.Id).ToList();
            if (toPool.Count == 0)
                toPool = participants.ToList();
            toChars.Add(toPool[rng.Next(toPool.Count)]);

            if (participants.Count > 2 && rng.NextDouble() < 0.25)
            {
                var ccPool = participants.Where(c => c.Id != fromChar.Id && c.Id != toChars[0].Id).ToList();
                if (ccPool.Count > 0)
                    ccChars.Add(ccPool[rng.Next(ccPool.Count)]);
            }
        }
        else if (slot.Intent == ThreadEmailIntent.Reply)
        {
            var replyFromPool = parentEmail.To.Concat(parentEmail.Cc).Distinct().ToList();
            if (replyFromPool.Count == 0)
                replyFromPool = participants;
            fromChar = replyFromPool[rng.Next(replyFromPool.Count)];

            var replyTo = parentEmail.From.Id == fromChar.Id
                ? participants.FirstOrDefault(c => c.Id != fromChar.Id) ?? fromChar
                : parentEmail.From;
            toChars.Add(replyTo);

            if (rng.NextDouble() < 0.4)
            {
                var ccPool = parentEmail.To.Concat(parentEmail.Cc)
                    .Where(c => c.Id != fromChar.Id && c.Id != replyTo.Id)
                    .Distinct()
                    .ToList();
                if (ccPool.Count == 0)
                    ccPool = participants.Where(c => c.Id != fromChar.Id && c.Id != replyTo.Id).ToList();
                ccChars.AddRange(ccPool);
            }
        }
        else
        {
            var forwardFromPool = parentEmail.To.Concat(parentEmail.Cc).Distinct().ToList();
            if (forwardFromPool.Count == 0)
                forwardFromPool = participants;
            fromChar = forwardFromPool[rng.Next(forwardFromPool.Count)];

            var excluded = new HashSet<Guid>(parentEmail.To.Select(c => c.Id));
            excluded.UnionWith(parentEmail.Cc.Select(c => c.Id));
            excluded.Add(parentEmail.From.Id);
            excluded.Add(fromChar.Id);

            var forwardToPool = participants.Where(c => !excluded.Contains(c.Id)).ToList();
            if (forwardToPool.Count == 0)
                forwardToPool = participants.Where(c => c.Id != fromChar.Id).ToList();
            if (forwardToPool.Count == 0)
                forwardToPool = participants;
            toChars.Add(forwardToPool[rng.Next(forwardToPool.Count)]);

            if (participants.Count > 2 && rng.NextDouble() < 0.2)
            {
                var ccPool = participants.Where(c => c.Id != fromChar.Id && c.Id != toChars[0].Id).ToList();
                if (ccPool.Count > 0)
                    ccChars.Add(ccPool[rng.Next(ccPool.Count)]);
            }
        }

        if (toChars.Count == 0)
        {
            var fallback = participants.FirstOrDefault(c => c.Id != fromChar.Id) ?? fromChar;
            toChars.Add(fallback);
        }

        return new ResolvedEmailParticipants(fromChar, toChars, ccChars);
    }

    private async Task<EmailDraftResult> GenerateValidatedEmailDraftAsync(
        ThreadEmailSlotPlan slot,
        EmailMessage? parentEmail,
        AttachmentRequirement requirement,
        AttachmentPlanDetails attachmentDetails,
        ResolvedEmailParticipants participants,
        ThreadExecutionState state,
        CancellationToken ct)
    {
        var isResponsive = state.Plan.Thread.Relevance == EmailThread.ThreadRelevance.Responsive || state.Plan.Thread.IsHot;
        var isNonResponsiveFirstEmail = !isResponsive && slot.Index == 0;
        var maxRepairs = Math.Max(0, state.Context.Config.MaxEmailRepairAttempts);
        EmailDraft? draft = null;
        List<string> errors = new();
        string? lastBody = null;

        for (var attempt = 0; attempt <= maxRepairs; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            SingleEmailApiResponse? response;
            if (attempt == 0)
            {
                if (isNonResponsiveFirstEmail)
                {
                    var archetypeSelection = state.NonResponsiveArchetypeSelection;
                    if (archetypeSelection == null)
                    {
                        var fallbackArchetype = SelectNonResponsiveArchetype(participants, state, slot.SentDate)
                                                ?? TopicGenerationModelStore.Model.Archetypes.FirstOrDefault();
                        if (fallbackArchetype == null)
                            throw new InvalidOperationException("No archetypes available for non-responsive body generation.");

                        var entityValues = NormalizeEntityValues(fallbackArchetype, null, state.Rng, slot.SentDate, participants);
                        archetypeSelection = new NonResponsiveArchetypeSelection(
                            fallbackArchetype,
                            NormalizeNonResponsiveSubject(state.ThreadSubject, "Project update", fallbackArchetype),
                            entityValues);
                        state.NonResponsiveArchetypeSelection = archetypeSelection;
                        state.ThreadSubject = archetypeSelection.Subject;
                    }

                    var prompt = BuildNonResponsiveBodyPrompt(
                        slot,
                        parentEmail,
                        requirement,
                        attachmentDetails,
                        participants,
                        state,
                        archetypeSelection);
                    response = await GetEmailResponseAsync(
                        state.Context.SystemPrompt,
                        prompt,
                        $"Non-responsive Email Generation (thread {state.Plan.Thread.Id}, slot {slot.Index + 1})",
                        ct);
                }
                else
                {
                    var prompt = BuildSingleEmailUserPrompt(
                        slot,
                        parentEmail,
                        requirement,
                        attachmentDetails,
                        participants,
                        state);
                    response = await GetEmailResponseAsync(
                        state.Context.SystemPrompt,
                        prompt,
                        $"Email Generation (thread {state.Plan.Thread.Id}, slot {slot.Index + 1})",
                        ct);
                }
            }
            else
            {
                var repairPrompt = BuildEmailRepairPrompt(
                    slot,
                    parentEmail,
                    requirement,
                    attachmentDetails,
                    participants,
                    state,
                    lastBody,
                    errors);
                response = await GetEmailResponseAsync(
                    state.Context.SystemPrompt,
                    repairPrompt,
                    $"Email Repair (thread {state.Plan.Thread.Id}, slot {slot.Index + 1}, attempt {attempt})",
                    ct);
            }

            if (response == null || string.IsNullOrWhiteSpace(response.BodyPlain))
            {
                errors = new List<string> { "LLM returned no email body." };
                continue;
            }

            lastBody = response.BodyPlain;

            var validation = ValidateEmailBody(
                response.BodyPlain,
                slot,
                parentEmail,
                requirement);

            errors = validation.Errors;
            if (validation.IsValid)
            {
                draft = new EmailDraft(response.BodyPlain);
                return new EmailDraftResult(true, draft, errors);
            }
        }

        return new EmailDraftResult(false, draft, errors);
    }

    private static EmailValidationResult ValidateEmailBody(
        string bodyPlain,
        ThreadEmailSlotPlan slot,
        EmailMessage? parentEmail,
        AttachmentRequirement requirement)
    {
        var result = new EmailValidationResult();
        if (string.IsNullOrWhiteSpace(bodyPlain))
            result.Errors.Add("Email body is required.");

        if (slot.Intent != ThreadEmailIntent.New && parentEmail == null)
            result.Errors.Add("Parent email is missing for reply/forward slot.");

        if (requirement.RequiresDocument && !MentionsAttachment(bodyPlain, "document"))
            result.Errors.Add("Email body must reference the document attachment.");
        if (requirement.RequiresImage && !MentionsAttachment(bodyPlain, "image"))
            result.Errors.Add("Email body must reference the image attachment.");
        if (requirement.RequiresVoicemail && !MentionsAttachment(bodyPlain, "voicemail"))
            result.Errors.Add("Email body must reference the voicemail attachment.");

        return result;
    }

    private static bool MentionsAttachment(string body, string type)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var lowered = body.ToLowerInvariant();
        return type switch
        {
            "document" => lowered.Contains("attach", StringComparison.Ordinal)
                          || lowered.Contains("attachment", StringComparison.Ordinal)
                          || lowered.Contains("document", StringComparison.Ordinal)
                          || lowered.Contains("spreadsheet", StringComparison.Ordinal)
                          || lowered.Contains("report", StringComparison.Ordinal),
            "image" => lowered.Contains("screenshot", StringComparison.Ordinal)
                       || lowered.Contains("photo", StringComparison.Ordinal)
                       || lowered.Contains("image", StringComparison.Ordinal)
                       || lowered.Contains("attached", StringComparison.Ordinal),
            "voicemail" => lowered.Contains("voicemail", StringComparison.Ordinal)
                           || lowered.Contains("voice message", StringComparison.Ordinal)
                           || lowered.Contains("left you a message", StringComparison.Ordinal),
            _ => lowered.Contains("attach", StringComparison.Ordinal)
        };
    }

    private static void ApplyDraftToEmail(
        EmailDraft draft,
        EmailMessage target,
        ThreadExecutionState state,
        ThreadEmailSlotPlan slot,
        AttachmentRequirement requirement,
        AttachmentPlanDetails attachmentDetails,
        EmailMessage? parentEmail,
        ResolvedEmailParticipants participants)
    {
        var thread = state.Plan.Thread;
        var participantsList = state.Plan.Participants;
        var fromChar = participants.From;
        var toChars = participants.To;
        var ccChars = participants.Cc;

        var resolvedDocType = ResolvePlannedDocumentType(requirement, state.Context.Config);

        var subject = ResolveSubject(state.ThreadSubject, slot.Intent, slot.Index);
        var sentDate = Clock.EnsureKind(slot.SentDate, DateTimeKind.Local);
        var correctedBody = CorrectSignatureBlock(draft.BodyPlain, fromChar, participantsList);
        var fullBody = AppendQuotedContent(correctedBody, parentEmail, slot.Intent == ThreadEmailIntent.Forward);

        var senderDomain = fromChar.Domain;
        state.Context.DomainThemes.TryGetValue(senderDomain, out var senderTheme);

        target.EmailThreadId = thread.Id;
        target.StoryBeatId = thread.StoryBeatId;
        target.StorylineId = thread.StorylineId;
        target.ParentEmailId = slot.ParentEmailId;
        target.RootEmailId = slot.RootEmailId;
        target.BranchId = slot.BranchId;
        target.From = fromChar;
        target.SetTo(toChars);
        target.SetCc(ccChars);
        target.Subject = subject;
        target.BodyPlain = fullBody;
        target.BodyHtml = HtmlEmailFormatter.ConvertToHtml(fullBody, senderTheme);
        target.SentDate = sentDate;
        target.SequenceInThread = slot.Index;
        target.PlannedHasDocument = requirement.RequiresDocument;
        target.PlannedDocumentType = requirement.RequiresDocument
            ? resolvedDocType.ToString().ToLowerInvariant()
            : null;
        target.PlannedDocumentDescription = requirement.RequiresDocument ? attachmentDetails.DocumentDescription : null;
        target.PlannedHasImage = requirement.RequiresImage;
        target.PlannedImageDescription = requirement.RequiresImage ? attachmentDetails.ImageDescription : null;
        target.PlannedIsImageInline = requirement.IsImageInline;
        target.PlannedHasVoicemail = requirement.RequiresVoicemail;
        target.PlannedVoicemailContext = requirement.RequiresVoicemail ? attachmentDetails.VoicemailContext : null;
        target.GenerationFailed = false;
        target.GenerationFailureReason = null;
    }

    private static string AppendQuotedContent(string body, EmailMessage? parentEmail, bool isForward)
    {
        if (parentEmail == null)
            return body;

        if (isForward)
            return body + ThreadingHelper.FormatForwardedContent(parentEmail);

        return body + ThreadingHelper.FormatQuotedReply(parentEmail);
    }

    private static void PopulateFailureEmail(
        EmailMessage target,
        ThreadExecutionState state,
        ThreadEmailSlotPlan slot,
        EmailMessage? parentEmail,
        string reason)
    {
        var participants = state.Plan.Participants;
        var fromChar = participants.FirstOrDefault() ?? throw new InvalidOperationException("No participants available.");
        var toChar = participants.FirstOrDefault(c => c.Id != fromChar.Id) ?? fromChar;

        var subjectBase = string.IsNullOrWhiteSpace(state.ThreadSubject)
            ? "Untitled thread"
            : state.ThreadSubject;

        var subject = slot.Intent switch
        {
            ThreadEmailIntent.Forward => ThreadingHelper.AddForwardPrefix(subjectBase),
            ThreadEmailIntent.Reply => ThreadingHelper.AddReplyPrefix(subjectBase),
            _ => subjectBase
        };

        var body = $@"Hi {toChar.FirstName},

This email could not be generated due to an internal error.

{fromChar.SignatureBlock}";

        target.EmailThreadId = state.Plan.Thread.Id;
        target.StoryBeatId = state.Plan.Thread.StoryBeatId;
        target.StorylineId = state.Plan.Thread.StorylineId;
        target.ParentEmailId = slot.ParentEmailId;
        target.RootEmailId = slot.RootEmailId;
        target.BranchId = slot.BranchId;
        target.From = fromChar;
        target.SetTo(new[] { toChar });
        target.SetCc(Array.Empty<Character>());
        target.Subject = subject;
        target.BodyPlain = body;
        target.BodyHtml = HtmlEmailFormatter.ConvertToHtml(body);
        target.SentDate = Clock.EnsureKind(slot.SentDate, DateTimeKind.Local);
        target.SequenceInThread = slot.Index;
        target.PlannedHasDocument = false;
        target.PlannedDocumentType = null;
        target.PlannedDocumentDescription = null;
        target.PlannedHasImage = false;
        target.PlannedImageDescription = null;
        target.PlannedIsImageInline = false;
        target.PlannedHasVoicemail = false;
        target.PlannedVoicemailContext = null;
        target.GenerationFailed = true;
        target.GenerationFailureReason = reason;
    }

    private static void UpdateAttachmentCarryover(
        AttachmentCarryoverState carryover,
        ThreadEmailSlotPlan slot,
        AttachmentRequirement requirement,
        bool success)
    {
        if (requirement.RequiresDocument)
        {
            if (!success)
            {
                if (slot.Attachments.DocumentType.HasValue)
                    carryover.PendingDocuments.Enqueue(slot.Attachments.DocumentType.Value);
            }
            else if (requirement.DocumentFromPending && carryover.PendingDocuments.Count > 0)
            {
                carryover.PendingDocuments.Dequeue();
            }
        }

        if (requirement.RequiresImage)
        {
            if (!success)
            {
                if (!requirement.ImageFromPending)
                    carryover.PendingImages++;
            }
            else if (requirement.ImageFromPending && carryover.PendingImages > 0)
            {
                carryover.PendingImages--;
            }
        }

        if (requirement.RequiresVoicemail)
        {
            if (!success)
            {
                if (!requirement.VoicemailFromPending)
                    carryover.PendingVoicemails++;
            }
            else if (requirement.VoicemailFromPending && carryover.PendingVoicemails > 0)
            {
                carryover.PendingVoicemails--;
            }
        }
    }

    private static void UpdateFactTable(ThreadExecutionState state, EmailMessage email)
    {
        var subject = ThreadingHelper.GetCleanSubject(email.Subject);
        var toNames = email.To.Count > 0
            ? string.Join(", ", email.To.Select(t => t.FirstName))
            : "recipients";
        state.FactTable.Events.Add($"{email.From.FirstName} emailed {toNames} about {subject}.");

        var body = email.BodyPlain ?? string.Empty;
        var lowered = body.ToLowerInvariant();
        if (lowered.Contains("approved", StringComparison.Ordinal) || lowered.Contains("decision", StringComparison.Ordinal))
            state.FactTable.Decisions.Add($"{email.From.FirstName} referenced a decision in '{subject}'.");
        if (lowered.Contains("concern", StringComparison.Ordinal)
            || lowered.Contains("problem", StringComparison.Ordinal)
            || lowered.Contains("issue", StringComparison.Ordinal)
            || lowered.Contains("disagree", StringComparison.Ordinal))
            state.FactTable.Conflicts.Add($"{email.From.FirstName} flagged a concern in '{subject}'.");

        var questions = body.Split('\n')
            .Where(line => line.Contains('?', StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Where(line => line.Length > 3)
            .Take(2);
        foreach (var question in questions)
        {
            if (state.FactTable.OpenQuestions.Count >= 5)
                break;
            state.FactTable.OpenQuestions.Add(question);
        }
    }

    private static string BuildBodyFormattingRules(ThreadEmailIntent intent)
    {
        var threadingRule = intent switch
        {
            ThreadEmailIntent.New => "- This is the first email (no reply/forward).",
            ThreadEmailIntent.Forward => "- This email MUST be a forward to the parent email.",
            _ => "- This email MUST be a reply to the parent email."
        };

        return $@"- Use the fixed From/To/Cc values exactly as provided
{threadingRule}
- Do NOT include any header lines (Subject/To/From/Cc) in bodyPlain
- DO NOT include quoted previous emails in bodyPlain
- The signature MUST match the From address";
    }

    private static string BuildThreadSubjectPrompt(
        ResolvedEmailParticipants participants,
        ThreadExecutionState state)
    {
        var thread = state.Plan.Thread;
        var isResponsive = thread.Relevance == EmailThread.ThreadRelevance.Responsive || thread.IsHot;
        var isExternal = IsExternalAudience(participants);
        var audienceLabel = isExternal ? "External (cross-organization)" : "Internal (same organization)";
        var topic = string.IsNullOrWhiteSpace(state.ThreadTopic) ? "Project update" : state.ThreadTopic;
        var participantSummary = BuildSubjectParticipantsSection(participants, state.Context.CharacterContexts);
        var beatContext = BuildSubjectBeatContext(state);
        var availableCharacters = state.Plan.ParticipantList;

        var storylineSection = isResponsive
            ? $@"Storyline: {state.Context.Storyline.Title}
Summary: {state.Context.Storyline.Summary}
{beatContext}"
            : string.Empty;

        var relevanceLine = isResponsive
            ? "Thread type: RESPONSIVE (related to the storyline)."
            : "Thread type: NON-RESPONSIVE (generic corporate thread).";

        var guidance = isResponsive
            ? @"Guidance:
- Generate a realistic subject for the FIRST email in this thread (no Re:/Fwd:).
- Use the responsive topic plus some context from the current story beat and participants.
- It is OK if the subject is only loosely related to the topic or storyline.
- Avoid story-like or dramatic phrasing; it should read like a normal workplace email.
- Keep it concise (roughly 3-10 words)."
            : @"Guidance:
- Generate a typical, mundane corporate subject for the FIRST email in this thread (no Re:/Fwd:).
- Use the topic, roles involved, and whether the audience is internal or external.
- Do NOT reference any case narrative or story beats.
- Keep it concise (roughly 3-10 words).";

        var schema = BuildEmailSubjectSchema();

        return PromptScaffolding.JoinSections($@"{relevanceLine}
Audience: {audienceLabel}
Topic: {topic}

{storylineSection}

Participants:
{participantSummary}

Available Characters:
{availableCharacters}

{guidance}", PromptScaffolding.JsonSchemaSection(schema));
    }

    private static string BuildNonResponsiveSubjectPrompt(
        ResolvedEmailParticipants participants,
        ThreadExecutionState state,
        TopicArchetype archetype)
    {
        var orgLookup = BuildOrganizationLookup(state);
        var sender = BuildParticipantDescriptor(participants.From, state, orgLookup);
        var recipients = participants.To.Count == 0
            ? "None"
            : string.Join("\n", participants.To.Select(p => BuildParticipantDescriptor(p, state, orgLookup)));
        var archetypeMeta = $@"Id: {archetype.Id}
Category: {archetype.Category}
Intent: {archetype.Intent}
Archetype tags: {string.Join(", ", archetype.ArchetypeTags)}";
        var entitiesSection = $@"Required entities: {FormatEntityList(archetype.EntitiesRequired)}
Optional entities: {FormatEntityList(archetype.EntitiesOptional)}
Entity values MUST include all required entities. Use strings for all values.";
        var schema = BuildNonResponsiveSubjectSchema();

        return PromptScaffolding.JoinSections(
            PromptScaffolding.Section("PRIMARY INSTRUCTION", archetype.ArchetypeSubjectPrompt),
            PromptScaffolding.Section("SENDER", sender),
            PromptScaffolding.Section("RECIPIENTS (To)", recipients),
            PromptScaffolding.Section("ARCHETYPE METADATA", archetypeMeta),
            PromptScaffolding.Section("ENTITIES", entitiesSection),
            PromptScaffolding.JsonSchemaSection(schema));
    }

    private static string BuildNonResponsiveBodyPrompt(
        ThreadEmailSlotPlan slot,
        EmailMessage? parentEmail,
        AttachmentRequirement requirement,
        AttachmentPlanDetails attachmentDetails,
        ResolvedEmailParticipants participants,
        ThreadExecutionState state,
        NonResponsiveArchetypeSelection selection)
    {
        var archetype = selection.Archetype;
        var schema = BuildSingleEmailSchema();
        var addressing = BuildAddressingSection(participants);
        var senderProfile = BuildSenderProfileSection(participants.From, state.Context.CharacterContexts);
        var attachmentInstructions = BuildSingleEmailAttachmentInstructions(requirement, attachmentDetails, isResponsiveThread: false);
        var bodyRules = BuildBodyFormattingRules(slot.Intent);
        var availableCharacters = $"Available Characters:\n{state.Plan.ParticipantList}";
        var plannedSentTime = $"Planned Sent Time: {slot.SentDate:O}";
        var threadTopicLine = string.IsNullOrWhiteSpace(state.ThreadTopic)
            ? string.Empty
            : $"Thread topic (for attachment relevance only): {state.ThreadTopic}";
        var orgLookup = BuildOrganizationLookup(state);
        var senderDescriptor = BuildParticipantDescriptor(participants.From, state, orgLookup);
        var recipientDescriptors = participants.To.Count == 0
            ? "None"
            : string.Join("\n", participants.To.Select(p => BuildParticipantDescriptor(p, state, orgLookup)));
        var ccDescriptors = participants.Cc.Count == 0
            ? "None"
            : string.Join("\n", participants.Cc.Select(p => BuildParticipantDescriptor(p, state, orgLookup)));
        var participantContext = $@"Sender: {senderDescriptor}
Recipients (To):
{recipientDescriptors}
Cc:
{ccDescriptors}";
        var entityValuesJson = JsonSerializer.Serialize(selection.EntityValues, IndentedJsonOptions);
        var archetypeMeta = $@"Id: {archetype.Id}
Category: {archetype.Category}
Intent: {archetype.Intent}
Archetype tags: {string.Join(", ", archetype.ArchetypeTags)}
Required entities: {FormatEntityList(archetype.EntitiesRequired)}
Optional entities: {FormatEntityList(archetype.EntitiesOptional)}";

        var parentContext = parentEmail == null
            ? "This is the first email in the thread."
            : $"Parent email: {BuildParentSummary(parentEmail)}";

        var criticalRules = $@"CRITICAL RULES:
{bodyRules}";

        return PromptScaffolding.JoinSections(
            PromptScaffolding.Section("PRIMARY INSTRUCTION", archetype.ArchetypeBodyPrompt),
            PromptScaffolding.Section("SUBJECT", selection.Subject),
            threadTopicLine,
            addressing,
            PromptScaffolding.Section("PARTICIPANT CONTEXT", participantContext),
            senderProfile,
            availableCharacters,
            plannedSentTime,
            PromptScaffolding.Section("ARCHETYPE METADATA", archetypeMeta),
            PromptScaffolding.Section("ENTITY VALUES (JSON)", entityValuesJson),
            PromptScaffolding.Section("ALIGNMENT REQUIREMENT",
                "The body MUST align with the provided subject and use all required entity values."),
            parentContext,
            PromptScaffolding.Section("ATTACHMENT REQUIREMENTS", attachmentInstructions),
            PromptScaffolding.JsonSchemaSection(schema),
            criticalRules);
    }

    private static string BuildSingleEmailUserPrompt(
        ThreadEmailSlotPlan slot,
        EmailMessage? parentEmail,
        AttachmentRequirement requirement,
        AttachmentPlanDetails attachmentDetails,
        ResolvedEmailParticipants participants,
        ThreadExecutionState state)
    {
        var thread = state.Plan.Thread;
        var isResponsive = thread.Relevance == EmailThread.ThreadRelevance.Responsive || thread.IsHot;
        var schema = BuildSingleEmailSchema();

        var storylineHeader = isResponsive
            ? $"Storyline: {state.Context.Storyline.Title}\nSummary: {state.Context.Storyline.Summary}"
            : "Thread Intent: NON-RESPONSIVE (generic corporate thread).";

        var storyBeatContext = isResponsive
            ? BuildStoryBeatContext(state.Context.Storyline, state.Plan.Start, state.Plan.End)
            : BuildNonResponsiveContext(state.ThreadTopic);

        var parentContext = parentEmail == null
            ? "This is the first email in the thread."
            : $"Parent email: {BuildParentSummary(parentEmail)}";

        var history = BuildThreadHistorySection(state.Chronological);
        var facts = BuildFactSummary(state.FactTable);
        var attachmentInstructions = BuildSingleEmailAttachmentInstructions(requirement, attachmentDetails, isResponsive);
        var bodyRules = BuildBodyFormattingRules(slot.Intent);

        var narrativeLabel = isResponsive
            ? slot.NarrativePhase
            : $"NON-RESPONSIVE THREAD - {slot.NarrativePhase}";

        var addressing = BuildAddressingSection(participants);
        var senderProfile = BuildSenderProfileSection(participants.From, state.Context.CharacterContexts);

        var criticalRules = $@"CRITICAL RULES:
{bodyRules}";

        return PromptScaffolding.JoinSections($@"{storylineHeader}

Subject: {state.ThreadSubject}
{addressing}
{senderProfile}

NARRATIVE PHASE: {narrativeLabel}

Available Characters:
{state.Plan.ParticipantList}

Date Range: {state.Plan.Start:yyyy-MM-dd} to {state.Plan.End:yyyy-MM-dd}
Planned Sent Time: {slot.SentDate:O}

{(isResponsive ? storyBeatContext : string.Empty)}

{parentContext}

{history}

FACT SUMMARY:
{facts}

CONTENT REQUIREMENTS:
- Keep tone workplace-appropriate.

{attachmentInstructions}", PromptScaffolding.JsonSchemaSection(schema), criticalRules);
    }

    private static string BuildEmailRepairPrompt(
        ThreadEmailSlotPlan slot,
        EmailMessage? parentEmail,
        AttachmentRequirement requirement,
        AttachmentPlanDetails attachmentDetails,
        ResolvedEmailParticipants participants,
        ThreadExecutionState state,
        string? lastBody,
        IEnumerable<string> errors)
    {
        var isResponsive = state.Plan.Thread.Relevance == EmailThread.ThreadRelevance.Responsive || state.Plan.Thread.IsHot;
        var parentContext = parentEmail == null
            ? "This is the first email in the thread."
            : $"Parent email: {BuildParentSummary(parentEmail)}";

        var errorList = string.Join("\n", errors.Select(e => $"- {e}"));
        var attachmentInstructions = BuildSingleEmailAttachmentInstructions(requirement, attachmentDetails, isResponsive);
        var schema = BuildSingleEmailSchema();
        var addressing = BuildAddressingSection(participants);
        var bodyRules = BuildBodyFormattingRules(slot.Intent);
        var availableCharacters = state.Plan.ParticipantList;
        var senderProfile = BuildSenderProfileSection(participants.From, state.Context.CharacterContexts);

        return PromptScaffolding.JoinSections($@"You MUST fix the following issues in the prior draft (do not rewrite the whole thread):
{errorList}

Subject: {state.ThreadSubject}
{addressing}
{senderProfile}

Available Characters:
{availableCharacters}

{parentContext}

Planned Sent Time: {slot.SentDate:O}

Attachment Requirements:
{attachmentInstructions}

BODY FORMAT RULES:
{bodyRules}

Previous Draft (for reference):
{lastBody}", PromptScaffolding.JsonSchemaSection(schema), "Return a corrected JSON response following the schema.");
    }

    private static string BuildSingleEmailSchema()
    {
        return """
{
  "bodyPlain": "string (full email body including greeting and signature)"
}
""";
    }

    private static string BuildEmailSubjectSchema()
    {
        return """
{
  "subject": "string (concise, realistic email subject line)"
}
""";
    }

    private static string BuildNonResponsiveSubjectSchema()
    {
        return """
{
  "subject": "string (4-12 words)",
  "entityValues": {
    "entity_name": "string value"
  }
}
""";
    }

    private static string BuildParentSummary(EmailMessage parent)
    {
        var preview = parent.BodyPlain ?? string.Empty;
        preview = preview.Length > 160 ? preview[..160] + "..." : preview;
        return $"{parent.From.FullName} on {parent.SentDate:yyyy-MM-dd HH:mm}: {preview.Replace("\n", " ").Trim()}";
    }

    private static string BuildAddressingSection(ResolvedEmailParticipants participants)
    {
        var toList = string.Join(", ", participants.To.Select(p => $"{p.FullName} <{p.Email}>"));
        var ccList = participants.Cc.Count == 0
            ? "None"
            : string.Join(", ", participants.Cc.Select(p => $"{p.FullName} <{p.Email}>"));
        return $"From: {participants.From.FullName} <{participants.From.Email}>\nTo: {toList}\nCc: {ccList}";
    }

    private static Dictionary<Guid, Organization> BuildOrganizationLookup(ThreadExecutionState state)
    {
        return state.Context.State.Organizations.ToDictionary(o => o.Id);
    }

    private static string BuildParticipantDescriptor(
        Character character,
        ThreadExecutionState state,
        IReadOnlyDictionary<Guid, Organization> orgLookup)
    {
        var display = $"{character.FullName} <{character.Email}>";
        var organizationName = "Unknown";
        var industryLabel = "Unknown";
        var departmentLabel = "Unknown";
        var roleLabel = "Unknown";

        if (state.Context.CharacterContexts.TryGetValue(character.Id, out var context))
        {
            organizationName = context.Organization;
            departmentLabel = context.Department;
            roleLabel = context.Role;
        }

        if (state.Context.CharacterRoutingContexts.TryGetValue(character.Id, out var routing)
            && orgLookup.TryGetValue(routing.OrganizationId, out var organization))
        {
            organizationName = organization.Name;
            industryLabel = EnumHelper.HumanizeEnumName(organization.Industry.ToString());
        }

        return $"Name: {display} | Org: {organizationName} | Industry: {industryLabel} | Department: {departmentLabel} | Role: {roleLabel}";
    }

    private static string FormatEntityList(IReadOnlyCollection<string> entities)
    {
        return entities.Count == 0 ? "None" : string.Join(", ", entities);
    }

    private static bool IsExternalAudience(ResolvedEmailParticipants participants)
    {
        var fromOrgId = participants.From.OrganizationId;
        return participants.To.Concat(participants.Cc)
            .Any(p => p.OrganizationId != fromOrgId);
    }

    private static string BuildSubjectParticipantsSection(
        ResolvedEmailParticipants participants,
        Dictionary<Guid, CharacterContext> contexts)
    {
        string Describe(Character character)
        {
            if (contexts.TryGetValue(character.Id, out var context))
                return $"{character.FullName} ({context.Role}, {context.Department} @ {context.Organization})";

            return character.FullName;
        }

        var toList = participants.To.Count == 0
            ? "None"
            : string.Join("; ", participants.To.Select(Describe));
        var ccList = participants.Cc.Count == 0
            ? "None"
            : string.Join("; ", participants.Cc.Select(Describe));

        return $"From: {Describe(participants.From)}\nTo: {toList}\nCc: {ccList}";
    }

    private static string BuildSubjectBeatContext(ThreadExecutionState state)
    {
        var beats = state.Context.Storyline.Beats;
        if (beats != null && beats.Count > 0)
        {
            var match = beats.FirstOrDefault(b => string.Equals(b.Name, state.Plan.BeatName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                var plot = string.IsNullOrWhiteSpace(match.Plot) ? string.Empty : $" - {match.Plot}";
                return $"Current story beat: {match.Name}{plot}";
            }
        }

        return string.IsNullOrWhiteSpace(state.Plan.BeatName)
            ? "Current story beat: (unknown)"
            : $"Current story beat: {state.Plan.BeatName}";
    }

    private static string NormalizeSubject(string? subject, string fallback)
    {
        var resolved = ThreadingHelper.GetCleanSubject(subject ?? string.Empty);
        if (string.IsNullOrWhiteSpace(resolved))
            resolved = ThreadingHelper.GetCleanSubject(fallback);
        return string.IsNullOrWhiteSpace(resolved) ? "Project update" : resolved;
    }

    private static string BuildThreadHistorySection(List<EmailMessage> history)
    {
        if (history.Count == 0)
            return "No prior emails in this thread yet.";

        var exports = history.Select(BuildExportedEmailForPrompt).ToList();
        var combined = string.Join("\n\n---\n\n", exports);

        if (combined.Length <= 3500)
            return $"THREAD HISTORY:\n{combined}";

        return $"THREAD HISTORY (summary):\n{BuildOrderedSummary(history)}";
    }

    private static string BuildOrderedSummary(IReadOnlyList<EmailMessage> history)
    {
        var lines = history.Select((m, i) =>
        {
            var preview = m.BodyPlain ?? string.Empty;
            preview = preview.Length > 120 ? preview[..120] + "..." : preview;
            return $"{i + 1}. {m.From.FirstName} to {string.Join(", ", m.To.Select(t => t.FirstName))}: {preview.Replace("\n", " ").Trim()}";
        });

        return string.Join("\n", lines);
    }

    private static string BuildFactSummary(ThreadFactTable factTable)
    {
        var lines = new List<string>();
        if (factTable.Events.Count > 0)
        {
            lines.Add("Recent events:");
            lines.AddRange(factTable.Events.TakeLast(3).Select(e => $"  - {e}"));
        }

        if (factTable.Decisions.Count > 0)
        {
            lines.Add("Decisions:");
            lines.AddRange(factTable.Decisions.TakeLast(2).Select(e => $"  - {e}"));
        }

        if (factTable.Conflicts.Count > 0)
        {
            lines.Add("Conflicts:");
            lines.AddRange(factTable.Conflicts.TakeLast(2).Select(e => $"  - {e}"));
        }

        if (factTable.OpenQuestions.Count > 0)
        {
            lines.Add("Open questions:");
            lines.AddRange(factTable.OpenQuestions.TakeLast(2).Select(e => $"  - {e}"));
        }

        return lines.Count == 0 ? "No prior facts recorded." : string.Join("\n", lines);
    }

    private static string BuildSingleEmailAttachmentInstructions(
        AttachmentRequirement requirement,
        AttachmentPlanDetails attachmentDetails,
        bool isResponsiveThread)
    {
        var instructions = new List<string>();
        var relevanceLabel = isResponsiveThread ? "storyline" : "thread topic";

        if (requirement.RequiresDocument)
        {
            var docType = requirement.DocumentType?.ToString().ToLowerInvariant() ?? "document";
            instructions.Add($@"DOCUMENT REQUIRED:
- Include a {docType} attachment relevant to the {relevanceLabel}
- The body MUST reference the attachment");
            if (!string.IsNullOrWhiteSpace(attachmentDetails.DocumentDescription))
                instructions.Add($"- Planned document description: {attachmentDetails.DocumentDescription}");
        }
        else
        {
            instructions.Add("No document attachment for this email (do not mention a document).");
        }

        if (requirement.RequiresImage)
        {
            var inlineLabel = requirement.IsImageInline ? "inline image" : "image attachment";
            instructions.Add($@"IMAGE REQUIRED:
- Include an {inlineLabel} relevant to the {relevanceLabel}
- The body MUST reference the image");
            if (!string.IsNullOrWhiteSpace(attachmentDetails.ImageDescription))
                instructions.Add($"- Planned image description: {attachmentDetails.ImageDescription}");
        }
        else
        {
            instructions.Add("No image attachment for this email (do not mention an image).");
        }

        if (requirement.RequiresVoicemail)
        {
            instructions.Add($@"VOICEMAIL REQUIRED:
- Include a voicemail attachment related to the {relevanceLabel}
- The body MUST reference the voicemail");
            if (!string.IsNullOrWhiteSpace(attachmentDetails.VoicemailContext))
                instructions.Add($"- Planned voicemail context: {attachmentDetails.VoicemailContext}");
        }
        else
        {
            instructions.Add("No voicemail attachment for this email (do not mention a voicemail).");
        }

        return string.Join("\n", instructions);
    }

    private static bool IsContractViolation(Exception ex)
    {
        if (ex is ArgumentException)
            return true;

        if (ex is InvalidOperationException && ex.Message.Contains("placeholder count", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    protected virtual Task<SingleEmailApiResponse?> GetEmailResponseAsync(
        string systemPrompt,
        string userPrompt,
        string operationName,
        CancellationToken ct)
        => _openAI.GetJsonCompletionAsync<SingleEmailApiResponse>(systemPrompt, userPrompt, operationName, ct);

    protected virtual Task<EmailSubjectResponse?> GetEmailSubjectResponseAsync(
        string systemPrompt,
        string userPrompt,
        string operationName,
        CancellationToken ct)
        => _openAI.GetJsonCompletionAsync<EmailSubjectResponse>(systemPrompt, userPrompt, operationName, ct);

    protected virtual Task<NonResponsiveSubjectResponse?> GetNonResponsiveSubjectResponseAsync(
        string systemPrompt,
        string userPrompt,
        string operationName,
        CancellationToken ct)
        => _openAI.GetJsonCompletionAsync<NonResponsiveSubjectResponse>(systemPrompt, userPrompt, operationName, ct);

    internal static string GetNarrativePhase(int batchIndex, int totalBatches)
    {
        if (totalBatches <= 1)
        {
            return "SINGLE-BATCH - Introduce the conflict and leave the core dispute unresolved with open questions or pending decisions.";
        }

        if (batchIndex == 0)
        {
            return "BEGINNING - Introduce the conflict and set up the storyline.";
        }

        if (batchIndex >= totalBatches - 1)
        {
            return $"LATE STAGE (Part {batchIndex + 1} of {totalBatches}) - Escalate consequences but do NOT fully resolve the case; leave open questions or pending action.";
        }

        return $"MIDDLE (Part {batchIndex + 1} of {totalBatches}) - Escalate tensions and develop the conflict.";
    }

    /// <summary>
    /// Build the system prompt for email generation (shared across batches)
    /// </summary>
    private static string BuildEmailSystemPrompt()
    {

        return PromptScaffolding.AppendJsonOnlyInstruction(@"You are generating realistic corporate email BODY content for a fictional eDiscovery dataset.
These should read like authentic workplace communications between the provided characters.

CORE RULES (NON-NEGOTIABLE)
- Entirely fictional: do NOT use real company names, real people, real domains, or identifiable real-world incidents.
- Use only the provided characters and their organizations/domains.
- Workplace realism: allow minor typos, shorthand (FYI/pls), and imperfect memory.
- No explicit sexual content, graphic violence, hate/harassment/slurs, or self-harm. Keep HR/retaliation professional and non-explicit.

EMOTIONAL AUTHENTICITY:
- People in conflict should show it (passive-aggressive, curt, defensive, frustrated).
- Allies share context and vent; rivals clash.
- Keep it workplace-appropriate, not abusive.

COMMUNICATION STYLE:
1. Vary email length dependent on the character's communication style (short replies, longer explanations, occasional rants, etc).
2. Use realistic formatting (bullets, numbered lists, action items, emphasis with *asterisks*).
3. Match tone to role, relationship, and communication style.
4. Use each character's personality notes and communication style to shape their voice.

ATTACHMENTS - INTEGRATE NATURALLY INTO EMAIL CONTENT:
When the user prompt says an email has an attachment, the body MUST reference it naturally:
- Documents: ""Attached is the Q2 forecast"" or ""See the attached spreadsheet for...""
- Images: ""Attached photo from the site walk"" or ""Screenshot of the error dialog""
- INLINE images: mention what is shown in the body copy

TECHNICAL RULES:
1. Each email must logically follow the previous one.
2. Reference previous emails naturally in replies (the system adds quoted text).
3. ALWAYS include the sender's signature block at the end of each email body.
4. Signature blocks must be used EXACTLY as provided.
5. DO NOT include quoted previous emails in bodyPlain.
6. DO NOT include any header lines (Subject/To/From/Cc) in bodyPlain.
7. The sender named in the prompt is the author; the greeting, body text, and signature MUST all match that person.

THREAD STRUCTURE:
- The parent email and threading intent are provided in the user prompt. Follow them exactly.
- Do NOT invent new branching or change the parent relationship.

OUTPUT:
- Return JSON only, matching the schema provided in the user prompt (bodyPlain only).

");
    }

    private static string BuildEmailSubjectSystemPrompt()
    {
        return PromptScaffolding.AppendJsonOnlyInstruction(@"You are generating a realistic corporate email SUBJECT line for a fictional eDiscovery dataset.

OUTPUT:
- Return JSON only, matching the schema provided in the user prompt.");
    }

    private const string RelationshipInternalInternal = "internal_internal";
    private const string RelationshipInternalExternal = "internal_external";
    private const double TagPresenceThreshold = 0.35;
    private const double TagPresenceSharpness = 8.0;
    private const double MinSenderGate = 0.12;
    private const double MinRecipientGateMax = 0.12;
    private const double CoverageBase = 0.80;
    private const double CoverageSpan = 0.40;
    private const double AlphaSender = 0.8;
    private const double BetaRecipient = 1.0;
    private const double GammaSender = 1.0;
    private const double GammaRecipient = 1.0;
    private const double TemperatureMean = 1.05;
    private const double TemperatureSigma = 0.10;

    private static TopicArchetype? SelectNonResponsiveArchetype(
        ResolvedEmailParticipants participants,
        ThreadExecutionState state,
        DateTime sentDate)
    {
        var model = TopicGenerationModelStore.Model;
        if (model.Archetypes.Count == 0)
            return null;

        var orgLookup = BuildOrganizationLookup(state);
        var senderProfile = BuildScoringProfile(participants.From, state, model, orgLookup);
        var recipientProfiles = participants.To
            .Select(p => BuildScoringProfile(p, state, model, orgLookup))
            .ToList();
        var ccProfiles = participants.Cc
            .Select(p => BuildScoringProfile(p, state, model, orgLookup))
            .ToList();
        var relationshipType = IsExternalAudience(participants)
            ? RelationshipInternalExternal
            : RelationshipInternalInternal;

        var temperature = SampleTemperature(state.Rng);
        var candidates = new List<(TopicArchetype Archetype, double Weight)>();

        foreach (var archetype in model.Archetypes)
        {
            var rel = GetRelationshipModifier(archetype, relationshipType);
            if (rel <= 0)
                continue;

            var senderGate = GateAny(senderProfile.TagPresence, archetype.Constraints.SenderTagsAny);
            if (senderGate < MinSenderGate)
                continue;

            var recGateValues = recipientProfiles
                .Select(r => GateAny(r.TagPresence, archetype.Constraints.RecipientTagsAny))
                .ToList();
            var recGateMax = recGateValues.Count == 0 ? 1.0 : recGateValues.Max();
            var recGateAvg = recGateValues.Count == 0 ? 1.0 : recGateValues.Average();
            if (recGateMax < MinRecipientGateMax)
                continue;

            if (archetype.Constraints.CcTagsAny is { Count: > 0 } && ccProfiles.Count > 0)
            {
                var ccGateMax = ccProfiles.Max(c => GateAny(c.TagPresence, archetype.Constraints.CcTagsAny));
                if (ccGateMax < MinRecipientGateMax)
                    continue;
            }

            var coverage = recipientProfiles.Count == 0
                ? 1.0
                : recipientProfiles.Average(r => GateAny(r.TagPresence, archetype.ArchetypeTags));
            var coverageFactor = CoverageBase + CoverageSpan * coverage;

            var senderAffinity = MeanPresence(senderProfile.TagPresence, archetype.ArchetypeTags);
            var recipAffinityMax = recipientProfiles.Count == 0
                ? 0.0
                : recipientProfiles.Max(r => MeanPresence(r.TagPresence, archetype.ArchetypeTags));
            var affinityFactor = (1 + AlphaSender * senderAffinity) * (1 + BetaRecipient * recipAffinityMax);

            var senderIndFactor = GetIndustryFactor(model, senderProfile.IndustryKey, archetype);
            var recipIndFactors = recipientProfiles
                .Select(r => GetIndustryFactor(model, r.IndustryKey, archetype))
                .ToList();
            var recipIndFactor = AverageTopTwo(recipIndFactors);

            var seasonFactor = GetSeasonFactor(archetype, sentDate);

            var score = archetype.BaseWeight;
            score *= rel;
            score *= seasonFactor;
            score *= senderIndFactor;
            score *= recipIndFactor;
            score *= Math.Pow(senderGate, GammaSender);
            score *= Math.Pow(0.7 * recGateMax + 0.3 * recGateAvg, GammaRecipient);
            score *= affinityFactor;
            score *= coverageFactor;

            if (!double.IsFinite(score) || score <= 0)
                continue;

            var weight = Math.Pow(score, 1.0 / temperature);
            if (!double.IsFinite(weight) || weight <= 0)
                continue;

            candidates.Add((archetype, weight));
        }

        return candidates.Count == 0 ? null : SampleWeightedCandidate(candidates, state.Rng);
    }

    private static ParticipantScoringProfile BuildScoringProfile(
        Character character,
        ThreadExecutionState state,
        TopicGenerationModel model,
        IReadOnlyDictionary<Guid, Organization> orgLookup)
    {
        var departmentKey = string.Empty;
        var roleKey = string.Empty;
        var industryKey = Industry.Other.ToString();

        if (state.Context.CharacterRoutingContexts.TryGetValue(character.Id, out var routing))
        {
            departmentKey = routing.Department?.ToString() ?? string.Empty;
            roleKey = routing.Role?.ToString() ?? string.Empty;
            if (orgLookup.TryGetValue(routing.OrganizationId, out var organization))
                industryKey = organization.Industry.ToString();
        }

        var tagPresence = BuildTagPresenceMap(model, departmentKey, roleKey, industryKey);
        return new ParticipantScoringProfile(character, industryKey, tagPresence);
    }

    private static Dictionary<string, double> BuildTagPresenceMap(
        TopicGenerationModel model,
        string? departmentKey,
        string? roleKey,
        string industryKey)
    {
        var deptWeights = !string.IsNullOrWhiteSpace(departmentKey)
            && model.DepartmentDefaultTagMultipliers.TryGetValue(departmentKey, out var deptMap)
            ? deptMap
            : null;
        var roleWeights = !string.IsNullOrWhiteSpace(roleKey)
            && model.RoleDefaultTagMultipliers.TryGetValue(roleKey, out var roleMap)
            ? roleMap
            : null;
        var industryWeights = model.IndustryMultipliers.TryGetValue(industryKey, out var industry)
            ? industry.TagMultipliers
            : null;

        var presence = new Dictionary<string, double>(model.Tags.Count);
        foreach (var tag in model.Tags)
        {
            var tagId = tag.Id;
            var roleWeight = roleWeights != null && roleWeights.TryGetValue(tagId, out var rWeight) ? rWeight : 0.0;
            var deptWeight = deptWeights != null && deptWeights.TryGetValue(tagId, out var dWeight) ? dWeight : 0.0;
            var baseWeight = Math.Max(roleWeight, deptWeight);
            var industryMultiplier = industryWeights != null && industryWeights.TryGetValue(tagId, out var iWeight) ? iWeight : 1.0;
            var weight = baseWeight * industryMultiplier;
            presence[tagId] = Sigmoid(TagPresenceSharpness * (weight - TagPresenceThreshold));
        }

        return presence;
    }

    private static double GateAny(Dictionary<string, double> tagPresence, IReadOnlyCollection<string>? tags)
    {
        if (tags == null || tags.Count == 0)
            return 1.0;

        var product = 1.0;
        foreach (var tag in tags)
        {
            if (!tagPresence.TryGetValue(tag, out var presence))
                presence = 0.0;
            product *= (1.0 - presence);
        }

        return 1.0 - product;
    }

    private static double MeanPresence(Dictionary<string, double> tagPresence, IReadOnlyCollection<string>? tags)
    {
        if (tags == null || tags.Count == 0)
            return 0.0;

        var sum = 0.0;
        var count = 0;
        foreach (var tag in tags)
        {
            if (!tagPresence.TryGetValue(tag, out var presence))
                presence = 0.0;
            sum += presence;
            count++;
        }

        return count == 0 ? 0.0 : sum / count;
    }

    private static double GetIndustryFactor(TopicGenerationModel model, string industryKey, TopicArchetype archetype)
    {
        if (!model.IndustryMultipliers.TryGetValue(industryKey, out var multipliers))
            return 1.0;

        var category = multipliers.CategoryMultipliers.TryGetValue(archetype.Category, out var c) ? c : 1.0;
        var intent = multipliers.IntentMultipliers.TryGetValue(archetype.Intent, out var i) ? i : 1.0;
        var idOverride = multipliers.ArchetypeIdOverrides.TryGetValue(archetype.Id, out var o) ? o : 1.0;
        return category * intent * idOverride;
    }

    private static double GetRelationshipModifier(TopicArchetype archetype, string relationshipType)
    {
        return archetype.Constraints.RelationshipModifiers.TryGetValue(relationshipType, out var modifier)
            ? modifier
            : 0.0;
    }

    private static double GetSeasonFactor(TopicArchetype archetype, DateTime sentDate)
    {
        if (archetype.Seasonality?.MonthsBoost == null)
            return 1.0;

        var monthKey = sentDate.Month.ToString(CultureInfo.InvariantCulture);
        return archetype.Seasonality.MonthsBoost.TryGetValue(monthKey, out var boost)
            ? boost
            : 1.0;
    }

    private static double AverageTopTwo(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 1.0;
        if (values.Count == 1)
            return values[0];

        var top1 = double.MinValue;
        var top2 = double.MinValue;
        foreach (var value in values)
        {
            if (value >= top1)
            {
                top2 = top1;
                top1 = value;
            }
            else if (value > top2)
            {
                top2 = value;
            }
        }

        return (top1 + top2) / 2.0;
    }

    private static TopicArchetype SampleWeightedCandidate(
        IReadOnlyList<(TopicArchetype Archetype, double Weight)> candidates,
        Random rng)
    {
        var total = candidates.Sum(c => c.Weight);
        if (!double.IsFinite(total) || total <= 0)
            return candidates[0].Archetype;

        var roll = rng.NextDouble() * total;
        var cumulative = 0.0;
        foreach (var candidate in candidates)
        {
            cumulative += candidate.Weight;
            if (roll <= cumulative)
                return candidate.Archetype;
        }

        return candidates[^1].Archetype;
    }

    private static double SampleTemperature(Random rng)
    {
        var standardNormal = SampleStandardNormal(rng);
        return TemperatureMean * Math.Exp(standardNormal * TemperatureSigma);
    }

    private static double SampleStandardNormal(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double Sigmoid(double value)
    {
        return 1.0 / (1.0 + Math.Exp(-value));
    }

    private static Dictionary<string, string> NormalizeEntityValues(
        TopicArchetype archetype,
        Dictionary<string, string>? rawValues,
        Random rng,
        DateTime sentDate,
        ResolvedEmailParticipants participants)
    {
        var rawLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (rawValues != null)
        {
            foreach (var (key, value) in rawValues)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;
                rawLookup[key.Trim()] = value.Trim();
            }
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var required in archetype.EntitiesRequired)
        {
            if (!TryGetEntityValue(rawLookup, required, out var value))
                value = BuildFallbackEntityValue(required, rng, sentDate, participants);
            normalized[required] = value;
        }

        foreach (var optional in archetype.EntitiesOptional)
        {
            if (TryGetEntityValue(rawLookup, optional, out var value))
                normalized[optional] = value;
        }

        return normalized;
    }

    private static bool TryGetEntityValue(
        IReadOnlyDictionary<string, string> values,
        string key,
        [NotNullWhen(true)] out string? value)
    {
        if (values.TryGetValue(key, out var direct) && !string.IsNullOrWhiteSpace(direct))
        {
            value = direct;
            return true;
        }

        value = null;
        return false;
    }

    private static string BuildFallbackEntityValue(
        string entityKey,
        Random rng,
        DateTime sentDate,
        ResolvedEmailParticipants participants)
    {
        var lowered = entityKey.ToLowerInvariant();
        if (lowered.Contains("person", StringComparison.Ordinal)
            || lowered.Contains("employee", StringComparison.Ordinal)
            || lowered.Contains("manager", StringComparison.Ordinal)
            || lowered.Contains("contact", StringComparison.Ordinal))
        {
            var person = participants.To.Count > 0 ? participants.To[0] : participants.From;
            return person.FullName;
        }

        if (lowered.Contains("date", StringComparison.Ordinal)
            || lowered.Contains("deadline", StringComparison.Ordinal)
            || lowered.Contains("time", StringComparison.Ordinal))
        {
            return sentDate.AddDays(rng.Next(2, 10)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (lowered.Contains("list", StringComparison.Ordinal)
            || lowered.Contains("systems", StringComparison.Ordinal)
            || lowered.Contains("assets", StringComparison.Ordinal)
            || lowered.Contains("items", StringComparison.Ordinal))
        {
            return "Item A, Item B";
        }

        if (lowered.Contains("ticket", StringComparison.Ordinal)
            || lowered.Contains("case", StringComparison.Ordinal)
            || lowered.Contains("id", StringComparison.Ordinal))
        {
            return $"TKT-{rng.Next(1000, 9999)}";
        }

        if (lowered.Contains("link", StringComparison.Ordinal))
        {
            return "https://intranet.local/request";
        }

        if (lowered.Contains("summary", StringComparison.Ordinal)
            || lowered.Contains("description", StringComparison.Ordinal)
            || lowered.Contains("reason", StringComparison.Ordinal))
        {
            return "brief summary";
        }

        return "details needed";
    }

    private static string NormalizeNonResponsiveSubject(
        string? subject,
        string fallback,
        TopicArchetype archetype)
    {
        var resolved = ThreadingHelper.GetCleanSubject(subject ?? string.Empty);
        if (string.IsNullOrWhiteSpace(resolved))
            resolved = ThreadingHelper.GetCleanSubject(fallback);
        if (string.IsNullOrWhiteSpace(resolved))
            resolved = BuildFallbackSubject(archetype);

        var words = SplitWords(resolved);
        if (words.Count < 4)
        {
            resolved = BuildFallbackSubject(archetype);
            words = SplitWords(resolved);
        }

        if (words.Count > 12)
            resolved = string.Join(" ", words.Take(12));
        else if (words.Count < 4)
            resolved = $"{resolved} update";

        return resolved.Trim();
    }

    private static string BuildFallbackSubject(TopicArchetype archetype)
    {
        var humanized = HumanizeIdentifier(archetype.Id);
        var subject = $"Action needed: {humanized}";
        var words = SplitWords(subject);
        if (words.Count < 4)
            subject = $"Action needed: {humanized} update";
        return subject;
    }

    private static string HumanizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "update";

        var cleaned = value.Replace('_', ' ').Replace('-', ' ').Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned);
    }

    private static List<string> SplitWords(string value)
    {
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static readonly string[] NonResponsiveTopicHints =
    {
        "meeting scheduling or rescheduling",
        "budget approvals or expense reports",
        "vendor onboarding or procurement updates",
        "routine project status updates",
        "IT support tickets or access requests",
        "HR training or policy reminders",
        "facilities or office logistics",
        "travel planning or reimbursement",
        "invoice questions or billing clarifications",
        "internal documentation clean-up"
    };

    private static string BuildNonResponsiveContext(string? topic)
    {
        if (!string.IsNullOrWhiteSpace(topic))
            return $"NON-RESPONSIVE TOPIC: Use this topic and stick to it: {topic}.";

        var hints = string.Join("; ", NonResponsiveTopicHints);
        return $"NON-RESPONSIVE TOPIC GUIDANCE: Pick ONE mundane corporate topic from these examples and stick to it: {hints}.";
    }

    private static string BuildStoryBeatContext(Storyline storyline, DateTime batchStartDate, DateTime batchEndDate)
    {
        var beats = storyline.Beats;
        if (beats == null || beats.Count == 0)
            return "";

        var relevantBeats = beats
            .Where(b => b.StartDate.Date <= batchEndDate.Date && b.EndDate.Date >= batchStartDate.Date)
            .ToList();

        if (relevantBeats.Count == 0)
            relevantBeats = beats.ToList();

        var lines = new List<string> { "Story Beats (ordered):" };
        for (var i = 0; i < relevantBeats.Count; i++)
        {
            var beat = relevantBeats[i];
            lines.Add($"{i + 1}. {beat.Name} ({beat.StartDate:yyyy-MM-dd} to {beat.EndDate:yyyy-MM-dd})");
            lines.Add(beat.Plot);
        }

        return string.Join("\n", lines);
    }

    private static string ResolveSubject(string threadSubject, ThreadEmailIntent intent, int sequence)
    {
        if (intent == ThreadEmailIntent.Forward)
            return ThreadingHelper.AddForwardPrefix(threadSubject);
        if (intent == ThreadEmailIntent.Reply && sequence > 0)
            return ThreadingHelper.AddReplyPrefix(threadSubject);
        return threadSubject;
    }
    /// <summary>
    /// Programmatically correct the signature block in an email body to match the actual sender.
    /// The AI sometimes puts the wrong character's signature on an email.
    /// </summary>
    private static string CorrectSignatureBlock(string body, Character fromChar, List<Character> allCharacters)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(fromChar.SignatureBlock))
            return body;

        var correctSig = fromChar.SignatureBlock.Trim();

        // Check if the correct signature is already present
        if (body.Contains(correctSig, StringComparison.OrdinalIgnoreCase))
            return body;

        return ApplySignatureCorrections(body, fromChar, allCharacters, correctSig);
    }

    private static string ApplySignatureCorrections(
        string body,
        Character fromChar,
        List<Character> allCharacters,
        string correctSig)
    {
        if (TryReplaceWrongSignature(body, fromChar, allCharacters, correctSig, out var updatedBody))
            return updatedBody;

        if (TryReplaceWrongNameWithSignOff(body, fromChar, allCharacters, correctSig, out updatedBody))
            return updatedBody;

        if (TryReplaceMissingSenderSignature(body, fromChar, correctSig, out updatedBody))
            return updatedBody;

        return body;
    }

    private static bool TryReplaceWrongSignature(
        string body,
        Character fromChar,
        List<Character> allCharacters,
        string correctSig,
        out string updatedBody)
    {
        foreach (var otherChar in allCharacters)
        {
            if (ReferenceEquals(otherChar, fromChar))
                continue;

            if (otherChar.Id != Guid.Empty && fromChar.Id != Guid.Empty && otherChar.Id == fromChar.Id)
                continue;

            if (string.IsNullOrWhiteSpace(otherChar.SignatureBlock))
                continue;

            var wrongSig = otherChar.SignatureBlock.Trim();
            if (string.IsNullOrWhiteSpace(wrongSig))
                continue;

            var sigIndex = body.IndexOf(wrongSig, StringComparison.OrdinalIgnoreCase);
            if (sigIndex >= 0)
            {
                updatedBody = body[..sigIndex] + correctSig + body[(sigIndex + wrongSig.Length)..];
                return true;
            }
        }

        updatedBody = body;
        return false;
    }

    private static bool TryReplaceWrongNameWithSignOff(
        string body,
        Character fromChar,
        List<Character> allCharacters,
        string correctSig,
        out string updatedBody)
    {
        foreach (var otherChar in allCharacters)
        {
            if (otherChar.Id == fromChar.Id)
                continue;

            var searchRegion = body.Length > 100
                ? body[(int)(body.Length * 0.7)..]
                : body;

            var nameIndex = searchRegion.IndexOf(otherChar.FullName, StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0)
                continue;

            var absoluteIndex = body.Length - searchRegion.Length + nameIndex;
            var beforeName = body[..absoluteIndex];

            if (TryFindLastSignOff(beforeName, out var signOffStart))
            {
                updatedBody = body[..signOffStart].TrimEnd() + "\n\n" + correctSig;
                return true;
            }

            updatedBody = body[..absoluteIndex].TrimEnd() + "\n\n" + correctSig;
            return true;
        }

        updatedBody = body;
        return false;
    }

    private static bool TryReplaceMissingSenderSignature(
        string body,
        Character fromChar,
        string correctSig,
        out string updatedBody)
    {
        var tailRegion = body.Length > 100 ? body[(int)(body.Length * 0.7)..] : body;
        if (tailRegion.Contains(fromChar.FullName, StringComparison.OrdinalIgnoreCase) ||
            tailRegion.Contains(fromChar.FirstName, StringComparison.OrdinalIgnoreCase))
        {
            updatedBody = body;
            return false;
        }

        var tailStart = body.Length > 100 ? (int)(body.Length * 0.7) : 0;
        if (TryFindFirstSignOffFrom(body, tailStart, out var signOffStart))
        {
            updatedBody = body[..signOffStart].TrimEnd() + "\n\n" + correctSig;
            return true;
        }

        updatedBody = body;
        return false;
    }

    private static bool TryFindLastSignOff(string text, out int startIndex)
    {
        startIndex = -1;
        foreach (var pattern in SignOffPatterns)
        {
            var idx = text.LastIndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx > startIndex)
            {
                startIndex = idx;
            }
        }

        return startIndex >= 0;
    }

    private static bool TryFindFirstSignOffFrom(string text, int startIndex, out int signOffIndex)
    {
        signOffIndex = -1;
        foreach (var pattern in SignOffPatterns)
        {
            var idx = text.IndexOf(pattern, startIndex, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (signOffIndex == -1 || idx < signOffIndex))
            {
                signOffIndex = idx;
            }
        }

        return signOffIndex >= 0;
    }

    private List<EmailMessage> SelectEmailsForAttachments(IReadOnlyList<EmailMessage> emails, int percentage)
    {
        if (percentage <= 0) return new List<EmailMessage>();

        var count = Math.Max(1, (int)Math.Round(emails.Count * percentage / 100.0));
        // Randomized ordering (seeded RNG) to sample attachments across the thread.
        return emails.OrderBy(_ => _rng.Next()).Take(count).ToList();
    }

    /// <summary>
    /// Generate a document attachment based on the AI-planned description
    /// </summary>
    private async Task GeneratePlannedDocumentAsync(EmailMessage email, WizardState state, Random rng, CancellationToken ct)
    {
        if (!email.PlannedHasDocument)
            return;
        if (string.IsNullOrWhiteSpace(email.PlannedDocumentType))
            throw new InvalidOperationException($"Planned document type is missing for email '{email.Subject ?? "Untitled"}'.");

        if (!TryResolvePlannedAttachmentType(email.PlannedDocumentType, state.Config, out var attachmentType))
        {
            var enabledTypes = state.Config.EnabledAttachmentTypes;
            var enabledLabel = enabledTypes.Count == 0
                ? "no attachment types are enabled"
                : $"enabled types: {string.Join(", ", enabledTypes)}";
            throw new InvalidOperationException(
                $"Planned document attachment type '{email.PlannedDocumentType}' is not available ({enabledLabel}).");
        }

        var isDetailed = state.Config.AttachmentComplexity == AttachmentComplexity.Detailed;

        // Use the planned description as context
        var context = $@"Email subject: {email.Subject}
Document purpose (from email): {email.PlannedDocumentDescription ?? "Supporting document for this email"}
Email body preview: {email.BodyPlain[..Math.Min(300, email.BodyPlain.Length)]}...";

        var (chainState, reservedVersion) = TryReserveDocumentChain(state, attachmentType, rng);
        if (chainState != null)
        {
            context += BuildDocumentChainContext(chainState, reservedVersion);
        }

        var attachment = await GeneratePlannedDocumentAttachmentAsync(
            attachmentType,
            context,
            email,
            isDetailed,
            state,
            chainState,
            ct);
        if (attachment == null)
            throw new InvalidOperationException($"Planned {attachmentType} attachment generation failed for email '{email.Subject ?? "Untitled"}'.");

        ApplyDocumentChainVersioning(attachment, chainState, reservedVersion, state, email, rng);

        if (string.IsNullOrEmpty(attachment.FileName))
        {
            attachment.FileName = FileNameHelper.GenerateAttachmentFileName(attachment, email);
        }

        EnsureAttachmentId(email, attachment);
        email.AddAttachment(attachment);
    }

    private static bool TryResolvePlannedAttachmentType(
        string plannedDocumentType,
        GenerationConfig config,
        out AttachmentType attachmentType)
    {
        attachmentType = ResolvePlannedAttachmentType(plannedDocumentType);
        if (config.EnabledAttachmentTypes.Contains(attachmentType))
            return true;

        if (config.EnabledAttachmentTypes.Count == 0)
            return false;

        attachmentType = config.EnabledAttachmentTypes[0];
        return true;
    }

    private static AttachmentType ResolvePlannedAttachmentType(string plannedDocumentType)
    {
        return plannedDocumentType.ToLowerInvariant() switch
        {
            "word" => AttachmentType.Word,
            "excel" => AttachmentType.Excel,
            "powerpoint" => AttachmentType.PowerPoint,
            _ => AttachmentType.Word
        };
    }

    private static bool IsSupportedDocumentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.Equals("word", StringComparison.OrdinalIgnoreCase)
               || value.Equals("excel", StringComparison.OrdinalIgnoreCase)
               || value.Equals("powerpoint", StringComparison.OrdinalIgnoreCase);
    }

    private (DocumentChainState? chainState, int? reservedVersion) TryReserveDocumentChain(
        WizardState state,
        AttachmentType attachmentType,
        Random rng)
    {
        if (!state.Config.EnableAttachmentChains || _documentChains.IsEmpty || rng.Next(100) >= 30)
            return (null, null);

        var matchingChains = _documentChains.Values
            .Where(c => c.Type == attachmentType)
            .ToList();
        if (matchingChains.Count == 0)
            return (null, null);

        var chainState = matchingChains[rng.Next(matchingChains.Count)];
        int reservedVersion;
        lock (chainState.SyncRoot)
        {
            chainState.VersionNumber++;
            reservedVersion = chainState.VersionNumber;
        }

        return (chainState, reservedVersion);
    }

    private static string BuildDocumentChainContext(DocumentChainState chainState, int? reservedVersion)
    {
        var versionLabel = reservedVersion ?? chainState.VersionNumber;
        return $"\n\nIMPORTANT: This is a REVISION of a document titled '{chainState.BaseTitle}'. " +
               $"This is version {versionLabel}. " +
               "Make changes/updates to reflect edits, feedback, or revisions.";
    }

    private async Task<Attachment?> GeneratePlannedDocumentAttachmentAsync(
        AttachmentType attachmentType,
        string context,
        EmailMessage email,
        bool isDetailed,
        WizardState state,
        DocumentChainState? chainState,
        CancellationToken ct)
    {
        return attachmentType switch
        {
            AttachmentType.Word => await GenerateWordAttachmentAsync(context, email, isDetailed, state, ct, chainState),
            AttachmentType.Excel => await GenerateExcelAttachmentAsync(context, email, isDetailed, ct),
            AttachmentType.PowerPoint => await GeneratePowerPointAttachmentAsync(context, email, isDetailed, state, ct),
            _ => null
        };
    }

    private void ApplyDocumentChainVersioning(
        Attachment attachment,
        DocumentChainState? chainState,
        int? reservedVersion,
        WizardState state,
        EmailMessage email,
        Random rng)
    {
        if (chainState != null && attachment.Content != null)
        {
            attachment.DocumentChainId = chainState.ChainId;
            var versionNumber = reservedVersion ?? chainState.VersionNumber;
            attachment.VersionLabel = GetVersionLabel(versionNumber);
            attachment.FileName = BuildVersionedAttachmentFileName(
                chainState.BaseTitle,
                attachment.VersionLabel ?? "v1",
                attachment.Extension);
            return;
        }

        if (state.Config.EnableAttachmentChains && attachment.Type == AttachmentType.Word && rng.Next(100) < 50)
        {
            // Start a new chain for Word documents
            var newChain = new DocumentChainState
            {
                ChainId = DeterministicIdHelper.CreateShortToken(
                    "doc-chain",
                    8,
                    email.Id.ToString("N"),
                    attachment.ContentDescription,
                    attachment.Type.ToString()),
                BaseTitle = attachment.ContentDescription,
                Type = attachment.Type,
                VersionNumber = 1
            };
            _documentChains.TryAdd(newChain.ChainId, newChain);
            attachment.DocumentChainId = newChain.ChainId;
            attachment.VersionLabel = "v1";
        }
    }

    /// <summary>
    /// Generate an image based on the AI-planned description
    /// </summary>
    private async Task GeneratePlannedImageAsync(
        EmailMessage email,
        WizardState state,
        string threadTopic,
        CancellationToken ct)
    {
        if (!email.PlannedHasImage)
            return;
        if (string.IsNullOrWhiteSpace(email.PlannedImageDescription))
            throw new InvalidOperationException($"Planned image description is missing for email '{email.Subject ?? "Untitled"}'.");

        // Generate the image using DALL-E with the planned description
        var imagePrompt = BuildPlannedImagePrompt(threadTopic, email.PlannedImageDescription);

        var imageBytes = await _openAI.GenerateImageAsync(imagePrompt, "Image Generation", ct);

        if (imageBytes == null || imageBytes.Length == 0)
            throw new InvalidOperationException($"Planned image generation failed for email '{email.Subject ?? "Untitled"}'.");

        var contentIdToken = DeterministicIdHelper.CreateShortToken(
            "inline-image",
            32,
            email.Id.ToString("N"),
            email.PlannedImageDescription ?? string.Empty,
            email.SentDate.ToString("O"));
        var contentId = $"img_{contentIdToken}";
        var attachment = BuildPlannedImageAttachment(email, imageBytes, contentId);

        EnsureAttachmentId(email, attachment);
        email.AddAttachment(attachment);

        InsertInlineImageIfNeeded(email, contentId);
    }

    private static string BuildPlannedImagePrompt(string topic, string plannedDescription)
    {
        return $"A vivid, realistic image in the style/universe of {topic}: {plannedDescription}. High quality, detailed.";
    }

    private static Attachment BuildPlannedImageAttachment(EmailMessage email, byte[] imageBytes, string contentId)
    {
        var plannedDescription = email.PlannedImageDescription ?? string.Empty;
        return new Attachment
        {
            Type = AttachmentType.Image,
            Content = imageBytes,
            ContentDescription = plannedDescription,
            IsInline = email.PlannedIsImageInline,
            ContentId = contentId,
            FileName = FileNameHelper.GenerateImageFileName(email, plannedDescription, contentId)
        };
    }

    private static void InsertInlineImageIfNeeded(EmailMessage email, string contentId)
    {
        // If inline, update the HTML body to include the image in the main content (before quoted text)
        if (!email.PlannedIsImageInline || string.IsNullOrEmpty(email.BodyHtml))
            return;

        var plannedDescription = email.PlannedImageDescription ?? string.Empty;
        var caption = plannedDescription.Length > 100
            ? plannedDescription[..100] + "..."
            : plannedDescription;

        var imageHtml = $@"<div style=""text-align: center; margin: 15px 0;""><img src=""cid:{contentId}"" alt=""{System.Net.WebUtility.HtmlEncode(caption)}"" style=""max-width: 600px; height: auto; border-radius: 4px;"" /></div>";

        // Try to insert the image BEFORE the quoted content (reply or forward sections)
        // This ensures the image appears in the new email content, not after the quoted text
        var insertionPoints = new[]
        {
            "<div class=\"quoted-content\">",  // Reply quoted content
            "<div class=\"forward-header\">",   // Forwarded message header
            "<div class=\"signature\">"         // Before signature if no quoted content
        };

        bool inserted = false;
        foreach (var marker in insertionPoints)
        {
            var markerIndex = email.BodyHtml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                email.BodyHtml = email.BodyHtml.Insert(markerIndex, imageHtml);
                inserted = true;
                break;
            }
        }

        // If no insertion point found, insert before the closing div/body
        if (!inserted)
        {
            if (email.BodyHtml.Contains("</div>\n</body>", StringComparison.Ordinal))
            {
                // Insert before closing email-body div
                email.BodyHtml = email.BodyHtml.Replace("</div>\n</body>", imageHtml + "</div>\n</body>");
            }
            else if (email.BodyHtml.Contains("</body>", StringComparison.Ordinal))
            {
                email.BodyHtml = email.BodyHtml.Replace("</body>", imageHtml + "</body>");
            }
            else
            {
                email.BodyHtml += imageHtml;
            }
        }
    }

    /// <summary>
    /// Generate a voicemail based on the AI-planned context
    /// </summary>
    private async Task GeneratePlannedVoicemailAsync(
        EmailMessage email,
        WizardState state,
        string threadTopic,
        CancellationToken ct)
    {
        if (!email.PlannedHasVoicemail)
            return;

        // Generate a voicemail script using the planned context

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are creating a voicemail message that relates to a fictional corporate email.
The voicemail should sound natural and conversational, as if someone called and left a message.
Keep the voicemail BRIEF - 15-30 seconds when spoken (about 40-80 words).
Do not use real company names or real people.
");

        var context = $@"Email subject: {email.Subject}
Sender: {email.From.FullName}
Voicemail context: {email.PlannedVoicemailContext ?? "A follow-up or urgent message related to the email"}
Narrative topic: {threadTopic}";


        var schema = """
{
  "voicemailScript": "string (the voicemail transcript)"
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"{context}

Create a voicemail that {email.From.FirstName} might leave related to this email.
The voicemail should:
- Sound natural and conversational (include 'um', 'uh', pauses indicated by '...')
- Reference the email topic with appropriate urgency
- Start with a greeting ('Hey, it's [name]...' or 'Hi, this is [name] calling about...')
- End naturally ('...call me back when you get this' or 'talk soon')
- Be 40-80 words total
- Keep all names and organizations fictional", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<VoicemailScriptResponse>(systemPrompt, userPrompt, "Voicemail Script", ct);

        if (response == null || string.IsNullOrWhiteSpace(response.VoicemailScript))
            throw new InvalidOperationException($"Voicemail script generation failed for email '{email.Subject ?? "Untitled"}'.");

        // Generate the audio using TTS
        var audioBytes = await _openAI.GenerateSpeechAsync(
            response.VoicemailScript,
            email.From.VoiceId,
            "Voicemail TTS",
            ct);

        if (audioBytes == null || audioBytes.Length == 0)
            throw new InvalidOperationException($"Voicemail audio generation failed for email '{email.Subject ?? "Untitled"}'.");

        var attachment = new Attachment
        {
            Type = AttachmentType.Voicemail,
            Content = audioBytes,
            ContentDescription = $"Voicemail from {email.From.FullName}",
            VoiceId = email.From.VoiceId,
            FileName = BuildVoicemailFileName(email.From.LastName, email.SentDate)
        };

        EnsureAttachmentId(email, attachment);
        email.AddAttachment(attachment);
    }

    private async Task GenerateAttachmentAsync(EmailMessage email, WizardState state, Random rng, CancellationToken ct)
    {
        var enabledTypes = state.Config.EnabledAttachmentTypes;
        if (enabledTypes.Count == 0) return;

        var attachmentType = enabledTypes[rng.Next(enabledTypes.Count)];
        var isDetailed = state.Config.AttachmentComplexity == AttachmentComplexity.Detailed;

        var (chainState, reservedVersion) = TryReserveDocumentChain(state, attachmentType, rng);
        var context = BuildAttachmentContext(email, chainState, reservedVersion);

        var attachment = await GeneratePlannedDocumentAttachmentAsync(
            attachmentType,
            context,
            email,
            isDetailed,
            state,
            chainState,
            ct);
        if (attachment == null)
            throw new InvalidOperationException($"Attachment generation failed for email '{email.Subject ?? "Untitled"}' (type: {attachmentType}).");

        ApplyDocumentChainVersioning(attachment, chainState, reservedVersion, state, email, rng);

        if (string.IsNullOrEmpty(attachment.FileName))
        {
            attachment.FileName = FileNameHelper.GenerateAttachmentFileName(attachment, email);
        }
        EnsureAttachmentId(email, attachment);
        email.AddAttachment(attachment);
    }

    private static string BuildAttachmentContext(
        EmailMessage email,
        DocumentChainState? chainState,
        int? reservedVersion)
    {
        var context = $"Email subject: {email.Subject}\nEmail body preview: {email.BodyPlain[..Math.Min(200, email.BodyPlain.Length)]}...";
        if (chainState == null)
            return context;

        context += $"\n\nIMPORTANT: This is a REVISION of a document titled '{chainState.BaseTitle}'. ";
        context += $"This is version {reservedVersion ?? chainState.VersionNumber}. ";
        context += "Make changes/updates to reflect edits, feedback, or revisions - don't create something completely new.";
        return context;
    }

    private static string GetVersionLabel(int version)
    {
        // Fun realistic version labels
        return version switch
        {
            1 => "v1",
            2 => "v2",
            3 => "v3_revised",
            4 => "v4_final",
            5 => "v5_FINAL",
            6 => "v6_FINAL_v2",
            7 => "v7_FINAL_FINAL",
            8 => "v8_USE_THIS_ONE",
            _ => $"v{version}_latest"
        };
    }

    private static string BuildVersionedAttachmentFileName(string baseTitle, string versionLabel, string extension)
    {
        var safeBase = FileNameHelper.SanitizeForFileName(baseTitle);
        if (string.IsNullOrWhiteSpace(safeBase) || safeBase == "unnamed")
        {
            safeBase = "document";
        }

        var safeVersion = FileNameHelper.SanitizeForFileName(versionLabel);
        if (string.IsNullOrWhiteSpace(safeVersion) || safeVersion == "unnamed")
        {
            safeVersion = "v1";
        }

        var maxBaseLength = Math.Max(1, MaxAttachmentFileNameLength - safeVersion.Length - extension.Length - 1);
        if (safeBase.Length > maxBaseLength)
        {
            safeBase = safeBase[..maxBaseLength];
        }

        return $"{safeBase}_{safeVersion}{extension}";
    }

    private static string BuildVoicemailFileName(string lastName, DateTime sentDate)
    {
        var safeLastName = FileNameHelper.SanitizeForFileName(lastName);
        if (string.IsNullOrWhiteSpace(safeLastName) || safeLastName == "unnamed")
        {
            safeLastName = "sender";
        }

        var prefix = "voicemail_";
        var suffix = $"_{sentDate:yyyyMMdd_HHmm}.mp3";
        var maxLastNameLength = Math.Max(1, MaxAttachmentFileNameLength - prefix.Length - suffix.Length);
        if (safeLastName.Length > maxLastNameLength)
        {
            safeLastName = safeLastName[..maxLastNameLength];
        }

        return $"{prefix}{safeLastName}{suffix}";
    }

    private static void EnsureAttachmentId(EmailMessage email, Attachment attachment, int? indexOverride = null)
    {
        if (attachment.Id != Guid.Empty)
            return;

        var index = indexOverride ?? email.Attachments.Count;
        attachment.Id = DeterministicIdHelper.CreateGuid(
            "attachment",
            email.Id.ToString("N"),
            index.ToString(CultureInfo.InvariantCulture),
            attachment.Type.ToString(),
            attachment.FileName ?? string.Empty,
            attachment.ContentDescription ?? string.Empty,
            attachment.ContentId ?? string.Empty);
    }

    private async Task<Attachment> GenerateWordAttachmentAsync(
        string context, EmailMessage email, bool detailed, WizardState state, CancellationToken ct, DocumentChainState? chainState = null)
    {

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"Generate content for a fictional corporate Word document attachment.
The content should be realistic, workplace-appropriate, and related to the email context.
Do not use real company names or real people.
");

        var detailLevel = detailed
            ? "Generate a detailed document with 4-6 paragraphs, including an introduction, main content with 2-3 key points, and a conclusion."
            : "Generate a brief document with 2-3 paragraphs of relevant content.";

        // Add revision instructions if this is a versioned document
        var versionNote = "";
        if (chainState != null)
        {
            versionNote = $@"

IMPORTANT: This is VERSION {chainState.VersionNumber} of '{chainState.BaseTitle}'.
Make realistic revisions:
- Keep the same overall topic/title
- Add or modify some sections
- Include tracked-changes style notes like '[Updated per feedback]' or '[Revised figures]'
- Maybe add a 'Revision History' section at the end";
        }


        var schema = """
{
  "title": "string (document title)",
  "content": "string (full document content, paragraphs separated by double newlines)"
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Context:
{context}
{versionNote}
{detailLevel}

Use only fictional names and organizations. If names are needed, derive them from the email context.
", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<WordDocResponse>(systemPrompt, userPrompt, "Word Attachment", ct);

        if (response == null)
            throw new InvalidOperationException($"Word attachment generation failed for email '{email.Subject ?? "Untitled"}'.");

        var title = response?.Title ?? "Document";

        // If continuing a chain, keep the original title
        if (chainState != null)
        {
            title = chainState.BaseTitle;
        }

        // Get the organization theme for the sender's domain
        OrganizationTheme? theme = null;
        var senderDomain = email.From?.Domain;
        if (!string.IsNullOrEmpty(senderDomain) && state.DomainThemes.TryGetValue(senderDomain, out var domainTheme))
        {
            theme = domainTheme;
        }

        var content = _officeService.CreateWordDocument(
            title,
            response?.Content ?? "Content unavailable.",
            theme);

        return new Attachment
        {
            Type = AttachmentType.Word,
            ContentDescription = title,
            Content = content
        };
    }

    private async Task<Attachment> GenerateExcelAttachmentAsync(
        string context, EmailMessage email, bool detailed, CancellationToken ct)
    {

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"Generate data for a fictional corporate Excel spreadsheet attachment.
The data should be realistic and related to the email context.
Do not use real company names or real people.
IMPORTANT: All values in the rows array MUST be strings, even if they represent numbers.
For example: use ""1234"" instead of 1234, use ""$5,000"" instead of 5000.
");

        var rowCount = detailed ? "10-15" : "5-8";


        var schema = """
{
  "title": "string (spreadsheet title)",
  "headers": ["string"],
  "rows": [["string (ALL values must be strings, even numbers)"]]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Context:
{context}

Generate spreadsheet data with:
- Appropriate column headers (3-6 columns)
- {rowCount} rows of realistic data
- Format numeric values as strings (e.g., ""$1,234"", ""500"", ""12.5%"")
- Use only fictional names and organizations", PromptScaffolding.JsonSchemaSection(schema), "CRITICAL: Every cell value in rows must be a JSON string, not a number. Use quotes around all values.");

        var response = await _openAI.GetJsonCompletionAsync<ExcelDocResponseRaw>(systemPrompt, userPrompt, "Excel Attachment", ct);

        if (response == null)
            throw new InvalidOperationException($"Excel attachment generation failed for email '{email.Subject ?? "Untitled"}'.");

        // Convert JsonElement rows to strings (handles both string and number values)
        var rows = new List<List<string>>();
        if (response?.Rows != null)
        {
            foreach (var row in response.Rows)
            {
                var stringRow = new List<string>();
                foreach (var cell in row)
                {
                    // Handle both string and numeric JSON values
                    stringRow.Add(cell.ValueKind == System.Text.Json.JsonValueKind.String
                        ? cell.GetString() ?? ""
                        : cell.ToString());
                }
                rows.Add(stringRow);
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(new List<string> { "Data1", "Data2" });
        }

        var content = _officeService.CreateExcelDocument(
            response?.Title ?? "Spreadsheet",
            response?.Headers ?? new List<string> { "Column1", "Column2" },
            rows);

        return new Attachment
        {
            Type = AttachmentType.Excel,
            ContentDescription = response?.Title ?? "Spreadsheet",
            Content = content
        };
    }

    private async Task<Attachment> GeneratePowerPointAttachmentAsync(
        string context, EmailMessage email, bool detailed, WizardState state, CancellationToken ct)
    {

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"Generate content for a fictional corporate PowerPoint presentation attachment.
The content should be realistic and related to the email context.
Do not use real company names or real people.
");

        var slideCount = detailed ? "5-8" : "3-4";


        var schema = """
{
  "title": "string (presentation title)",
  "slides": [
    {
      "slideTitle": "string",
      "content": "string (bullet points or paragraph)"
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Context:
{context}

Generate presentation content with:
- A main title
- {slideCount} content slides
- Each slide should have a title and bullet points or brief content
- Use only fictional names and organizations", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<PowerPointDocResponse>(systemPrompt, userPrompt, "PowerPoint Attachment", ct);

        if (response == null)
            throw new InvalidOperationException($"PowerPoint attachment generation failed for email '{email.Subject ?? "Untitled"}'.");

        var slides = response?.Slides?
            .Select(s => (s.SlideTitle, s.Content))
            .ToList() ?? new List<(string, string)> { ("Slide 1", "Content") };

        // Get the organization theme for the sender's domain
        OrganizationTheme? theme = null;
        var senderDomain = email.From?.Domain;
        if (!string.IsNullOrEmpty(senderDomain) && state.DomainThemes.TryGetValue(senderDomain, out var domainTheme))
        {
            theme = domainTheme;
        }

        var content = _officeService.CreatePowerPointDocument(
            response?.Title ?? "Presentation",
            slides,
            theme);

        return new Attachment
        {
            Type = AttachmentType.PowerPoint,
            ContentDescription = response?.Title ?? "Presentation",
            Content = content
        };
    }

    private async Task GenerateImageForEmailAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        // First, get AI to describe what image would be appropriate for this email

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are helping generate an image for a fictional corporate email.
Based on the email content, suggest a single image that someone might attach or embed in this email.
The image should feel authentic to a workplace setting and relevant to the email's content.
Do not include real logos, real brands, or identifiable real people.
");

        var narrativeTopic = ResolveEmailTopic(email, state);
        var context = $@"Email subject: {email.Subject}
Email body: {email.BodyPlain[..Math.Min(500, email.BodyPlain.Length)]}
Narrative topic: {narrativeTopic}";


        var schema = """
{
  "shouldIncludeImage": boolean (false if no image makes sense for this email),
  "imageDescription": "string (detailed description for image generation, 2-3 sentences)",
  "imageContext": "string (brief caption or how it's referenced in email, e.g., 'Attached: Photo from the banquet')",
  "isInline": boolean (true if image should display in email body, false for attachment)
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"{context}

Suggest ONE image that would be realistic to include with this email. Consider:
- Photos someone might share ('Here's a picture from the event')
- Screenshots or diagrams being discussed
- Images that add context to the email
- Office-appropriate visuals only (no sensitive or explicit content)", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<ImageSuggestionResponse>(systemPrompt, userPrompt, "Image Suggestion", ct);

        if (response == null)
            throw new InvalidOperationException($"Image suggestion generation failed for email '{email.Subject ?? "Untitled"}'.");
        if (!response.ShouldIncludeImage)
            return;
        if (string.IsNullOrWhiteSpace(response.ImageDescription))
            throw new InvalidOperationException($"Image suggestion is missing a description for email '{email.Subject ?? "Untitled"}'.");

        // Generate the image using DALL-E
        // Craft a safe, descriptive prompt

        var imagePrompt = $"A realistic, fictional corporate image inspired by the narrative topic \"{narrativeTopic}\": {response.ImageDescription}. No real brands, logos, or identifiable people. High quality, photorealistic where appropriate.";

        var imageBytes = await _openAI.GenerateImageAsync(imagePrompt, "Image Generation", ct);

        if (imageBytes == null || imageBytes.Length == 0)
            throw new InvalidOperationException($"Image generation failed for email '{email.Subject ?? "Untitled"}'.");

        var contentIdToken = DeterministicIdHelper.CreateShortToken(
            "inline-image",
            32,
            email.Id.ToString("N"),
            response.ImageDescription ?? string.Empty,
            response.ImageContext ?? string.Empty);
        var contentId = $"img_{contentIdToken}";
        var attachment = new Attachment
        {
            Type = AttachmentType.Image,
            Content = imageBytes,
            ContentDescription = response.ImageContext ?? "Attached image",
            IsInline = response.IsInline,
            ContentId = contentId,
            FileName = FileNameHelper.GenerateImageFileName(email, response.ImageContext ?? response.ImageDescription, contentId)
        };

        EnsureAttachmentId(email, attachment);
        email.AddAttachment(attachment);

        // If inline, update the HTML body to include the image in the main content (before quoted text)
        if (response.IsInline && !string.IsNullOrEmpty(email.BodyHtml))
        {
            var safeContext = System.Net.WebUtility.HtmlEncode(response.ImageContext ?? "Attached image");
            var imageHtml = BuildInlineImageHtml(safeContext, contentId);
            InsertInlineImageHtml(email, imageHtml);
        }
    }

    private static string BuildInlineImageHtml(string safeContext, string contentId)
    {
        return $@"<div style=""margin: 15px 0;""><p><em>{safeContext}</em></p><div style=""text-align: center;""><img src=""cid:{contentId}"" alt=""{safeContext}"" style=""max-width: 600px; height: auto; border-radius: 4px;"" /></div></div>";
    }

    private static void InsertInlineImageHtml(EmailMessage email, string imageHtml)
    {
        if (string.IsNullOrEmpty(email.BodyHtml))
            return;

        // Try to insert the image BEFORE the quoted content (reply or forward sections)
        var insertionPoints = new[]
        {
            "<div class=\"quoted-content\">",
            "<div class=\"forward-header\">",
            "<div class=\"signature\">"
        };

        bool inserted = false;
        foreach (var marker in insertionPoints)
        {
            var markerIndex = email.BodyHtml.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                email.BodyHtml = email.BodyHtml.Insert(markerIndex, imageHtml);
                inserted = true;
                break;
            }
        }

        if (!inserted)
        {
            if (email.BodyHtml.Contains("</body>", StringComparison.Ordinal))
            {
                email.BodyHtml = email.BodyHtml.Replace("</body>", imageHtml + "</body>");
            }
            else
            {
                email.BodyHtml += imageHtml;
            }
        }
    }

    private sealed class ImageSuggestionResponse
    {
        [JsonPropertyName("shouldIncludeImage")]
        public bool ShouldIncludeImage { get; set; }

        [JsonPropertyName("imageDescription")]
        public string ImageDescription { get; set; } = string.Empty;

        [JsonPropertyName("imageContext")]
        public string? ImageContext { get; set; }

        [JsonPropertyName("isInline")]
        public bool IsInline { get; set; }
    }

    private async Task DetectAndAddCalendarInviteAsync(EmailMessage email, List<Character> characters, CancellationToken ct)
    {
        // Ask AI if this email mentions a meeting that should have a calendar invite

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You analyze emails to detect if they are scheduling or confirming a meeting/event that should have a calendar invite attached.

Look for:
- Specific dates and times mentioned ('tomorrow at 3pm', 'Friday at noon', 'next week Monday')
- Meeting requests or confirmations
- Event invitations
- Scheduled calls or gatherings

If there is no clear meeting date/time, set hasMeeting to false.
");


        var schema = """
{
  "hasMeeting": boolean,
  "meetingTitle": "string (title for the calendar invite)",
  "meetingDescription": "string (brief description)",
  "location": "string (meeting location or 'Virtual' or 'TBD')",
  "suggestedDate": "YYYY-MM-DD (the date of the meeting, based on context)",
  "suggestedStartTime": "HH:MM (24-hour format)",
  "durationMinutes": number (30, 60, 90, 120, etc.)
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Email subject: {email.Subject}
Email body: {email.BodyPlain[..Math.Min(800, email.BodyPlain.Length)]}
Email sent date: {email.SentDate:yyyy-MM-dd}

Does this email mention a specific meeting, event, or call that should have a calendar invite?
If details are vague or missing, set hasMeeting to false.
", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<MeetingDetectionResponse>(systemPrompt, userPrompt, "Meeting Detection", ct);

        if (response == null)
            throw new InvalidOperationException($"Meeting detection failed for email '{email.Subject ?? "Untitled"}'.");
        if (!response.HasMeeting)
            return;

        // Parse the meeting date and time
        if (!DateHelper.TryParseAiDate(response.SuggestedDate, out var meetingDate))
        {
            meetingDate = email.SentDate.AddDays(1); // Default to next day
        }

        var timeParts = (response.SuggestedStartTime ?? "10:00").Split(':');
        var hour = int.TryParse(timeParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : 10;
        var minute = timeParts.Length > 1
            && int.TryParse(timeParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
                ? m
                : 0;

        var startTime = new DateTime(meetingDate.Year, meetingDate.Month, meetingDate.Day, hour, minute, 0);
        var endTime = startTime.AddMinutes(response.DurationMinutes > 0 ? response.DurationMinutes : 60);

        // Get attendees from the email recipients
        var attendees = email.To
            .Concat(email.Cc)
            .Where(c => c.Email != email.From.Email)
            .Select(c => (c.FullName, c.Email))
            .ToList();

        var icsContent = CalendarService.CreateCalendarInvite(
            new CalendarService.CalendarInviteRequest(
                response.MeetingTitle ?? email.Subject,
                response.MeetingDescription ?? "",
                startTime,
                endTime,
                response.Location ?? "TBD",
                email.From.FullName,
                email.From.Email,
                attendees),
            _logger.ForContext<CalendarService>());

        var attachment = new Attachment
        {
            Type = AttachmentType.CalendarInvite,
            Content = icsContent,
            ContentDescription = response.MeetingTitle ?? "Meeting Invite",
            FileName = FileNameHelper.GenerateCalendarInviteFileName(
                email,
                startTime,
                response.MeetingTitle,
                email.From.Email)
        };

        EnsureAttachmentId(email, attachment);
        email.AddAttachment(attachment);
    }

    private sealed class MeetingDetectionResponse
    {
        [JsonPropertyName("hasMeeting")]
        public bool HasMeeting { get; set; }

        [JsonPropertyName("meetingTitle")]
        public string? MeetingTitle { get; set; }

        [JsonPropertyName("meetingDescription")]
        public string? MeetingDescription { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("suggestedDate")]
        public string? SuggestedDate { get; set; }

        [JsonPropertyName("suggestedStartTime")]
        public string? SuggestedStartTime { get; set; }

        [JsonPropertyName("durationMinutes")]
        public int DurationMinutes { get; set; }
    }

    private async Task GenerateVoicemailForEmailAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        // Ask AI to create a voicemail script related to this email thread

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are creating a voicemail message that relates to a fictional corporate email thread.
The voicemail should sound natural and conversational, as if someone called and left a message.
It should relate to the email content but not simply read the email aloud.
Do not use real company names or real people.

Keep the voicemail BRIEF - 15-30 seconds when spoken (about 40-80 words).
");

        var narrativeTopic = ResolveEmailTopic(email, state);
        var context = $@"Email subject: {email.Subject}
Email body preview: {email.BodyPlain[..Math.Min(400, email.BodyPlain.Length)]}
Sender: {email.From.FullName}
Narrative topic: {narrativeTopic}";


        var schema = """
{
  "shouldCreateVoicemail": boolean (false if voicemail doesn't make sense),
  "voicemailScript": "string (the voicemail transcript)",
  "recipientName": "string (who the voicemail is for)"
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"{context}

Create a voicemail that {email.From.FirstName} might leave that relates to this email thread.
The voicemail should:
- Sound natural and conversational (include 'um', 'uh', pauses indicated by '...')
- Reference the email topic but add urgency or context
- Start with a greeting ('Hey, it's [name]...' or 'Hi, this is [name] calling about...')
- End naturally ('...call me back when you get this' or 'talk soon')
- Be 40-80 words total
- Keep all names and organizations fictional", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<VoicemailScriptResponse>(systemPrompt, userPrompt, "Voicemail Script", ct);

        if (response == null)
            throw new InvalidOperationException($"Voicemail suggestion failed for email '{email.Subject ?? "Untitled"}'.");
        if (!response.ShouldCreateVoicemail)
            return;
        if (string.IsNullOrWhiteSpace(response.VoicemailScript))
            throw new InvalidOperationException($"Voicemail script was missing for email '{email.Subject ?? "Untitled"}'.");

        // Generate the audio using TTS
        var audioBytes = await _openAI.GenerateSpeechAsync(
            response.VoicemailScript,
            email.From.VoiceId,
            "Voicemail TTS",
            ct);

        if (audioBytes == null || audioBytes.Length == 0)
            throw new InvalidOperationException($"Voicemail audio generation failed for email '{email.Subject ?? "Untitled"}'.");

        var attachment = new Attachment
        {
            Type = AttachmentType.Voicemail,
            Content = audioBytes,
            ContentDescription = $"Voicemail from {email.From.FullName}",
            VoiceId = email.From.VoiceId,
            FileName = BuildVoicemailFileName(email.From.LastName, email.SentDate)
        };

        EnsureAttachmentId(email, attachment);
        email.AddAttachment(attachment);
    }

    private sealed class VoicemailScriptResponse
    {
        [JsonPropertyName("shouldCreateVoicemail")]
        public bool ShouldCreateVoicemail { get; set; }

        [JsonPropertyName("voicemailScript")]
        public string VoicemailScript { get; set; } = string.Empty;

        [JsonPropertyName("recipientName")]
        public string? RecipientName { get; set; }
    }

    // Response DTOs
    protected internal class SingleEmailApiResponse
    {
        [JsonPropertyName("bodyPlain")]
        public string BodyPlain { get; set; } = string.Empty;
    }

    protected internal sealed class EmailSubjectResponse
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;
    }

    protected internal sealed class NonResponsiveSubjectResponse
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("entityValues")]
        public Dictionary<string, string> EntityValues { get; set; } = new();
    }

    private sealed class WordDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ExcelDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        public List<List<string>> Rows { get; set; } = new();
    }

    // Raw response that handles mixed types (strings and numbers) in Excel rows
    private sealed class ExcelDocResponseRaw
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        public List<List<System.Text.Json.JsonElement>> Rows { get; set; } = new();
    }

    private sealed class PowerPointDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("slides")]
        public List<SlideDto> Slides { get; set; } = new();
    }

    private sealed class SlideDto
    {
        [JsonPropertyName("slideTitle")]
        public string SlideTitle { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
