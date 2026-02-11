using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.UserControls;

public class StepWorldModel : UserControl, IWizardStep
{
    private WizardState _state = null!;
    private Button _btnRegenerate = null!;
    private DataGridView _gridOrganizations = null!;
    private DataGridView _gridKeyPeople = null!;
    private Label _lblStatus = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private Panel _contentPanel = null!;
    private Panel _emptyStatePanel = null!;
    private Label _lblEmptyState = null!;
    private bool _isLoading = false;
    private BindingList<WorldOrganizationRow> _organizationRows = null!;
    private BindingList<WorldPersonRow> _personRows = null!;

    public string StepTitle => "Review World Model";
    public bool CanMoveNext => _state?.WorldModel != null && !_isLoading;
    public bool CanMoveBack => !_isLoading;
    public string NextButtonText => "Generate Storyline >";

    public event EventHandler? StateChanged;

    public StepWorldModel()
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

        _btnRegenerate = ButtonHelper.CreateButton("Regenerate", 110, 32, ButtonStyle.Primary);
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
            Text = "The AI has generated a world model. Review, edit details, or regenerate.",
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

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window
        };

        var gridsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        gridsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        gridsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));

        _gridOrganizations = BuildOrganizationGrid();
        _gridKeyPeople = BuildKeyPeopleGrid();
        _gridOrganizations.CurrentCellDirtyStateChanged += (s, e) => CommitGridEdit(_gridOrganizations);
        _gridKeyPeople.CurrentCellDirtyStateChanged += (s, e) => CommitGridEdit(_gridKeyPeople);
        _gridOrganizations.CellValueChanged += GridOrganizations_CellValueChanged;
        _gridOrganizations.CellValidating += GridOrganizations_CellValidating;
        _gridKeyPeople.CellValidating += GridKeyPeople_CellValidating;

        gridsLayout.Controls.Add(BuildLabeledPanel("Organizations", _gridOrganizations), 0, 0);
        gridsLayout.Controls.Add(BuildLabeledPanel("Key People", _gridKeyPeople), 0, 1);

        _contentPanel.Controls.Add(gridsLayout);

        _emptyStatePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Visible = false
        };
        _lblEmptyState = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Font = new Font(this.Font.FontFamily, 10F, FontStyle.Italic)
        };
        _emptyStatePanel.Controls.Add(_lblEmptyState);
        _contentPanel.Controls.Add(_emptyStatePanel);

        mainLayout.Controls.Add(_contentPanel, 0, 1);

        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.WorldModel);

        _lblStatus = new Label
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
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

    private static Panel BuildLabeledPanel(string title, Control content)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0)
        };

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(label);

        return panel;
    }

    private static DataGridView BuildOrganizationGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Side",
            HeaderText = "Side",
            DataPropertyName = "Side",
            Width = 90
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "Name",
            DataPropertyName = "Name",
            Width = 180
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Domain",
            HeaderText = "Domain",
            DataPropertyName = "Domain",
            Width = 160
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "OrganizationType",
            HeaderText = "Type",
            DataPropertyName = "OrganizationType",
            DataSource = Enum.GetValues<OrganizationType>(),
            Width = 130
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Industry",
            HeaderText = "Industry",
            DataPropertyName = "Industry",
            DataSource = Enum.GetValues<Industry>(),
            Width = 170
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "State",
            HeaderText = "State",
            DataPropertyName = "State",
            DataSource = Enum.GetValues<UsState>(),
            Width = 90
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Founded",
            HeaderText = "Founded",
            DataPropertyName = "Founded",
            Width = 80
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Description",
            HeaderText = "Description",
            DataPropertyName = "Description",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        return grid;
    }

    private static DataGridView BuildKeyPeopleGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FirstName",
            HeaderText = "First",
            DataPropertyName = "FirstName",
            Width = 120
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "LastName",
            HeaderText = "Last",
            DataPropertyName = "LastName",
            Width = 120
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Email",
            HeaderText = "Email",
            DataPropertyName = "Email",
            Width = 220,
            ReadOnly = true
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Organization",
            HeaderText = "Organization",
            DataPropertyName = "Organization",
            Width = 160,
            ReadOnly = true
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Department",
            HeaderText = "Department",
            DataPropertyName = "Department",
            Width = 130,
            ReadOnly = true
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Role",
            HeaderText = "Role",
            DataPropertyName = "Role",
            Width = 160,
            ReadOnly = true
        });

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Involvement",
            HeaderText = "Involvement",
            DataPropertyName = "Involvement",
            DataSource = new[] { "Actor", "Target", "Intermediary" },
            Width = 120
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Personality",
            HeaderText = "Personality",
            DataPropertyName = "Personality",
            Width = 220
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CommunicationStyle",
            HeaderText = "Communication",
            DataPropertyName = "CommunicationStyle",
            Width = 220
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "InvolvementSummary",
            HeaderText = "Involvement Summary",
            DataPropertyName = "InvolvementSummary",
            Width = 260
        });

        // Keep edge columns fixed-width so the first and last fields stay readable when scrolling.
        ApplyEdgeColumnSizing(grid, minWidth: 140);

        return grid;
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        UpdateWorldModelDisplay();
        UpdateStatus();

        if (_state.WorldModel == null)
        {
            await GenerateWorldModelAsync();
        }
    }

    public Task OnLeaveStepAsync()
    {
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (_state.WorldModel == null)
        {
            MessageBox.Show("A world model is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        if (HasValidationErrors())
        {
            MessageBox.Show("Please resolve validation issues before continuing.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private static void CommitGridEdit(DataGridView grid)
    {
        if (grid.IsCurrentCellDirty)
        {
            grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private static void ApplyEdgeColumnSizing(DataGridView grid, int minWidth)
    {
        if (grid.Columns.Count == 0)
            return;

        var first = grid.Columns[0];
        var last = grid.Columns[^1];

        first.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        last.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

        if (first.Width < minWidth)
            first.Width = minWidth;
        if (last.Width < minWidth)
            last.Width = minWidth;
    }

    private void GridOrganizations_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        if (_gridOrganizations.Columns[e.ColumnIndex].Name == "Domain"
            && _gridOrganizations.Rows[e.RowIndex].DataBoundItem is WorldOrganizationRow row)
        {
            UpdateEmailsForOrganization(row.Organization);
            _gridKeyPeople.Refresh();
        }

        _gridOrganizations.InvalidateRow(e.RowIndex);
    }

    private void GridOrganizations_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        var column = _gridOrganizations.Columns[e.ColumnIndex];
        var cell = _gridOrganizations.Rows[e.RowIndex].Cells[e.ColumnIndex];
        cell.ErrorText = string.Empty;

        var value = e.FormattedValue?.ToString() ?? string.Empty;
        var error = column.Name switch
        {
            "Name" => string.IsNullOrWhiteSpace(value) ? "Name is required." : string.Empty,
            "Domain" => ValidateDomain(value),
            "Founded" => ValidateFoundedYear(value),
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(error))
        {
            cell.ErrorText = error;
            e.Cancel = true;
        }
    }

    private void GridKeyPeople_CellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        var column = _gridKeyPeople.Columns[e.ColumnIndex];
        var cell = _gridKeyPeople.Rows[e.RowIndex].Cells[e.ColumnIndex];
        cell.ErrorText = string.Empty;

        var value = e.FormattedValue?.ToString() ?? string.Empty;
        var error = column.Name switch
        {
            "FirstName" => string.IsNullOrWhiteSpace(value) ? "First name is required." : string.Empty,
            "LastName" => string.IsNullOrWhiteSpace(value) ? "Last name is required." : string.Empty,
            "Involvement" => string.IsNullOrWhiteSpace(value) ? "Select involvement." : string.Empty,
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(error))
        {
            cell.ErrorText = error;
            e.Cancel = true;
        }
    }

    private bool HasValidationErrors()
    {
        return HasGridErrors(_gridOrganizations) || HasGridErrors(_gridKeyPeople);
    }

    private static bool HasGridErrors(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (!string.IsNullOrWhiteSpace(cell.ErrorText))
                    return true;
            }
        }

        return false;
    }

    private static string ValidateDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Domain is required.";

        var trimmed = value.Trim();
        if (trimmed.Contains('@'))
            return "Domain should not include '@'.";
        if (!trimmed.Contains('.'))
            return "Domain must include a '.'.";

        return string.Empty;
    }

    private static string ValidateFoundedYear(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!int.TryParse(value, out var year))
            return "Founded year must be a number.";

        if (year == 0)
            return string.Empty;

        var maxYear = DateTime.UtcNow.Year;
        if (year < 1800 || year > maxYear)
            return $"Founded year must be between 1800 and {maxYear}.";

        return string.Empty;
    }

    private async void BtnRegenerate_Click(object? sender, EventArgs e)
    {
        await GenerateWorldModelAsync();
    }

    private async Task GenerateWorldModelAsync()
    {
        _isLoading = true;
        _btnRegenerate.Enabled = false;
        _lblStatus.Text = "";
        _loadingOverlay.Show(this);
        UpdateEmptyState();
        StateChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var openAI = _state.CreateOpenAIService();
            var generator = new WorldModelGenerator(openAI);

            var progress = new Progress<string>(status =>
            {
                _lblStatus.Text = status;
                _lblStatus.ForeColor = Color.Blue;
            });

            var request = new WorldModelRequest
            {
                CaseArea = _state.CaseArea,
                MatterType = _state.MatterType,
                Issue = _state.Issue,
                IssueDescription = _state.IssueDescription,
                PlaintiffIndustry = _state.PlaintiffIndustry,
                DefendantIndustry = _state.DefendantIndustry,
                PlaintiffOrganizationCount = _state.PlaintiffOrganizationCount,
                DefendantOrganizationCount = _state.DefendantOrganizationCount,
                AdditionalUserContext = _state.AdditionalInstructions
            };
            var world = await generator.GenerateWorldModelAsync(request, progress);

            _state.WorldModel = world;
            UpdateWorldModelDisplay();

            _lblStatus.Text = "World model ready.";
            _lblStatus.ForeColor = Color.Green;
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
            UpdateEmptyState();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateWorldModelDisplay()
    {
        var world = _state.WorldModel;

        _organizationRows = new BindingList<WorldOrganizationRow>();
        _personRows = new BindingList<WorldPersonRow>();

        if (world != null)
        {
            foreach (var org in world.Plaintiffs)
            {
                _organizationRows.Add(CreateOrganizationRow(org, "Plaintiff"));
                AddKeyPeopleRows(org, _personRows);
            }

            foreach (var org in world.Defendants)
            {
                _organizationRows.Add(CreateOrganizationRow(org, "Defendant"));
                AddKeyPeopleRows(org, _personRows);
            }
        }

        _gridOrganizations.DataSource = _organizationRows;
        _gridKeyPeople.DataSource = _personRows;
    }

    private static WorldOrganizationRow CreateOrganizationRow(Organization organization, string side)
    {
        return new WorldOrganizationRow(organization, side);
    }

    private static void AddKeyPeopleRows(Organization organization, BindingList<WorldPersonRow> rows)
    {
        foreach (var assignment in organization.EnumerateCharacters())
        {
            rows.Add(new WorldPersonRow(assignment.Character, organization, assignment.Department, assignment.Role));
        }
    }

    private static void UpdateEmailsForOrganization(Organization organization)
    {
        foreach (var assignment in organization.EnumerateCharacters())
        {
            assignment.Character.Email = EmailAddressHelper.GenerateEmail(
                assignment.Character.FirstName,
                assignment.Character.LastName,
                organization.Domain);
        }
    }

    private void UpdateStatus()
    {
        if (_state.WorldModel == null)
        {
            _lblStatus.Text = "No world model available.";
            _lblStatus.ForeColor = Color.Gray;
            UpdateEmptyState();
            return;
        }

        _lblStatus.Text = "World model ready.";
        _lblStatus.ForeColor = Color.Green;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (_emptyStatePanel == null || _lblEmptyState == null)
            return;
        if (_state == null)
            return;

        if (_isLoading && _state.WorldModel == null)
        {
            _lblEmptyState.Text = "Generating world model...\nThis can take a minute.";
            _emptyStatePanel.Visible = true;
            _contentPanel.Enabled = false;
            return;
        }

        if (!_isLoading && _state.WorldModel == null)
        {
            _lblEmptyState.Text = "No world model available.\nClick Regenerate to try again.";
            _emptyStatePanel.Visible = true;
            _contentPanel.Enabled = false;
            return;
        }

        _emptyStatePanel.Visible = false;
        _contentPanel.Enabled = true;
    }

    private sealed class WorldOrganizationRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public WorldOrganizationRow(Organization organization, string side)
        {
            Organization = organization;
            Side = side;
        }

        public Organization Organization { get; }
        public string Side { get; }

        public string Name
        {
            get => Organization.Name;
            set
            {
                if (Organization.Name == value)
                    return;
                Organization.Name = value ?? string.Empty;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Domain
        {
            get => Organization.Domain;
            set
            {
                if (Organization.Domain == value)
                    return;
                Organization.Domain = value ?? string.Empty;
                OnPropertyChanged(nameof(Domain));
            }
        }

        public OrganizationType OrganizationType
        {
            get => Organization.OrganizationType;
            set
            {
                if (Organization.OrganizationType == value)
                    return;
                Organization.OrganizationType = value;
                OnPropertyChanged(nameof(OrganizationType));
            }
        }

        public Industry Industry
        {
            get => Organization.Industry;
            set
            {
                if (Organization.Industry == value)
                    return;
                Organization.Industry = value;
                OnPropertyChanged(nameof(Industry));
            }
        }

        public UsState State
        {
            get => Organization.State;
            set
            {
                if (Organization.State == value)
                    return;
                Organization.State = value;
                OnPropertyChanged(nameof(State));
            }
        }

        public int Founded
        {
            get => Organization.Founded?.Year ?? 0;
            set
            {
                var newValue = value <= 0 ? (DateTime?)null : new DateTime(value, 1, 1);
                if (Organization.Founded == newValue)
                    return;
                Organization.Founded = newValue;
                OnPropertyChanged(nameof(Founded));
            }
        }

        public string Description
        {
            get => Organization.Description;
            set
            {
                if (Organization.Description == value)
                    return;
                Organization.Description = value ?? string.Empty;
                OnPropertyChanged(nameof(Description));
            }
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class WorldPersonRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public WorldPersonRow(Character character, Organization organization, Department department, Role role)
        {
            Character = character;
            OrganizationModel = organization;
            DepartmentModel = department;
            RoleModel = role;
        }

        public Character Character { get; }
        public Organization OrganizationModel { get; }
        public Department DepartmentModel { get; }
        public Role RoleModel { get; }

        public string FirstName
        {
            get => Character.FirstName;
            set
            {
                if (Character.FirstName == value)
                    return;
                Character.FirstName = value ?? string.Empty;
                UpdateEmail();
                OnPropertyChanged(nameof(FirstName));
            }
        }

        public string LastName
        {
            get => Character.LastName;
            set
            {
                if (Character.LastName == value)
                    return;
                Character.LastName = value ?? string.Empty;
                UpdateEmail();
                OnPropertyChanged(nameof(LastName));
            }
        }

        public string Email => Character.Email;

        [SuppressMessage("SonarLint", "S1144:Unused private types or members should be removed", Justification = "Referenced by the Key People grid via data binding.")]
        public string Organization => OrganizationModel.Name;

        public string Personality
        {
            get => Character.Personality;
            set
            {
                if (Character.Personality == value)
                    return;
                Character.Personality = value ?? string.Empty;
                OnPropertyChanged(nameof(Personality));
            }
        }

        public string CommunicationStyle
        {
            get => Character.CommunicationStyle;
            set
            {
                if (Character.CommunicationStyle == value)
                    return;
                Character.CommunicationStyle = value ?? string.Empty;
                OnPropertyChanged(nameof(CommunicationStyle));
            }
        }

        public string Involvement
        {
            get => Character.Involvement;
            set
            {
                if (Character.Involvement == value)
                    return;
                Character.Involvement = value ?? string.Empty;
                OnPropertyChanged(nameof(Involvement));
            }
        }

        public string InvolvementSummary
        {
            get => Character.InvolvementSummary;
            set
            {
                if (Character.InvolvementSummary == value)
                    return;
                Character.InvolvementSummary = value ?? string.Empty;
                OnPropertyChanged(nameof(InvolvementSummary));
            }
        }

        private void UpdateEmail()
        {
            Character.Email = EmailAddressHelper.GenerateEmail(
                Character.FirstName,
                Character.LastName,
                OrganizationModel.Domain);
            OnPropertyChanged(nameof(Email));
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
