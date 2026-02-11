using System.Diagnostics;
using EvidenceFoundry.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceFoundry.Services;

public class EntityGeneratorOrchestrator
{
    private readonly OrganizationGenerator _organizationGenerator;
    private readonly CharacterGenerator _characterGenerator;
    private readonly ILogger<EntityGeneratorOrchestrator> _logger;
    private readonly ILoggerFactory? _loggerFactory;

    public EntityGeneratorOrchestrator(
        OpenAIService openAI,
        Random rng,
        ILogger<EntityGeneratorOrchestrator>? logger = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(openAI);
        ArgumentNullException.ThrowIfNull(rng);
        _loggerFactory = loggerFactory;
        _logger = ResolveLogger(logger, loggerFactory);
        _organizationGenerator = new OrganizationGenerator(
            openAI,
            ResolveLogger<OrganizationGenerator>(null, loggerFactory));
        _characterGenerator = new CharacterGenerator(
            openAI,
            rng,
            ResolveLogger<CharacterGenerator>(null, loggerFactory));
        _logger.LogDebug("EntityGeneratorOrchestrator initialized.");
    }

    private static ILogger<T> ResolveLogger<T>(ILogger<T>? logger, ILoggerFactory? loggerFactory)
        => logger ?? loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;

    public async Task<CharacterGenerationResult> GenerateEntitiesAsync(
        string topic,
        Storyline storyline,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic is required.", nameof(topic));
        ArgumentNullException.ThrowIfNull(storyline);
        if (!storyline.StartDate.HasValue || !storyline.EndDate.HasValue)
            throw new ArgumentException("Storyline must have a start and end date before character generation.", nameof(storyline));

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Topic"] = topic,
            ["StorylineId"] = storyline.Id,
            ["StorylineTitle"] = storyline.Title
        });

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting entity generation.");

        try
        {
            progress?.Report("Extracting known organizations...");
            var seedOrganizations = await _organizationGenerator.GenerateKnownOrganizationsAsync(topic, storyline, ct);
            if (seedOrganizations.Count == 0)
                throw new InvalidOperationException("No organizations were generated from the storyline.");

            _logger.LogInformation(
                "Extracted {SeedOrganizationCount} seed organizations.",
                seedOrganizations.Count);

            progress?.Report($"Filling organization structures ({seedOrganizations.Count})...");
            var organizations = new List<Organization>();
            var usedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var seed in seedOrganizations)
            {
                ct.ThrowIfCancellationRequested();
                var enriched = await _organizationGenerator.EnrichOrganizationAsync(storyline, seed, ct);
                OrganizationGenerator.NormalizeOrganization(
                    enriched,
                    storyline.StartDate.Value.Date,
                    usedDomains,
                    ResolveLogger<OrganizationGenerator>(null, _loggerFactory));
                organizations.Add(enriched);
            }

            var hadPlaintiff = organizations.Any(o => o.IsPlaintiff);
            var hadDefendant = organizations.Any(o => o.IsDefendant);
            OrganizationGenerator.EnsureCaseParties(organizations);
            if (!hadPlaintiff && organizations.Count > 0)
            {
                _logger.LogWarning(
                    "No plaintiff organization specified; defaulted to {OrganizationName}.",
                    organizations[0].Name);
            }
            if (!hadDefendant && organizations.Count > 1)
            {
                var defaultDefendant = organizations.FirstOrDefault(o => o.IsDefendant);
                if (defaultDefendant != null)
                {
                    _logger.LogWarning(
                        "No defendant organization specified; defaulted to {OrganizationName}.",
                        defaultDefendant.Name);
                }
            }

            progress?.Report("Mapping known characters...");
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var org in organizations)
            {
                ct.ThrowIfCancellationRequested();
                await _characterGenerator.MapKnownCharactersAsync(topic, storyline, org, usedNames, usedEmails, ct);
            }

            progress?.Report("Generating additional key characters...");
            foreach (var org in organizations)
            {
                ct.ThrowIfCancellationRequested();
                await _characterGenerator.GenerateAdditionalCharactersAsync(topic, storyline, org, usedNames, usedEmails, ct);
            }

            progress?.Report("Enriching character details...");
            foreach (var org in organizations)
            {
                ct.ThrowIfCancellationRequested();
                await _characterGenerator.EnrichCharactersAsync(topic, storyline, org, ct);
            }

            var characters = CharacterGenerator.FlattenCharacters(organizations);
            if (characters.Count < 2)
                throw new InvalidOperationException("At least 2 characters are required to generate emails.");

            storyline.SetOrganizations(organizations);
            var primaryOrg = organizations.FirstOrDefault(o => o.IsPlaintiff)
                ?? organizations.FirstOrDefault(o => o.IsDefendant)
                ?? organizations.FirstOrDefault();

            _logger.LogInformation(
                "Entity generation produced {OrganizationCount} organizations and {CharacterCount} characters in {DurationMs} ms.",
                organizations.Count,
                characters.Count,
                stopwatch.ElapsedMilliseconds);

            return new CharacterGenerationResult
            {
                Organizations = organizations,
                Characters = characters,
                PrimaryDomain = primaryOrg?.Domain ?? string.Empty
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Entity generation canceled after {DurationMs} ms.",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Entity generation failed after {DurationMs} ms.",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
