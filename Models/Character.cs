namespace ReelDiscovery.Models;

public class Character
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string PersonalityNotes { get; set; } = string.Empty;
    public string SignatureBlock { get; set; } = string.Empty;
    public bool IsExternal { get; set; } = false;

    /// <summary>
    /// TTS voice for voicemails: alloy, echo, fable, onyx, nova, shimmer
    /// </summary>
    public string VoiceId { get; set; } = "alloy";

    public string FullName => $"{FirstName} {LastName}";
    public string DisplayName => $"{FullName} <{Email}>";
    public string Domain => Email.Contains('@') ? Email.Split('@')[1] : string.Empty;

    public override string ToString() => DisplayName;
}
