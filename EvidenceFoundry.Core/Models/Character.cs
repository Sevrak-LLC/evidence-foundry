namespace EvidenceFoundry.Models;

public class Character
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoleId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string CommunicationStyle { get; set; } = string.Empty;
    public string SignatureBlock { get; set; } = string.Empty;
    public string Involvement { get; set; } = string.Empty;
    public string InvolvementSummary { get; set; } = string.Empty;
    public bool IsKeyCharacter { get; set; }
    public string StorylineRelevance { get; set; } = string.Empty;
    public Dictionary<Guid, string> PlotRelevance { get; set; } = new();

    /// <summary>
    /// TTS voice for voicemails: alloy, echo, fable, onyx, nova, shimmer
    /// </summary>
    public string VoiceId { get; set; } = "alloy";

    public string FullName => $"{FirstName} {LastName}".Trim();
    public string DisplayName => $"{FullName} <{Email}>";
    public string Domain => Email.Contains('@') ? Email.Split('@')[1] : string.Empty;

    public override string ToString() => DisplayName;
}
