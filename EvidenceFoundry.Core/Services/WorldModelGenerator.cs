using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using Serilog;

namespace EvidenceFoundry.Services;

public sealed class WorldModelRequest
{
    public string? CaseArea { get; set; }
    public string? MatterType { get; set; }
    public string? Issue { get; set; }
    public string? IssueDescription { get; set; }
    public string? PlaintiffIndustry { get; set; }
    public string? DefendantIndustry { get; set; }
    public int PlaintiffOrganizationCount { get; set; }
    public int DefendantOrganizationCount { get; set; }
    public string? AdditionalUserContext { get; set; }
}

public partial class WorldModelGenerator
{
    private readonly OpenAIService _openAI;
    private readonly Random _rng;
    private readonly ILogger _logger;
    private static readonly JsonSerializerOptions WorldModelSerializerOptions = JsonSerializationDefaults.CaseInsensitiveWithEnums;
    private static readonly Dictionary<string, UsState> UsStateAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = UsState.Alabama,
        ["AK"] = UsState.Alaska,
        ["AZ"] = UsState.Arizona,
        ["AR"] = UsState.Arkansas,
        ["CA"] = UsState.California,
        ["CO"] = UsState.Colorado,
        ["CT"] = UsState.Connecticut,
        ["DE"] = UsState.Delaware,
        ["FL"] = UsState.Florida,
        ["GA"] = UsState.Georgia,
        ["HI"] = UsState.Hawaii,
        ["ID"] = UsState.Idaho,
        ["IL"] = UsState.Illinois,
        ["IN"] = UsState.Indiana,
        ["IA"] = UsState.Iowa,
        ["KS"] = UsState.Kansas,
        ["KY"] = UsState.Kentucky,
        ["LA"] = UsState.Louisiana,
        ["ME"] = UsState.Maine,
        ["MD"] = UsState.Maryland,
        ["MA"] = UsState.Massachusetts,
        ["MI"] = UsState.Michigan,
        ["MN"] = UsState.Minnesota,
        ["MS"] = UsState.Mississippi,
        ["MO"] = UsState.Missouri,
        ["MT"] = UsState.Montana,
        ["NE"] = UsState.Nebraska,
        ["NV"] = UsState.Nevada,
        ["NH"] = UsState.NewHampshire,
        ["NJ"] = UsState.NewJersey,
        ["NM"] = UsState.NewMexico,
        ["NY"] = UsState.NewYork,
        ["NC"] = UsState.NorthCarolina,
        ["ND"] = UsState.NorthDakota,
        ["OH"] = UsState.Ohio,
        ["OK"] = UsState.Oklahoma,
        ["OR"] = UsState.Oregon,
        ["PA"] = UsState.Pennsylvania,
        ["RI"] = UsState.RhodeIsland,
        ["SC"] = UsState.SouthCarolina,
        ["SD"] = UsState.SouthDakota,
        ["TN"] = UsState.Tennessee,
        ["TX"] = UsState.Texas,
        ["UT"] = UsState.Utah,
        ["VT"] = UsState.Vermont,
        ["VA"] = UsState.Virginia,
        ["WA"] = UsState.Washington,
        ["WV"] = UsState.WestVirginia,
        ["WI"] = UsState.Wisconsin,
        ["WY"] = UsState.Wyoming,
        ["DC"] = UsState.DistrictOfColumbia
    };

    public WorldModelGenerator(OpenAIService openAI, Random rng, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(openAI);
        ArgumentNullException.ThrowIfNull(rng);
        _openAI = openAI;
        _rng = rng;
        _logger = (logger ?? Serilog.Log.Logger).ForContext<WorldModelGenerator>();
        Log.WorldModelGeneratorInitialized(_logger);
    }

    public async Task<World> GenerateWorldModelAsync(
        WorldModelRequest request,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CaseArea))
            throw new ArgumentException("Case area is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.MatterType))
            throw new ArgumentException("Matter type is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Issue))
            throw new ArgumentException("Issue is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.IssueDescription))
            throw new ArgumentException("Issue description is required.", nameof(request));

        var stopwatch = Stopwatch.StartNew();
        var normalizedPlaintiffIndustry = GenerationRequestNormalizer.NormalizeIndustryPreference(request.PlaintiffIndustry);
        var normalizedDefendantIndustry = GenerationRequestNormalizer.NormalizeIndustryPreference(request.DefendantIndustry);
        var normalizedPlaintiffCount = GenerationRequestNormalizer.NormalizePartyCount(request.PlaintiffOrganizationCount);
        var normalizedDefendantCount = GenerationRequestNormalizer.NormalizePartyCount(request.DefendantOrganizationCount);

        Log.GeneratingWorldModel(_logger, normalizedPlaintiffCount, normalizedDefendantCount);

        var industriesForPrompt = ResolveIndustriesForPrompt(normalizedPlaintiffIndustry, normalizedDefendantIndustry, _rng);
        if (industriesForPrompt.Count == 0)
        {
            industriesForPrompt = new List<Industry> { Industry.Other };
            Log.NoIndustriesResolved(_logger);
        }

        var departmentRolesJson = DepartmentGenerator.BuildIndustryOrganizationRoleCatalogJson(
            industriesForPrompt,
            _logger);
        var orgTypes = EnumHelper.FormatEnumOptions<OrganizationType>();
        var industries = FormatIndustryOptions(industriesForPrompt);
        var states = EnumHelper.FormatEnumOptions<UsState>();

        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry World Model Generator.

Purpose
Create an extremely realistic, entirely fictional corporate world model (organizations + a minimal set of directly-involved people) for a pre-dispute scenario. This output will be used as immutable input to downstream generators that create the storyline, beats, scenes, and synthetic evidence.

Non-Negotiable Rules
1) Entirely fictional: invent all organizations, domains, people, products, internal project names, places, and events. Do NOT use real company names, real people, or identifiable real-world incidents. If user inputs contain real entities, replace them with fictional equivalents while preserving the scenario type.
2) Pre-dispute only: do NOT mention lawsuit, litigation, arbitration, claim, investigation, subpoena, regulator, enforcement, discovery, preservation, legal hold, or proceedings.
3) Professional/non-edgy: avoid explicit sexual content, graphic violence, hate/harassment/slurs, self-harm, or shock content.

World Model Requirements
- Create EXACTLY the required number of plaintiff organizations and defendant organizations.
- Each organization must include the following properties:
 - name (The name of the organization. Try to be realistic yet creative.)
 - domain (The email domain of the organization in the format ""domain.com"" or ""domain.org"", or similar depending on the type of organization. The domain must be plausible and match the organization's name.)
 - description (A 2-3 sentence ""Who We Are"" style description where the organization describes itself.)
 - organizationType (A realistic organization type for the organization based on the generated description and case/issue type. Must match one of the OrganizationType Enum values provided in the USER prompt.)
 - industry (If the USER prompt provides a specific industry, it must match exactly. If the USER prompt says ""Random"", choose any allowed industry value and use that value for the organization.)
 - state (The US state that the organization is headquartered in. Must match one of the UsState enum values provided in the USER prompt.)
 - founded (The year the organization was founded. It must be plausible. For example, 1980-2026)
- Ensure plaintiffs and defendants are distinct (no org appears on both sides).
- Each key person must include the following properties:
 - role (The user's role within the organization. Must match one the role values provided in the USER prompt.)
 - department (The department the user's role belongs to within the organization. Must match the selected role provided in the USER prompt.)
 - firstName (the person's first name.)
 - lastName (the person's last name.)
 - email (the person's email address. this MUST be provided in the format ""firstName.lastName@domain.com"" where ""domain.com"" matches the domain of the organization the person is a part of.)
 - involvement (must match Actor or Target or Intermediary, as described below.)
 - involvementSummary (a short summary of how they're involved as a key person.)

Key People
Generate ONLY people who are directly involved in the the core conduct and impact:
- Actor: directly carries out or approves the relevant actions that create the issue (initiate/approve/transmit/negotiate/misrepresent/misuse/etc.).
- Target: the person those actions are directed toward or who is materially affected.
- Intermediary (optional): include only if necessary to make the scenario coherent (single gatekeeper/approver/recipient/coordinator).
Exclude background/support staff unless directly involved.

Key People Constraints
- For each key person ensure to use department and role values selected ONLY by the allowed Department and Role values provided by the USER prompt.
- When choosing a department and role, use ONLY the roles listed under the organization's chosen organizationType within its industry in the provided department/role JSON.
- Keep the keyPeople list minimal but sufficient (typically 4–10 total, unless USER input clearly requires more).

Additional Constraints
- Do not write the storyline here; do not add beats/scenes/timeline.

Output Rules (Strict)
- Output must match the USER-provided schema exactly.");

        var schema = """
{
   "worldModel":{
      "caseContext":{
         "caseArea":"string",
         "matterType":"string",
         "issue":"string",
         "issueDescription":"string"
      },
      "organizations":{
         "plaintiffs":[
            {
               "name":"string",
               "domain":"string",
               "description":"string",
               "organizationType":"string",
               "industry":"string",
               "state":"string",
               "founded":0,
               "keyPeople":[
                  {
                  "role":"string",
                  "department":"string",
                  "firstName":"string",
                  "lastName":"string",
                  "email":"string",
                  "involvement":"Actor|Target|Intermediary",
                  "involvementSummary":"string"
                }
               ]
            }
         ],
         "defendants":[
            {
               "name":"string",
               "domain":"string",
               "description":"string",
               "organizationType":"string",
               "industry":"string",
               "state":"string",
               "founded":0,
               "keyPeople":[
                  {
                  "role":"string",
                  "department":"string",
                  "firstName":"string",
                  "lastName":"string",
                  "email":"string",
                  "involvement":"Actor|Target|Intermediary",
                  "involvementSummary":"string"
                }
               ]
            }
         ]
      }
   }
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"CASE INPUTS
- Case Area: {request.CaseArea}
- Matter Type: {request.MatterType}
- Issue: {request.Issue}
- Issue Description (scenario ground truth): {request.IssueDescription}

ORG REQUIREMENTS
- Plaintiff Industry: {normalizedPlaintiffIndustry}
- Defendant Industry: {normalizedDefendantIndustry}
- Number of Plaintiff Orgs: {normalizedPlaintiffCount}
- Number of Defendant Orgs: {normalizedDefendantCount}

ADDITIONAL CONTEXT
{(string.IsNullOrWhiteSpace(request.AdditionalUserContext) ? "None" : request.AdditionalUserContext)}

ALLOWED INDUSTRIES (use exact values only)
{industries}

ORGANIZATION TYPES (select ONLY from these for each organization)
{orgTypes}

ALLOWED US STATES (use exact values only)
{states}

DEPARTMENTS AND ROLES (select only roles from these for each key person)
{departmentRolesJson}

TASK
Generate the world model ONLY: organizations + minimal directly-involved key people. No storyline.
", PromptScaffolding.JsonSchemaSection(schema));

        progress?.Report("Generating world model...");

        try
        {
            var response = await _openAI.GetJsonCompletionAsync<WorldModelResponse>(
                systemPrompt,
                userPrompt,
                "World Model Generation",
                ct);

            if (response == null)
                throw new InvalidOperationException("Failed to generate a world model.");

            var world = ParseWorldModelResponse(response, normalizedPlaintiffCount, normalizedDefendantCount);
            Log.WorldModelGenerated(
                _logger,
                world.Plaintiffs.Count,
                world.Defendants.Count,
                world.KeyPeople.Count,
                stopwatch.ElapsedMilliseconds);
            return world;
        }
        catch (OperationCanceledException)
        {
            Log.WorldModelGenerationCanceled(_logger, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Log.WorldModelGenerationFailed(_logger, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
    }

    private static class Log
    {
        public static void WorldModelGeneratorInitialized(ILogger logger)
            => logger.Debug("WorldModelGenerator initialized.");

        public static void GeneratingWorldModel(ILogger logger, int plaintiffCount, int defendantCount)
            => logger.Information(
                "Generating world model with {PlaintiffCount} plaintiff(s) and {DefendantCount} defendant(s).",
                plaintiffCount,
                defendantCount);

        public static void NoIndustriesResolved(ILogger logger)
            => logger.Warning("No industries resolved; defaulting to Industry.Other for world model generation.");

        public static void WorldModelGenerated(
            ILogger logger,
            int plaintiffCount,
            int defendantCount,
            int keyPersonCount,
            long durationMs)
            => logger.Information(
                "Generated world model with {PlaintiffCount} plaintiff(s), {DefendantCount} defendant(s), and {KeyPersonCount} key people in {DurationMs} ms.",
                plaintiffCount,
                defendantCount,
                keyPersonCount,
                durationMs);

        public static void WorldModelGenerationCanceled(ILogger logger, long durationMs)
            => logger.Warning("World model generation canceled after {DurationMs} ms.", durationMs);

        public static void WorldModelGenerationFailed(ILogger logger, long durationMs, Exception exception)
            => logger.Error(exception, "World model generation failed after {DurationMs} ms.", durationMs);
    }

    internal static World ParseWorldModelJson(string json, int plaintiffCount, int defendantCount)
    {
        var response = JsonSerializer.Deserialize<WorldModelResponse>(json, WorldModelSerializerOptions);
        if (response == null)
            throw new InvalidOperationException("World model JSON could not be parsed.");

        return ParseWorldModelResponse(response, plaintiffCount, defendantCount);
    }

    internal static World ParseWorldModelResponse(WorldModelResponse response, int plaintiffCount, int defendantCount)
    {
        if (response.WorldModel == null)
            throw new InvalidOperationException("World model response was empty.");

        var caseContext = response.WorldModel.CaseContext ?? new CaseContextDto();
        var caseContextModel = new CaseContext
        {
            CaseArea = caseContext.CaseArea?.Trim() ?? string.Empty,
            MatterType = caseContext.MatterType?.Trim() ?? string.Empty,
            Issue = caseContext.Issue?.Trim() ?? string.Empty,
            IssueDescription = caseContext.IssueDescription?.Trim() ?? string.Empty
        };
        caseContextModel.Id = DeterministicIdHelper.CreateGuid(
            "case-context",
            caseContextModel.CaseArea,
            caseContextModel.MatterType,
            caseContextModel.Issue,
            caseContextModel.IssueDescription);

        var world = new World
        {
            Id = DeterministicIdHelper.CreateGuid(
                "world",
                caseContextModel.CaseArea,
                caseContextModel.MatterType,
                caseContextModel.Issue,
                caseContextModel.IssueDescription),
            CaseContext = caseContextModel
        };

        var organizationsDto = response.WorldModel.Organizations ?? new OrganizationGroupDto();
        world.SetPlaintiffs(BuildOrganizations(organizationsDto.Plaintiffs, true, false, "plaintiff"));
        world.SetDefendants(BuildOrganizations(organizationsDto.Defendants, false, true, "defendant"));

        if (world.Plaintiffs.Count != plaintiffCount)
            throw new InvalidOperationException($"Expected {plaintiffCount} plaintiff organization(s) but received {world.Plaintiffs.Count}.");
        if (world.Defendants.Count != defendantCount)
            throw new InvalidOperationException($"Expected {defendantCount} defendant organization(s) but received {world.Defendants.Count}.");

        var orgNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateOrg = world.Plaintiffs
            .Concat(world.Defendants)
            .Where(org => !orgNames.Add(org.Name))
            .Select(org => org.Name)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(duplicateOrg))
            throw new InvalidOperationException($"Duplicate organization name detected: '{duplicateOrg}'.");

        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characters = world.Plaintiffs
            .Concat(world.Defendants)
            .SelectMany(org => org.EnumerateCharacters())
            .Select(assignment => assignment.Character)
            .ToList();
        var duplicateCharacter = characters.FirstOrDefault(character => !seenEmails.Add(character.Email));
        if (duplicateCharacter != null)
            throw new InvalidOperationException($"Duplicate key person email detected: '{duplicateCharacter.Email}'.");
        world.SetKeyPeople(characters);

        return world;
    }

    private static List<Organization> BuildOrganizations(
        List<OrganizationDto>? organizations,
        bool isPlaintiff,
        bool isDefendant,
        string label)
    {
        if (organizations == null)
            return new List<Organization>();

        var results = new List<Organization>();
        foreach (var dto in organizations)
        {
            if (dto == null)
                continue;

            var organization = BuildOrganization(dto, isPlaintiff, isDefendant, label);
            AddKeyPeople(organization, dto.KeyPeople);
            results.Add(organization);
        }

        return results;
    }

    private static void AddKeyPeople(Organization organization, List<KeyPersonDto>? keyPeople)
    {
        if (keyPeople == null || keyPeople.Count == 0)
            return;

        foreach (var person in keyPeople)
        {
            AddKeyPerson(organization, person);
        }
    }

    private static Organization BuildOrganization(
        OrganizationDto dto,
        bool isPlaintiff,
        bool isDefendant,
        string label)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"{label} organization name is required.");

        var organizationType = ParseRequiredEnum<OrganizationType>(
            dto.OrganizationType,
            $"Invalid organizationType '{dto.OrganizationType}' for organization '{name}'.");
        var industry = ParseRequiredEnum<Industry>(
            dto.Industry,
            $"Invalid industry '{dto.Industry}' for organization '{name}'.");
        var state = ParseRequiredUsState(
            dto.State,
            $"Invalid state '{dto.State}' for organization '{name}'.");

        var domain = (dto.Domain ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException($"Organization '{name}' is missing a domain.");
        if (domain.Contains('@'))
            throw new InvalidOperationException($"Organization '{name}' domain should not contain '@': '{domain}'.");

        var foundedYear = ValidateFoundedYear(dto.Founded, name);

        return new Organization
        {
            Id = DeterministicIdHelper.CreateGuid("organization", name),
            Name = name,
            Domain = domain,
            Description = dto.Description?.Trim() ?? string.Empty,
            OrganizationType = organizationType,
            Industry = industry,
            State = state,
            Founded = new DateTime(foundedYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            IsPlaintiff = isPlaintiff,
            IsDefendant = isDefendant
        };
    }

    private static TEnum ParseRequiredEnum<TEnum>(string? value, string errorMessage)
        where TEnum : struct, Enum
    {
        if (!EnumHelper.TryParseEnum(value, out TEnum parsed))
            throw new InvalidOperationException(errorMessage);

        return parsed;
    }

    private static UsState ParseRequiredUsState(string? value, string errorMessage)
    {
        if (EnumHelper.TryParseEnum(value, out UsState parsed))
            return parsed;

        if (!string.IsNullOrWhiteSpace(value) &&
            UsStateAbbreviations.TryGetValue(value.Trim(), out var mapped))
            return mapped;

        throw new InvalidOperationException(errorMessage);
    }

    private static int ValidateFoundedYear(int foundedYear, string organizationName)
    {
        if (foundedYear <= 0)
            throw new InvalidOperationException($"Organization '{organizationName}' must include a founded year.");

        var maxYear = Clock.UtcNowDateTime.Year;
        if (foundedYear < 1980 || foundedYear > maxYear)
            throw new InvalidOperationException($"Organization '{organizationName}' founded year must be between 1980 and {maxYear}.");

        return foundedYear;
    }

    private static void AddKeyPerson(Organization organization, KeyPersonDto person)
    {
        var (firstName, lastName, email) = NormalizeKeyPersonNames(person);
        var departmentName = ParseRequiredEnum<DepartmentName>(
            person.Department,
            $"Invalid department '{person.Department}' for key person '{firstName} {lastName}' in '{organization.Name}'.");
        var roleName = ParseRequiredEnum<RoleName>(
            person.Role,
            $"Invalid role '{person.Role}' for key person '{firstName} {lastName}' in '{organization.Name}'.");
        var involvement = ValidateKeyPersonInvolvement(person, organization.Name, organization.Domain, firstName, lastName);

        var department = GetOrCreateDepartment(organization, departmentName);
        var role = GetOrCreateRole(organization, department, roleName);

        var character = new Character
        {
            Id = DeterministicIdHelper.CreateGuid(
                "character",
                organization.Id.ToString("N"),
                email.ToLowerInvariant()),
            RoleId = role.Id,
            DepartmentId = department.Id,
            OrganizationId = organization.Id,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            IsKeyCharacter = true,
            StorylineRelevance = string.Empty,
            Involvement = involvement,
            InvolvementSummary = person.InvolvementSummary?.Trim() ?? string.Empty
        };

        DeterministicPersonalityHelper.EnsurePersonality(character);
        role.AddCharacter(character);
    }

    private static (string firstName, string lastName, string email) NormalizeKeyPersonNames(KeyPersonDto person)
    {
        var firstName = person.FirstName?.Trim() ?? string.Empty;
        var lastName = person.LastName?.Trim() ?? string.Empty;
        var email = person.Email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new InvalidOperationException("Key person must include first and last name.");
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException($"Key person '{firstName} {lastName}' is missing an email.");

        return (firstName, lastName, email);
    }

    private static string ValidateKeyPersonInvolvement(
        KeyPersonDto person,
        string organizationName,
        string organizationDomain,
        string firstName,
        string lastName)
    {
        var expectedEmail = $"{firstName}.{lastName}@{organizationDomain}".ToLowerInvariant();
        if (!string.Equals(person.Email?.Trim(), expectedEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Key person '{firstName} {lastName}' email must be '{expectedEmail}'.");

        var involvement = person.Involvement?.Trim() ?? string.Empty;
        if (!IsValidInvolvement(involvement))
            throw new InvalidOperationException($"Key person '{firstName} {lastName}' in '{organizationName}' has invalid involvement '{person.Involvement}'.");

        return involvement;
    }

    private static Department GetOrCreateDepartment(Organization organization, DepartmentName departmentName)
    {
        var department = organization.Departments.FirstOrDefault(d => d.Name == departmentName);
        if (department != null)
        {
            if (department.Id == Guid.Empty)
            {
                department.Id = DeterministicIdHelper.CreateGuid(
                    "department",
                    organization.Id.ToString("N"),
                    departmentName.ToString());
            }
            return department;
        }

        department = new Department
        {
            Id = DeterministicIdHelper.CreateGuid(
                "department",
                organization.Id.ToString("N"),
                departmentName.ToString()),
            Name = departmentName,
            OrganizationId = organization.Id
        };
        organization.AddDepartment(department);
        return department;
    }

    private static Role GetOrCreateRole(Organization organization, Department department, RoleName roleName)
    {
        var role = department.Roles.FirstOrDefault(r => r.Name == roleName);
        if (role != null)
        {
            if (role.Id == Guid.Empty)
            {
                role.Id = DeterministicIdHelper.CreateGuid(
                    "role",
                    organization.Id.ToString("N"),
                    department.Name.ToString(),
                    roleName.ToString());
            }
            return role;
        }

        role = new Role
        {
            Id = DeterministicIdHelper.CreateGuid(
                "role",
                organization.Id.ToString("N"),
                department.Name.ToString(),
                roleName.ToString()),
            Name = roleName,
            DepartmentId = department.Id,
            OrganizationId = organization.Id
        };
        department.AddRole(role);
        return role;
    }

    internal static List<Industry> ResolveIndustriesForPrompt(string plaintiffIndustry, string defendantIndustry, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var industries = new List<Industry>();

        if (GenerationRequestNormalizer.IsRandomIndustry(plaintiffIndustry))
        {
            industries.Add(GetRandomIndustry(rng));
        }
        else if (EnumHelper.TryParseEnum(plaintiffIndustry, out Industry plaintiff))
        {
            industries.Add(plaintiff);
        }

        if (GenerationRequestNormalizer.IsRandomIndustry(defendantIndustry))
        {
            industries.Add(GetRandomIndustry(rng));
        }
        else if (EnumHelper.TryParseEnum(defendantIndustry, out Industry defendant))
        {
            industries.Add(defendant);
        }

        return industries.Distinct().ToList();
    }

    private static Industry GetRandomIndustry(Random rng)
    {
        var values = Enum.GetValues<Industry>();
        return values[rng.Next(values.Length)];
    }

    private static bool IsValidInvolvement(string involvement)
    {
        return involvement.Equals("Actor", StringComparison.OrdinalIgnoreCase)
            || involvement.Equals("Target", StringComparison.OrdinalIgnoreCase)
            || involvement.Equals("Intermediary", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatIndustryOptions(IEnumerable<Industry> industries)
    {
        var options = industries
            .Distinct()
            .Select(industry => $"{industry} ({EnumHelper.HumanizeEnumName(industry.ToString())})");

        return string.Join(", ", options);
    }

    public class WorldModelResponse
    {
        [JsonPropertyName("worldModel")]
        public WorldModelDto? WorldModel { get; set; }
    }

    public class WorldModelDto
    {
        [JsonPropertyName("caseContext")]
        public CaseContextDto? CaseContext { get; set; }

        [JsonPropertyName("organizations")]
        public OrganizationGroupDto? Organizations { get; set; }
    }

    public class CaseContextDto
    {
        [JsonPropertyName("caseArea")]
        public string? CaseArea { get; set; }

        [JsonPropertyName("matterType")]
        public string? MatterType { get; set; }

        [JsonPropertyName("issue")]
        public string? Issue { get; set; }

        [JsonPropertyName("issueDescription")]
        public string? IssueDescription { get; set; }
    }

    public class OrganizationGroupDto
    {
        [JsonPropertyName("plaintiffs")]
        public List<OrganizationDto>? Plaintiffs { get; set; }

        [JsonPropertyName("defendants")]
        public List<OrganizationDto>? Defendants { get; set; }
    }

    public class OrganizationDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("domain")]
        public string? Domain { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("organizationType")]
        public string? OrganizationType { get; set; }

        [JsonPropertyName("industry")]
        public string? Industry { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("founded")]
        public int Founded { get; set; }

        [JsonPropertyName("keyPeople")]
        public List<KeyPersonDto>? KeyPeople { get; set; }
    }

    public class KeyPersonDto
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("department")]
        public string? Department { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("involvement")]
        public string? Involvement { get; set; }

        [JsonPropertyName("involvementSummary")]
        public string? InvolvementSummary { get; set; }
    }
}
