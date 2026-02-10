namespace EvidenceFoundry.Models;

public class EmailThread
{
    public enum ThreadRelevance
    {
        NonResponsive,
        Responsive
    }

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StoryBeatId { get; set; }
    public Guid StorylineId { get; set; }
    public EmailThreadScope Scope { get; set; } = EmailThreadScope.Internal;
    public string Topic { get; set; } = string.Empty;
    public List<Organization> OrganizationParticipants { get; set; } = new();
    public List<Role> RoleParticipants { get; set; } = new();
    public List<Character> CharacterParticipants { get; set; } = new();
    public List<EmailMessage> EmailMessages { get; set; } = new();
    public ThreadRelevance Relevance { get; set; } = ThreadRelevance.NonResponsive;
    public bool IsHot { get; set; }

    public string DisplaySubject => EmailMessages.FirstOrDefault()?.Subject ?? string.Empty;
    public string RootMessageId => EmailMessages.FirstOrDefault()?.MessageId ?? string.Empty;

    public override string ToString()
    {
        var subject = string.IsNullOrWhiteSpace(DisplaySubject) ? "Untitled thread" : DisplaySubject;
        return $"{subject} ({EmailMessages.Count} messages)";
    }
}
