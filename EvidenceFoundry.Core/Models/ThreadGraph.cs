namespace EvidenceFoundry.Models;

public sealed class ThreadGraph
{
    private readonly Dictionary<Guid, EmailMessage> _nodes;
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> _childrenByParent;
    private readonly List<Guid> _chronologicalOrder;

    private ThreadGraph(
        Guid threadId,
        Guid rootEmailId,
        Dictionary<Guid, EmailMessage> nodes,
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> childrenByParent,
        List<Guid> chronologicalOrder)
    {
        ThreadId = threadId;
        RootEmailId = rootEmailId;
        _nodes = nodes;
        _childrenByParent = childrenByParent;
        _chronologicalOrder = chronologicalOrder;
    }

    public Guid ThreadId { get; }
    public Guid RootEmailId { get; }
    public IReadOnlyDictionary<Guid, EmailMessage> Nodes => _nodes;
    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> ChildrenByParent => _childrenByParent;
    public IReadOnlyList<Guid> ChronologicalOrder => _chronologicalOrder;

    public static ThreadGraph Build(EmailThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        var nodes = thread.EmailMessages.ToDictionary(e => e.Id);
        var childrenByParent = new Dictionary<Guid, List<Guid>>();

        foreach (var email in thread.EmailMessages)
        {
            if (email.ParentEmailId is not { } parentId)
                continue;

            if (!childrenByParent.TryGetValue(parentId, out var children))
            {
                children = new List<Guid>();
                childrenByParent[parentId] = children;
            }

            children.Add(email.Id);
        }

        foreach (var children in childrenByParent.Values)
        {
            children.Sort((a, b) =>
            {
                var left = nodes[a];
                var right = nodes[b];
                var dateCompare = left.SentDate.CompareTo(right.SentDate);
                if (dateCompare != 0)
                    return dateCompare;
                var sequenceCompare = left.SequenceInThread.CompareTo(right.SequenceInThread);
                if (sequenceCompare != 0)
                    return sequenceCompare;
                return left.Id.CompareTo(right.Id);
            });
        }

        var chronological = thread.EmailMessages
            .OrderBy(e => e.SentDate)
            .ThenBy(e => e.SequenceInThread)
            .ThenBy(e => e.Id)
            .Select(e => e.Id)
            .ToList();

        var rootEmailId = thread.EmailMessages.FirstOrDefault(e => e.ParentEmailId == null)?.Id
                          ?? thread.EmailMessages.FirstOrDefault()?.Id
                          ?? Guid.Empty;

        var readOnlyChildrenByParent = childrenByParent.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<Guid>)kvp.Value.AsReadOnly());

        return new ThreadGraph(thread.Id, rootEmailId, nodes, readOnlyChildrenByParent, chronological);
    }
}
