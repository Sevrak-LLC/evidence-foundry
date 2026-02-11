using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using EvidenceFoundry.Models;
using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Services;

public class EmailGenerator
{
    private readonly OpenAIService _openAI;
    private readonly OfficeDocumentService _officeService;
    private readonly CalendarService _calendarService;
    private readonly EmailThreadGenerator _threadGenerator;
    private readonly SuggestedSearchTermGenerator _searchTermGenerator;
    private static readonly Random _random = Random.Shared;

    // Track document chains across threads for versioning
    private readonly ConcurrentDictionary<string, DocumentChainState> _documentChains = new();

    // Maximum emails per API call to avoid token limits and timeouts
    private const int MaxEmailsPerBatch = 15;
    private const int MaxThreadGenerationAttempts = 3;
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

    public EmailGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
        _officeService = new OfficeDocumentService();
        _calendarService = new CalendarService();
        _threadGenerator = new EmailThreadGenerator();
        _searchTermGenerator = new SuggestedSearchTermGenerator(openAI);
    }

    private class DocumentChainState
    {
        public string ChainId { get; set; } = Guid.NewGuid().ToString("N")[..8];
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
        var result = new GenerationResult
        {
            OutputFolder = state.Config.OutputFolder
        };

        var stopwatch = Stopwatch.StartNew();
        var activeStorylines = state.GetActiveStorylines().ToList();
        var progressData = new GenerationProgress
        {
            TotalEmails = activeStorylines.Sum(s => s.EmailCount),
            CurrentOperation = "Initializing..."
        };

        try
        {
            if (activeStorylines.Count == 0)
                throw new InvalidOperationException("No storyline available for email generation.");
            var characterContexts = BuildCharacterContextMap(state.Organizations);

            var threads = new ConcurrentBag<EmailThread>();
            var progressLock = new object();
            var savedThreads = new ConcurrentDictionary<Guid, bool>();
            var saveSemaphore = new SemaphoreSlim(1, 1);

            // EML service for incremental saving
            var emlService = new EmlFileService();
            Directory.CreateDirectory(state.Config.OutputFolder);

            var processContext = new ProcessStorylineContext(
                characterContexts,
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
            await SaveRemainingEmlAsync(
                threadsList,
                state,
                processContext,
                ct);

            await GenerateSuggestedSearchTermsAsync(
                threadsList,
                activeStorylines,
                state.Config,
                progressData,
                progress,
                progressLock,
                result,
                ct);

            // Finalize results
            result.TotalEmailsGenerated = threadsList.Sum(t => t.EmailMessages.Count);
            result.TotalThreadsGenerated = threadsList.Count;
            result.TotalAttachmentsGenerated = result.WordDocumentsGenerated + result.ExcelDocumentsGenerated + result.PowerPointDocumentsGenerated;
            result.ElapsedTime = stopwatch.Elapsed;

            state.GeneratedThreads = threadsList;
            state.Result = result;

            ReportProgress(progress, progressData, progressLock, p => p.CurrentOperation = "Complete!");

            return result;
        }
        catch (OperationCanceledException)
        {
            result.WasCancelled = true;
            result.ElapsedTime = stopwatch.Elapsed;
            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            result.ElapsedTime = stopwatch.Elapsed;
            return result;
        }
    }

    private sealed class ProcessStorylineContext
    {
        public ProcessStorylineContext(
            Dictionary<Guid, CharacterContext> characterContexts,
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

        try
        {
            ct.ThrowIfCancellationRequested();

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
                storyline, state.Characters, state.CompanyDomain,
                beats,
                state.Config, state.DomainThemes, context.CharacterContexts,
                state, context.Result, context.ProgressData, context.Progress, context.ProgressLock,
                context.EmlService, context.SaveSemaphore, context.SavedThreads, ct);

            // Add to concurrent collection
            foreach (var thread in storylineThreads)
            {
                context.Threads.Add(thread);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (Exception ex)
        {
            // Per-storyline error handling: log and continue with others
            lock (context.ProgressLock)
            {
                context.Result.Errors.Add($"Storyline '{storyline.Title}' failed: {ex.Message}");
            }

            // Still count these emails as "completed" for progress tracking
            var completedAtFailure = 0;
            lock (context.ProgressLock)
            {
                completedAtFailure = context.ProgressData.CompletedEmails;
            }

            var completedInStoryline = completedAtFailure - completedAtStart;
            var remaining = Math.Max(0, emailCount - completedInStoryline);
            if (remaining > 0)
            {
                ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, p =>
                {
                    p.CompletedEmails += remaining;
                });
            }
        }
    }

    private static async Task SaveRemainingEmlAsync(
        List<EmailThread> threadsList,
        WizardState state,
        ProcessStorylineContext context,
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
                ct,
                state.Config.ParallelThreads,
                releaseAttachmentContent: true);
        }
        catch (Exception ex)
        {
            lock (context.ProgressLock)
            {
                context.Result.Errors.Add($"Failed to save EML files: {ex.Message}");
                if (ex.InnerException != null)
                {
                    context.Result.Errors.Add($"  Inner error: {ex.InnerException.Message}");
                }
            }
        }
    }

    private static string IndentSignature(string signature)
    {
        if (string.IsNullOrEmpty(signature)) return "    (no signature)";
        var lines = signature.Replace("\\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(l => $"    {l}"));
    }

    private static string GetThreadSubject(EmailThread thread)
    {
        var subject = thread.EmailMessages.FirstOrDefault()?.Subject;
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
        sb.AppendLine($"Subject: {email.Subject}");
        if (email.From != null)
        {
            sb.AppendLine($"From: {email.From.FullName} <{email.From.Email}>");
        }

        if (email.To.Count > 0)
        {
            sb.AppendLine($"To: {string.Join("; ", email.To.Select(c => $"{c.FullName} <{c.Email}>"))}");
        }

        if (email.Cc.Count > 0)
        {
            sb.AppendLine($"Cc: {string.Join("; ", email.Cc.Select(c => $"{c.FullName} <{c.Email}>"))}");
        }

        sb.AppendLine($"Date: {email.SentDate:yyyy-MM-dd HH:mm}");

        if (email.Attachments.Count > 0)
        {
            sb.AppendLine("Attachments:");
            foreach (var attachment in email.Attachments)
            {
                var description = string.IsNullOrWhiteSpace(attachment.ContentDescription)
                    ? ""
                    : $" - {attachment.ContentDescription}";
                sb.AppendLine($"- {attachment.Type} {attachment.FileName}{description}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(email.BodyPlain ?? string.Empty);
        return sb.ToString();
    }

    private async Task GenerateSuggestedSearchTermsAsync(
        IReadOnlyList<EmailThread> threads,
        IReadOnlyList<Storyline> storylines,
        GenerationConfig config,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        GenerationResult result,
        CancellationToken ct)
    {
        if (threads.Count == 0)
            return;

        var responsiveThreads = threads
            .Where(t => t.Relevance == EmailThread.ThreadRelevance.Responsive || t.IsHot)
            .ToList();

        if (responsiveThreads.Count == 0)
            return;

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
                storylineLookup,
                progressLock,
                result);

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
            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Generating suggested search terms: {subject}";
            });

            var terms = await GenerateSuggestedSearchTermsForThreadAsync(
                new SuggestedSearchTermsRequest(subject, largestEmail, storyline, beat, thread.IsHot),
                progressLock,
                result,
                ct);

            results.Add(new SuggestedSearchTermResult
            {
                ThreadId = thread.Id,
                Subject = subject,
                IsHot = thread.IsHot,
                Terms = terms
            });
        }

        if (results.Count == 0)
            return;

        ReportProgress(progress, progressData, progressLock, p =>
        {
            p.CurrentOperation = "Writing suggested search terms markdown...";
        });

        var markdown = BuildSuggestedSearchTermsMarkdown(results);
        var outputPath = Path.Combine(config.OutputFolder, "suggested-search-terms.md");
        try
        {
            Directory.CreateDirectory(config.OutputFolder);
            await File.WriteAllTextAsync(outputPath, markdown, ct);
        }
        catch (Exception ex)
        {
            lock (progressLock)
            {
                result.Errors.Add($"Failed to write suggested search terms markdown: {ex.Message}");
            }
        }
    }

    private async Task<List<string>> GenerateSuggestedSearchTermsForThreadAsync(
        SuggestedSearchTermsRequest request,
        object progressLock,
        GenerationResult result,
        CancellationToken ct)
    {
        try
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
                AddSuggestedTermsError(
                    progressLock,
                    result,
                    $"Suggested terms returned fewer than 2 entries for thread '{request.Subject}'.");
            }

            return terms;
        }
        catch (Exception ex)
        {
            AddSuggestedTermsError(
                progressLock,
                result,
                $"Failed to generate suggested terms for thread '{request.Subject}': {ex.Message}");
            return new List<string>();
        }
    }

    private sealed record SuggestedSearchTermsRequest(
        string Subject,
        EmailMessage LargestEmail,
        Storyline Storyline,
        StoryBeat Beat,
        bool IsHot);

    private sealed record SuggestedSearchContextOptions(
        IReadOnlyDictionary<Guid, StoryBeat> BeatLookup,
        IReadOnlyDictionary<Guid, Storyline> StorylineLookup,
        object ProgressLock,
        GenerationResult Result);

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
            AddSuggestedTermsError(options.ProgressLock, options.Result, $"Suggested terms skipped: missing story beat for thread {thread.Id}.");
            return false;
        }

        if (!options.StorylineLookup.TryGetValue(thread.StorylineId, out var resolvedStoryline))
        {
            AddSuggestedTermsError(options.ProgressLock, options.Result, $"Suggested terms skipped: missing storyline for thread {thread.Id}.");
            return false;
        }

        var resolvedEmail = GetLargestEmailInThread(thread);
        if (resolvedEmail == null)
        {
            AddSuggestedTermsError(options.ProgressLock, options.Result, $"Suggested terms skipped: thread {thread.Id} has no emails.");
            return false;
        }

        beat = resolvedBeat;
        storyline = resolvedStoryline;
        largestEmail = resolvedEmail;
        return true;
    }

    private static void AddSuggestedTermsError(object progressLock, GenerationResult result, string message)
    {
        lock (progressLock)
        {
            result.Errors.Add(message);
        }
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
        sb.AppendLine($"## {title}");
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
            sb.AppendLine($"- {term}");
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
        sb.AppendLine($"- Subject: {item.Subject}");
        if (item.Terms.Count == 0)
        {
            sb.AppendLine("  - (no terms generated)");
            return;
        }

        foreach (var term in item.Terms)
        {
            sb.AppendLine($"  - {term}");
        }
    }

    private readonly record struct CharacterContext(string Role, string Department, string Organization);

    private readonly record struct ThreadPlan(
        int Index,
        EmailThread Thread,
        int EmailCount,
        DateTime Start,
        DateTime End,
        string BeatName,
        List<Character> Participants,
        Dictionary<string, Character> ParticipantLookup,
        string ParticipantList);

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

    private static string BuildCharacterList(IEnumerable<Character> characters, Dictionary<Guid, CharacterContext> contexts)
    {
        return string.Join("\n\n", characters.Select(c =>
        {
            if (!contexts.TryGetValue(c.Id, out var context))
                throw new InvalidOperationException($"Character '{c.FullName}' has no organization assignment.");

            return $"- {c.FullName} ({c.Email})\n  Role: {context.Role}, {context.Department} @ {context.Organization}\n  Personality: {c.Personality}\n  Communication Style: {c.CommunicationStyle}\n  Signature:\n{IndentSignature(c.SignatureBlock)}";
        }));
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

    private async Task<List<EmailThread>> GenerateThreadsForStorylineAsync(
        Storyline storyline,
        List<Character> characters,
        string domain,
        IReadOnlyList<StoryBeat> beats,
        GenerationConfig config,
        Dictionary<string, OrganizationTheme> domainThemes,
        Dictionary<Guid, CharacterContext> characterContexts,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        EmlFileService emlService,
        SemaphoreSlim saveSemaphore,
        ConcurrentDictionary<Guid, bool> savedThreads,
        CancellationToken ct)
    {
        if (beats == null || beats.Count == 0) return new List<EmailThread>();

        var threadPlans = BuildThreadPlans(storyline, characters, beats, characterContexts, state);
        if (threadPlans.Count == 0) return new List<EmailThread>();

        var threads = new EmailThread?[threadPlans.Count];
        var systemPrompt = BuildEmailSystemPrompt();
        var context = new ThreadPlanContext(
            storyline,
            domain,
            config,
            domainThemes,
            systemPrompt,
            state,
            result,
            progressData,
            progress,
            progressLock,
            emlService,
            saveSemaphore,
            savedThreads);
        var degree = Math.Max(1, config.ParallelThreads);

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

    private sealed class ThreadPlanContext(
        Storyline storyline,
        string domain,
        GenerationConfig config,
        Dictionary<string, OrganizationTheme> domainThemes,
        string systemPrompt,
        WizardState state,
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
        public GenerationResult Result { get; } = result;
        public GenerationProgress ProgressData { get; } = progressData;
        public IProgress<GenerationProgress> Progress { get; } = progress;
        public object ProgressLock { get; } = progressLock;
        public EmlFileService EmlService { get; } = emlService;
        public SemaphoreSlim SaveSemaphore { get; } = saveSemaphore;
        public ConcurrentDictionary<Guid, bool> SavedThreads { get; } = savedThreads;
    }

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
                _threadGenerator.AssignThreadParticipants(thread, state.Organizations, _random);

                var participants = ResolveThreadParticipants(thread, characters);
                var participantLookup = participants.ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);
                var participantList = BuildCharacterList(participants, characterContexts);

                threadPlans.Add(new ThreadPlan(
                    planIndex++,
                    thread,
                    threadEmailCount,
                    threadStartDate,
                    threadEndDate,
                    beat.Name,
                    participants,
                    participantLookup,
                    participantList));

                emailsAssigned += threadEmailCount;
            }

            EnsureBeatEmailCountMatches(beat, emailsAssigned);
        }

        return threadPlans;
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
        var thread = await GenerateThreadWithRetriesAsync(
            context.Storyline,
            plan.Thread,
            plan.Participants,
            plan.ParticipantLookup,
            context.Domain,
            plan.EmailCount,
            plan.Start,
            plan.End,
            context.Config,
            context.DomainThemes,
            context.SystemPrompt,
            plan.ParticipantList,
            plan.BeatName,
            context.Result,
            context.ProgressLock,
            ct);

        ReportProgress(context.Progress, context.ProgressData, context.ProgressLock, p =>
        {
            p.CompletedEmails = Math.Min(p.TotalEmails, p.CompletedEmails + plan.EmailCount);
            p.CurrentOperation = thread != null
                ? $"Generated thread: {GetThreadSubject(thread)}"
                : $"Skipped thread after {MaxThreadGenerationAttempts} attempts (beat: {plan.BeatName})";
        });

        if (thread == null)
            return;

        await GenerateThreadAssetsAsync(thread, context.State, context.Result, context.ProgressData, context.Progress, context.ProgressLock, ct);
        await SaveThreadAsync(thread, context.Config, context.EmlService, context.ProgressData, context.Progress, context.ProgressLock, context.SaveSemaphore, context.Result, context.SavedThreads, ct);
        threads[plan.Index] = thread;
    }

    private async Task GenerateThreadAssetsAsync(
        EmailThread thread,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        CancellationToken ct)
    {
        var emails = thread.EmailMessages;

        await GeneratePlannedDocumentsAsync(emails, state, result, progressData, progress, progressLock, ct);
        await GeneratePlannedImagesAsync(emails, state, result, progressData, progress, progressLock, ct);
        await GenerateCalendarInvitesAsync(emails, state, result, progressData, progress, progressLock, ct);
        await GeneratePlannedVoicemailsAsync(emails, state, result, progressData, progress, progressLock, ct);
    }

    private async Task GeneratePlannedDocumentsAsync(
        List<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        CancellationToken ct)
    {
        var emailsWithPlannedDocuments = emails.Where(e => e.PlannedHasDocument).ToList();
        if (emailsWithPlannedDocuments.Count == 0)
            return;

        ReportProgress(progress, progressData, progressLock, p =>
        {
            p.TotalAttachments += emailsWithPlannedDocuments.Count;
        });

        foreach (var email in emailsWithPlannedDocuments)
        {
            ct.ThrowIfCancellationRequested();

            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Creating attachment for: {email.Subject}";
            });

            await GeneratePlannedDocumentAsync(email, state, ct);

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
        List<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        CancellationToken ct)
    {
        if (!state.Config.IncludeImages)
            return;

        var emailsWithPlannedImages = emails.Where(e => e.PlannedHasImage).ToList();
        if (emailsWithPlannedImages.Count == 0)
            return;

        ReportProgress(progress, progressData, progressLock, p =>
        {
            p.TotalAttachments += emailsWithPlannedImages.Count;
            p.TotalImages += emailsWithPlannedImages.Count;
        });

        foreach (var email in emailsWithPlannedImages)
        {
            ct.ThrowIfCancellationRequested();

            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Generating image for: {email.Subject}";
            });

            await GeneratePlannedImageAsync(email, state, ct);

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
        List<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        CancellationToken ct)
    {
        if (!state.Config.IncludeCalendarInvites || state.Config.CalendarInvitePercentage <= 0)
            return;

        var maxCalendarEmails = Math.Max(1, (int)Math.Round(emails.Count * state.Config.CalendarInvitePercentage / 100.0));
        var emailsToCheckForCalendar = emails
            .OrderBy(_ => _random.Next())
            .Take(maxCalendarEmails)
            .ToList();

        if (emailsToCheckForCalendar.Count > 0)
        {
            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.TotalAttachments += emailsToCheckForCalendar.Count;
            });
        }

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
        List<EmailMessage> emails,
        WizardState state,
        GenerationResult result,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        CancellationToken ct)
    {
        if (!state.Config.IncludeVoicemails)
            return;

        var emailsWithPlannedVoicemails = emails.Where(e => e.PlannedHasVoicemail).ToList();
        if (emailsWithPlannedVoicemails.Count == 0)
            return;

        ReportProgress(progress, progressData, progressLock, p =>
        {
            p.TotalAttachments += emailsWithPlannedVoicemails.Count;
        });

        foreach (var email in emailsWithPlannedVoicemails)
        {
            ct.ThrowIfCancellationRequested();

            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Generating voicemail for: {email.From.FullName}";
            });

            await GeneratePlannedVoicemailAsync(email, state, ct);

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

    private async Task SaveThreadAsync(
        EmailThread thread,
        GenerationConfig config,
        EmlFileService emlService,
        GenerationProgress progressData,
        IProgress<GenerationProgress> progress,
        object progressLock,
        SemaphoreSlim saveSemaphore,
        GenerationResult result,
        ConcurrentDictionary<Guid, bool> savedThreads,
        CancellationToken ct)
    {
        var subject = GetThreadSubject(thread);

        await saveSemaphore.WaitAsync(ct);
        try
        {
            ReportProgress(progress, progressData, progressLock, p =>
            {
                p.CurrentOperation = $"Saving EML files for thread: {subject}";
            });

            var emlProgress = new Progress<(int completed, int total, string currentFile)>(p =>
            {
                ReportProgress(progress, progressData, progressLock, pd =>
                {
                    pd.CurrentOperation = $"Saving: {p.currentFile}";
                });
            });

            await emlService.SaveThreadEmailsAsync(
                thread,
                config.OutputFolder,
                config.OrganizeBySender,
                emlProgress,
                ct,
                releaseAttachmentContent: true);

            savedThreads.TryAdd(thread.Id, true);
        }
        catch (Exception ex)
        {
            lock (progressLock)
            {
                result.Errors.Add($"Failed to save EML files for thread '{subject}': {ex.Message}");
                if (ex.InnerException != null)
                {
                    result.Errors.Add($"  Inner error: {ex.InnerException.Message}");
                }
            }
        }
        finally
        {
            saveSemaphore.Release();
        }
    }

    private async Task<EmailThread> GenerateSingleThreadForStorylineAsync(
        Storyline storyline,
        EmailThread thread,
        List<Character> participants,
        Dictionary<string, Character> participantLookup,
        string domain,
        int emailCount,
        DateTime startDate,
        DateTime endDate,
        GenerationConfig config,
        Dictionary<string, OrganizationTheme> domainThemes,
        string systemPrompt,
        string characterList,
        CancellationToken ct)
    {
        ValidateThreadGenerationInputs(thread, emailCount);

        _threadGenerator.EnsurePlaceholderMessages(thread, emailCount);

        // Break into batches to avoid token limits and timeouts
        var totalBatches = (int)Math.Ceiling((double)emailCount / MaxEmailsPerBatch);

        var isResponsiveThread = thread.Relevance == EmailThread.ThreadRelevance.Responsive || thread.IsHot;

        var attachmentTotals = CalculateAttachmentTotals(config, emailCount);
        var attachmentState = new AttachmentDistributionState(
            attachmentTotals.totalDocAttachments,
            attachmentTotals.totalImageAttachments,
            attachmentTotals.totalVoicemailAttachments);

        var batchContext = new ThreadBatchContext(
            storyline,
            thread,
            participants,
            participantLookup,
            startDate,
            endDate,
            config,
            domainThemes,
            systemPrompt,
            characterList,
            emailCount);

        var generationState = new ThreadGenerationState(0, string.Empty);

        for (int batch = 0; batch < totalBatches; batch++)
        {
            ct.ThrowIfCancellationRequested();

            generationState = await GenerateThreadBatchAsync(
                batchContext,
                attachmentState,
                generationState,
                batch,
                totalBatches,
                isResponsiveThread,
                ct);
        }

        if (generationState.EmailsGenerated != emailCount)
            throw new InvalidOperationException($"Thread generated {generationState.EmailsGenerated} emails but expected {emailCount}.");

        // Setup threading headers
        ThreadingHelper.SetupThreading(thread, domain);

        return thread;
    }

    private static void ValidateThreadGenerationInputs(EmailThread thread, int emailCount)
    {
        if (thread == null) throw new ArgumentNullException(nameof(thread));
        if (emailCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(emailCount), "Thread email count must be positive.");
    }

    internal static (int totalDocAttachments, int totalImageAttachments, int totalVoicemailAttachments) CalculateAttachmentTotals(
        GenerationConfig config,
        int emailCount)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        var totalDocAttachments = config.AttachmentPercentage > 0 && config.EnabledAttachmentTypes.Count > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.AttachmentPercentage / 100.0))
            : 0;
        var totalImageAttachments = config.IncludeImages && config.ImagePercentage > 0
            ? Math.Max(1, (int)Math.Round(emailCount * config.ImagePercentage / 100.0))
            : 0;
        var totalVoicemailAttachments = config.IncludeVoicemails && config.VoicemailPercentage > 0
            ? Math.Max(0, (int)Math.Round(emailCount * config.VoicemailPercentage / 100.0))
            : 0;

        return (totalDocAttachments, totalImageAttachments, totalVoicemailAttachments);
    }

    private async Task<ThreadGenerationState> GenerateThreadBatchAsync(
        ThreadBatchContext context,
        AttachmentDistributionState attachmentState,
        ThreadGenerationState state,
        int batch,
        int totalBatches,
        bool isResponsiveThread,
        CancellationToken ct)
    {
        var emailsGenerated = state.EmailsGenerated;
        var sequence = emailsGenerated;
        var batchSize = Math.Min(MaxEmailsPerBatch, context.EmailCount - emailsGenerated);
        var isFirstBatch = batch == 0;
        var isLastBatch = batch == totalBatches - 1;

        var (batchStartDate, batchEndDate) = GetBatchDateWindow(
            context.StartDate,
            context.EndDate,
            emailsGenerated,
            batchSize,
            context.EmailCount);

        var (batchDocs, batchImages, batchVoicemails) = CalculateBatchAttachments(
            attachmentState,
            batchSize,
            context.EmailCount - emailsGenerated,
            isLastBatch);

        var batchAttachmentInstructions = BuildBatchAttachmentInstructions(
            context.Config,
            batchDocs,
            batchImages,
            batchVoicemails,
            isResponsiveThread);

        var narrativeContext = BuildNarrativeContextForBatch(context.Thread, isFirstBatch, emailsGenerated);
        var narrativePhase = GetNarrativePhase(batch, totalBatches);

        var userPrompt = BuildBatchUserPrompt(
            context.Storyline,
            context.Thread,
            context.CharacterList,
            batchStartDate,
            batchEndDate,
            batchSize,
            batchAttachmentInstructions,
            narrativeContext,
            narrativePhase,
            isFirstBatch,
            state.ThreadSubject);

        var response = await GetThreadResponseAsync(
            context.SystemPrompt,
            userPrompt,
            $"Email Thread Generation (batch {batch + 1}/{totalBatches})",
            ct);

        if (response == null)
            throw new InvalidOperationException($"Failed to generate batch {batch + 1} for storyline: {context.Storyline.Title}");

        var threadSubject = ResolveThreadSubject(response, state.ThreadSubject, isFirstBatch, context.Storyline);
        var batchOffset = emailsGenerated;
        sequence = ApplyBatchEmails(
            context,
            response,
            threadSubject,
            sequence,
            batchOffset);

        return new ThreadGenerationState(sequence, threadSubject);
    }

    private static (DateTime batchStartDate, DateTime batchEndDate) GetBatchDateWindow(
        DateTime startDate,
        DateTime endDate,
        int emailsGenerated,
        int batchSize,
        int emailCount)
    {
        var batchStartDate = DateHelper.InterpolateDateInRange(startDate, endDate, (double)emailsGenerated / emailCount);
        var batchEndDate = DateHelper.InterpolateDateInRange(startDate, endDate, (double)(emailsGenerated + batchSize) / emailCount);
        return (batchStartDate, batchEndDate);
    }

    private static (int docs, int images, int voicemails) CalculateBatchAttachments(
        AttachmentDistributionState state,
        int batchSize,
        int remainingEmails,
        bool isLastBatch)
    {
        var docs = DistributeAttachmentsForBatch(state.TotalDocAttachments, ref state.DocsAssigned, batchSize, remainingEmails, isLastBatch);
        var images = DistributeAttachmentsForBatch(state.TotalImageAttachments, ref state.ImagesAssigned, batchSize, remainingEmails, isLastBatch);
        var voicemails = DistributeAttachmentsForBatch(state.TotalVoicemailAttachments, ref state.VoicemailsAssigned, batchSize, remainingEmails, isLastBatch);
        return (docs, images, voicemails);
    }

    private static string BuildNarrativeContextForBatch(EmailThread thread, bool isFirstBatch, int emailsGenerated)
    {
        if (isFirstBatch || emailsGenerated <= 0)
            return string.Empty;

        return BuildNarrativeContext(thread.EmailMessages.Take(emailsGenerated).ToList());
    }

    private static string ResolveThreadSubject(
        ThreadApiResponse response,
        string existingSubject,
        bool isFirstBatch,
        Storyline storyline)
    {
        if (!isFirstBatch)
            return existingSubject;

        if (string.IsNullOrWhiteSpace(response.Subject))
            throw new InvalidOperationException($"Thread subject missing for storyline: {storyline.Title}");

        return response.Subject;
    }

    private int ApplyBatchEmails(
        ThreadBatchContext context,
        ThreadApiResponse response,
        string threadSubject,
        int sequence,
        int batchOffset)
    {
        foreach (var email in response.Emails)
        {
            if (sequence >= context.Thread.EmailMessages.Count)
                throw new InvalidOperationException($"Thread plan expected {context.Thread.EmailMessages.Count} emails but generated more.");

            var target = context.Thread.EmailMessages[sequence];
            ConvertDtoToEmailMessage(
                email,
                context.Thread,
                threadSubject,
                target,
                context.Participants,
                context.ParticipantLookup,
                context.DomainThemes,
                context.StartDate,
                context.EndDate,
                ref sequence,
                batchOffset);
        }

        return sequence;
    }

    private sealed record ThreadGenerationState(int EmailsGenerated, string ThreadSubject);

    private sealed record ThreadBatchContext(
        Storyline Storyline,
        EmailThread Thread,
        List<Character> Participants,
        Dictionary<string, Character> ParticipantLookup,
        DateTime StartDate,
        DateTime EndDate,
        GenerationConfig Config,
        Dictionary<string, OrganizationTheme> DomainThemes,
        string SystemPrompt,
        string CharacterList,
        int EmailCount);

    private sealed class AttachmentDistributionState
    {
        public AttachmentDistributionState(int totalDocAttachments, int totalImageAttachments, int totalVoicemailAttachments)
        {
            TotalDocAttachments = totalDocAttachments;
            TotalImageAttachments = totalImageAttachments;
            TotalVoicemailAttachments = totalVoicemailAttachments;
        }

        public int TotalDocAttachments { get; }
        public int TotalImageAttachments { get; }
        public int TotalVoicemailAttachments { get; }
        public int DocsAssigned;
        public int ImagesAssigned;
        public int VoicemailsAssigned;
    }

    internal async Task<EmailThread?> GenerateThreadWithRetriesAsync(
        Storyline storyline,
        EmailThread thread,
        List<Character> participants,
        Dictionary<string, Character> participantLookup,
        string domain,
        int emailCount,
        DateTime startDate,
        DateTime endDate,
        GenerationConfig config,
        Dictionary<string, OrganizationTheme> domainThemes,
        string systemPrompt,
        string characterList,
        string beatName,
        GenerationResult result,
        object progressLock,
        CancellationToken ct)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxThreadGenerationAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _threadGenerator.ResetThreadForRetry(thread, emailCount);

                return await GenerateSingleThreadForStorylineAsync(
                    storyline,
                    thread,
                    participants,
                    participantLookup,
                    domain,
                    emailCount,
                    startDate,
                    endDate,
                    config,
                    domainThemes,
                    systemPrompt,
                    characterList,
                    ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (!IsContractViolation(ex))
            {
                lastException = ex;
            }
        }

        var message = lastException?.Message ?? "Unknown error";
        var errorLine = $"Thread generation failed after {MaxThreadGenerationAttempts} attempts for beat '{beatName}' (thread {thread.Id}): {lastException?.GetType().Name ?? "Error"} - {message}";
        lock (progressLock)
        {
            result.Errors.Add(errorLine);
            if (lastException?.InnerException != null)
            {
                result.Errors.Add($"  Inner error: {lastException.InnerException.Message}");
            }
        }

        _threadGenerator.ResetThreadForRetry(thread, emailCount);
        return null;
    }

    private static bool IsContractViolation(Exception ex)
    {
        if (ex is ArgumentException)
            return true;

        if (ex is InvalidOperationException && ex.Message.Contains("placeholder count", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    protected virtual Task<ThreadApiResponse?> GetThreadResponseAsync(
        string systemPrompt,
        string userPrompt,
        string operationName,
        CancellationToken ct)
        => _openAI.GetJsonCompletionAsync<ThreadApiResponse>(systemPrompt, userPrompt, operationName, ct);

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

        return @"You are generating realistic corporate email/message threads for a fictional eDiscovery dataset.
These should read like authentic workplace communications between the provided characters.

CORE RULES (NON-NEGOTIABLE)
- Entirely fictional: do NOT use real company names, real people, real domains, or identifiable real-world incidents.
- Use only the provided characters and their organizations/domains.
- Workplace realism: mix routine work with tension; allow minor typos, shorthand (FYI/pls), and imperfect memory.
- eDiscovery usefulness: include ambiguity, conflicting statements, red herrings, and misunderstandings. Not every clue is conclusive.
- Optional hooks when plausible: privilege/confidentiality/policy/retention/spoliation hints. Keep them non-instructional.
- No explicit sexual content, graphic violence, hate/harassment/slurs, or self-harm. Keep HR/retaliation professional and non-explicit.

EMOTIONAL AUTHENTICITY:
- People in conflict should show it (passive-aggressive, curt, defensive, frustrated).
- Allies share context and vent; rivals clash.
- Keep it workplace-appropriate, not abusive.

COMMUNICATION STYLE:
1. Vary email length (short replies, longer explanations, occasional rants).
2. Use realistic formatting (bullets, numbered lists, action items, emphasis with *asterisks*).
3. Match tone to relationship and role. Do not make everyone polite all the time.
4. Use each character's personality notes and communication style to shape their voice.

ATTACHMENTS - INTEGRATE NATURALLY INTO EMAIL CONTENT:
When an email has an attachment, the body MUST reference it naturally:
- Documents: ""Attached is the Q2 forecast"" or ""See the attached spreadsheet for...""
- Images: ""Attached photo from the site walk"" or ""Screenshot of the error dialog""
- INLINE images: mention what is shown in the body copy

Be specific in the attachment fields:
- documentDescription: What the document contains
- imageDescription: What the image shows

TECHNICAL RULES:
1. Each email must logically follow the previous one.
2. Reference previous emails naturally in replies (the system adds quoted text).
3. ALWAYS include the sender's signature block at the end of each email body.
4. Signature blocks must be used EXACTLY as provided.
5. DO NOT include quoted previous emails in bodyPlain.
6. IDENTITY RULE: The person in fromEmail IS the person writing the email. The greeting, body text, and signature MUST all be written as that person.

THREAD STRUCTURE:
For longer threads (5+ emails), include branching and side conversations:
- Side conversations to an ally
- Forwards to bring in new people (FYI/See below)
- Replies that go to only one person

Use replyToIndex to specify which email is being replied to or forwarded (0-based within the batch).

Respond with valid JSON only.";
    }

    /// <summary>
    /// Build a narrative context summary from previous emails for batch continuity
    /// </summary>
    private static string BuildNarrativeContext(List<EmailMessage> previousMessages)
    {
        // Summarize the last few emails to give the AI context
        var recentMessages = previousMessages.TakeLast(5).ToList();
        var summaries = recentMessages.Select(m =>
        {
            // Take first 150 chars of body (before any quoted content)
            var bodyPreview = m.BodyPlain;
            var quoteIndex = bodyPreview.IndexOf("\n> ", StringComparison.Ordinal);
            if (quoteIndex > 0) bodyPreview = bodyPreview[..quoteIndex];
            var forwardIndex = bodyPreview.IndexOf("---------- Forwarded", StringComparison.Ordinal);
            if (forwardIndex > 0) bodyPreview = bodyPreview[..forwardIndex];
            if (bodyPreview.Length > 150) bodyPreview = bodyPreview[..150] + "...";

            return $"  - {m.From.FullName} → {string.Join(", ", m.To.Select(t => t.FirstName))}: {bodyPreview.Replace("\n", " ").Trim()}";
        });

        return $@"PREVIOUS EMAILS IN THIS THREAD (continue from here - use replyToIndex relative to THIS batch, starting at 0):
The thread subject is already established. Here's what happened so far ({previousMessages.Count} emails total):
{string.Join("\n", summaries)}

IMPORTANT: Continue the narrative naturally from where it left off. Reference events from the previous emails.
Your replyToIndex values should be 0-based within THIS batch (0 = first email in this batch).
The first email in this batch should be a reply or forward continuing the conversation.";
    }

    /// <summary>
    /// Distribute attachment counts for a batch proportionally
    /// </summary>
    private static int DistributeAttachmentsForBatch(int totalAttachments, ref int assigned, int batchSize, int remaining, bool isLastBatch)
    {
        if (totalAttachments <= 0 || assigned >= totalAttachments)
            return 0;

        if (isLastBatch)
        {
            // Last batch gets whatever is left
            var left = totalAttachments - assigned;
            assigned += left;
            return left;
        }

        // Proportional distribution
        var batchShare = (int)Math.Round((double)(totalAttachments - assigned) * batchSize / remaining);
        batchShare = Math.Min(batchShare, totalAttachments - assigned);
        batchShare = Math.Min(batchShare, batchSize); // Can't have more attachments than emails
        assigned += batchShare;
        return batchShare;
    }

    /// <summary>
    /// Build attachment instructions for a specific batch
    /// </summary>
    private static string BuildBatchAttachmentInstructions(
        GenerationConfig config,
        int docCount,
        int imageCount,
        int voicemailCount,
        bool isResponsiveThread)
    {
        var instructions = new List<string>();
        var limits = new List<string>();
        var relevanceLabel = isResponsiveThread ? "storyline" : "thread topic (not the storyline)";

        if (docCount > 0 && config.EnabledAttachmentTypes.Count > 0)
        {
            var types = string.Join(", ", config.EnabledAttachmentTypes.Select(t => t.ToString().ToLower()));

            instructions.Add($@"DOCUMENT ATTACHMENTS: Include EXACTLY {docCount} email(s) with document attachments (no more, no less).
  - Available types: {types}
  - Make documents relevant to the {relevanceLabel} (reports, spreadsheets, presentations)
  - The email body MUST reference the attachment naturally
  - Provide a detailed documentDescription that explains what the document contains
  - Keep all names and organizations fictional");
            limits.Add($"documents: {docCount}");
        }

        if (imageCount > 0)
        {

            instructions.Add($@"IMAGE ATTACHMENTS - MANDATORY: You MUST set hasImage: true for EXACTLY {imageCount} email(s).
  - This is REQUIRED, not optional. Set hasImage: true for {imageCount} emails.
  - Images should be photos, screenshots, or visual evidence relevant to the {relevanceLabel}
  - MAKE MOST IMAGES INLINE (isImageInline: true) - the reader sees the image embedded in the email
  - Provide a VIVID, DETAILED imageDescription that can be used to generate the image
  - Example: 'A candid photo from the office charity event showing the project team next to a banner with their fictional company name'");
            limits.Add($"images: {imageCount}");
        }

        if (voicemailCount > 0)
        {

            instructions.Add($@"VOICEMAIL ATTACHMENTS: Include EXACTLY {voicemailCount} email(s) with voicemail attachments (no more, no less).
  - Voicemails are audio messages that complement or precede the email
  - Great for urgent situations, follow-ups
  - Provide voicemailContext describing what the voice message is about (aligned to the {relevanceLabel})
  - Keep all names and organizations fictional");
            limits.Add($"voicemails: {voicemailCount}");
        }

        if (instructions.Count == 0)
        {
            return "ATTACHMENTS: Do NOT include any attachments in this batch. Set all hasDocument, hasImage, hasVoicemail to false.";
        }

        var limitsStr = string.Join(", ", limits);
        return $"ATTACHMENT LIMITS - STRICT COUNT: {limitsStr}\n" +
               "DO NOT exceed these counts. Most emails should have NO attachments.\n\n" +
               string.Join("\n\n", instructions);
    }

    /// <summary>
    /// Build the user prompt for a specific batch
    /// </summary>
    private string BuildBatchUserPrompt(
        Storyline storyline, EmailThread thread, string characterList,
        DateTime batchStartDate, DateTime batchEndDate,
        int batchSize, string attachmentInstructions,
        string narrativeContext, string narrativePhase,
        bool isFirstBatch, string? threadSubject)
    {
        if (!isFirstBatch && string.IsNullOrWhiteSpace(threadSubject))
            throw new InvalidOperationException("Thread subject was not established before generating a follow-up batch.");

        var subjectInstruction = isFirstBatch
            ? @"""subject"": ""string (original email subject, not including RE: or FW:)"""
            : $@"""subject"": ""{threadSubject}"" (MUST use this exact subject)";

        var firstEmailNote = isFirstBatch
            ? "- First email should NOT be a reply (replyToIndex: -1 or omit)"
            : "- First email in this batch should continue the thread (reply or forward)";

        var isResponsive = thread.Relevance == EmailThread.ThreadRelevance.Responsive || thread.IsHot;
        var storyBeatContext = isResponsive
            ? BuildStoryBeatContext(storyline, batchStartDate, batchEndDate)
            : BuildNonResponsiveContext();
        var storylineHeader = isResponsive
            ? $"Storyline: {storyline.Title}\nSummary: {storyline.Summary}"
            : "Thread Intent: NON-RESPONSIVE (generic corporate thread unrelated to the storyline).";
        var narrativeLabel = isResponsive
            ? narrativePhase
            : $"NON-RESPONSIVE THREAD - {narrativePhase}";
        var generationScope = isResponsive
            ? $"Generate EXACTLY {batchSize} emails for this storyline."
            : $"Generate EXACTLY {batchSize} emails for this thread.";
        var continuityNote = isResponsive
            ? "The emails must be entirely fictional and remain consistent with the storyline context."
            : "The emails must be entirely fictional and must NOT be tied to the storyline context or story beats.";
        var relevanceRequirement = isResponsive
            ? (thread.IsHot
                ? "- Follow the story beats closely; treat this as a primary signal thread."
                : "- Align with the story beats but include mixed ambiguity and plausible false positives.")
            : "- This thread must be unrelated to the story beats and storyline summary; any overlap should be accidental.";
        var disputeRequirement = isResponsive
            ? "- Do NOT fully resolve the core dispute; keep open questions or pending decisions."
            : "- Avoid referencing any storyline dispute; keep tensions mundane and unrelated.";

        return $@"{storylineHeader}

NARRATIVE PHASE: {narrativeLabel}

Available Characters:
{characterList}

Date Range: {batchStartDate:yyyy-MM-dd} to {batchEndDate:yyyy-MM-dd}

{storyBeatContext}

{narrativeContext}

{generationScope}
{continuityNote}

CONTENT REQUIREMENTS:
- Use only the listed characters and their organizations/domains.
- Mix routine work with tension and stress.
- Include ambiguity and at least one misunderstanding or conflicting statement.
{relevanceRequirement}
{disputeRequirement}
- Include at least ONE email where someone is clearly frustrated, upset, or defensive.
- Keep tone workplace-appropriate (no slurs or explicit content).

STYLE REQUIREMENTS:
- Vary lengths: short replies, longer explanations, occasional rants.
- Use realistic formatting: bullets, numbered lists, action items, *emphasis*, occasional ALL CAPS.
- Reflect relationships: allies are warmer, rivals are curt or passive-aggressive.

{attachmentInstructions}

For threads with 5+ emails, include at least ONE of these realistic patterns:
- A private side conversation (someone forwards to an ally asking for their take)
- Someone brings in another character via forward
- A reply that only goes to one person instead of the whole group
- Someone who was CC'd jumps into the conversation

Respond with JSON in this exact format:
{{
  {subjectInstruction},
  ""emails"": [
    {{
      ""fromEmail"": ""string (must be one of the character emails)"",
      ""toEmails"": [""string""],
      ""ccEmails"": [""string""] (optional, can be empty array),
      ""sentDateTime"": ""ISO 8601 format"",
      ""bodyPlain"": ""string (full email body including greeting and signature)"",
      ""isReply"": boolean,
      ""isForward"": boolean,
      ""replyToIndex"": number (0-based index WITHIN THIS BATCH of which email this replies to/forwards; use -1 for first email),
      ""hasDocument"": boolean (true if this email has a document attachment),
      ""documentType"": ""word"" | ""excel"" | ""powerpoint"" (only if hasDocument is true),
      ""documentDescription"": ""string describing document content (only if hasDocument is true)"",
      ""hasImage"": boolean (true if this email includes an image),
      ""imageDescription"": ""string describing what the image shows (only if hasImage is true)"",
      ""isImageInline"": boolean (true = image embedded in email body, false = regular attachment),
      ""hasVoicemail"": boolean (true if a voicemail accompanies this email),
      ""voicemailContext"": ""string describing voicemail context (only if hasVoicemail is true)""
    }}
  ]
}}

CRITICAL RULES:
- Generate EXACTLY {batchSize} emails
- All email addresses must match exactly one of the characters listed above
- Dates must be within the specified range and in chronological order
{firstEmailNote}
- Use replyToIndex to create branching within this batch
- Forwards bring new people into the conversation
- DO NOT include '> ' quoted text or 'On [date] wrote:' sections - the system adds those automatically
- Write only the NEW content the sender is adding (their message + signature)
- When an email has an attachment, the body MUST reference it naturally

EMAIL BODY FORMAT - IMPORTANT:
- The bodyPlain field should contain ONLY the email message content
- NEVER start bodyPlain with 'Subject:', 'RE:', 'FW:', or any header-like text
- The subject is a SEPARATE field - do not repeat it in the body
- bodyPlain should start with a greeting or jump straight into the message

SIGNATURE BLOCK RULES - EXTREMELY IMPORTANT:
- The signature at the end of bodyPlain MUST belong to the person in fromEmail
- NEVER put one character's signature on another character's email
- Copy the EXACT signature block for that character from the list above - character by character
- This is a DATA INTEGRITY issue - wrong signatures make the emails invalid

ATTACHMENT REMINDER:
- If the instructions say to include N images, you MUST set hasImage: true for exactly N emails
- If the instructions say to include N documents, you MUST set hasDocument: true for exactly N emails
- If the instructions say to include N voicemails, you MUST set hasVoicemail: true for exactly N emails
- Do NOT skip attachments - they are REQUIRED if specified in the instructions above";
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

    private static string BuildNonResponsiveContext()
    {
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

    /// <summary>
    /// Convert an EmailDto from the API response into an EmailMessage, handling threading and quoted content
    /// </summary>
    private EmailMessage ConvertDtoToEmailMessage(
        EmailDto e, EmailThread thread, string threadSubject, EmailMessage target, List<Character> participants,
        Dictionary<string, Character> participantLookup,
        Dictionary<string, OrganizationTheme> domainThemes,
        DateTime startDate, DateTime endDate, ref int sequence, int batchOffset)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        var fromChar = ResolveFromCharacter(e, participants, participantLookup);
        var toChars = ResolveRecipients(e.ToEmails, participantLookup);
        if (toChars.Count == 0)
        {
            toChars = ResolveFallbackRecipients(participants, fromChar);
        }

        var ccChars = e.CcEmails != null
            ? ResolveRecipients(e.CcEmails, participantLookup)
            : new List<Character>();

        var subject = ResolveSubject(threadSubject, e, sequence);
        var sentDate = ResolveSentDate(e, startDate, endDate);
        var fullBody = BuildEmailBody(e, thread, fromChar, participants, sequence, batchOffset);

        var senderDomain = fromChar.Domain;
        domainThemes.TryGetValue(senderDomain, out var senderTheme);

        target.EmailThreadId = thread.Id;
        target.StoryBeatId = thread.StoryBeatId;
        target.StorylineId = thread.StorylineId;
        target.From = fromChar;
        target.To = toChars;
        target.Cc = ccChars;
        target.Subject = subject;
        target.BodyPlain = fullBody;
        target.BodyHtml = HtmlEmailFormatter.ConvertToHtml(fullBody, senderTheme);
        target.SentDate = sentDate;
        target.SequenceInThread = sequence++;
        target.PlannedHasDocument = e.HasDocument;
        target.PlannedDocumentType = e.DocumentType;
        target.PlannedDocumentDescription = e.DocumentDescription;
        target.PlannedHasImage = e.HasImage;
        target.PlannedImageDescription = e.ImageDescription;
        target.PlannedIsImageInline = e.IsImageInline;
        target.PlannedHasVoicemail = e.HasVoicemail;
        target.PlannedVoicemailContext = e.VoicemailContext;

        return target;
    }

    private Character ResolveFromCharacter(
        EmailDto e,
        List<Character> participants,
        Dictionary<string, Character> participantLookup)
    {
        if (participantLookup.TryGetValue(e.FromEmail, out var fromChar))
            return fromChar;

        return participants[_random.Next(participants.Count)];
    }

    private static List<Character> ResolveRecipients(
        IEnumerable<string> emailAddresses,
        Dictionary<string, Character> participantLookup)
    {
        var recipients = new List<Character>();
        foreach (var emailAddress in emailAddresses)
        {
            if (participantLookup.TryGetValue(emailAddress, out var recipient))
            {
                recipients.Add(recipient);
            }
        }

        return recipients;
    }

    private static List<Character> ResolveFallbackRecipients(List<Character> participants, Character fromChar)
    {
        var fallback = participants.FirstOrDefault(c => c.Id != fromChar.Id) ?? fromChar;
        return new List<Character> { fallback };
    }

    private static string ResolveSubject(string threadSubject, EmailDto e, int sequence)
    {
        if (e.IsForward)
            return ThreadingHelper.AddForwardPrefix(threadSubject);
        if (e.IsReply && sequence > 0)
            return ThreadingHelper.AddReplyPrefix(threadSubject);
        return threadSubject;
    }

    private static DateTime ResolveSentDate(EmailDto e, DateTime startDate, DateTime endDate)
    {
        return DateTime.TryParse(e.SentDateTime, out var sentDate)
            ? sentDate
            : DateHelper.RandomDateInRange(startDate, endDate);
    }

    private static string BuildEmailBody(
        EmailDto e,
        EmailThread thread,
        Character fromChar,
        List<Character> participants,
        int sequence,
        int batchOffset)
    {
        // Fix sender/signature mismatch: ensure the body's signature matches the fromEmail character
        var correctedBody = CorrectSignatureBlock(e.BodyPlain, fromChar, participants);
        var fullBody = correctedBody;

        var referencedEmail = ResolveReferencedEmail(thread, e, sequence, batchOffset);
        if (referencedEmail == null)
            return fullBody;

        if (ShouldQuoteAsReply(e, sequence, referencedEmail))
        {
            fullBody += ThreadingHelper.FormatQuotedReply(referencedEmail);
        }
        else if (e.IsForward)
        {
            fullBody += ThreadingHelper.FormatForwardedContent(referencedEmail);
        }

        return fullBody;
    }

    private static EmailMessage? ResolveReferencedEmail(
        EmailThread thread,
        EmailDto e,
        int sequence,
        int batchOffset)
    {
        if (e.ReplyToIndex >= 0)
        {
            var globalIndex = batchOffset + e.ReplyToIndex;
            if (globalIndex >= 0 && globalIndex < sequence)
            {
                return thread.EmailMessages[globalIndex];
            }
        }

        return sequence > 0 ? thread.EmailMessages[sequence - 1] : null;
    }

    private static bool ShouldQuoteAsReply(EmailDto e, int sequence, EmailMessage? referencedEmail)
    {
        if (e.IsReply)
            return true;

        return !e.IsForward && sequence > 0 && referencedEmail != null;
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
            if (otherChar.Id == fromChar.Id || string.IsNullOrWhiteSpace(otherChar.SignatureBlock))
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

    private List<EmailMessage> SelectEmailsForAttachments(List<EmailMessage> emails, int percentage)
    {
        if (percentage <= 0) return new List<EmailMessage>();

        var count = Math.Max(1, (int)Math.Round(emails.Count * percentage / 100.0));
        return emails.OrderBy(_ => _random.Next()).Take(count).ToList();
    }

    /// <summary>
    /// Generate a document attachment based on the AI-planned description
    /// </summary>
    private async Task GeneratePlannedDocumentAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        if (!email.PlannedHasDocument || string.IsNullOrEmpty(email.PlannedDocumentType))
            return;

        if (!TryResolvePlannedAttachmentType(email.PlannedDocumentType, state.Config, out var attachmentType))
            return;

        var isDetailed = state.Config.AttachmentComplexity == AttachmentComplexity.Detailed;

        // Use the planned description as context
        var context = $@"Email subject: {email.Subject}
Document purpose (from email): {email.PlannedDocumentDescription ?? "Supporting document for this email"}
Email body preview: {email.BodyPlain[..Math.Min(300, email.BodyPlain.Length)]}...";

        var (chainState, reservedVersion) = TryReserveDocumentChain(state, attachmentType);
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
            ct,
            chainState);
        if (attachment == null)
            return;

        ApplyDocumentChainVersioning(attachment, chainState, reservedVersion, state);

        if (string.IsNullOrEmpty(attachment.FileName))
        {
            attachment.FileName = FileNameHelper.GenerateAttachmentFileName(attachment, email);
        }

        email.Attachments.Add(attachment);
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

    private (DocumentChainState? chainState, int? reservedVersion) TryReserveDocumentChain(
        WizardState state,
        AttachmentType attachmentType)
    {
        if (!state.Config.EnableAttachmentChains || _documentChains.Count == 0 || _random.Next(100) >= 30)
            return (null, null);

        var matchingChains = _documentChains.Values
            .Where(c => c.Type == attachmentType)
            .ToList();
        if (matchingChains.Count == 0)
            return (null, null);

        var chainState = matchingChains[_random.Next(matchingChains.Count)];
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
        CancellationToken ct,
        DocumentChainState? chainState)
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
        WizardState state)
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

        if (state.Config.EnableAttachmentChains && attachment.Type == AttachmentType.Word && _random.Next(100) < 50)
        {
            // Start a new chain for Word documents
            var newChain = new DocumentChainState
            {
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
    private async Task GeneratePlannedImageAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        if (!email.PlannedHasImage || string.IsNullOrEmpty(email.PlannedImageDescription))
            return;

        // Generate the image using DALL-E with the planned description
        var imagePrompt = BuildPlannedImagePrompt(state.Topic, email.PlannedImageDescription);

        var imageBytes = await _openAI.GenerateImageAsync(imagePrompt, "Image Generation", ct);

        if (imageBytes == null || imageBytes.Length == 0)
            return;

        var contentId = $"img_{Guid.NewGuid():N}";
        var attachment = BuildPlannedImageAttachment(email, imageBytes, contentId);

        email.Attachments.Add(attachment);

        InsertInlineImageIfNeeded(email, contentId);
    }

    private static string BuildPlannedImagePrompt(string topic, string plannedDescription)
    {
        return $"A vivid, realistic image in the style/universe of {topic}: {plannedDescription}. High quality, detailed.";
    }

    private static Attachment BuildPlannedImageAttachment(EmailMessage email, byte[] imageBytes, string contentId)
    {
        return new Attachment
        {
            Type = AttachmentType.Image,
            Content = imageBytes,
            ContentDescription = email.PlannedImageDescription,
            IsInline = email.PlannedIsImageInline,
            ContentId = contentId,
            FileName = $"image_{email.SentDate:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..6]}.png"
        };
    }

    private static void InsertInlineImageIfNeeded(EmailMessage email, string contentId)
    {
        // If inline, update the HTML body to include the image in the main content (before quoted text)
        if (!email.PlannedIsImageInline || string.IsNullOrEmpty(email.BodyHtml))
            return;

        var caption = email.PlannedImageDescription.Length > 100
            ? email.PlannedImageDescription[..100] + "..."
            : email.PlannedImageDescription;

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
            if (email.BodyHtml.Contains("</div>\n</body>"))
            {
                // Insert before closing email-body div
                email.BodyHtml = email.BodyHtml.Replace("</div>\n</body>", imageHtml + "</div>\n</body>");
            }
            else if (email.BodyHtml.Contains("</body>"))
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
    private async Task GeneratePlannedVoicemailAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        if (!email.PlannedHasVoicemail)
            return;

        // Generate a voicemail script using the planned context

        var systemPrompt = @"You are creating a voicemail message that relates to a fictional corporate email.
The voicemail should sound natural and conversational, as if someone called and left a message.
Keep the voicemail BRIEF - 15-30 seconds when spoken (about 40-80 words).
Do not use real company names or real people.

Respond with JSON only.";

        var context = $@"Email subject: {email.Subject}
Sender: {email.From.FullName}
Voicemail context: {email.PlannedVoicemailContext ?? "A follow-up or urgent message related to the email"}
Narrative topic: {state.Topic}";


        var userPrompt = $@"{context}

Create a voicemail that {email.From.FirstName} might leave related to this email.
The voicemail should:
- Sound natural and conversational (include 'um', 'uh', pauses indicated by '...')
- Reference the email topic with appropriate urgency
- Start with a greeting ('Hey, it's [name]...' or 'Hi, this is [name] calling about...')
- End naturally ('...call me back when you get this' or 'talk soon')
- Be 40-80 words total
- Keep all names and organizations fictional

Respond with JSON:
{{
  ""voicemailScript"": ""string (the voicemail transcript)""
}}";

        var response = await _openAI.GetJsonCompletionAsync<VoicemailScriptResponse>(systemPrompt, userPrompt, "Voicemail Script", ct);

        if (response == null || string.IsNullOrEmpty(response.VoicemailScript))
            return;

        // Generate the audio using TTS
        var audioBytes = await _openAI.GenerateSpeechAsync(
            response.VoicemailScript,
            email.From.VoiceId,
            "Voicemail TTS",
            ct);

        if (audioBytes == null || audioBytes.Length == 0)
            return;

        var attachment = new Attachment
        {
            Type = AttachmentType.Voicemail,
            Content = audioBytes,
            ContentDescription = $"Voicemail from {email.From.FullName}",
            VoiceId = email.From.VoiceId,
            FileName = BuildVoicemailFileName(email.From.LastName, email.SentDate)
        };

        email.Attachments.Add(attachment);
    }

    private async Task GenerateAttachmentAsync(EmailMessage email, WizardState state, CancellationToken ct)
    {
        var enabledTypes = state.Config.EnabledAttachmentTypes;
        if (enabledTypes.Count == 0) return;

        var attachmentType = enabledTypes[_random.Next(enabledTypes.Count)];
        var isDetailed = state.Config.AttachmentComplexity == AttachmentComplexity.Detailed;

        var (chainState, reservedVersion) = TryReserveDocumentChain(state, attachmentType);
        var context = BuildAttachmentContext(email, chainState, reservedVersion);

        var attachment = await GeneratePlannedDocumentAttachmentAsync(
            attachmentType,
            context,
            email,
            isDetailed,
            state,
            ct,
            chainState);
        if (attachment == null)
            return;

        ApplyDocumentChainVersioning(attachment, chainState, reservedVersion, state);

        if (string.IsNullOrEmpty(attachment.FileName))
        {
            attachment.FileName = FileNameHelper.GenerateAttachmentFileName(attachment, email);
        }
        email.Attachments.Add(attachment);
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

    private async Task<Attachment> GenerateWordAttachmentAsync(
        string context, EmailMessage email, bool detailed, WizardState state, CancellationToken ct, DocumentChainState? chainState = null)
    {

        var systemPrompt = @"Generate content for a fictional corporate Word document attachment.
The content should be realistic, workplace-appropriate, and related to the email context.
Do not use real company names or real people.
Respond with valid JSON only.";

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


        var userPrompt = $@"Context:
{context}
{versionNote}
{detailLevel}

Use only fictional names and organizations. If names are needed, derive them from the email context.

Respond with JSON:
{{
  ""title"": ""string (document title)"",
  ""content"": ""string (full document content, paragraphs separated by double newlines)""
}}";

        var response = await _openAI.GetJsonCompletionAsync<WordDocResponse>(systemPrompt, userPrompt, "Word Attachment", ct);

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

        var systemPrompt = @"Generate data for a fictional corporate Excel spreadsheet attachment.
The data should be realistic and related to the email context.
Do not use real company names or real people.
IMPORTANT: All values in the rows array MUST be strings, even if they represent numbers.
For example: use ""1234"" instead of 1234, use ""$5,000"" instead of 5000.
Respond with valid JSON only.";

        var rowCount = detailed ? "10-15" : "5-8";


        var userPrompt = $@"Context:
{context}

Generate spreadsheet data with:
- Appropriate column headers (3-6 columns)
- {rowCount} rows of realistic data
- Format numeric values as strings (e.g., ""$1,234"", ""500"", ""12.5%"")
- Use only fictional names and organizations

Respond with JSON:
{{
  ""title"": ""string (spreadsheet title)"",
  ""headers"": [""string""],
  ""rows"": [[""string (ALL values must be strings, even numbers)""]]
}}

CRITICAL: Every cell value in rows must be a JSON string, not a number. Use quotes around all values.";

        var response = await _openAI.GetJsonCompletionAsync<ExcelDocResponseRaw>(systemPrompt, userPrompt, "Excel Attachment", ct);

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

        var systemPrompt = @"Generate content for a fictional corporate PowerPoint presentation attachment.
The content should be realistic and related to the email context.
Do not use real company names or real people.
Respond with valid JSON only.";

        var slideCount = detailed ? "5-8" : "3-4";


        var userPrompt = $@"Context:
{context}

Generate presentation content with:
- A main title
- {slideCount} content slides
- Each slide should have a title and bullet points or brief content
- Use only fictional names and organizations

Respond with JSON:
{{
  ""title"": ""string (presentation title)"",
  ""slides"": [
    {{
      ""slideTitle"": ""string"",
      ""content"": ""string (bullet points or paragraph)""
    }}
  ]
}}";

        var response = await _openAI.GetJsonCompletionAsync<PowerPointDocResponse>(systemPrompt, userPrompt, "PowerPoint Attachment", ct);

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

        var systemPrompt = @"You are helping generate an image for a fictional corporate email.
Based on the email content, suggest a single image that someone might attach or embed in this email.
The image should feel authentic to a workplace setting and relevant to the email's content.
Do not include real logos, real brands, or identifiable real people.

Respond with JSON only.";

        var context = $@"Email subject: {email.Subject}
Email body: {email.BodyPlain[..Math.Min(500, email.BodyPlain.Length)]}
Narrative topic: {state.Topic}";


        var userPrompt = $@"{context}

Suggest ONE image that would be realistic to include with this email. Consider:
- Photos someone might share ('Here's a picture from the event')
- Screenshots or diagrams being discussed
- Images that add context to the storyline
- Office-appropriate visuals only (no sensitive or explicit content)

Respond with JSON:
{{
  ""shouldIncludeImage"": boolean (false if no image makes sense for this email),
  ""imageDescription"": ""string (detailed description for image generation, 2-3 sentences)"",
  ""imageContext"": ""string (brief caption or how it's referenced in email, e.g., 'Attached: Photo from the banquet')"",
  ""isInline"": boolean (true if image should display in email body, false for attachment)
}}";

        var response = await _openAI.GetJsonCompletionAsync<ImageSuggestionResponse>(systemPrompt, userPrompt, "Image Suggestion", ct);

        if (response == null || !response.ShouldIncludeImage || string.IsNullOrEmpty(response.ImageDescription))
            return;

        // Generate the image using DALL-E
        // Craft a safe, descriptive prompt

        var imagePrompt = $"A realistic, fictional corporate image inspired by the narrative topic \"{state.Topic}\": {response.ImageDescription}. No real brands, logos, or identifiable people. High quality, photorealistic where appropriate.";

        var imageBytes = await _openAI.GenerateImageAsync(imagePrompt, "Image Generation", ct);

        if (imageBytes == null || imageBytes.Length == 0)
            return;

        var contentId = $"img_{Guid.NewGuid():N}";
        var attachment = new Attachment
        {
            Type = AttachmentType.Image,
            Content = imageBytes,
            ContentDescription = response.ImageContext ?? "Attached image",
            IsInline = response.IsInline,
            ContentId = contentId,
            FileName = $"image_{email.SentDate:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..6]}.png"
        };

        email.Attachments.Add(attachment);

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
            if (email.BodyHtml.Contains("</body>"))
            {
                email.BodyHtml = email.BodyHtml.Replace("</body>", imageHtml + "</body>");
            }
            else
            {
                email.BodyHtml += imageHtml;
            }
        }
    }

    private class ImageSuggestionResponse
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

        var systemPrompt = @"You analyze emails to detect if they are scheduling or confirming a meeting/event that should have a calendar invite attached.

Look for:
- Specific dates and times mentioned ('tomorrow at 3pm', 'Friday at noon', 'next week Monday')
- Meeting requests or confirmations
- Event invitations
- Scheduled calls or gatherings

If there is no clear meeting date/time, set hasMeeting to false.
Respond with JSON only.";


        var userPrompt = $@"Email subject: {email.Subject}
Email body: {email.BodyPlain[..Math.Min(800, email.BodyPlain.Length)]}
Email sent date: {email.SentDate:yyyy-MM-dd}

Does this email mention a specific meeting, event, or call that should have a calendar invite?
If details are vague or missing, set hasMeeting to false.

Respond with JSON:
{{
  ""hasMeeting"": boolean,
  ""meetingTitle"": ""string (title for the calendar invite)"",
  ""meetingDescription"": ""string (brief description)"",
  ""location"": ""string (meeting location or 'Virtual' or 'TBD')"",
  ""suggestedDate"": ""YYYY-MM-DD (the date of the meeting, based on context)"",
  ""suggestedStartTime"": ""HH:MM (24-hour format)"",
  ""durationMinutes"": number (30, 60, 90, 120, etc.)
}}";

        var response = await _openAI.GetJsonCompletionAsync<MeetingDetectionResponse>(systemPrompt, userPrompt, "Meeting Detection", ct);

        if (response == null || !response.HasMeeting)
            return;

        // Parse the meeting date and time
        if (!DateTime.TryParse(response.SuggestedDate, out var meetingDate))
        {
            meetingDate = email.SentDate.AddDays(1); // Default to next day
        }

        var timeParts = (response.SuggestedStartTime ?? "10:00").Split(':');
        var hour = int.TryParse(timeParts[0], out var h) ? h : 10;
        var minute = timeParts.Length > 1 && int.TryParse(timeParts[1], out var m) ? m : 0;

        var startTime = new DateTime(meetingDate.Year, meetingDate.Month, meetingDate.Day, hour, minute, 0);
        var endTime = startTime.AddMinutes(response.DurationMinutes > 0 ? response.DurationMinutes : 60);

        // Get attendees from the email recipients
        var attendees = email.To
            .Concat(email.Cc)
            .Where(c => c.Email != email.From.Email)
            .Select(c => (c.FullName, c.Email))
            .ToList();

        var icsContent = _calendarService.CreateCalendarInvite(
            response.MeetingTitle ?? email.Subject,
            response.MeetingDescription ?? "",
            startTime,
            endTime,
            response.Location ?? "TBD",
            email.From.FullName,
            email.From.Email,
            attendees);

        var attachment = new Attachment
        {
            Type = AttachmentType.CalendarInvite,
            Content = icsContent,
            ContentDescription = response.MeetingTitle ?? "Meeting Invite",
            FileName = $"invite_{startTime:yyyyMMdd}_{Guid.NewGuid().ToString("N")[..6]}.ics"
        };

        email.Attachments.Add(attachment);
    }

    private class MeetingDetectionResponse
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

        var systemPrompt = @"You are creating a voicemail message that relates to a fictional corporate email thread.
The voicemail should sound natural and conversational, as if someone called and left a message.
It should relate to the email content but not simply read the email aloud.
Do not use real company names or real people.

Keep the voicemail BRIEF - 15-30 seconds when spoken (about 40-80 words).

Respond with JSON only.";

        var context = $@"Email subject: {email.Subject}
Email body preview: {email.BodyPlain[..Math.Min(400, email.BodyPlain.Length)]}
Sender: {email.From.FullName}
Narrative topic: {state.Topic}";


        var userPrompt = $@"{context}

Create a voicemail that {email.From.FirstName} might leave that relates to this email thread.
The voicemail should:
- Sound natural and conversational (include 'um', 'uh', pauses indicated by '...')
- Reference the email topic but add urgency or context
- Start with a greeting ('Hey, it's [name]...' or 'Hi, this is [name] calling about...')
- End naturally ('...call me back when you get this' or 'talk soon')
- Be 40-80 words total
- Keep all names and organizations fictional

Respond with JSON:
{{
  ""shouldCreateVoicemail"": boolean (false if voicemail doesn't make sense),
  ""voicemailScript"": ""string (the voicemail transcript)"",
  ""recipientName"": ""string (who the voicemail is for)""
}}";

        var response = await _openAI.GetJsonCompletionAsync<VoicemailScriptResponse>(systemPrompt, userPrompt, "Voicemail Script", ct);

        if (response == null || !response.ShouldCreateVoicemail || string.IsNullOrEmpty(response.VoicemailScript))
            return;

        // Generate the audio using TTS
        var audioBytes = await _openAI.GenerateSpeechAsync(
            response.VoicemailScript,
            email.From.VoiceId,
            "Voicemail TTS",
            ct);

        if (audioBytes == null || audioBytes.Length == 0)
            return;

        var attachment = new Attachment
        {
            Type = AttachmentType.Voicemail,
            Content = audioBytes,
            ContentDescription = $"Voicemail from {email.From.FullName}",
            VoiceId = email.From.VoiceId,
            FileName = BuildVoicemailFileName(email.From.LastName, email.SentDate)
        };

        email.Attachments.Add(attachment);
    }

    private class VoicemailScriptResponse
    {
        [JsonPropertyName("shouldCreateVoicemail")]
        public bool ShouldCreateVoicemail { get; set; }

        [JsonPropertyName("voicemailScript")]
        public string VoicemailScript { get; set; } = string.Empty;

        [JsonPropertyName("recipientName")]
        public string? RecipientName { get; set; }
    }

    // Response DTOs
    protected internal class ThreadApiResponse
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("emails")]
        public List<EmailDto> Emails { get; set; } = new();
    }

    protected internal class EmailDto
    {
        [JsonPropertyName("fromEmail")]
        public string FromEmail { get; set; } = string.Empty;

        [JsonPropertyName("toEmails")]
        public List<string> ToEmails { get; set; } = new();

        [JsonPropertyName("ccEmails")]
        public List<string>? CcEmails { get; set; }

        [JsonPropertyName("sentDateTime")]
        public string SentDateTime { get; set; } = string.Empty;

        [JsonPropertyName("bodyPlain")]
        public string BodyPlain { get; set; } = string.Empty;

        [JsonPropertyName("isReply")]
        public bool IsReply { get; set; }

        [JsonPropertyName("isForward")]
        public bool IsForward { get; set; }

        [JsonPropertyName("replyToIndex")]
        public int ReplyToIndex { get; set; } = -1;

        // Attachment planning fields - AI decides which emails get attachments
        [JsonPropertyName("hasDocument")]
        public bool HasDocument { get; set; }

        [JsonPropertyName("documentType")]
        public string? DocumentType { get; set; } // "word", "excel", "powerpoint"

        [JsonPropertyName("documentDescription")]
        public string? DocumentDescription { get; set; } // What the document is about

        [JsonPropertyName("hasImage")]
        public bool HasImage { get; set; }

        [JsonPropertyName("imageDescription")]
        public string? ImageDescription { get; set; } // What the image shows

        [JsonPropertyName("isImageInline")]
        public bool IsImageInline { get; set; } // true = inline in body, false = attachment

        [JsonPropertyName("hasVoicemail")]
        public bool HasVoicemail { get; set; }

        [JsonPropertyName("voicemailContext")]
        public string? VoicemailContext { get; set; } // Context for the voicemail
    }

    private class WordDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class ExcelDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        public List<List<string>> Rows { get; set; } = new();
    }

    // Raw response that handles mixed types (strings and numbers) in Excel rows
    private class ExcelDocResponseRaw
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        public List<string> Headers { get; set; } = new();

        [JsonPropertyName("rows")]
        public List<List<System.Text.Json.JsonElement>> Rows { get; set; } = new();
    }

    private class PowerPointDocResponse
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("slides")]
        public List<SlideDto> Slides { get; set; } = new();
    }

    private class SlideDto
    {
        [JsonPropertyName("slideTitle")]
        public string SlideTitle { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
