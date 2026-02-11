using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class ServiceArgumentValidationTests
{
    [Fact]
    public void OfficeDocumentService_RequiresTitleAndContent()
    {
        var service = new OfficeDocumentService();

        Assert.Throws<ArgumentException>(() => service.CreateWordDocument("", "Content"));
        Assert.Throws<ArgumentException>(() => service.CreateWordDocument("Title", ""));
    }

    [Fact]
    public void OfficeDocumentService_RequiresExcelTitleAndHeaders()
    {
        var service = new OfficeDocumentService();

        Assert.Throws<ArgumentException>(() => service.CreateExcelDocument("", ["Header"], []));
        Assert.Throws<ArgumentNullException>(() => service.CreateExcelDocument("Title", null!, []));
        Assert.Throws<ArgumentNullException>(() => service.CreateExcelDocument("Title", ["Header"], null!));
        Assert.Throws<ArgumentException>(() => service.CreateExcelDocument("Title", [], []));
    }

    [Fact]
    public void OfficeDocumentService_RequiresPowerPointTitleAndSlides()
    {
        var service = new OfficeDocumentService();

        Assert.Throws<ArgumentException>(() => service.CreatePowerPointDocument("", [("Slide", "Content")]));
        Assert.Throws<ArgumentNullException>(() => service.CreatePowerPointDocument("Title", null!));
        Assert.Throws<ArgumentException>(() => service.CreatePowerPointDocument("Title", []));
    }

    [Fact]
    public void CalendarService_RequiresRequestFields()
    {
        var now = DateTime.UtcNow;

        Assert.Throws<ArgumentNullException>(() => CalendarService.CreateCalendarInvite(null!));
        Assert.Throws<ArgumentException>(() => CalendarService.CreateCalendarInvite(
            new CalendarService.CalendarInviteRequest(
                "",
                "Description",
                now,
                now.AddHours(1),
                "Location",
                "Organizer",
                "organizer@example.com",
                [])));
        Assert.Throws<ArgumentException>(() => CalendarService.CreateCalendarInvite(
            new CalendarService.CalendarInviteRequest(
                "Title",
                "Description",
                now,
                now.AddHours(1),
                "Location",
                "Organizer",
                "",
                [])));
        Assert.Throws<ArgumentNullException>(() => CalendarService.CreateCalendarInvite(
            new CalendarService.CalendarInviteRequest(
                "Title",
                "Description",
                now,
                now.AddHours(1),
                "Location",
                "Organizer",
                "organizer@example.com",
                null!)));
    }

    [Fact]
    public async Task EmailGenerator_RequiresStateAndProgress()
    {
        var service = new EmailGenerator(CreateOpenAiService(), new Random(1));

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GenerateEmailsAsync(null!, new Progress<GenerationProgress>()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GenerateEmailsAsync(new WizardState(), null!));
    }

    [Fact]
    public async Task WorldModelGenerator_RequiresRequestFields()
    {
        var generator = new WorldModelGenerator(CreateOpenAiService(), new Random(1));

        await Assert.ThrowsAsync<ArgumentNullException>(() => generator.GenerateWorldModelAsync(null!));

        var request = new WorldModelRequest
        {
            CaseArea = "",
            MatterType = "Matter",
            Issue = "Issue",
            IssueDescription = "Description"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateWorldModelAsync(request));
    }

    [Fact]
    public async Task EmlFileService_RequiresInputs()
    {
        var service = new EmlFileService();
        var email = new EmailMessage
        {
            From = new Character { FirstName = "Alex", LastName = "Smith", Email = "alex@contoso.com" },
            SentDate = DateTime.UtcNow
        };

        await Assert.ThrowsAsync<ArgumentNullException>(() => EmlFileService.SaveEmailAsEmlAsync(null!, "output"));
        await Assert.ThrowsAsync<ArgumentException>(() => EmlFileService.SaveEmailAsEmlAsync(email, " "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveAllEmailsAsync(null!, "output"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAllEmailsAsync([], ""));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveThreadEmailsAsync(null!, "output"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveThreadEmailsAsync(new EmailThread(), " "));
    }

    [Fact]
    public void OpenAIService_RequiresConstructorInputs()
    {
        Assert.Throws<ArgumentException>(() => new OpenAIService("", "gpt-4o-mini", new Random(1)));
        Assert.Throws<ArgumentException>(() => new OpenAIService("test-key", "", new Random(1)));
        Assert.Throws<ArgumentNullException>(() => new OpenAIService("test-key", "gpt-4o-mini", null!));
        Assert.Throws<ArgumentNullException>(() => new OpenAIService("test-key", null!, null, new Random(1)));
        Assert.Throws<ArgumentException>(() => new OpenAIService("test-key", new AIModelConfig { ModelId = "" }, null, new Random(1)));
    }

    [Fact]
    public async Task OpenAIService_RequiresPrompts()
    {
        var service = CreateOpenAiService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetCompletionAsync("", "User prompt"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetCompletionAsync("System prompt", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetJsonCompletionAsync<object>("", "User prompt"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetJsonCompletionAsync<object>("System prompt", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateImageAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateSpeechAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GenerateSpeechAsync("Text", ""));
    }

    [Fact]
    public async Task GeneratorMethods_RequireTopic()
    {
        var openAi = CreateOpenAiService();
        var rng = new Random(1);
        var storyline = BuildStoryline();
        var organizations = BuildOrganizations();
        var characters = BuildCharacters();

        var beatGenerator = new StoryBeatGenerator(openAi);
        await Assert.ThrowsAsync<ArgumentException>(() => beatGenerator.GenerateStoryBeatsAsync("", storyline, organizations, characters));

        var storylineGenerator = new StorylineGenerator(openAi, rng);
        await Assert.ThrowsAsync<ArgumentException>(() => storylineGenerator.GenerateStorylineAsync(new StorylineGenerationRequest { Topic = "" }, null, default));
        await Assert.ThrowsAsync<ArgumentException>(() => storylineGenerator.GenerateStoryBeatsAsync("", storyline, organizations, characters));

        var entityOrchestrator = new EntityGeneratorOrchestrator(openAi, rng);
        await Assert.ThrowsAsync<ArgumentException>(() => entityOrchestrator.GenerateEntitiesAsync("", storyline));

        var organizationGenerator = new OrganizationGenerator(openAi);
        await Assert.ThrowsAsync<ArgumentException>(() => organizationGenerator.GenerateKnownOrganizationsAsync("", storyline, default));

        var themeGenerator = new ThemeGenerator(openAi);
        await Assert.ThrowsAsync<ArgumentException>(() => themeGenerator.GenerateThemesForOrganizationsAsync("", organizations));

        var characterGenerator = new CharacterGenerator(openAi, rng);
        await Assert.ThrowsAsync<ArgumentException>(() => characterGenerator.AnnotateStorylineRelevanceAsync("", storyline, organizations, characters));
    }

    private static OpenAIService CreateOpenAiService() =>
        new("test-key", "gpt-4o-mini", new Random(1));

    private static Storyline BuildStoryline()
    {
        var startDate = DateTime.Today;
        var storyline = new Storyline
        {
            Title = "Storyline",
            Summary = "Summary",
            StartDate = startDate,
            EndDate = startDate.AddDays(1)
        };
        storyline.SetBeats([new StoryBeat
        {
            Id = Guid.NewGuid(),
            Name = "Beat",
            Plot = "Plot",
            StartDate = startDate,
            EndDate = startDate.AddDays(1)
        }]);
        return storyline;
    }

    private static List<Organization> BuildOrganizations() =>
        [new Organization { Name = "Org", Domain = "org.test" }];

    private static List<Character> BuildCharacters() =>
    [
        new Character { FirstName = "Alex", LastName = "Smith", Email = "alex@org.test" },
        new Character { FirstName = "Jamie", LastName = "Lee", Email = "jamie@org.test" }
    ];
}
