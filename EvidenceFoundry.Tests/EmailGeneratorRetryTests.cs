using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmailGeneratorRetryTests
{
    [Fact]
    public async Task GenerateThreadWithRetriesAsync_RetriesAndSucceeds()
    {
        var characters = BuildCharacters();
        var characterLookup = characters.ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);
        var responses = new[]
        {
            BuildResponse("Budget Review", characters, 1),
            BuildResponse("Budget Review", characters, 2)
        };

        var generator = new TestEmailGenerator(responses);
        var thread = new EmailThread();
        var result = new GenerationResult();
        var progressLock = new object();

        var output = await generator.GenerateThreadWithRetriesAsync(
            BuildStoryline(),
            thread,
            characters,
            characterLookup,
            "corp.com",
            2,
            new DateTime(2024, 1, 1),
            new DateTime(2024, 1, 2),
            new GenerationConfig(),
            new Dictionary<string, OrganizationTheme>(StringComparer.OrdinalIgnoreCase),
            "system",
            "characters",
            "Beat 1",
            result,
            progressLock,
            CancellationToken.None);

        Assert.NotNull(output);
        Assert.Equal(2, output!.EmailMessages.Count);
        Assert.Equal(2, generator.CallCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task GenerateThreadWithRetriesAsync_StopsAfterMaxAttemptsAndLogs()
    {
        var characters = BuildCharacters();
        var characterLookup = characters.ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);
        var responses = new[]
        {
            BuildResponse("Budget Review", characters, 1),
            BuildResponse("Budget Review", characters, 1),
            BuildResponse("Budget Review", characters, 1)
        };

        var generator = new TestEmailGenerator(responses);
        var thread = new EmailThread();
        var result = new GenerationResult();
        var progressLock = new object();

        var output = await generator.GenerateThreadWithRetriesAsync(
            BuildStoryline(),
            thread,
            characters,
            characterLookup,
            "corp.com",
            2,
            new DateTime(2024, 1, 1),
            new DateTime(2024, 1, 2),
            new GenerationConfig(),
            new Dictionary<string, OrganizationTheme>(StringComparer.OrdinalIgnoreCase),
            "system",
            "characters",
            "Beat 2",
            result,
            progressLock,
            CancellationToken.None);

        Assert.Null(output);
        Assert.Equal(3, generator.CallCount);
        Assert.Contains(result.Errors, e => e.Contains("failed after 3 attempts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("Beat 2", StringComparison.OrdinalIgnoreCase));
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

    private static Storyline BuildStoryline()
    {
        return new Storyline
        {
            Title = "Budget Review",
            Summary = "Quarterly budget review.",
            Beats = new List<StoryBeat>()
        };
    }

    private static EmailGenerator.ThreadApiResponse BuildResponse(
        string subject,
        IReadOnlyList<Character> characters,
        int emailCount)
    {
        var from = characters[0];
        var to = characters[1];
        var emails = new List<EmailGenerator.EmailDto>(emailCount);

        for (var i = 0; i < emailCount; i++)
        {
            emails.Add(new EmailGenerator.EmailDto
            {
                FromEmail = from.Email,
                ToEmails = new List<string> { to.Email },
                CcEmails = new List<string>(),
                SentDateTime = new DateTime(2024, 1, 1, 9, 0, 0).AddHours(i).ToString("O"),
                BodyPlain = $"Hello,\n\nMessage {i + 1}\n\n{from.SignatureBlock}",
                IsReply = i > 0,
                IsForward = false,
                ReplyToIndex = i == 0 ? -1 : i - 1,
                HasDocument = false,
                HasImage = false,
                IsImageInline = false,
                HasVoicemail = false
            });
        }

        return new EmailGenerator.ThreadApiResponse
        {
            Subject = subject,
            Emails = emails
        };
    }

    private sealed class TestEmailGenerator : EmailGenerator
    {
        private readonly Queue<EmailGenerator.ThreadApiResponse?> _responses;

        public int CallCount { get; private set; }

        public TestEmailGenerator(IEnumerable<EmailGenerator.ThreadApiResponse?> responses)
            : base(new OpenAIService("test-key", "gpt-4o-mini"))
        {
            _responses = new Queue<EmailGenerator.ThreadApiResponse?>(responses);
        }

        protected override Task<EmailGenerator.ThreadApiResponse?> GetThreadResponseAsync(
            string systemPrompt,
            string userPrompt,
            string operationName,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : null);
        }
    }
}
