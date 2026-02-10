using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class EntityGeneratorOrchestrator
{
    private readonly OrganizationGenerator _organizationGenerator;
    private readonly CharacterGenerator _characterGenerator;

    public EntityGeneratorOrchestrator(OpenAIService openAI)
    {
        _organizationGenerator = new OrganizationGenerator(openAI);
        _characterGenerator = new CharacterGenerator(openAI);
    }

    public async Task<CharacterGenerationResult> GenerateEntitiesAsync(
        string topic,
        Storyline storyline,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (storyline == null)
            throw new ArgumentNullException(nameof(storyline));
        if (!storyline.StartDate.HasValue || !storyline.EndDate.HasValue)
            throw new InvalidOperationException("Storyline must have a start and end date before character generation.");

        progress?.Report("Extracting known organizations...");
        var seedOrganizations = await _organizationGenerator.GenerateKnownOrganizationsAsync(topic, storyline, ct);
        if (seedOrganizations.Count == 0)
            throw new InvalidOperationException("No organizations were generated from the storyline.");

        progress?.Report($"Filling organization structures ({seedOrganizations.Count})...");
        var organizations = new List<Organization>();
        var usedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seedOrganizations)
        {
            ct.ThrowIfCancellationRequested();
            var enriched = await _organizationGenerator.EnrichOrganizationAsync(storyline, seed, ct);
            _organizationGenerator.NormalizeOrganization(enriched, storyline.StartDate.Value.Date, usedDomains);
            organizations.Add(enriched);
        }

        OrganizationGenerator.EnsureCaseParties(organizations);

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

        storyline.Organizations = organizations;
        var primaryOrg = organizations.FirstOrDefault(o => o.IsPlaintiff)
            ?? organizations.FirstOrDefault(o => o.IsDefendant)
            ?? organizations.FirstOrDefault();

        return new CharacterGenerationResult
        {
            Organizations = organizations,
            Characters = characters,
            PrimaryDomain = primaryOrg?.Domain ?? string.Empty
        };
    }
}
