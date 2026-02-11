using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.UserControls;

public class StepStorylines : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private Button _btnRegenerate = null!;
    private Panel _editorPanel = null!;
    private Panel _emptyStatePanel = null!;
    private Label _lblEmptyState = null!;
    private TextBox _txtStorylineTitle = null!;
    private TextBox _txtStorylineLogline = null!;
    private TextBox _txtStorylineSummary = null!;
    private TextBox _txtPlotOutline = null!;
    private TextBox _txtTensionDrivers = null!;
    private TextBox _txtAmbiguities = null!;
    private TextBox _txtRedHerrings = null!;
    private TextBox _txtEvidenceThemes = null!;
    private DateTimePicker _dtpStartDate = null!;
    private DateTimePicker _dtpEndDate = null!;
    private Label _lblStatus = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private bool _isLoading = false;
    private bool _suppressEditorEvents = false;
    private const int RegenerateButtonWidth = 110;
    private const int RegenerateButtonHeight = 32;
    private const int EditorLabelWidth = 100;

    public string StepTitle => "Review Storyline";
    public bool CanMoveNext => _state?.Storyline != null && !_isLoading;
    public bool CanMoveBack => !_isLoading;
    public string NextButtonText => "Next >";

    public event EventHandler? StateChanged;

    public StepStorylines()
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

        // Header with regenerate button
        _btnRegenerate = ButtonHelper.CreateButton("Regenerate", RegenerateButtonWidth, RegenerateButtonHeight, ButtonStyle.Primary);
        _btnRegenerate.Click += BtnRegenerate_Click;

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
            Text = "The AI has generated a storyline. Review and edit details if needed, or regenerate.",
            AutoSize = true,
            Margin = new Padding(0, 6, 10, 0)
        };
        headerLayout.Controls.Add(lblHeader, 0, 0);

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

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window
        };

        _editorPanel = BuildStorylineEditorPanel();
        _editorPanel.Dock = DockStyle.Fill;
        contentPanel.Controls.Add(_editorPanel);

        _emptyStatePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Visible = false
        };
        _lblEmptyState = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Font = new Font(this.Font.FontFamily, 10F, FontStyle.Italic)
        };
        _emptyStatePanel.Controls.Add(_lblEmptyState);
        contentPanel.Controls.Add(_emptyStatePanel);

        mainLayout.Controls.Add(contentPanel, 0, 1);

        // Create loading overlay for the editor
        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.Storyline);

        // Status label
        _lblStatus = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSize = true,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = Color.Gray
        };
        mainLayout.Layout += (s, e) =>
        {
            var maxWidth = Math.Max(0, mainLayout.ClientSize.Width - mainLayout.Padding.Horizontal);
            _lblStatus.MaximumSize = new Size(maxWidth, 0);
        };
        mainLayout.Controls.Add(_lblStatus, 0, 2);

        this.Controls.Add(mainLayout);
        UpdateEmptyState();
    }

    private Panel BuildStorylineEditorPanel()
    {
        var scrollPanel = new Panel
        {
            AutoScroll = true,
            BackColor = this.BackColor,
            Padding = new Padding(12, 8, 12, 8)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, EditorLabelWidth));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = new Label
        {
            Text = "Storyline Details",
            AutoSize = true,
            Font = new Font(this.Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        layout.Controls.Add(header, 0, 0);
        layout.SetColumnSpan(header, 2);

        var row = 1;

        layout.Controls.Add(new Label
        {
            Text = "Title",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, row);

        _txtStorylineTitle = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3)
        };
        _txtStorylineTitle.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.Title = _txtStorylineTitle.Text;
            });
        };
        layout.Controls.Add(_txtStorylineTitle, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Logline",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _txtStorylineLogline = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3)
        };
        _txtStorylineLogline.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.Logline = _txtStorylineLogline.Text;
            });
        };
        layout.Controls.Add(_txtStorylineLogline, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Summary",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _txtStorylineSummary = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 260,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3)
        };
        _txtStorylineSummary.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.Summary = _txtStorylineSummary.Text;
            });
        };
        layout.Controls.Add(_txtStorylineSummary, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Plot Outline (1 per line)",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _txtPlotOutline = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 120,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3)
        };
        _txtPlotOutline.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.SetPlotOutline(ParseOutline(_txtPlotOutline.Text));
            });
        };
        layout.Controls.Add(_txtPlotOutline, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Tension Drivers (1 per line)",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _txtTensionDrivers = BuildListTextBox(120);
        _txtTensionDrivers.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.SetTensionDrivers(ParseList(_txtTensionDrivers.Text));
            });
        };
        layout.Controls.Add(_txtTensionDrivers, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Ambiguities (1 per line)",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _txtAmbiguities = BuildListTextBox(120);
        _txtAmbiguities.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.SetAmbiguities(ParseList(_txtAmbiguities.Text));
            });
        };
        layout.Controls.Add(_txtAmbiguities, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Red Herrings (1 per line)",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _txtRedHerrings = BuildListTextBox(120);
        _txtRedHerrings.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.SetRedHerrings(ParseList(_txtRedHerrings.Text));
            });
        };
        layout.Controls.Add(_txtRedHerrings, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Evidence Themes (1 per line)",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _txtEvidenceThemes = BuildListTextBox(120);
        _txtEvidenceThemes.TextChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.SetEvidenceThemes(ParseList(_txtEvidenceThemes.Text));
            });
        };
        layout.Controls.Add(_txtEvidenceThemes, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "Start Date",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _dtpStartDate = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "MMM d, yyyy",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(220, 0),
            Margin = new Padding(0, 3, 0, 3)
        };
        _dtpStartDate.ValueChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.StartDate = _dtpStartDate.Value.Date;
            });
        };
        layout.Controls.Add(_dtpStartDate, 1, row);

        row++;

        layout.Controls.Add(new Label
        {
            Text = "End Date",
            AutoSize = true,
            Margin = new Padding(0, 10, 8, 0)
        }, 0, row);

        _dtpEndDate = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "MMM d, yyyy",
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(220, 0),
            Margin = new Padding(0, 3, 0, 3)
        };
        _dtpEndDate.ValueChanged += (s, e) =>
        {
            ApplyStorylineEditorChanges(storyline =>
            {
                storyline.EndDate = _dtpEndDate.Value.Date;
            });
        };
        layout.Controls.Add(_dtpEndDate, 1, row);

        scrollPanel.Controls.Add(layout);
        return scrollPanel;
    }

    private static TextBox BuildListTextBox(int height)
    {
        return new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = height,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 3)
        };
    }

    private static List<string> ParseList(string value)
    {
        return (value ?? string.Empty)
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private static List<StoryOutline> ParseOutline(string value)
    {
        return ParseList(value)
            .Select(line => new StoryOutline { Point = line })
            .ToList();
    }

    private void ApplyStorylineEditorChanges(Action<Storyline> update)
    {
        if (_suppressEditorEvents)
            return;

        var storyline = _state.Storyline;
        if (storyline == null)
            return;

        update(storyline);
        UpdateStatus();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStorylineEditor(Storyline? storyline)
    {
        _suppressEditorEvents = true;
        try
        {
            var enabled = storyline != null;
            _txtStorylineTitle.Enabled = enabled;
            _txtStorylineLogline.Enabled = enabled;
            _txtStorylineSummary.Enabled = enabled;
            _txtPlotOutline.Enabled = enabled;
            _txtTensionDrivers.Enabled = enabled;
            _txtAmbiguities.Enabled = enabled;
            _txtRedHerrings.Enabled = enabled;
            _txtEvidenceThemes.Enabled = enabled;
            _dtpStartDate.Enabled = enabled;
            _dtpEndDate.Enabled = enabled;

            if (storyline == null)
            {
                _txtStorylineTitle.Text = string.Empty;
                _txtStorylineLogline.Text = string.Empty;
                _txtStorylineSummary.Text = string.Empty;
                _txtPlotOutline.Text = string.Empty;
                _txtTensionDrivers.Text = string.Empty;
                _txtAmbiguities.Text = string.Empty;
                _txtRedHerrings.Text = string.Empty;
                _txtEvidenceThemes.Text = string.Empty;
                _dtpStartDate.Value = DateTime.Today;
                _dtpEndDate.Value = DateTime.Today;
                return;
            }

            _txtStorylineTitle.Text = storyline.Title;
            _txtStorylineLogline.Text = storyline.Logline;
            _txtStorylineSummary.Text = storyline.Summary;
            _txtPlotOutline.Text = string.Join(Environment.NewLine, storyline.PlotOutline.Select(p => p.Point));
            _txtTensionDrivers.Text = string.Join(Environment.NewLine, storyline.TensionDrivers);
            _txtAmbiguities.Text = string.Join(Environment.NewLine, storyline.Ambiguities);
            _txtRedHerrings.Text = string.Join(Environment.NewLine, storyline.RedHerrings);
            _txtEvidenceThemes.Text = string.Join(Environment.NewLine, storyline.EvidenceThemes);

            _dtpStartDate.Value = storyline.StartDate ?? DateTime.Today;
            _dtpEndDate.Value = storyline.EndDate ?? DateTime.Today;
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private async void BtnRegenerate_Click(object? sender, EventArgs e)
    {
        await GenerateStorylineAsync();
    }

    private void UpdateStatus()
    {
        if (_state.Storyline == null)
        {
            _lblStatus.Text = "No storyline available.";
            _lblStatus.ForeColor = Color.Gray;
            UpdateEmptyState();
            return;
        }

        _lblStatus.Text = $"Storyline ready: {_state.Storyline.Title}";
        _lblStatus.ForeColor = Color.Green;
        UpdateEmptyState();
    }

    private async Task GenerateStorylineAsync()
    {
        await WizardStepUiHelper.RunWithLoadingStateAsync(
            this,
            _btnRegenerate,
            _lblStatus,
            _loadingOverlay,
            isLoading => _isLoading = isLoading,
            () => StateChanged?.Invoke(this, EventArgs.Empty),
            async progress =>
            {
                var openAI = _state.CreateOpenAIService();
                var generator = new StorylineGenerator(
                    openAI,
                    _state.GenerationRandom,
                    _state.CreateLogger<StorylineGenerator>(),
                    _state.LoggerFactory);

                var request = new StorylineGenerationRequest
                {
                    Topic = _state.TopicDisplayName,
                    IssueDescription = _state.StorylineIssueDescription,
                    AdditionalInstructions = _state.AdditionalInstructions,
                    PlaintiffIndustry = _state.PlaintiffIndustry,
                    DefendantIndustry = _state.DefendantIndustry,
                    PlaintiffOrganizationCount = _state.PlaintiffOrganizationCount,
                    DefendantOrganizationCount = _state.DefendantOrganizationCount,
                    WorldModel = _state.WorldModel,
                    WantsDocuments = _state.WantsDocuments,
                    WantsImages = _state.WantsImages,
                    WantsVoicemails = _state.WantsVoicemails
                };

                var result = await generator.GenerateStorylineAsync(request, progress);

                _state.Storyline = result.Storyline;
                ClearDerivedState();
                UpdateStorylineEditor(_state.Storyline);

                _lblStatus.Text = _state.Storyline != null
                    ? $"Storyline ready: {_state.Storyline.Title}"
                    : "Storyline generated.";
                if (!string.IsNullOrWhiteSpace(result.StorylineFilterSummary))
                {
                    _lblStatus.Text += $"\n{result.StorylineFilterSummary}";
                }
                _lblStatus.ForeColor = Color.Green;
            },
            UpdateEmptyState);
    }

    private void ClearDerivedState()
    {
        _state.Organizations.Clear();
        _state.Characters.Clear();
        _state.DomainThemes.Clear();
        _state.CompanyDomain = string.Empty;
        _state.GeneratedThreads.Clear();
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        UpdateStorylineEditor(_state.Storyline);
        UpdateStatus();

        // Auto-generate if no storyline yet
        if (_state.Storyline == null)
        {
            await GenerateStorylineAsync();
        }
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (_state.Storyline == null)
        {
            MessageBox.Show("A storyline is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private void UpdateEmptyState()
    {
        if (_emptyStatePanel == null || _lblEmptyState == null)
            return;
        if (_state == null)
            return;

        if (_isLoading && _state.Storyline == null)
        {
            _lblEmptyState.Text = "Generating storyline...\nThis can take a minute.";
            _emptyStatePanel.Visible = true;
            _editorPanel.Enabled = false;
            return;
        }

        if (!_isLoading && _state.Storyline == null)
        {
            _lblEmptyState.Text = "No storyline available.\nClick Regenerate to try again.";
            _emptyStatePanel.Visible = true;
            _editorPanel.Enabled = false;
            return;
        }

        _emptyStatePanel.Visible = false;
        _editorPanel.Enabled = true;
    }
}
