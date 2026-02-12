using System.Collections.Concurrent;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmailGeneratorRepairTests
{
    [Fact]
    public async Task GenerateThreadWithRetriesAsyncRepairsInvalidEmail()
    {
        var characters = BuildCharacters();
        var config = new GenerationConfig
        {
            AttachmentPercentage = 100,
            IncludeWord = true,
            IncludeExcel = false,
            IncludePowerPoint = false,
            IncludeImages = false,
            IncludeVoicemails = false,
            MaxEmailRepairAttempts = 1
        };

        var plan = BuildPlan(characters, config, emailCount: 1);
        var result = new GenerationResult();
        var context = BuildThreadPlanContext(plan, config, result);

        var responses = new[]
        {
            BuildSingleEmailResponse("Budget Review", characters[0], characters[1], hasDocument: false),
            BuildSingleEmailResponse("Budget Review", characters[0], characters[1], hasDocument: true)
        };

        var generator = new TestEmailGenerator(responses);

        var output = await generator.GenerateThreadWithRetriesAsync(plan, context, CancellationToken.None);

        Assert.NotNull(output);
        Assert.Single(output.EmailMessages);
        Assert.Equal(2, generator.CallCount);
        Assert.Equal(1, result.SucceededEmails);
        Assert.Equal(0, result.FailedEmails);
        Assert.False(output.EmailMessages[0].GenerationFailed);
    }

    [Fact]
    public async Task GenerateThreadWithRetriesAsyncContinuesAfterEmailFailure()
    {
        var characters = BuildCharacters();
        var config = new GenerationConfig
        {
            AttachmentPercentage = 0,
            IncludeWord = false,
            IncludeExcel = false,
            IncludePowerPoint = false,
            IncludeImages = false,
            IncludeVoicemails = false,
            MaxEmailRepairAttempts = 0
        };

        var plan = BuildPlan(characters, config, emailCount: 2);
        var result = new GenerationResult();
        var context = BuildThreadPlanContext(plan, config, result);

        var responses = new[]
        {
            BuildSingleEmailResponse("Status Update", characters[0], characters[1], hasDocument: false),
            BuildInvalidResponse("Status Update")
        };

        var generator = new TestEmailGenerator(responses);

        var output = await generator.GenerateThreadWithRetriesAsync(plan, context, CancellationToken.None);

        Assert.NotNull(output);
        Assert.Equal(2, output.EmailMessages.Count);
        Assert.Equal(2, generator.CallCount);
        Assert.Equal(1, result.SucceededEmails);
        Assert.Equal(1, result.FailedEmails);
        Assert.True(output.EmailMessages[1].GenerationFailed);
    }

    [Fact]
    public async Task GenerateThreadWithRetriesAsyncCarriesAttachmentsForwardAfterFailure()
    {
        var characters = BuildCharacters();
        var config = new GenerationConfig
        {
            AttachmentPercentage = 0,
            IncludeWord = true,
            IncludeExcel = false,
            IncludePowerPoint = false,
            IncludeImages = false,
            IncludeVoicemails = false,
            MaxEmailRepairAttempts = 0
        };

        var plan = BuildManualPlanWithAttachmentCarryover(characters);
        var result = new GenerationResult();
        var context = BuildThreadPlanContext(plan, config, result);

        var responses = new[]
        {
            BuildSingleEmailResponse("Budget Review", characters[0], characters[1], hasDocument: false, sentDateTime: new DateTime(2024, 1, 1, 9, 0, 0)),
            BuildSingleEmailResponse("Budget Review", characters[1], characters[0], hasDocument: false, isReply: true, sentDateTime: new DateTime(2024, 1, 1, 10, 0, 0)),
            BuildSingleEmailResponse("Budget Review", characters[0], characters[1], hasDocument: true, isReply: true, sentDateTime: new DateTime(2024, 1, 1, 11, 0, 0))
        };

        var generator = new TestEmailGenerator(responses);

        var output = await generator.GenerateThreadWithRetriesAsync(plan, context, CancellationToken.None);

        Assert.NotNull(output);
        Assert.Equal(3, output.EmailMessages.Count);
        Assert.Equal(3, generator.CallCount);
        Assert.Equal(2, result.SucceededEmails);
        Assert.Equal(1, result.FailedEmails);
        Assert.False(output.EmailMessages[0].GenerationFailed);
        Assert.True(output.EmailMessages[1].GenerationFailed);
        Assert.False(output.EmailMessages[2].GenerationFailed);
        Assert.True(output.EmailMessages[2].PlannedHasDocument);
    }

    private static List<Character> BuildCharacters()
    {
        return new List<Character>
        {
            new()
            {
                FirstName = "Alice",
                LastName = "Adams",
                Email = "alice@corp.com",
                SignatureBlock = "Regards,\nAlice"
            },
            new()
            {
                FirstName = "Bob",
                LastName = "Baker",
                Email = "bob@corp.com",
                SignatureBlock = "Thanks,\nBob"
            }
        };
    }

    private static EmailGenerator.ThreadPlan BuildPlan(List<Character> characters, GenerationConfig config, int emailCount)
    {
        var thread = new EmailThread
        {
            StoryBeatId = Guid.NewGuid(),
            StorylineId = Guid.NewGuid(),
            Topic = "Budget"
        };

        EmailThreadGenerator.EnsurePlaceholderMessages(thread, emailCount);

        var participantLookup = characters.ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);
        var structurePlan = ThreadStructurePlanner.BuildPlan(
            thread,
            emailCount,
            new DateTime(2024, 1, 1),
            new DateTime(2024, 1, 2),
            config,
            generationSeed: 12345);
        var threadSeed = DeterministicSeedHelper.CreateSeed(
            "thread-gen",
            "12345",
            thread.Id.ToString("N"));

        return new EmailGenerator.ThreadPlan(
            0,
            thread,
            emailCount,
            new DateTime(2024, 1, 1),
            new DateTime(2024, 1, 2),
            "Beat 1",
            characters,
            participantLookup,
            "characters",
            structurePlan,
            threadSeed);
    }

    private static EmailGenerator.ThreadPlan BuildManualPlanWithAttachmentCarryover(List<Character> characters)
    {
        var thread = new EmailThread
        {
            StoryBeatId = Guid.NewGuid(),
            StorylineId = Guid.NewGuid(),
            Topic = "Budget"
        };

        EmailThreadGenerator.EnsurePlaceholderMessages(thread, 3);

        var emailIds = thread.EmailMessages.Select(m => m.Id).ToList();
        var rootEmailId = emailIds[0];
        var branchId = DeterministicIdHelper.CreateGuid("email-branch", thread.Id.ToString("N"), "root");

        var slots = new List<ThreadEmailSlotPlan>
        {
            new(
                0,
                emailIds[0],
                null,
                rootEmailId,
                branchId,
                new DateTime(2024, 1, 1, 9, 0, 0),
                "BEGINNING",
                ThreadEmailIntent.New,
                new ThreadAttachmentPlan(false, null, false, false, false)),
            new(
                1,
                emailIds[1],
                emailIds[0],
                rootEmailId,
                branchId,
                new DateTime(2024, 1, 1, 10, 0, 0),
                "MIDDLE",
                ThreadEmailIntent.Reply,
                new ThreadAttachmentPlan(true, AttachmentType.Word, false, false, false)),
            new(
                2,
                emailIds[2],
                emailIds[1],
                rootEmailId,
                branchId,
                new DateTime(2024, 1, 1, 11, 0, 0),
                "LATE",
                ThreadEmailIntent.Reply,
                new ThreadAttachmentPlan(false, null, false, false, false))
        };

        var structurePlan = new ThreadStructurePlan(thread.Id, rootEmailId, slots);
        var participantLookup = characters.ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);
        var threadSeed = DeterministicSeedHelper.CreateSeed(
            "thread-gen",
            "12345",
            thread.Id.ToString("N"));

        return new EmailGenerator.ThreadPlan(
            0,
            thread,
            3,
            new DateTime(2024, 1, 1),
            new DateTime(2024, 1, 2),
            "Beat 1",
            characters,
            participantLookup,
            "characters",
            structurePlan,
            threadSeed);
    }

    private static EmailGenerator.ThreadPlanContext BuildThreadPlanContext(
        EmailGenerator.ThreadPlan plan,
        GenerationConfig config,
        GenerationResult result)
    {
        var storyline = new Storyline
        {
            Title = "Budget Review",
            Summary = "Quarterly budget review."
        };
        storyline.SetBeats(new List<StoryBeat>());

        var state = new WizardState { Config = config };
        return new EmailGenerator.ThreadPlanContext(
            storyline,
            "corp.com",
            config,
            new Dictionary<string, OrganizationTheme>(StringComparer.OrdinalIgnoreCase),
            "system",
            state,
            new Dictionary<Guid, EmailGenerator.CharacterContext>(),
            new Dictionary<Guid, EmailGenerator.CharacterRoutingContext>(),
            result,
            new GenerationProgress(),
            new Progress<GenerationProgress>(_ => { }),
            new object(),
            new EmlFileService(),
            new SemaphoreSlim(1, 1),
            new ConcurrentDictionary<Guid, bool>());
    }

    private static EmailGenerator.SingleEmailApiResponse BuildSingleEmailResponse(
        string subject,
        Character from,
        Character to,
        bool hasDocument,
        bool isReply = false,
        DateTime? sentDateTime = null)
    {
        return new EmailGenerator.SingleEmailApiResponse
        {
            BodyPlain = hasDocument
                ? $"Hello,\n\nAttached is the report.\n\n{from.SignatureBlock}"
                : $"Hello,\n\nQuick update below.\n\n{from.SignatureBlock}"
        };
    }

    private static EmailGenerator.SingleEmailApiResponse BuildInvalidResponse(string subject)
    {
        return new EmailGenerator.SingleEmailApiResponse
        {
            BodyPlain = string.Empty
        };
    }

    private sealed class TestEmailGenerator : EmailGenerator
    {
        private readonly Queue<EmailGenerator.SingleEmailApiResponse?> _responses;

        public int CallCount { get; private set; }

        public TestEmailGenerator(IEnumerable<EmailGenerator.SingleEmailApiResponse?> responses)
            : base(new OpenAIService("test-key", "gpt-4o-mini", new Random(1)), new Random(1))
        {
            _responses = new Queue<EmailGenerator.SingleEmailApiResponse?>(responses);
        }

        protected override Task<EmailGenerator.SingleEmailApiResponse?> GetEmailResponseAsync(
            string systemPrompt,
            string userPrompt,
            string operationName,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : null);
        }

        protected override Task<EmailGenerator.EmailSubjectResponse?> GetEmailSubjectResponseAsync(
            string systemPrompt,
            string userPrompt,
            string operationName,
            CancellationToken ct)
        {
            return Task.FromResult<EmailGenerator.EmailSubjectResponse?>(new EmailGenerator.EmailSubjectResponse
            {
                Subject = "Status update"
            });
        }
    }
}
