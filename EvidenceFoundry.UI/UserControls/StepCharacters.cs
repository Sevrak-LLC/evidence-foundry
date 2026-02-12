using System.ComponentModel;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.UserControls;

public class StepCharacters : UserControl, IWizardStep
{
    private const string StateColumnName = "State";
    private WizardState _state = null!;
    private DataGridView _gridCharacters = null!;
    private DataGridView _gridPlaintiffs = null!;
    private DataGridView _gridDefendants = null!;
    private Button _btnRegenerate = null!;
    private Button _btnDelete = null!;
    private Label _lblStatus = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private bool _isLoading;
    private BindingList<CharacterRow> _characterRows = null!;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private int _sortColumnIndex = -1;

    public string StepTitle => "Review Organizations & Characters";
    public bool CanMoveNext => _state?.Characters.Count >= 2 && !_isLoading;
    public bool CanMoveBack => !_isLoading;
    public string NextButtonText => "Next >";

    public event EventHandler? StateChanged;

    public StepCharacters()
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

        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Header with regenerate button
        _btnDelete = ButtonHelper.CreateButton("Delete Selected", 110, 32, ButtonStyle.Default);
        _btnDelete.Click += BtnDelete_Click;

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
            Text = "The AI has generated organizations and characters. You can review and edit details.",
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
        headerButtonPanel.Controls.Add(_btnDelete);
        headerLayout.Controls.Add(headerButtonPanel, 1, 0);

        headerLayout.Layout += (s, e) =>
        {
            var maxWidth = Math.Max(0, headerLayout.Width - headerButtonPanel.Width - 10);
            lblHeader.MaximumSize = new Size(maxWidth, 0);
        };

        mainLayout.Controls.Add(headerLayout, 0, 0);

        // Organization grids panel
        var orgPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 0)
        };
        orgPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        orgPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var plaintiffPanel = BuildOrgPanel("Plaintiff Organizations", out _gridPlaintiffs);
        var defendantPanel = BuildOrgPanel("Defendant Organizations", out _gridDefendants);

        orgPanel.Controls.Add(plaintiffPanel, 0, 0);
        orgPanel.Controls.Add(defendantPanel, 1, 0);

        mainLayout.Controls.Add(orgPanel, 0, 1);

        // Characters grid
        _gridCharacters = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D
        };

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(CharacterRow.FullName),
            HeaderText = "Full Name",
            DataPropertyName = nameof(CharacterRow.FullName),
            Width = 160
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(CharacterRow.Email),
            HeaderText = "Email",
            DataPropertyName = nameof(CharacterRow.Email),
            Width = 200
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Organization",
            HeaderText = "Organization",
            DataPropertyName = "Organization",
            Width = 160,
            ReadOnly = true
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Department",
            HeaderText = "Dept",
            DataPropertyName = "Department",
            Width = 110,
            ReadOnly = true
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Role",
            HeaderText = "Role",
            DataPropertyName = "Role",
            Width = 160,
            ReadOnly = true
        });

        _gridCharacters.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Personality",
            HeaderText = "Personality",
            DataPropertyName = nameof(CharacterRow.Personality),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        _gridCharacters.ColumnHeaderMouseClick += GridCharacters_ColumnHeaderMouseClick;
        _gridCharacters.SelectionChanged += GridCharacters_SelectionChanged;
        _gridCharacters.UserDeletingRow += GridCharacters_UserDeletingRow;

        mainLayout.Controls.Add(_gridCharacters, 0, 2);

        // Create loading overlay for the grid
        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.Characters);

        // Status label
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
        mainLayout.Controls.Add(_lblStatus, 0, 3);

        this.Controls.Add(mainLayout);
    }

    private Panel BuildOrgPanel(string title, out DataGridView grid)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 5, 0) };
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
        };

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D
        };

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
            Width = 150
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "OrganizationType",
            HeaderText = "Type",
            DataPropertyName = "OrganizationType",
            Width = 80
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = StateColumnName,
            HeaderText = StateColumnName,
            DataPropertyName = StateColumnName,
            Width = 110
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Founded",
            HeaderText = "Founded",
            DataPropertyName = "Founded",
            Width = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy" }
        });

        grid.CellFormatting += GridOrganizations_CellFormatting;

        panel.Controls.Add(grid);
        panel.Controls.Add(label);

        return panel;
    }

    private async void BtnRegenerate_Click(object? sender, EventArgs e)
    {
        await GenerateCharactersAsync();
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (_gridCharacters.SelectedRows.Count > 0)
        {
            var selectedRows = _gridCharacters.SelectedRows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .OrderByDescending(r => r.Index)
                .ToList();

            foreach (var row in selectedRows)
            {
                if (row.DataBoundItem is CharacterRow characterRow)
                {
                    RemoveCharacter(characterRow.Id);
                }
            }

            RefreshCharacterGrid();
            UpdateStatus();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void GridCharacters_SelectionChanged(object? sender, EventArgs e)
    {
        _btnDelete.Enabled = _gridCharacters.SelectedRows.Count > 0 &&
                             _gridCharacters.SelectedRows.Cast<DataGridViewRow>().Any(r => !r.IsNewRow);
    }

    private void GridCharacters_UserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
    {
        if (e.Row?.DataBoundItem is CharacterRow characterRow)
        {
            RemoveCharacter(characterRow.Id);
        }

        UpdateStatus();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GridCharacters_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        var column = _gridCharacters.Columns[e.ColumnIndex];
        if (column == null || _characterRows == null || _characterRows.Count == 0)
            return;

        if (_sortColumnIndex == e.ColumnIndex)
        {
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        }
        else
        {
            _sortColumnIndex = e.ColumnIndex;
            _sortDirection = ListSortDirection.Ascending;
        }

        var propertyName = column.DataPropertyName;
        if (string.IsNullOrEmpty(propertyName))
            return;

        var sorted = _sortDirection == ListSortDirection.Ascending
            ? _characterRows.OrderBy(c => GetPropertyValue(c, propertyName)).ToList()
            : _characterRows.OrderByDescending(c => GetPropertyValue(c, propertyName)).ToList();

        _characterRows = new BindingList<CharacterRow>(sorted);
        _gridCharacters.DataSource = _characterRows;

        foreach (DataGridViewColumn col in _gridCharacters.Columns)
        {
            col.HeaderCell.SortGlyphDirection = SortOrder.None;
        }
        column.HeaderCell.SortGlyphDirection = _sortDirection == ListSortDirection.Ascending
            ? SortOrder.Ascending
            : SortOrder.Descending;
    }

    private static object? GetPropertyValue(object obj, string propertyName)
    {
        return obj.GetType().GetProperty(propertyName)?.GetValue(obj);
    }

    private void UpdateStatus()
    {
        var orgCount = _state.Organizations.Count;
        var plaintiffCount = _state.Organizations.Count(o => o.IsPlaintiff);
        var defendantCount = _state.Organizations.Count(o => o.IsDefendant);
        _lblStatus.Text = $"{_state.Characters.Count} characters across {orgCount} organizations ({plaintiffCount} plaintiff, {defendantCount} defendant).";
        _lblStatus.ForeColor = _state.Characters.Count >= 2 ? Color.Green : Color.Gray;
    }

    private async Task GenerateCharactersAsync()
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
                var storyline = _state.Storyline;
                if (storyline == null)
                    throw new InvalidOperationException("Storyline is required before generating characters.");

                var openAI = _state.CreateOpenAIService();
                var generator = new EntityGeneratorOrchestrator(
                    openAI,
                    _state.GenerationRandom,
                    _state.CreateLogger<EntityGeneratorOrchestrator>());

                var result = await generator.GenerateEntitiesAsync(
                    _state.Topic,
                    storyline,
                    progress);

                _state.Organizations = result.Organizations;
                _state.Characters = result.Characters;
                _state.CompanyDomain = result.PrimaryDomain;
                storyline.SetBeats(Array.Empty<StoryBeat>());

                RefreshOrganizationGrids();
                RefreshCharacterGrid();

                progress.Report("Generating presentation themes...");
                var themeGenerator = new ThemeGenerator(
                    openAI,
                    _state.CreateLogger<ThemeGenerator>());
                _state.DomainThemes = await themeGenerator.GenerateThemesForOrganizationsAsync(
                    _state.Topic,
                    _state.Organizations,
                    progress);

                UpdateStatus();
            });
    }

    private void RefreshOrganizationGrids()
    {
        var plaintiffRows = new BindingList<Organization>(_state.Organizations.Where(o => o.IsPlaintiff).ToList());
        var defendantRows = new BindingList<Organization>(_state.Organizations.Where(o => o.IsDefendant).ToList());
        _gridPlaintiffs.DataSource = plaintiffRows;
        _gridDefendants.DataSource = defendantRows;
    }

    private void RefreshCharacterGrid()
    {
        _characterRows = new BindingList<CharacterRow>(BuildCharacterRows());
        _gridCharacters.DataSource = _characterRows;
    }

    private List<CharacterRow> BuildCharacterRows()
    {
        var rows = new List<CharacterRow>();
        foreach (var assignment in _state.Organizations.SelectMany(o => o.EnumerateCharacters()))
        {
            rows.Add(new CharacterRow(
                assignment.Character,
                assignment.Organization.Name,
                EnumHelper.HumanizeEnumName(assignment.Department.Name.ToString()),
                EnumHelper.HumanizeEnumName(assignment.Role.Name.ToString())));
        }
        return rows;
    }

    private void RemoveCharacter(Guid characterId)
    {
        var match = _state.Organizations
            .SelectMany(org => org.Departments)
            .SelectMany(dept => dept.Roles)
            .SelectMany(role => role.Characters.Select(character => new { Role = role, Character = character }))
            .FirstOrDefault(entry => entry.Character.Id == characterId);

        if (match == null)
            return;

        match.Role.RemoveCharacter(match.Character);
        _state.Characters.RemoveAll(c => c.Id == characterId);
    }

    private void GridOrganizations_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.Value == null)
            return;

        var columnName = grid.Columns[e.ColumnIndex].Name;
        if (columnName == "OrganizationType" || columnName == StateColumnName)
        {
            e.Value = EnumHelper.HumanizeEnumName(e.Value.ToString() ?? string.Empty);
            e.FormattingApplied = true;
        }
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        RefreshOrganizationGrids();
        RefreshCharacterGrid();

        if (_state.Characters.Count == 0)
        {
            await GenerateCharactersAsync();
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
        if (_state.Characters.Count < 2)
        {
            MessageBox.Show("At least 2 characters are required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        var hasIncompleteCharacters = _state.Characters.Any(character =>
            string.IsNullOrWhiteSpace(character.FirstName) ||
            string.IsNullOrWhiteSpace(character.LastName) ||
            string.IsNullOrWhiteSpace(character.Email));

        if (hasIncompleteCharacters)
        {
            MessageBox.Show("All characters must have a name and email.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    private sealed class CharacterRow
    {
        private readonly Character _character;

        public CharacterRow(Character character, string organization, string department, string role)
        {
            _character = character;
            Organization = organization;
            Department = department;
            Role = role;
        }

        public Guid Id => _character.Id;

        public string FullName
        {
            get => _character.FullName;
            set
            {
                var parts = (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    _character.FirstName = string.Empty;
                    _character.LastName = string.Empty;
                }
                else if (parts.Length == 1)
                {
                    _character.FirstName = parts[0];
                    _character.LastName = string.Empty;
                }
                else
                {
                    _character.FirstName = parts[0];
                    _character.LastName = string.Join(" ", parts.Skip(1));
                }
            }
        }

        public string Email
        {
            get => _character.Email;
            set => _character.Email = value;
        }

        public string Personality
        {
            get => _character.Personality;
            set => _character.Personality = value;
        }

        public string Organization { get; }
        public string Department { get; }
        public string Role { get; }
    }
}
