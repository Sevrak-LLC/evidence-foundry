using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class CharacterGenerator
{
    private readonly OpenAIService _openAI;
    private static readonly Random _random = Random.Shared;

    // Available TTS voices with characteristics
    private static readonly string[] MaleVoices = ["echo", "onyx", "fable"];
    private static readonly string[] FemaleVoices = ["nova", "shimmer", "alloy"];

    private const int MaxCharactersPerOrganization = 15;

    public CharacterGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
    }

    /// <summary>
    /// Assigns a TTS voice based on character gender (from AI) or inferred from name/personality.
    /// </summary>
    private string AssignVoice(string gender, string firstName, string personalityNotes)
    {
        var lowerGender = gender?.ToLowerInvariant() ?? "";
        if (lowerGender == "female" || lowerGender == "f")
        {
            return FemaleVoices[_random.Next(FemaleVoices.Length)];
        }
        if (lowerGender == "male" || lowerGender == "m")
        {
            return MaleVoices[_random.Next(MaleVoices.Length)];
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
            return FemaleVoices[_random.Next(FemaleVoices.Length)];
        }
        if (likelyMale && !likelyFemale)
        {
            return MaleVoices[_random.Next(MaleVoices.Length)];
        }

        var allVoices = MaleVoices.Concat(FemaleVoices).ToArray();
        return allVoices[_random.Next(allVoices.Length)];
    }

    internal async Task MapKnownCharactersAsync(
        string topic,
        Storyline storyline,
        Organization organization,
        HashSet<string> usedNames,
        HashSet<string> usedEmails,
        CancellationToken ct)
    {
        var systemPrompt = @"You are the EvidenceFoundry Character Mapper.
Identify explicitly named people in the storyline who belong to the given organization and map them to roles.

Rules:
- Only include characters explicitly named in the storyline description.
- Use ONLY roles present in the organization.
- Do NOT invent new people.
- Use the organization's domain for email addresses.";

        var orgJson = SerializeOrganizationForPrompt(organization, includeCharacters: false);

        var userPrompt = $@"Topic: {topic}
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

Respond with JSON in this exact format:
{{
  ""roles"": [
    {{
      ""name"": ""RoleName enum value (must exist in org)"",
      ""characters"": [
        {{
          ""firstName"": ""string"",
          ""lastName"": ""string"",
          ""email"": ""string""
        }}
      ]
    }}
  ]
}}";

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

        var systemPrompt = @"You are the EvidenceFoundry Character Generator.
Generate additional key characters for the organization.

Rules:
- Use ONLY roles that exist in the organization.
- Do NOT create characters for single-occupant roles if already filled.
- Avoid duplicate names or emails.
- Return up to the requested number of characters.";

        var orgJson = SerializeOrganizationForPrompt(organization, includeCharacters: true);
        var existingNames = string.Join(", ", usedNames.OrderBy(n => n));
        var singletonRoles = string.Join(", ", RoleGenerator.SingleOccupantRoles.Select(r =>
            $"{r} ({EnumHelper.HumanizeEnumName(r.ToString())})"));
        var remaining = MaxCharactersPerOrganization - existingCount;

        var userPrompt = $@"Topic: {topic}
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
Respond with JSON in this exact format:
{{
  ""roles"": [
    {{
      ""name"": ""RoleName enum value (must exist in org)"",
      ""characters"": [
        {{
          ""firstName"": ""string"",
          ""lastName"": ""string"",
          ""email"": ""string""
        }}
      ]
    }}
  ]
}}";

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

        var systemPrompt = @"You are the EvidenceFoundry Character Detailer.
Fill in personality notes, communication style, and signature blocks for each character.

Rules:
- Keep it workplace-appropriate and fictional.
- Personality notes must be EXACTLY 3 sentences about the individual only (no relationships/tensions or other characters).
- Communication style should align with the personality notes and role; describe how they write emails.
- Signature blocks should be consistent with organization name and role.
- Return JSON matching the schema exactly.";

        var characterJson = JsonSerializer.Serialize(assignments.Select(a => new
        {
            firstName = a.Character.FirstName,
            lastName = a.Character.LastName,
            email = a.Character.Email,
            role = EnumHelper.HumanizeEnumName(a.Role.Name.ToString()),
            department = EnumHelper.HumanizeEnumName(a.Department.Name.ToString()),
            organization = organization.Name
        }), new JsonSerializerOptions { WriteIndented = true });

        var userPrompt = $@"Topic: {topic}
Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Organization: {organization.Name} ({organization.Domain})

Characters (JSON):
{characterJson}

Respond with JSON in this exact format:
{{
  ""characters"": [
    {{
      ""email"": ""string"",
      ""gender"": ""male|female|unspecified"",
      ""personalityNotes"": ""string (exactly 3 sentences about the character's personality with a mix of positive and negative traits to varying degrees.)"",
      ""communicationStyle"": ""string (1-2 sentences describing their email communication style aligned with personality)"",
      ""signatureBlock"": ""string (use \\n for line breaks)""
    }}
  ]
}}";

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

        var systemPrompt = @"You are the EvidenceFoundry Character Relevance Analyst.
Determine whether each character is likely to communicate in email threads relevant to the storyline overall.

Rules:
- Use ONLY the provided characters and organizations.
- Consider the storyline summary and all story beats.
- Mark isKeyCharacter true only if the character would likely generate relevant communications for the overall storyline.
- storylineRelevance must be exactly two concise sentences.
- plotRelevance must include an entry for EVERY story beat ID, each with exactly two concise sentences (even if tangential).";

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

        var characterJson = JsonSerializer.Serialize(assignments, new JsonSerializerOptions { WriteIndented = true });
        var beatsJson = JsonSerializer.Serialize(storyline.Beats.Select(b => new
        {
            id = b.Id,
            name = b.Name,
            plot = b.Plot
        }), new JsonSerializerOptions { WriteIndented = true });

        var userPrompt = $@"Topic: {topic}

Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Story beats (JSON):
{beatsJson}

Characters (JSON):
{characterJson}

Respond with JSON in this exact format:
{{
  ""characters"": [
    {{
      ""email"": ""string"",
      ""isKeyCharacter"": true|false,
      ""storylineRelevance"": ""string (exactly two concise sentences)"",
      ""plotRelevance"": {{
        ""beatId"": ""string (exactly two concise sentences)""
      }}
    }}
  ]
}}";

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
        var roleLookup = organization.Departments
            .SelectMany(d => d.Roles.Select(r => (Role: r, Department: d)))
            .GroupBy(r => r.Role.Name)
            .ToDictionary(g => g.Key, g => g.ToList());

        var totalCount = organization.EnumerateCharacters().Count();

        foreach (var roleDto in roles)
        {
            if (!EnumHelper.TryParseEnum(roleDto.Name, out RoleName roleName))
                continue;
            if (!roleLookup.TryGetValue(roleName, out var roleAssignments))
                continue;

            if (!allowSingleOccupant &&
                RoleGenerator.SingleOccupantRoles.Contains(roleName) &&
                roleAssignments.Any(r => r.Role.Characters.Count > 0))
                continue;

            var assignment = RoleGenerator.SelectRoleAssignment(roleName, roleAssignments);
            var targetRole = assignment.Role;
            var targetDepartment = assignment.Department;

            foreach (var c in roleDto.Characters ?? new())
            {
                if (maxTotalCharacters.HasValue && totalCount >= maxTotalCharacters.Value)
                    return;
                if (string.IsNullOrWhiteSpace(c.FirstName) || string.IsNullOrWhiteSpace(c.LastName))
                    continue;

                var fullName = $"{c.FirstName.Trim()} {c.LastName.Trim()}".Trim();
                if (usedNames.Contains(fullName))
                    continue;

                var email = EmailAddressHelper.GenerateUniqueEmail(
                    c.FirstName,
                    c.LastName,
                    organization.Domain,
                    usedEmails,
                    c.Email);
                if (string.IsNullOrWhiteSpace(email))
                    continue;
                var character = new Character
                {
                    FirstName = c.FirstName.Trim(),
                    LastName = c.LastName.Trim(),
                    Email = email,
                    RoleId = targetRole.Id,
                    DepartmentId = targetDepartment.Id,
                    OrganizationId = organization.Id,
                    Personality = string.Empty,
                    CommunicationStyle = string.Empty,
                    SignatureBlock = string.Empty
                };

                targetRole.Characters.Add(character);
                usedNames.Add(fullName);
                usedEmails.Add(email);
                totalCount++;
            }
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
            if (string.IsNullOrWhiteSpace(entry.Email))
                continue;
            if (!characterLookup.TryGetValue(entry.Email, out var character))
                continue;

            character.IsKeyCharacter = entry.IsKeyCharacter;
            character.StorylineRelevance = entry.StorylineRelevance?.Trim() ?? character.StorylineRelevance;

            if (entry.PlotRelevance == null)
                continue;

            var plotRelevance = new Dictionary<Guid, string>();
            foreach (var kvp in entry.PlotRelevance)
            {
                if (!Guid.TryParse(kvp.Key, out var beatId))
                    continue;
                if (!beatIds.Contains(beatId))
                    continue;
                if (string.IsNullOrWhiteSpace(kvp.Value))
                    continue;

                plotRelevance[beatId] = kvp.Value.Trim();
            }

            if (plotRelevance.Count > 0)
            {
                character.PlotRelevance = plotRelevance;
            }
        }
    }

    private static string SerializeOrganizationForPrompt(Organization organization, bool includeCharacters)
    {
        var org = new
        {
            name = organization.Name,
            domain = organization.Domain,
            description = organization.Description,
            organizationType = organization.OrganizationType.ToString(),
            industry = organization.Industry.ToString(),
            state = organization.State.ToString(),
            plaintiff = organization.IsPlaintiff,
            defendant = organization.IsDefendant,
            departments = organization.Departments.Select(d => new
            {
                name = d.Name.ToString(),
                roles = d.Roles.Select(r => new
                {
                    name = r.Name.ToString(),
                    reportsToRole = r.ReportsToRole?.ToString(),
                    characters = includeCharacters
                        ? r.Characters.Select(c => new { firstName = c.FirstName, lastName = c.LastName, email = c.Email })
                        : null
                })
            })
        };

        return JsonSerializer.Serialize(org, new JsonSerializerOptions { WriteIndented = true });
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
