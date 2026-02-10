using EvidenceFoundry.Models;

namespace EvidenceFoundry.UserControls;

public class StepGenerationSummary : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private TableLayoutPanel _summaryTable = null!;
    private Label _lblStatus = null!;

    public string StepTitle => "Generation Summary";
    public bool CanMoveNext => IsReadyToGenerate();
    public bool CanMoveBack => true;
    public string NextButtonText => "Generate Emails >";

    public event EventHandler? StateChanged;

    public StepGenerationSummary()
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

        var headerLabel = new Label
        {
            Text = "Review what will be generated before starting.",
            AutoSize = true,
            Location = new Point(0, 8)
        };
        mainLayout.Controls.Add(headerLabel, 0, 0);

        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(4)
        };

        _summaryTable = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Padding = new Padding(0, 0, 20, 20)
        };
        _summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        _summaryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        scrollPanel.Controls.Add(_summaryTable);
        mainLayout.Controls.Add(scrollPanel, 0, 1);

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

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        RenderSummary();
        UpdateStatus();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (!IsReadyToGenerate())
        {
            MessageBox.Show("Complete the required steps before generating emails.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private bool IsReadyToGenerate()
    {
        var storyline = _state.Storyline;
        if (storyline == null)
            return false;

        if (string.IsNullOrWhiteSpace(_state.Config.OutputFolder))
            return false;

        var beatCount = storyline.Beats?.Count ?? 0;
        if (beatCount == 0)
            return false;

        if (_state.Characters.Count < 2)
            return false;

        return true;
    }

    private void RenderSummary()
    {
        _summaryTable.SuspendLayout();
        _summaryTable.Controls.Clear();
        _summaryTable.RowStyles.Clear();
        _summaryTable.RowCount = 0;

        var storyline = _state.Storyline;
        if (storyline == null)
        {
            AddSectionHeader("Storyline", 0);
            AddRow("Status:", "No storyline available.");
            _summaryTable.ResumeLayout();
            return;
        }

        var summary = _state.GetGenerationSummary();
        var attachmentTypes = _state.Config.EnabledAttachmentTypes;
        var attachmentTypeText = attachmentTypes.Count > 0
            ? string.Join(", ", attachmentTypes.Select(t => t.ToString()))
            : "None";

        var dateRange = summary.StartDate.HasValue && summary.EndDate.HasValue
            ? $"{summary.StartDate:MMM d, yyyy} – {summary.EndDate:MMM d, yyyy}"
            : "Not set";

        AddSectionHeader("Storyline", 0);
        AddRow("Title:", storyline.Title);
        if (!string.IsNullOrWhiteSpace(_state.TopicDisplayName))
        {
            AddRow("Topic:", _state.TopicDisplayName);
        }
        AddRow("Date Range:", dateRange);
        AddRow("Story Beats:", summary.BeatCount.ToString("N0"));
        AddRow("Threads:", summary.ThreadCount.ToString("N0"));
        AddRow("Hot Threads:", summary.HotThreadCount.ToString("N0"));
        AddRow("Relevant Threads:", summary.RelevantThreadCount.ToString("N0"));
        AddRow("Non-Relevant Threads:", summary.NonRelevantThreadCount.ToString("N0"));
        AddRow("Emails:", summary.EmailCount.ToString("N0"));
        AddRow("Characters:", _state.Characters.Count.ToString("N0"));
        AddRow("Organizations:", _state.Organizations.Count.ToString("N0"));

        AddSectionHeader("Generation Settings", 1);
        AddRow("Model:", _state.SelectedModelConfig?.DisplayName ?? _state.SelectedModel);
        AddRow("Parallel API Calls:", _state.Config.ParallelThreads.ToString("N0"));
        AddRow("Attachment Complexity:", _state.Config.AttachmentComplexity.ToString());
        AddRow("Attachment Chains:", _state.Config.EnableAttachmentChains ? "Enabled" : "Disabled");

        AddSectionHeader("Attachments & Media", 1);
        AddRow("Docs %:", $"{_state.Config.AttachmentPercentage}% (~{summary.EstimatedDocumentAttachments:N0} files)");
        AddRow("Doc Types:", attachmentTypeText);
        AddRow("Images:", _state.Config.IncludeImages
            ? $"{_state.Config.ImagePercentage}% (~{summary.EstimatedImageAttachments:N0} images)"
            : "Disabled");
        AddRow("Voicemails:", _state.Config.IncludeVoicemails
            ? $"{_state.Config.VoicemailPercentage}% (~{summary.EstimatedVoicemailAttachments:N0} files)"
            : "Disabled");
        AddRow("Calendar Invites:", _state.Config.IncludeCalendarInvites
            ? $"{_state.Config.CalendarInvitePercentage}% (~{summary.EstimatedCalendarInviteChecks:N0} checks)"
            : "Disabled");

        AddSectionHeader("Output", 1);
        AddRow("Folder:", string.IsNullOrWhiteSpace(_state.Config.OutputFolder) ? "Not set" : _state.Config.OutputFolder);
        AddRow("Organize By Sender:", _state.Config.OrganizeBySender ? "Yes" : "No");

        AddSectionHeader("Notes", 1);
        AddRow("", "Estimated counts are approximate and will vary by storyline and thread structure.");

        _summaryTable.ResumeLayout();
    }

    private void UpdateStatus()
    {
        if (IsReadyToGenerate())
        {
            _lblStatus.Text = "Ready to generate.";
            _lblStatus.ForeColor = Color.Green;
            return;
        }

        var issues = new List<string>();
        if (_state.Storyline == null)
            issues.Add("Generate a storyline");
        if (string.IsNullOrWhiteSpace(_state.Config.OutputFolder))
            issues.Add("Choose an output folder");
        if ((_state.Storyline?.Beats.Count ?? 0) == 0)
            issues.Add("Generate story beats");
        if (_state.Characters.Count < 2)
            issues.Add("Generate characters");

        _lblStatus.Text = issues.Count == 0
            ? "Complete the required steps before generating."
            : $"To continue: {string.Join(", ", issues)}.";
        _lblStatus.ForeColor = Color.DarkOrange;
    }

    private void AddSectionHeader(string text, int marginTop)
    {
        var header = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(this.Font.FontFamily, 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 120, 215),
            Margin = new Padding(0, marginTop == 0 && _summaryTable.RowCount == 0 ? 0 : 12, 0, 6)
        };

        var rowIndex = _summaryTable.RowCount++;
        _summaryTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _summaryTable.Controls.Add(header, 0, rowIndex);
        _summaryTable.SetColumnSpan(header, 2);
    }

    private void AddRow(string labelText, string valueText)
    {
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Font = new Font(this.Font.FontFamily, 9.5F),
            Margin = new Padding(0, 2, 6, 2)
        };

        var value = new Label
        {
            Text = valueText,
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = new Font(this.Font.FontFamily, 9.5F),
            Margin = new Padding(0, 2, 0, 2)
        };

        var rowIndex = _summaryTable.RowCount++;
        _summaryTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _summaryTable.Controls.Add(label, 0, rowIndex);
        _summaryTable.Controls.Add(value, 1, rowIndex);
    }
}
