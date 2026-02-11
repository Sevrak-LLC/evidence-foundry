namespace EvidenceFoundry.Helpers;

/// <summary>
/// A semi-transparent overlay panel with rotating fun loading messages.
/// Shows over a control while AI generation is in progress.
/// </summary>
public class LoadingOverlay : Panel
{
    private readonly DoubleBufferedLabel _lblMessage;
    private readonly System.Windows.Forms.Timer _messageTimer;
    private readonly System.Windows.Forms.Timer _dotsTimer;
    private int _currentMessageIndex = 0;
    private int _dotCount = 0;
    private readonly string[] _messages;
    private string _currentBaseMessage = "";

    private static readonly string[] StorylineMessageOptions = new[]
    {
        "Gathering lore",
        "Weaving plot threads",
        "Consulting the muses",
        "Brewing drama",
        "Spinning tales",
        "Crafting intrigue",
        "Summoning storyline",
        "Mining for narrative gold",
        "Connecting the dots",
        "Building tension",
        "Plotting twists",
        "Assembling the saga"
    };

    private static readonly string[] CharacterMessages = new[]
    {
        "Casting characters",
        "Assigning personalities",
        "Generating backstories",
        "Creating personas",
        "Building the ensemble",
        "Recruiting talent",
        "Defining relationships",
        "Writing character bios",
        "Assembling the cast",
        "Designing identities",
        "Forging personalities",
        "Breathing life into names"
    };

    private static readonly string[] StoryBeatMessages = new[]
    {
        "Outlining beats",
        "Sequencing events",
        "Charting the arc",
        "Tightening timelines",
        "Layering twists",
        "Balancing motives",
        "Refining the narrative",
        "Mapping the fallout",
        "Connecting the dots",
        "Sculpting the climax"
    };

    private static readonly string[] WorldModelMessages = new[]
    {
        "Sketching the world",
        "Inventing organizations",
        "Mapping key people",
        "Detailing the landscape"
    };

    public enum LoadingType
    {
        Storyline,
        Characters,
        StoryBeats,
        WorldModel
    }

    public LoadingOverlay(LoadingType type)
    {
        _messages = type switch
        {
            LoadingType.Storyline => StorylineMessageOptions,
            LoadingType.Characters => CharacterMessages,
            LoadingType.StoryBeats => StoryBeatMessages,
            LoadingType.WorldModel => WorldModelMessages,
            _ => StorylineMessageOptions
        };

        // Enable double buffering to prevent flickering
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                      ControlStyles.AllPaintingInWmPaint |
                      ControlStyles.UserPaint, true);
        this.UpdateStyles();

        // Semi-transparent dark background
        this.BackColor = Color.FromArgb(220, 30, 30, 35);
        this.Visible = false;

        // Single centered message label with double buffering
        _lblMessage = new DoubleBufferedLabel
        {
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill
        };

        this.Controls.Add(_lblMessage);

        // Timer to rotate messages every 5-8 seconds
        _messageTimer = new System.Windows.Forms.Timer
        {
            Interval = 5000 + Random.Shared.Next(3000)
        };
        _messageTimer.Tick += (s, e) =>
        {
            _currentMessageIndex = (_currentMessageIndex + 1) % _messages.Length;
            _currentBaseMessage = _messages[_currentMessageIndex];
            UpdateDisplayText();
            _messageTimer.Interval = 5000 + Random.Shared.Next(3000);
        };

        // Timer for animated dots
        _dotsTimer = new System.Windows.Forms.Timer
        {
            Interval = 400
        };
        _dotsTimer.Tick += (s, e) =>
        {
            _dotCount = (_dotCount + 1) % 4;
            UpdateDisplayText();
        };
    }

    private void UpdateDisplayText()
    {
        var dots = new string('.', _dotCount);
        _lblMessage.Text = _currentBaseMessage + dots;
    }

    public void Show(Control parent)
    {
        if (parent == null) return;

        // Position overlay to cover the parent
        this.Location = new Point(0, 0);
        this.Size = parent.Size;
        this.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // Randomize starting message
        _currentMessageIndex = Random.Shared.Next(_messages.Length);
        _currentBaseMessage = _messages[_currentMessageIndex];
        _dotCount = 0;
        UpdateDisplayText();

        // Add to parent's controls and bring to front
        if (!parent.Controls.Contains(this))
        {
            parent.Controls.Add(this);
        }
        this.BringToFront();
        this.Visible = true;

        // Start timers
        _messageTimer.Start();
        _dotsTimer.Start();
    }

    public new void Hide()
    {
        _messageTimer.Stop();
        _dotsTimer.Stop();
        this.Visible = false;

        // Remove from parent
        if (this.Parent != null)
        {
            this.Parent.Controls.Remove(this);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _messageTimer?.Stop();
            _messageTimer?.Dispose();
            _dotsTimer?.Stop();
            _dotsTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// A label with double buffering enabled to prevent flickering during updates.
    /// </summary>
    private class DoubleBufferedLabel : Label
    {
        public DoubleBufferedLabel()
        {
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.UpdateStyles();
        }
    }
}
