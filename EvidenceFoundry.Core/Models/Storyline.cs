namespace EvidenceFoundry.Models;

public class Storyline
{
    private readonly List<StoryOutline> _plotOutline = new();
    private readonly List<string> _tensionDrivers = new();
    private readonly List<string> _ambiguities = new();
    private readonly List<string> _redHerrings = new();
    private readonly List<string> _evidenceThemes = new();
    private readonly List<StoryBeat> _beats = new();
    private readonly List<Organization> _organizations = new();

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Logline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<StoryOutline> PlotOutline => _plotOutline;
    public IReadOnlyList<string> TensionDrivers => _tensionDrivers;
    public IReadOnlyList<string> Ambiguities => _ambiguities;
    public IReadOnlyList<string> RedHerrings => _redHerrings;
    public IReadOnlyList<string> EvidenceThemes => _evidenceThemes;
    public IReadOnlyList<StoryBeat> Beats => _beats;
    public IReadOnlyList<Organization> Organizations => _organizations;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int EmailCount => Beats?.Sum(b => b.EmailCount) ?? 0;
    public int ThreadCount => Beats?.Sum(b => b.Threads?.Count ?? 0) ?? 0;

    public void SetPlotOutline(IEnumerable<StoryOutline> outline)
    {
        ArgumentNullException.ThrowIfNull(outline);
        _plotOutline.Clear();
        _plotOutline.AddRange(outline);
    }

    public void SetTensionDrivers(IEnumerable<string> drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        _tensionDrivers.Clear();
        _tensionDrivers.AddRange(drivers);
    }

    public void SetAmbiguities(IEnumerable<string> ambiguities)
    {
        ArgumentNullException.ThrowIfNull(ambiguities);
        _ambiguities.Clear();
        _ambiguities.AddRange(ambiguities);
    }

    public void SetRedHerrings(IEnumerable<string> redHerrings)
    {
        ArgumentNullException.ThrowIfNull(redHerrings);
        _redHerrings.Clear();
        _redHerrings.AddRange(redHerrings);
    }

    public void SetEvidenceThemes(IEnumerable<string> themes)
    {
        ArgumentNullException.ThrowIfNull(themes);
        _evidenceThemes.Clear();
        _evidenceThemes.AddRange(themes);
    }

    public void SetBeats(IEnumerable<StoryBeat> beats)
    {
        ArgumentNullException.ThrowIfNull(beats);
        _beats.Clear();
        _beats.AddRange(beats);
    }

    public void AddBeat(StoryBeat beat) => _beats.Add(beat);

    public void SetOrganizations(IEnumerable<Organization> organizations)
    {
        ArgumentNullException.ThrowIfNull(organizations);
        _organizations.Clear();
        _organizations.AddRange(organizations);
    }

    public override string ToString() => Title;
}
