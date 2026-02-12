using System.Diagnostics;
using EvidenceFoundry.Models;
using Serilog;
using Serilog.Context;

namespace EvidenceFoundry.Services;

public partial class EntityGeneratorOrchestrator
{
    private readonly OrganizationGenerator _organizationGenerator;
    private readonly CharacterGenerator _characterGenerator;
    private readonly ILogger _logger;

    public EntityGeneratorOrchestrator(
        OpenAIService openAI,
        Random rng,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(openAI);
        ArgumentNullException.ThrowIfNull(rng);
        var baseLogger = logger ?? Serilog.Log.Logger;
        _logger = baseLogger.ForContext<EntityGeneratorOrchestrator>();
        _organizationGenerator = new OrganizationGenerator(
            openAI,
            baseLogger.ForContext<OrganizationGenerator>());
        _characterGenerator = new CharacterGenerator(
            openAI,
            rng,
            baseLogger.ForContext<CharacterGenerator>());
        Log.EntityGeneratorOrchestratorInitialized(_logger);
    }

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

        using var topicScope = LogContext.PushProperty("Topic", topic);
        using var storylineIdScope = LogContext.PushProperty("StorylineId", storyline.Id);
        using var storylineTitleScope = LogContext.PushProperty("StorylineTitle", storyline.Title);

        var stopwatch = Stopwatch.StartNew();
        Log.StartingEntityGeneration(_logger);

        try
        {
            progress?.Report("Extracting known organizations...");
            var seedOrganizations = await _organizationGenerator.GenerateKnownOrganizationsAsync(topic, storyline, ct);
            if (seedOrganizations.Count == 0)
                throw new InvalidOperationException("No organizations were generated from the storyline.");

            Log.ExtractedSeedOrganizations(_logger, seedOrganizations.Count);

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
                    _logger.ForContext<OrganizationGenerator>());
                organizations.Add(enriched);
            }

            var hadPlaintiff = organizations.Any(o => o.IsPlaintiff);
            var hadDefendant = organizations.Any(o => o.IsDefendant);
            OrganizationGenerator.EnsureCaseParties(organizations);
            if (!hadPlaintiff && organizations.Count > 0)
            {
                Log.NoPlaintiffOrganizationSpecified(_logger, organizations[0].Name);
            }
            if (!hadDefendant && organizations.Count > 1)
            {
                var defaultDefendant = organizations.FirstOrDefault(o => o.IsDefendant);
                if (defaultDefendant != null)
                {
                    Log.NoDefendantOrganizationSpecified(_logger, defaultDefendant.Name);
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

            Log.EntityGenerationProduced(
                _logger,
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
            Log.EntityGenerationCanceled(_logger, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Log.EntityGenerationFailed(_logger, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
    }

    private static class Log
    {
        public static void EntityGeneratorOrchestratorInitialized(ILogger logger)
            => logger.Debug("EntityGeneratorOrchestrator initialized.");

        public static void StartingEntityGeneration(ILogger logger)
            => logger.Information("Starting entity generation.");

        public static void ExtractedSeedOrganizations(ILogger logger, int seedOrganizationCount)
            => logger.Information("Extracted {SeedOrganizationCount} seed organizations.", seedOrganizationCount);

        public static void NoPlaintiffOrganizationSpecified(ILogger logger, string organizationName)
            => logger.Warning("No plaintiff organization specified; defaulted to {OrganizationName}.", organizationName);

        public static void NoDefendantOrganizationSpecified(ILogger logger, string organizationName)
            => logger.Warning("No defendant organization specified; defaulted to {OrganizationName}.", organizationName);

        public static void EntityGenerationProduced(
            ILogger logger,
            int organizationCount,
            int characterCount,
            long durationMs)
            => logger.Information(
                "Entity generation produced {OrganizationCount} organizations and {CharacterCount} characters in {DurationMs} ms.",
                organizationCount,
                characterCount,
                durationMs);

        public static void EntityGenerationCanceled(ILogger logger, long durationMs)
            => logger.Warning("Entity generation canceled after {DurationMs} ms.", durationMs);

        public static void EntityGenerationFailed(ILogger logger, long durationMs, Exception exception)
            => logger.Error(exception, "Entity generation failed after {DurationMs} ms.", durationMs);
    }
}
