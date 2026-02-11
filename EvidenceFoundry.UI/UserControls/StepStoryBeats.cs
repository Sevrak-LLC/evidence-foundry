using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.UserControls;

public class StepStoryBeats : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private FlowLayoutPanel _storyFlow = null!;
    private Panel _storyScroll = null!;
    private Button _btnRegenerate = null!;
    private Label _lblStatus = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private bool _isLoading = false;

    public string StepTitle => "Review Story Beats";
    public bool CanMoveNext => !_isLoading && (_state?.Storyline?.Beats.Count ?? 0) > 0;
    public bool CanMoveBack => !_isLoading;
    public string NextButtonText => "Next >";

    public event EventHandler? StateChanged;

    public StepStoryBeats()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Padding = new Padding(20);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblHeader = new Label
        {
            Text = "The AI expanded the narrative into story beats. Review below or regenerate.",
            AutoSize = true,
            Margin = new Padding(0, 6, 10, 0)
        };
        headerLayout.Controls.Add(lblHeader, 0, 0);

        _btnRegenerate = ButtonHelper.CreateButton("Regenerate", 110, 32, ButtonStyle.Primary);
        _btnRegenerate.Click += BtnRegenerate_Click;

        var headerButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0)
        };
        headerButtonPanel.Controls.Add(_btnRegenerate);
        headerLayout.Controls.Add(headerButtonPanel, 1, 0);

        headerLayout.Layout += (s, e) =>
        {
            var maxWidth = Math.Max(0, headerLayout.Width - headerButtonPanel.Width - 10);
            lblHeader.MaximumSize = new Size(maxWidth, 0);
        };

        mainLayout.Controls.Add(headerLayout, 0, 0);

        _storyScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(4)
        };

        _storyFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(6, 0, 6, 10)
        };

        _storyScroll.Controls.Add(_storyFlow);
        _storyScroll.Resize += (s, e) => ApplyLayoutSizing();
        mainLayout.Controls.Add(_storyScroll, 0, 1);

        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.StoryBeats);

        _lblStatus = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray
        };
        _lblStatus.AutoSize = true;
        mainLayout.Layout += (s, e) =>
        {
            var maxWidth = Math.Max(0, mainLayout.ClientSize.Width - mainLayout.Padding.Horizontal);
            _lblStatus.MaximumSize = new Size(maxWidth, 0);
        };
        mainLayout.Controls.Add(_lblStatus, 0, 2);

        this.Controls.Add(mainLayout);
    }

    private async void BtnRegenerate_Click(object? sender, EventArgs e)
    {
        await GenerateStoryBeatsAsync();
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        RefreshStoryView();

        var beats = _state.Storyline?.Beats ?? new List<StoryBeat>();
        if (beats.Count == 0)
        {
            await GenerateStoryBeatsAsync();
        }
        else
        {
            UpdateStatus();
        }
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if ((_state.Storyline?.Beats.Count ?? 0) == 0)
        {
            MessageBox.Show("Generate story beats before continuing.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private async Task GenerateStoryBeatsAsync()
    {
        _isLoading = true;
        _btnRegenerate.Enabled = false;
        _lblStatus.Text = "";
        _loadingOverlay.Show(this);
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var storyline = _state.Storyline;
            if (storyline == null)
                throw new InvalidOperationException("Generate a storyline before generating story beats.");

            var openAI = _state.CreateOpenAIService();
            var generator = new StorylineGenerator(openAI);

            var organizations = storyline.Organizations.Count > 0
                ? storyline.Organizations
                : _state.Organizations;
            var characters = organizations
                .SelectMany(o => o.EnumerateCharacters())
                .Select(a => a.Character)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .ToList();

            IProgress<string> progress = new Progress<string>(status =>
            {
                _lblStatus.Text = status;
                _lblStatus.ForeColor = Color.Blue;
            });

            var beats = await generator.GenerateStoryBeatsAsync(
                _state.Topic,
                storyline,
                organizations,
                characters,
                progress);

            storyline.Beats = beats.ToList();

            var characterGenerator = new CharacterGenerator(openAI);
            await characterGenerator.AnnotateStorylineRelevanceAsync(
                _state.Topic,
                storyline,
                organizations,
                characters,
                progress);

            RefreshStoryView();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            var errorMsg = ex.InnerException != null
                ? $"{ex.Message} ({ex.InnerException.Message})"
                : ex.Message;
            _lblStatus.Text = $"Error: {errorMsg}";
            _lblStatus.ForeColor = Color.Red;
        }
        finally
        {
            _isLoading = false;
            _btnRegenerate.Enabled = true;
            _loadingOverlay.Hide();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshStoryView()
    {
        _storyFlow.SuspendLayout();
        _storyFlow.Controls.Clear();

        var storyline = _state.Storyline;
        if (storyline == null)
        {
            _storyFlow.Controls.Add(BuildInfoLabel("No storyline available."));
            _storyFlow.ResumeLayout();
            ApplyLayoutSizing();
            return;
        }

        _storyFlow.Controls.Add(BuildTitleLabel(storyline.Title));

        var dateRange = storyline.StartDate.HasValue && storyline.EndDate.HasValue
            ? $"{storyline.StartDate:MMM d, yyyy} – {storyline.EndDate:MMM d, yyyy}"
            : "Date range not set";
        _storyFlow.Controls.Add(BuildSubTitleLabel(dateRange));

        _storyFlow.Controls.Add(BuildSectionHeader("Summary"));
        _storyFlow.Controls.Add(BuildIndentedBodyLabel(storyline.Summary));

        var beats = storyline.Beats ?? new List<StoryBeat>();
        if (beats.Count == 0)
        {
            _storyFlow.Controls.Add(BuildInfoLabel("No story beats generated yet."));
            _storyFlow.ResumeLayout();
            ApplyLayoutSizing();
            return;
        }

        foreach (var beat in beats)
        {
            AddBeatControls(beat);
        }

        _storyFlow.ResumeLayout();
        ApplyLayoutSizing();
    }

    private void UpdateStatus()
    {
        var count = _state.Storyline?.Beats.Count ?? 0;
        _lblStatus.Text = count > 0 ? $"Generated {count} story beats." : "No story beats generated.";
        _lblStatus.ForeColor = count > 0 ? Color.Green : Color.Gray;
    }

    private void AddBeatControls(StoryBeat beat)
    {
        _storyFlow.Controls.Add(BuildBeatHeader(beat.Name));

        foreach (var paragraph in SplitParagraphs(beat.Plot))
        {
            _storyFlow.Controls.Add(BuildIndentedBodyLabel(paragraph));
        }
    }

    private static Label BuildTitleLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };
    }

    private static Label BuildSubTitleLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9F, FontStyle.Italic),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 12)
        };
    }

    private static Label BuildSectionHeader(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 6, 0, 4)
        };
    }

    private static Label BuildBeatHeader(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 4)
        };
    }

    private static Label BuildIndentedBodyLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5F),
            Margin = new Padding(12, 0, 0, 6)
        };
    }

    private static Label BuildInfoLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 10, 0, 0)
        };
    }

    private void ApplyLayoutSizing()
    {
        if (_storyScroll.ClientSize.Width <= 0)
            return;

        var width = Math.Max(300, _storyScroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        _storyFlow.Width = width;

        foreach (Control control in _storyFlow.Controls)
        {
            ApplySizingToControl(control, width);
        }
    }

    private static void ApplySizingToControl(Control control, int width)
    {
        if (control is Label label)
        {
            SetLabelMaxWidth(label, width);
            return;
        }

        if (control is Panel panel)
        {
            ApplyPanelSizing(panel, width);
        }
    }

    private static void ApplyPanelSizing(Panel panel, int width)
    {
        panel.Width = width;
        foreach (Control child in panel.Controls)
        {
            if (child is Label childLabel)
            {
                SetLabelMaxWidth(childLabel, width);
                continue;
            }

            if (child is TableLayoutPanel table)
            {
                ApplyTableSizing(table, width);
            }
        }
    }

    private static void ApplyTableSizing(TableLayoutPanel table, int width)
    {
        table.MaximumSize = new Size(Math.Max(120, width - 24), 0);
        foreach (Control tableChild in table.Controls)
        {
            if (tableChild is Label tableLabel)
            {
                SetLabelMaxWidth(tableLabel, width);
            }
        }
    }

    private static void SetLabelMaxWidth(Label label, int width)
    {
        var maxWidth = Math.Max(120, width - label.Margin.Horizontal);
        label.MaximumSize = new Size(maxWidth, 0);
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0);
    }
}
