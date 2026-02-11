using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class CharacterGenerator
{
    private readonly OpenAIService _openAI;
    private readonly Random _rng;

    // Available TTS voices with characteristics
    private static readonly string[] MaleVoices = ["echo", "onyx", "fable"];
    private static readonly string[] FemaleVoices = ["nova", "shimmer", "alloy"];

    private const int MaxCharactersPerOrganization = 15;

    public CharacterGenerator(OpenAIService openAI, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        _openAI = openAI;
        _rng = rng;
    }

    /// <summary>
    /// Assigns a TTS voice based on character gender (from AI) or inferred from name/personality.
    /// </summary>
    private string AssignVoice(string gender, string firstName, string personalityNotes)
    {
        var lowerGender = gender?.ToLowerInvariant() ?? "";
        if (lowerGender == "female" || lowerGender == "f")
        {
            return FemaleVoices[_rng.Next(FemaleVoices.Length)];
        }
        if (lowerGender == "male" || lowerGender == "m")
        {
            return MaleVoices[_rng.Next(MaleVoices.Length)];
        }

        var lowerName = firstName.ToLowerInvariant();
        var lowerNotes = personalityNotes?.ToLowerInvariant() ?? "";

        var likelyFemale = lowerNotes.Contains("she ") || lowerNotes.Contains("her ") ||
                           lowerNotes.Contains("woman") || lowerNotes.Contains("female");
        var likelyMale = lowerNotes.Contains("he ") || lowerNotes.Contains("his ") ||
                         lowerNotes.Contains(" man") || lowerNotes.Contains("male");

        var femalePatterns = new[] { "a", "ie", "y", "elle", "ine", "ette", "lyn", "een", "is" };
        var malePatterns = new[] { "ew", "ck", "on", "er", "rd", "ld", "ke" };

        if (!likelyMale && !likelyFemale)
        {
            likelyFemale = femalePatterns.Any(p => lowerName.EndsWith(p));
            likelyMale = malePatterns.Any(p => lowerName.EndsWith(p));
        }

        if (likelyFemale && !likelyMale)
        {
            return FemaleVoices[_rng.Next(FemaleVoices.Length)];
        }
        if (likelyMale && !likelyFemale)
        {
            return MaleVoices[_rng.Next(MaleVoices.Length)];
        }

        var allVoices = MaleVoices.Concat(FemaleVoices).ToArray();
        return allVoices[_rng.Next(allVoices.Length)];
    }

    internal async Task MapKnownCharactersAsync(
        string topic,
        Storyline storyline,
        Organization organization,
        HashSet<string> usedNames,
        HashSet<string> usedEmails,
        CancellationToken ct)
    {
        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Character Mapper.
Identify explicitly named people in the storyline who belong to the given organization and map them to roles.

Rules:
- Only include characters explicitly named in the storyline description.
- Use ONLY roles present in the organization.
- Do NOT invent new people.
 - Use the organization's domain for email addresses.");

        var orgJson = PromptPayloadSerializer.SerializeOrganization(organization, includeCharacters: false);

        var rolesSchema = """
{
  "roles": [
    {
      "name": "RoleName enum value (must exist in org)",
      "characters": [
        {
          "firstName": "string",
          "lastName": "string",
          "email": "string"
        }
      ]
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}
Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Organization (JSON):
{orgJson}

Organization type/state (Raw -> Humanized):
{organization.OrganizationType} -> {EnumHelper.HumanizeEnumName(organization.OrganizationType.ToString())}
{organization.Industry} -> {EnumHelper.HumanizeEnumName(organization.Industry.ToString())}
{organization.State} -> {EnumHelper.HumanizeEnumName(organization.State.ToString())}

Role/Department legend (Raw -> Humanized):
{RoleGenerator.BuildRoleDepartmentLegend(organization)}
", PromptScaffolding.JsonSchemaSection(rolesSchema));

        var response = await _openAI.GetJsonCompletionAsync<RoleCharactersResponse>(
            systemPrompt,
            userPrompt,
            $"Known Character Mapping: {organization.Name}",
            ct);

        if (response?.Roles == null)
            return;

        AddCharactersToRole(organization, response.Roles, usedNames, usedEmails, allowSingleOccupant: true);
    }

    internal async Task GenerateAdditionalCharactersAsync(
        string topic,
        Storyline storyline,
        Organization organization,
        HashSet<string> usedNames,
        HashSet<string> usedEmails,
        CancellationToken ct)
    {
        var existingCount = organization.EnumerateCharacters().Count();
        if (existingCount >= MaxCharactersPerOrganization)
            return;

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Character Generator.
Generate additional key characters for the organization.

Rules:
- Use ONLY roles that exist in the organization.
- Do NOT create characters for single-occupant roles if already filled.
- Avoid duplicate names or emails.
 - Return up to the requested number of characters.");

        var orgJson = PromptPayloadSerializer.SerializeOrganization(organization, includeCharacters: true);
        var existingNames = string.Join(", ", usedNames.OrderBy(n => n));
        var singletonRoles = string.Join(", ", RoleGenerator.SingleOccupantRoles.Select(r =>
            $"{r} ({EnumHelper.HumanizeEnumName(r.ToString())})"));
        var remaining = MaxCharactersPerOrganization - existingCount;

        var additionalSchema = """
{
  "roles": [
    {
      "name": "RoleName enum value (must exist in org)",
      "characters": [
        {
          "firstName": "string",
          "lastName": "string",
          "email": "string"
        }
      ]
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}
Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Organization (JSON):
{orgJson}

Organization type/state (Raw -> Humanized):
{organization.OrganizationType} -> {EnumHelper.HumanizeEnumName(organization.OrganizationType.ToString())}
{organization.Industry} -> {EnumHelper.HumanizeEnumName(organization.Industry.ToString())}
{organization.State} -> {EnumHelper.HumanizeEnumName(organization.State.ToString())}

Role/Department legend (Raw -> Humanized):
{RoleGenerator.BuildRoleDepartmentLegend(organization)}

Single-occupant roles (do not add if already filled):
{singletonRoles}

Existing character names (do not reuse):
{existingNames}

Generate up to {remaining} additional characters for this organization.
", PromptScaffolding.JsonSchemaSection(additionalSchema));

        var response = await _openAI.GetJsonCompletionAsync<RoleCharactersResponse>(
            systemPrompt,
            userPrompt,
            $"Additional Characters: {organization.Name}",
            ct);

        if (response?.Roles == null)
            return;

        AddCharactersToRole(organization, response.Roles, usedNames, usedEmails, allowSingleOccupant: false, maxTotalCharacters: MaxCharactersPerOrganization);
    }

    internal async Task EnrichCharactersAsync(
        string topic,
        Storyline storyline,
        Organization organization,
        CancellationToken ct)
    {
        var assignments = organization.EnumerateCharacters().ToList();
        if (assignments.Count == 0)
            return;

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Character Detailer.
Fill in personality notes, communication style, and signature blocks for each character.

Rules:
- Keep it workplace-appropriate and fictional.
- Personality notes must be EXACTLY 3 sentences about the individual only (no relationships/tensions or other characters).
- Communication style should align with the personality notes and role; describe how they write emails.
- Signature blocks should be consistent with organization name and role.
 - Return JSON matching the schema exactly.");

        var characterJson = PromptPayloadSerializer.SerializeCharacters(organization, humanizeRoleDepartment: true);

        var detailSchema = """
{
  "characters": [
    {
      "email": "string",
      "gender": "male|female|unspecified",
      "personalityNotes": "string (exactly 3 sentences about the character's personality with a mix of positive and negative traits to varying degrees.)",
      "communicationStyle": "string (1-2 sentences describing their email communication style aligned with personality)",
      "signatureBlock": "string (use \\n for line breaks)"
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}
Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Organization: {organization.Name} ({organization.Domain})

Characters (JSON):
{characterJson}", PromptScaffolding.JsonSchemaSection(detailSchema));

        var response = await _openAI.GetJsonCompletionAsync<CharacterDetailResponse>(
            systemPrompt,
            userPrompt,
            $"Character Details: {organization.Name}",
            ct);

        if (response?.Characters == null)
            return;

        var lookup = assignments.ToDictionary(a => a.Character.Email, a => a.Character, StringComparer.OrdinalIgnoreCase);
        foreach (var detail in response.Characters)
        {
            if (string.IsNullOrWhiteSpace(detail.Email))
                continue;
            if (!lookup.TryGetValue(detail.Email, out var character))
                continue;

            character.Personality = detail.PersonalityNotes?.Trim() ?? character.Personality;
            character.CommunicationStyle = detail.CommunicationStyle?.Trim() ?? character.CommunicationStyle;
            character.SignatureBlock = detail.SignatureBlock?.Trim() ?? character.SignatureBlock;
            character.VoiceId = AssignVoice(detail.Gender ?? string.Empty, character.FirstName, character.Personality);
        }
    }

    public async Task AnnotateStorylineRelevanceAsync(
        string topic,
        Storyline storyline,
        IReadOnlyList<Organization> organizations,
        IReadOnlyList<Character> characters,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (storyline == null)
            throw new ArgumentNullException(nameof(storyline));
        if (string.IsNullOrWhiteSpace(storyline.Summary))
            throw new InvalidOperationException("Storyline summary is required before evaluating character relevance.");
        if (storyline.Beats == null || storyline.Beats.Count == 0)
            throw new InvalidOperationException("Storyline beats are required before evaluating character relevance.");
        if (organizations == null || organizations.Count == 0)
            throw new InvalidOperationException("At least one organization is required before evaluating character relevance.");
        if (characters == null || characters.Count == 0)
            throw new InvalidOperationException("At least one character is required before evaluating character relevance.");

        progress?.Report("Assessing character relevance...");

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Character Relevance Analyst.
Determine whether each character is likely to communicate in email threads relevant to the storyline overall.

Rules:
- Use ONLY the provided characters and organizations.
- Consider the storyline summary and all story beats.
- Mark isKeyCharacter true only if the character would likely generate relevant communications for the overall storyline.
- storylineRelevance must be exactly two concise sentences.
 - plotRelevance must include an entry for EVERY story beat ID, each with exactly two concise sentences (even if tangential).");

        var assignments = organizations
            .SelectMany(o => o.EnumerateCharacters())
            .GroupBy(a => a.Character.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(a => new
            {
                firstName = a.Character.FirstName,
                lastName = a.Character.LastName,
                email = a.Character.Email,
                role = EnumHelper.HumanizeEnumName(a.Role.Name.ToString()),
                department = EnumHelper.HumanizeEnumName(a.Department.Name.ToString()),
                organization = a.Organization.Name,
                organizationDescription = a.Organization.Description,
                organizationType = a.Organization.OrganizationType.ToString(),
                isPlaintiff = a.Organization.IsPlaintiff,
                isDefendant = a.Organization.IsDefendant
            })
            .ToList();

        var characterJson = JsonSerializer.Serialize(assignments, JsonSerializationDefaults.Indented);
        var beatsJson = JsonSerializer.Serialize(storyline.Beats.Select(b => new
        {
            id = b.Id,
            name = b.Name,
            plot = b.Plot
        }), JsonSerializationDefaults.Indented);

        var relevanceSchema = """
{
  "characters": [
    {
      "email": "string",
      "isKeyCharacter": true|false,
      "storylineRelevance": "string (exactly two concise sentences)",
      "plotRelevance": {
        "beatId": "string (exactly two concise sentences)"
      }
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}

Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Story beats (JSON):
{beatsJson}

Characters (JSON):
{characterJson}", PromptScaffolding.JsonSchemaSection(relevanceSchema));

        var response = await _openAI.GetJsonCompletionAsync<CharacterRelevanceResponse>(
            systemPrompt,
            userPrompt,
            "Character Relevance",
            ct);

        if (response?.Characters == null)
            return;

        ApplyStorylineRelevance(characters, storyline.Beats, response.Characters);
    }

    internal static void AddCharactersToRole(
        Organization organization,
        List<RoleCharactersDto> roles,
        HashSet<string> usedNames,
        HashSet<string> usedEmails,
        bool allowSingleOccupant,
        int? maxTotalCharacters = null)
    {
        var roleLookup = BuildRoleLookup(organization);
        var tracker = new CharacterAddTracker(
            usedNames,
            usedEmails,
            maxTotalCharacters,
            organization.EnumerateCharacters().Count());

        foreach (var roleDto in roles)
        {
            if (!TryGetRoleAssignments(roleLookup, roleDto, out var roleName, out var roleAssignments))
                continue;
            if (!CanAssignRole(roleAssignments, roleName, allowSingleOccupant))
                continue;

            var assignment = RoleGenerator.SelectRoleAssignment(roleName, roleAssignments);
            var targetRole = assignment.Role;
            var targetDepartment = assignment.Department;

            if (TryAddCharactersToRole(
                    roleDto.Characters,
                    organization,
                    targetRole,
                    targetDepartment,
                    tracker))
                return;
        }
    }

    internal static void ApplyStorylineRelevance(
        IReadOnlyList<Character> characters,
        IReadOnlyList<StoryBeat> beats,
        List<CharacterRelevanceDto> relevance)
    {
        if (characters == null) throw new ArgumentNullException(nameof(characters));
        if (beats == null) throw new ArgumentNullException(nameof(beats));
        if (relevance == null) throw new ArgumentNullException(nameof(relevance));

        var beatIds = new HashSet<Guid>(beats.Select(b => b.Id));
        var characterLookup = characters
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .ToDictionary(c => c.Email, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in relevance)
        {
            if (!TryGetCharacter(characterLookup, entry, out var character))
                continue;

            character.IsKeyCharacter = entry.IsKeyCharacter;
            character.StorylineRelevance = entry.StorylineRelevance?.Trim() ?? character.StorylineRelevance;

            var plotRelevance = BuildPlotRelevance(entry.PlotRelevance, beatIds);
            if (plotRelevance.Count > 0)
                character.PlotRelevance = plotRelevance;
        }
    }

    private static Dictionary<RoleName, List<(Role Role, Department Department)>> BuildRoleLookup(Organization organization)
    {
        return organization.Departments
            .SelectMany(d => d.Roles.Select(r => (Role: r, Department: d)))
            .GroupBy(r => r.Role.Name)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static bool TryGetRoleAssignments(
        IReadOnlyDictionary<RoleName, List<(Role Role, Department Department)>> roleLookup,
        RoleCharactersDto roleDto,
        out RoleName roleName,
        out List<(Role Role, Department Department)> roleAssignments)
    {
        roleAssignments = new List<(Role Role, Department Department)>();
        roleName = default;

        if (!EnumHelper.TryParseEnum(roleDto.Name, out roleName))
            return false;

        if (!roleLookup.TryGetValue(roleName, out var assignments))
            return false;

        roleAssignments = assignments;
        return true;
    }

    private static bool CanAssignRole(
        IReadOnlyList<(Role Role, Department Department)> roleAssignments,
        RoleName roleName,
        bool allowSingleOccupant)
    {
        if (allowSingleOccupant)
            return true;

        if (!RoleGenerator.SingleOccupantRoles.Contains(roleName))
            return true;

        return roleAssignments.All(r => r.Role.Characters.Count == 0);
    }

    private sealed class CharacterAddTracker
    {
        public CharacterAddTracker(HashSet<string> usedNames, HashSet<string> usedEmails, int? maxTotalCharacters, int totalCount)
        {
            UsedNames = usedNames;
            UsedEmails = usedEmails;
            MaxTotalCharacters = maxTotalCharacters;
            TotalCount = totalCount;
        }

        public HashSet<string> UsedNames { get; }
        public HashSet<string> UsedEmails { get; }
        public int? MaxTotalCharacters { get; }
        public int TotalCount { get; set; }
    }

    private sealed class CharacterBuildContext
    {
        public CharacterBuildContext(string domain, Guid roleId, Guid departmentId, Guid organizationId, HashSet<string> usedNames, HashSet<string> usedEmails)
        {
            Domain = domain;
            RoleId = roleId;
            DepartmentId = departmentId;
            OrganizationId = organizationId;
            UsedNames = usedNames;
            UsedEmails = usedEmails;
        }

        public string Domain { get; }
        public Guid RoleId { get; }
        public Guid DepartmentId { get; }
        public Guid OrganizationId { get; }
        public HashSet<string> UsedNames { get; }
        public HashSet<string> UsedEmails { get; }
    }

    private static bool TryAddCharactersToRole(
        IEnumerable<SimpleCharacterDto>? characters,
        Organization organization,
        Role targetRole,
        Department targetDepartment,
        CharacterAddTracker tracker)
    {
        var buildContext = new CharacterBuildContext(
            organization.Domain,
            targetRole.Id,
            targetDepartment.Id,
            organization.Id,
            tracker.UsedNames,
            tracker.UsedEmails);

        foreach (var c in characters ?? Enumerable.Empty<SimpleCharacterDto>())
        {
            if (tracker.MaxTotalCharacters.HasValue && tracker.TotalCount >= tracker.MaxTotalCharacters.Value)
                return true;
            if (!TryBuildCharacter(
                    c,
                    buildContext,
                    out var character,
                    out var fullName,
                    out var email))
                continue;

            targetRole.Characters.Add(character);
            tracker.UsedNames.Add(fullName);
            tracker.UsedEmails.Add(email);
            tracker.TotalCount++;
        }

        return false;
    }

    private static bool TryBuildCharacter(
        SimpleCharacterDto character,
        CharacterBuildContext context,
        out Character model,
        out string fullName,
        out string email)
    {
        model = default!;
        fullName = string.Empty;
        email = string.Empty;

        if (string.IsNullOrWhiteSpace(character.FirstName) || string.IsNullOrWhiteSpace(character.LastName))
            return false;

        fullName = $"{character.FirstName.Trim()} {character.LastName.Trim()}".Trim();
        if (context.UsedNames.Contains(fullName))
            return false;

        email = EmailAddressHelper.GenerateUniqueEmail(
            character.FirstName,
            character.LastName,
            context.Domain,
            context.UsedEmails,
            character.Email);
        if (string.IsNullOrWhiteSpace(email))
            return false;

        model = new Character
        {
            Id = DeterministicIdHelper.CreateGuid(
                "character",
                context.OrganizationId.ToString("N"),
                email.ToLowerInvariant()),
            FirstName = character.FirstName.Trim(),
            LastName = character.LastName.Trim(),
            Email = email,
            RoleId = context.RoleId,
            DepartmentId = context.DepartmentId,
            OrganizationId = context.OrganizationId,
            Personality = string.Empty,
            CommunicationStyle = string.Empty,
            SignatureBlock = string.Empty
        };

        return true;
    }

    private static bool TryGetCharacter(
        IReadOnlyDictionary<string, Character> characterLookup,
        CharacterRelevanceDto entry,
        out Character character)
    {
        character = default!;
        if (string.IsNullOrWhiteSpace(entry.Email))
            return false;

        if (!characterLookup.TryGetValue(entry.Email, out var found))
            return false;

        character = found;
        return true;
    }

    private static Dictionary<Guid, string> BuildPlotRelevance(
        IReadOnlyDictionary<string, string>? plotRelevance,
        HashSet<Guid> beatIds)
    {
        if (plotRelevance == null || plotRelevance.Count == 0)
            return new Dictionary<Guid, string>();

        var result = new Dictionary<Guid, string>();
        foreach (var kvp in plotRelevance)
        {
            if (!Guid.TryParse(kvp.Key, out var beatId))
                continue;
            if (!beatIds.Contains(beatId))
                continue;
            if (string.IsNullOrWhiteSpace(kvp.Value))
                continue;

            result[beatId] = kvp.Value.Trim();
        }

        return result;
    }


    internal static List<Character> FlattenCharacters(IEnumerable<Organization> organizations)
    {
        var results = new List<Character>();
        var seen = new HashSet<Guid>();

        foreach (var assignment in organizations.SelectMany(o => o.EnumerateCharacters()))
        {
            if (seen.Add(assignment.Character.Id))
            {
                results.Add(assignment.Character);
            }
        }

        return results;
    }

    private class RoleCharactersResponse
    {
        [JsonPropertyName("roles")]
        public List<RoleCharactersDto>? Roles { get; set; }
    }

    internal class RoleCharactersDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("characters")]
        public List<SimpleCharacterDto>? Characters { get; set; }
    }

    internal class SimpleCharacterDto
    {
        [JsonPropertyName("firstName")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    private class CharacterDetailResponse
    {
        [JsonPropertyName("characters")]
        public List<CharacterDetailDto>? Characters { get; set; }
    }

    private class CharacterDetailDto
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("gender")]
        public string? Gender { get; set; }

        [JsonPropertyName("personalityNotes")]
        public string? PersonalityNotes { get; set; }

        [JsonPropertyName("communicationStyle")]
        public string? CommunicationStyle { get; set; }

        [JsonPropertyName("signatureBlock")]
        public string? SignatureBlock { get; set; }
    }

    private class CharacterRelevanceResponse
    {
        [JsonPropertyName("characters")]
        public List<CharacterRelevanceDto>? Characters { get; set; }
    }

    internal class CharacterRelevanceDto
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("isKeyCharacter")]
        public bool IsKeyCharacter { get; set; }

        [JsonPropertyName("storylineRelevance")]
        public string? StorylineRelevance { get; set; }

        [JsonPropertyName("plotRelevance")]
        public Dictionary<string, string>? PlotRelevance { get; set; }
    }
}

public class CharacterGenerationResult
{
    public string PrimaryDomain { get; set; } = string.Empty;
    public List<Organization> Organizations { get; set; } = new();
    public List<Character> Characters { get; set; } = new();
}
