namespace EvidenceFoundry.Models;

public class EmailThread
{
    private readonly List<Organization> _organizationParticipants = new();
    private readonly List<Role> _roleParticipants = new();
    private readonly List<Character> _characterParticipants = new();
    private readonly List<EmailMessage> _emailMessages = new();

    public enum ThreadRelevance
    {
        NonResponsive,
        Responsive
    }

    public Guid Id { get; set; }
    public Guid StoryBeatId { get; set; }
    public Guid StorylineId { get; set; }
    public EmailThreadScope Scope { get; set; } = EmailThreadScope.Internal;
    public string Topic { get; set; } = string.Empty;
    public IReadOnlyList<Organization> OrganizationParticipants => _organizationParticipants;
    public IReadOnlyList<Role> RoleParticipants => _roleParticipants;
    public IReadOnlyList<Character> CharacterParticipants => _characterParticipants;
    public IReadOnlyList<EmailMessage> EmailMessages => _emailMessages;
    public ThreadRelevance Relevance { get; set; } = ThreadRelevance.NonResponsive;
    public bool IsHot { get; set; }

    public string DisplaySubject => EmailMessages.FirstOrDefault()?.Subject ?? string.Empty;
    public string RootMessageId => EmailMessages.FirstOrDefault()?.MessageId ?? string.Empty;

    public void SetOrganizationParticipants(IEnumerable<Organization> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        _organizationParticipants.Clear();
        _organizationParticipants.AddRange(participants);
    }

    public void ClearOrganizationParticipants() => _organizationParticipants.Clear();

    public void SetRoleParticipants(IEnumerable<Role> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        _roleParticipants.Clear();
        _roleParticipants.AddRange(participants);
    }

    public void ClearRoleParticipants() => _roleParticipants.Clear();

    public void SetCharacterParticipants(IEnumerable<Character> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        _characterParticipants.Clear();
        _characterParticipants.AddRange(participants);
    }

    public void ClearCharacterParticipants() => _characterParticipants.Clear();

    public void SetEmailMessages(IEnumerable<EmailMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _emailMessages.Clear();
        _emailMessages.AddRange(messages);
    }

    public void ClearEmailMessages() => _emailMessages.Clear();

    public void AddEmailMessage(EmailMessage message) => _emailMessages.Add(message);

    public override string ToString()
    {
        var subject = string.IsNullOrWhiteSpace(DisplaySubject) ? "Untitled thread" : DisplaySubject;
        return $"{subject} ({EmailMessages.Count} messages)";
    }
}
