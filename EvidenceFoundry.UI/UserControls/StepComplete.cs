using System.Diagnostics;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.UserControls;

public class StepComplete : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private Label _lblSummary = null!;
    private DataGridView _gridStats = null!;

    public string StepTitle => "Complete";
    public bool CanMoveNext => true;
    public bool CanMoveBack => false;
    public string NextButtonText => "Finish";

    public event EventHandler? StateChanged;

    public StepComplete()
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
            RowCount = 4,
            Padding = new Padding(10)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));

        // Success header
        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(40, 167, 69)
        };

        var lblHeader = new Label
        {
            Text = "Email Dataset Generated Successfully!",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false
        };
        headerPanel.Controls.Add(lblHeader);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // Statistics grid
        _gridStats = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            ColumnHeadersVisible = false
        };

        _gridStats.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Metric",
            Width = 250
        });

        _gridStats.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        mainLayout.Controls.Add(_gridStats, 0, 1);

        // Open folder button
        var buttonPanel = new Panel { Dock = DockStyle.Fill };

        var btnOpenFolder = ButtonHelper.CreateButton("Open Output Folder", 180, 40, ButtonStyle.Success);
        btnOpenFolder.Location = new Point(0, 10);
        btnOpenFolder.Click += BtnOpenFolder_Click;
        buttonPanel.Controls.Add(btnOpenFolder);

        mainLayout.Controls.Add(buttonPanel, 0, 2);

        // Summary label
        _lblSummary = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray
        };
        mainLayout.Controls.Add(_lblSummary, 0, 3);

        this.Controls.Add(mainLayout);
    }

    private void BtnOpenFolder_Click(object? sender, EventArgs e)
    {
        if (_state.Result != null && Directory.Exists(_state.Result.OutputFolder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _state.Result.OutputFolder,
                UseShellExecute = true
            });
        }
    }

    private void LoadStatistics()
    {
        _gridStats.Rows.Clear();

        if (_state.Result == null) return;

        var result = _state.Result;

        AddStatRow("Total Emails Generated", result.TotalEmailsGenerated.ToString());
        AddStatRow("Email Threads Created", result.TotalThreadsGenerated.ToString());
        AddStatRow("", "");
        AddStatRow("--- Attachments ---", "");
        AddStatRow("Document Attachments", result.TotalAttachmentsGenerated.ToString());
        AddStatRow("  - Word Documents", result.WordDocumentsGenerated.ToString());
        AddStatRow("  - Excel Spreadsheets", result.ExcelDocumentsGenerated.ToString());
        AddStatRow("  - PowerPoint Presentations", result.PowerPointDocumentsGenerated.ToString());
        if (result.ImagesGenerated > 0)
            AddStatRow("Images Generated", result.ImagesGenerated.ToString());
        if (result.CalendarInvitesGenerated > 0)
            AddStatRow("Calendar Invites", result.CalendarInvitesGenerated.ToString());
        if (result.VoicemailsGenerated > 0)
            AddStatRow("Voicemails Generated", result.VoicemailsGenerated.ToString());
        AddStatRow("", "");
        AddStatRow("Topic", _state.TopicDisplayName);
        AddStatRow("Storyline Used", _state.Storyline != null ? "1" : "0");
        AddStatRow("Characters Used", _state.Characters.Count.ToString());
        AddStatRow("", "");
        AddStatRow("Generation Time", result.ElapsedTime.ToString(@"mm\:ss"));
        AddStatRow("Output Folder", result.OutputFolder);
        AddStatRow("", "");
        AddStatRow("--- API Usage ---", "");
        AddStatRow("Model Used", _state.SelectedModel);
        var usageSummary = _state.UsageTracker.GetSummary();
        AddStatRow("Input Tokens", TokenUsageFormatter.FormatTokenCount(usageSummary.TotalInputTokens));
        AddStatRow("Output Tokens", TokenUsageFormatter.FormatTokenCount(usageSummary.TotalOutputTokens));
        AddStatRow("Total Tokens", TokenUsageFormatter.FormatTokenCount(usageSummary.TotalTokens));
        AddStatRow("Estimated Cost", TokenUsageFormatter.FormatCost(usageSummary.TotalCost));

        _lblSummary.Text = "Your email dataset is ready for import into your e-discovery platform.\n\n" +
                          "The .EML files contain proper threading headers (Message-ID, In-Reply-To, References)\n" +
                          "so email threads will be grouped correctly when imported.\n\n" +
                          "Click 'Finish' to close this wizard, or 'Open Output Folder' to view the generated files.";
    }

    private void AddStatRow(string metric, string value)
    {
        _gridStats.Rows.Add(metric, value);
    }

    public void BindState(WizardState state)
    {
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task OnEnterStepAsync()
    {
        LoadStatistics();
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        return Task.FromResult(true);
    }
}
