using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.UserControls;

public class StepTopicInput : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private ComboBox _cboCaseArea = null!;
    private ComboBox _cboMatterType = null!;
    private ComboBox _cboIssue = null!;
    private ComboBox _cboPlaintiffIndustry = null!;
    private ComboBox _cboDefendantIndustry = null!;
    private NumericUpDown _numPlaintiffCount = null!;
    private NumericUpDown _numDefendantCount = null!;
    private TextBox _txtInstructions = null!;
    private TextBox _txtIssueDescription = null!;
    private CheckBox _chkDocuments = null!;
    private CheckBox _chkImages = null!;
    private CheckBox _chkVoicemails = null!;
    private bool _isLoadingSelections;
    private const string DefaultFontFamily = "Segoe UI";
    private const string RandomIndustryOption = "Random";
    private const string ValidationDialogTitle = "Validation";

    public string StepTitle => "Case Issue Selection";
    public bool CanMoveNext => _cboIssue?.SelectedItem is string;
    public bool CanMoveBack => true;
    public string NextButtonText => "Generate World Model >";

    public event EventHandler? StateChanged;

    public StepTopicInput()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Padding = new Padding(20);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 12,
            Padding = new Padding(10)
        };

        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 0: Case area label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 1: Case area input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 2: Matter type label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 3: Matter type input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 4: Issue label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 5: Issue input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // 6: Industry inputs
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 7: Issue description label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120)); // 8: Issue description input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 9: Media types label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 10: Media type checkboxes
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 11: Help text

        // Case area label
        var lblCaseArea = new Label
        {
            Text = "Case Area:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblCaseArea, 0, 0);

        var lblInstructions = new Label
        {
            Text = "Additional Instructions (Optional):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblInstructions, 1, 0);

        // Case area input
        _cboCaseArea = new ComboBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(DefaultFontFamily, 11F),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboCaseArea.SelectedIndexChanged += (s, e) => OnCaseAreaChanged();
        mainLayout.Controls.Add(_cboCaseArea, 0, 1);

        // Matter type label
        var lblMatterType = new Label
        {
            Text = "Matter Type:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblMatterType, 0, 2);

        // Matter type input
        _cboMatterType = new ComboBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(DefaultFontFamily, 11F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = false
        };
        _cboMatterType.SelectedIndexChanged += (s, e) => OnMatterTypeChanged();
        mainLayout.Controls.Add(_cboMatterType, 0, 3);

        // Issue label
        var lblIssue = new Label
        {
            Text = "Issue:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblIssue, 0, 4);

        // Issue input
        _cboIssue = new ComboBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(DefaultFontFamily, 11F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = false
        };
        _cboIssue.SelectedIndexChanged += (s, e) => OnIssueChanged();
        mainLayout.Controls.Add(_cboIssue, 0, 5);

        var industryPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0)
        };
        industryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        industryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        industryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        industryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        industryPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        industryPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

        var lblPlaintiffIndustry = new Label
        {
            Text = "Plaintiff Industry",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        industryPanel.Controls.Add(lblPlaintiffIndustry, 0, 0);

        var lblPlaintiffCount = new Label
        {
            Text = "Count",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        industryPanel.Controls.Add(lblPlaintiffCount, 1, 0);

        var lblDefendantIndustry = new Label
        {
            Text = "Defendant Industry",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        industryPanel.Controls.Add(lblDefendantIndustry, 2, 0);

        var lblDefendantCount = new Label
        {
            Text = "Count",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        industryPanel.Controls.Add(lblDefendantCount, 3, 0);

        _cboPlaintiffIndustry = new ComboBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(DefaultFontFamily, 11F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(IndustryOption.Display),
            ValueMember = nameof(IndustryOption.Name)
        };
        _cboPlaintiffIndustry.SelectedIndexChanged += (s, e) => OnIndustryPreferenceChanged();
        industryPanel.Controls.Add(_cboPlaintiffIndustry, 0, 1);

        _numPlaintiffCount = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Font = new Font(DefaultFontFamily, 11F),
            Minimum = 1,
            Maximum = 3,
            Value = 1,
            TextAlign = HorizontalAlignment.Center
        };
        _numPlaintiffCount.ValueChanged += (s, e) => OnPartyCountChanged();
        industryPanel.Controls.Add(_numPlaintiffCount, 1, 1);

        _cboDefendantIndustry = new ComboBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(DefaultFontFamily, 11F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = nameof(IndustryOption.Display),
            ValueMember = nameof(IndustryOption.Name)
        };
        _cboDefendantIndustry.SelectedIndexChanged += (s, e) => OnIndustryPreferenceChanged();
        industryPanel.Controls.Add(_cboDefendantIndustry, 2, 1);

        _numDefendantCount = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Font = new Font(DefaultFontFamily, 11F),
            Minimum = 1,
            Maximum = 3,
            Value = 1,
            TextAlign = HorizontalAlignment.Center
        };
        _numDefendantCount.ValueChanged += (s, e) => OnPartyCountChanged();
        industryPanel.Controls.Add(_numDefendantCount, 3, 1);

        mainLayout.Controls.Add(industryPanel, 0, 6);
        mainLayout.SetColumnSpan(industryPanel, 2);

        // Instructions input
        _txtInstructions = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(DefaultFontFamily, 10F),
            PlaceholderText = "Add any specific instructions here...\n\nExamples:\n- Focus on legal issues and compliance problems\n- Include a financial fraud storyline\n- Make the tone more dramatic\n- Include HR complaints and workplace issues"
        };
        mainLayout.Controls.Add(_txtInstructions, 1, 1);
        mainLayout.SetRowSpan(_txtInstructions, 5);

        var lblIssueDescription = new Label
        {
            Text = "Selected Issue Description:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblIssueDescription, 0, 7);
        mainLayout.SetColumnSpan(lblIssueDescription, 2);

        _txtIssueDescription = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(DefaultFontFamily, 9.5F),
            BackColor = SystemColors.Window,
            TabStop = false
        };
        mainLayout.Controls.Add(_txtIssueDescription, 0, 8);
        mainLayout.SetColumnSpan(_txtIssueDescription, 2);

        // Media types label
        var lblMediaTypes = new Label
        {
            Text = "Include in Storyline (optional - helps AI craft relevant scenarios):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        mainLayout.Controls.Add(lblMediaTypes, 0, 9);
        mainLayout.SetColumnSpan(lblMediaTypes, 2);

        // Media type checkboxes panel
        var mediaPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _chkDocuments = new CheckBox
        {
            Text = "📄 Documents (reports, spreadsheets)",
            AutoSize = true,
            Checked = true,
            Font = new Font(DefaultFontFamily, 9.5F),
            Margin = new Padding(0, 5, 20, 0)
        };
        mediaPanel.Controls.Add(_chkDocuments);

        _chkImages = new CheckBox
        {
            Text = "🖼️ Images (photos, evidence)",
            AutoSize = true,
            Checked = false,
            Font = new Font(DefaultFontFamily, 9.5F),
            Margin = new Padding(0, 5, 20, 0)
        };
        mediaPanel.Controls.Add(_chkImages);

        _chkVoicemails = new CheckBox
        {
            Text = "🎙️ Voicemails (audio messages)",
            AutoSize = true,
            Checked = false,
            Font = new Font(DefaultFontFamily, 9.5F),
            Margin = new Padding(0, 5, 0, 0)
        };
        mediaPanel.Controls.Add(_chkVoicemails);

        mainLayout.Controls.Add(mediaPanel, 0, 10);
        mainLayout.SetColumnSpan(mediaPanel, 2);

        // Help text
        var helpText = new Label
        {
            Text = "Tips:\n" +
                   "• Choose a case area, matter type, and issue to anchor the narrative\n" +
                   "• Optionally, choose the industry for the plaintiff and defendant organization(s)\n" +
                   "• The AI will generate a pre-dispute storyline that leads to the selected issue\n" +
                   "• Use Additional Instructions to refine what you want the dispute to be about\n" +
                   "• Checking media types helps create a storyline with natural attachment opportunities",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 10, 0, 0)
        };
        mainLayout.Controls.Add(helpText, 0, 11);
        mainLayout.SetColumnSpan(helpText, 2);

        this.Controls.Add(mainLayout);
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        LoadSelectionsFromState();
        if (!string.IsNullOrEmpty(_state.AdditionalInstructions))
        {
            _txtInstructions.Text = _state.AdditionalInstructions;
        }

        // Restore media type preferences
        _chkDocuments.Checked = _state.WantsDocuments;
        _chkImages.Checked = _state.WantsImages;
        _chkVoicemails.Checked = _state.WantsVoicemails;

        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (_cboCaseArea.SelectedItem is not string caseArea)
        {
            MessageBox.Show("Please select a case area.", ValidationDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        if (_cboMatterType.SelectedItem is not string matterType)
        {
            MessageBox.Show("Please select a matter type.", ValidationDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        if (_cboIssue.SelectedItem is not string issue)
        {
            MessageBox.Show("Please select an issue.", ValidationDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        string issueDescription;
        try
        {
            issueDescription = CaseIssueCatalog.GetIssueDescription(caseArea, matterType, issue);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message, ValidationDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        var topicDisplay = $"{caseArea} > {matterType} > {issue}";
        var previousTopic = _state.Topic;
        var previousIssueDescription = _state.IssueDescription;
        var previousPlaintiffIndustry = _state.PlaintiffIndustry;
        var previousDefendantIndustry = _state.DefendantIndustry;
        var previousPlaintiffCount = _state.PlaintiffOrganizationCount;
        var previousDefendantCount = _state.DefendantOrganizationCount;

        _state.CaseArea = caseArea;
        _state.MatterType = matterType;
        _state.Issue = issue;
        _state.IssueDescription = issueDescription;
        _state.Topic = topicDisplay;
        _state.AdditionalInstructions = _txtInstructions.Text.Trim();
        _state.PlaintiffIndustry = GetSelectedIndustryPreference(_cboPlaintiffIndustry);
        _state.DefendantIndustry = GetSelectedIndustryPreference(_cboDefendantIndustry);
        _state.PlaintiffOrganizationCount = (int)_numPlaintiffCount.Value;
        _state.DefendantOrganizationCount = (int)_numDefendantCount.Value;

        // Save media type preferences
        _state.WantsDocuments = _chkDocuments.Checked;
        _state.WantsImages = _chkImages.Checked;
        _state.WantsVoicemails = _chkVoicemails.Checked;

        // Also pre-populate the generation config so these carry forward
        _state.Config.IncludeImages = _chkImages.Checked;
        _state.Config.IncludeVoicemails = _chkVoicemails.Checked;

        // Clear previously generated data if topic changed
        var topicChanged = !string.Equals(previousTopic, topicDisplay, StringComparison.Ordinal)
            || !string.Equals(previousIssueDescription, issueDescription, StringComparison.Ordinal)
            || !string.Equals(previousPlaintiffIndustry, _state.PlaintiffIndustry, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousDefendantIndustry, _state.DefendantIndustry, StringComparison.OrdinalIgnoreCase)
            || previousPlaintiffCount != _state.PlaintiffOrganizationCount
            || previousDefendantCount != _state.DefendantOrganizationCount;

        if (topicChanged)
        {
            _state.WorldModel = null;
            _state.Storyline = null;
            _state.Organizations.Clear();
            _state.Characters.Clear();
            _state.DomainThemes.Clear();
            _state.CompanyDomain = string.Empty;
            _state.GeneratedThreads.Clear();
        }

        return Task.FromResult(true);
    }

    private void LoadSelectionsFromState()
    {
        _isLoadingSelections = true;

        _cboCaseArea.BeginUpdate();
        _cboCaseArea.Items.Clear();
        foreach (var area in CaseIssueCatalog.GetCaseAreas())
        {
            _cboCaseArea.Items.Add(area);
        }
        _cboCaseArea.EndUpdate();

        SelectComboItem(_cboCaseArea, _state.CaseArea);
        PopulateMatterTypes(_state.CaseArea, _state.MatterType);
        PopulateIssues(_state.CaseArea, _state.MatterType, _state.Issue);
        PopulateIndustrySelections(_state.PlaintiffIndustry, _state.DefendantIndustry);
        _numPlaintiffCount.Value = ClampPartyCount(_state.PlaintiffOrganizationCount);
        _numDefendantCount.Value = ClampPartyCount(_state.DefendantOrganizationCount);

        UpdateIssueDescription();
        _isLoadingSelections = false;
    }

    private void OnCaseAreaChanged()
    {
        if (_isLoadingSelections)
            return;

        var caseArea = _cboCaseArea.SelectedItem as string;
        PopulateMatterTypes(caseArea, null);
        PopulateIssues(caseArea, null, null);
        UpdateIssueDescription();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMatterTypeChanged()
    {
        if (_isLoadingSelections)
            return;

        var caseArea = _cboCaseArea.SelectedItem as string;
        var matterType = _cboMatterType.SelectedItem as string;
        PopulateIssues(caseArea, matterType, null);
        UpdateIssueDescription();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnIssueChanged()
    {
        if (_isLoadingSelections)
            return;

        UpdateIssueDescription();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnIndustryPreferenceChanged()
    {
        if (_isLoadingSelections)
            return;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPartyCountChanged()
    {
        OnIndustryPreferenceChanged();
    }

    private void UpdateIssueDescription()
    {
        if (_cboCaseArea.SelectedItem is not string caseArea
            || _cboMatterType.SelectedItem is not string matterType
            || _cboIssue.SelectedItem is not string issue)
        {
            _txtIssueDescription.Text = string.Empty;
            return;
        }

        try
        {
            _txtIssueDescription.Text = CaseIssueCatalog.GetIssueDescription(caseArea, matterType, issue);
        }
        catch (ArgumentException)
        {
            _txtIssueDescription.Text = string.Empty;
        }
    }

    private void PopulateMatterTypes(string? caseArea, string? selectedMatterType)
    {
        _cboMatterType.BeginUpdate();
        _cboMatterType.Items.Clear();
        _cboMatterType.Enabled = false;

        if (!string.IsNullOrWhiteSpace(caseArea))
        {
            try
            {
                foreach (var matterType in CaseIssueCatalog.GetMatterTypes(caseArea))
                {
                    _cboMatterType.Items.Add(matterType);
                }
                _cboMatterType.Enabled = true;
                SelectComboItem(_cboMatterType, selectedMatterType);
            }
            catch (ArgumentException)
            {
                _cboMatterType.SelectedIndex = -1;
            }
        }
        else
        {
            _cboMatterType.SelectedIndex = -1;
        }

        _cboMatterType.EndUpdate();
    }

    private void PopulateIssues(string? caseArea, string? matterType, string? selectedIssue)
    {
        _cboIssue.BeginUpdate();
        _cboIssue.Items.Clear();
        _cboIssue.Enabled = false;

        if (!string.IsNullOrWhiteSpace(caseArea) && !string.IsNullOrWhiteSpace(matterType))
        {
            try
            {
                foreach (var issue in CaseIssueCatalog.GetIssues(caseArea, matterType))
                {
                    _cboIssue.Items.Add(issue);
                }
                _cboIssue.Enabled = true;
                SelectComboItem(_cboIssue, selectedIssue);
            }
            catch (ArgumentException)
            {
                _cboIssue.SelectedIndex = -1;
            }
        }
        else
        {
            _cboIssue.SelectedIndex = -1;
        }

        _cboIssue.EndUpdate();
    }

    private void PopulateIndustrySelections(string? selectedPlaintiffIndustry, string? selectedDefendantIndustry)
    {
        var industryOptions = Enum.GetNames<Industry>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PopulateIndustryCombo(_cboPlaintiffIndustry, selectedPlaintiffIndustry, industryOptions);
        PopulateIndustryCombo(_cboDefendantIndustry, selectedDefendantIndustry, industryOptions);
    }

    private static void PopulateIndustryCombo(ComboBox combo, string? selectedValue, IReadOnlyList<string> options)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.Add(new IndustryOption(RandomIndustryOption, RandomIndustryOption));
        foreach (var option in options)
        {
            combo.Items.Add(new IndustryOption(option, EnumHelper.HumanizeEnumName(option)));
        }
        SelectComboItem(combo, selectedValue);
        if (combo.SelectedIndex < 0)
        {
            combo.SelectedIndex = 0;
        }
        combo.EndUpdate();
    }

    private static string GetSelectedIndustryPreference(ComboBox combo)
    {
        if (combo.SelectedItem is IndustryOption option && !string.IsNullOrWhiteSpace(option.Name))
        {
            return option.Name;
        }

        if (combo.SelectedValue is string value && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return RandomIndustryOption;
    }

    private static void SelectComboItem(ComboBox combo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            combo.SelectedIndex = -1;
            return;
        }

        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is IndustryOption option
                && string.Equals(option.Name, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }

            if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        combo.SelectedIndex = -1;
    }

    private static decimal ClampPartyCount(int value)
    {
        if (value < 1)
            return 1;
        if (value > 3)
            return 3;
        return value;
    }

    private sealed class IndustryOption
    {
        public IndustryOption(string name, string display)
        {
            Name = name;
            Display = display;
        }

        public string Name { get; }
        public string Display { get; }

        public override string ToString() => Display;
    }
}
