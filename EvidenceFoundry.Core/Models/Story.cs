namespace EvidenceFoundry.Models;

public class StoryBeat
{
    private readonly List<EmailThread> _threads = new();

    public Guid Id { get; set; }
    public Guid StorylineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Plot { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int EmailCount { get; set; }
    public IReadOnlyList<EmailThread> Threads => _threads;

    public void SetThreads(IEnumerable<EmailThread> threads)
    {
        ArgumentNullException.ThrowIfNull(threads);
        _threads.Clear();
        _threads.AddRange(threads);
    }

    public void AddThread(EmailThread thread) => _threads.Add(thread);

    public void ClearThreads() => _threads.Clear();
}
