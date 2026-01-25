namespace ReelDiscovery.Models;

public class EmailThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StorylineId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public List<EmailMessage> Messages { get; set; } = new();

    public string RootMessageId => Messages.FirstOrDefault()?.MessageId ?? string.Empty;

    public override string ToString() => $"{Subject} ({Messages.Count} messages)";
}
