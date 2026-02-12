using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class CharacterGeneratorTests
{
    [Fact]
    public void AddCharactersToRoleAllowsDuplicateRoleNamesAcrossDepartments()
    {
        var organization = new Organization { Name = "Acme", Domain = "acme.com" };
        var operations = new Department { Name = DepartmentName.Operations };
        var engineering = new Department { Name = DepartmentName.Engineering };
        var opsRole = new Role { Name = RoleName.ProjectManager };
        var engRole = new Role { Name = RoleName.ProjectManager };
        operations.AddRole(opsRole);
        engineering.AddRole(engRole);
        organization.AddDepartment(operations);
        organization.AddDepartment(engineering);

        var roles = new List<CharacterGenerator.RoleCharactersDto>
        {
            new()
            {
                Name = RoleName.ProjectManager.ToString(),
                Characters = new List<CharacterGenerator.SimpleCharacterDto>
                {
                    new() { FirstName = "Ava", LastName = "Reed", Email = "ava@acme.com" }
                }
            }
        };

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CharacterGenerator.AddCharactersToRole(organization, roles, usedNames, usedEmails, allowSingleOccupant: true);

        Assert.Single(opsRole.Characters);
        Assert.Empty(engRole.Characters);
    }

    [Fact]
    public void AddCharactersToRoleSkipsSingleOccupantWhenAnyAlreadyFilled()
    {
        var organization = new Organization { Name = "Acme", Domain = "acme.com" };
        var executive = new Department { Name = DepartmentName.Executive };
        var finance = new Department { Name = DepartmentName.Finance };
        var execRole = new Role { Name = RoleName.ChiefFinancialOfficer };
        var financeRole = new Role { Name = RoleName.ChiefFinancialOfficer };
        var existing = new Character { FirstName = "Chris", LastName = "Ng", Email = "chris@acme.com" };
        execRole.AddCharacter(existing);
        executive.AddRole(execRole);
        finance.AddRole(financeRole);
        organization.AddDepartment(executive);
        organization.AddDepartment(finance);

        var roles = new List<CharacterGenerator.RoleCharactersDto>
        {
            new()
            {
                Name = RoleName.ChiefFinancialOfficer.ToString(),
                Characters = new List<CharacterGenerator.SimpleCharacterDto>
                {
                    new() { FirstName = "Taylor", LastName = "Morgan", Email = "taylor@acme.com" }
                }
            }
        };

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { existing.FullName };
        var usedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { existing.Email };

        CharacterGenerator.AddCharactersToRole(organization, roles, usedNames, usedEmails, allowSingleOccupant: false);

        Assert.Single(execRole.Characters);
        Assert.Empty(financeRole.Characters);
    }

    [Fact]
    public void ApplyStorylineRelevanceMapsByEmailAndBeatId()
    {
        var beatId = Guid.NewGuid();
        var beats = new List<StoryBeat>
        {
            new()
            {
                Id = beatId,
                Name = "Kickoff",
                Plot = "Initial planning.\nStakeholders align.",
                StartDate = DateTime.Today,
                EndDate = DateTime.Today
            }
        };

        var character = new Character
        {
            FirstName = "Ava",
            LastName = "Reed",
            Email = "ava@acme.com"
        };

        var relevance = new List<CharacterGenerator.CharacterRelevanceDto>
        {
            new()
            {
                Email = "AVA@ACME.COM",
                IsKeyCharacter = true,
                StorylineRelevance = "Owns the project scope. Coordinates cross-team deliverables.",
                PlotRelevance = new Dictionary<string, string>
                {
                    { beatId.ToString(), "Leads the kickoff planning. Aligns stakeholders on timeline." },
                    { Guid.NewGuid().ToString(), "Should be ignored." }
                }
            }
        };

        CharacterGenerator.ApplyStorylineRelevance(new List<Character> { character }, beats, relevance);

        Assert.True(character.IsKeyCharacter);
        Assert.Equal("Owns the project scope. Coordinates cross-team deliverables.", character.StorylineRelevance);
        Assert.Single(character.PlotRelevance);
        Assert.True(character.PlotRelevance.ContainsKey(beatId));
    }
}
