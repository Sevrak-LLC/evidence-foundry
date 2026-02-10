using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.UserControls;

public class ModelConfigurationDialog : Form
{
    private DataGridView _gridModels = null!;
    private Button _btnAdd = null!;
    private Button _btnRemove = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;
    private Button _btnResetDefaults = null!;
    private BindingSource _bindingSource = null!;

    public List<AIModelConfig> Models { get; private set; }

    public ModelConfigurationDialog(List<AIModelConfig> models)
    {
        // Clone the models to avoid modifying the original until OK is clicked
        Models = models.Select(m => new AIModelConfig
        {
            ModelId = m.ModelId,
            DisplayName = m.DisplayName,
            InputTokenPricePerMillion = m.InputTokenPricePerMillion,
            OutputTokenPricePerMillion = m.OutputTokenPricePerMillion,
            IsDefault = m.IsDefault,
            MaxOutputTokens = m.MaxOutputTokens,
            MaxJsonOutputTokens = m.MaxJsonOutputTokens
        }).ToList();

        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Text = "Configure AI Models";
        this.Size = new Size(700, 450);
        this.MinimumSize = new Size(600, 350);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        // Header label
        var lblHeader = new Label
        {
            Text = "Configure model IDs and pricing (per million tokens). Pricing is used for cost tracking.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        mainLayout.Controls.Add(lblHeader, 0, 0);

        // Grid panel
        var gridPanel = new Panel { Dock = DockStyle.Fill };

        _bindingSource = new BindingSource { DataSource = Models };

        _gridModels = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            DataSource = _bindingSource
        };

        _gridModels.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "IsDefault",
            HeaderText = "Default",
            DataPropertyName = "IsDefault",
            Width = 55
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ModelId",
            HeaderText = "Model ID",
            DataPropertyName = "ModelId",
            Width = 150
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DisplayName",
            HeaderText = "Display Name",
            DataPropertyName = "DisplayName",
            Width = 130
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "InputPrice",
            HeaderText = "Input $/M",
            DataPropertyName = "InputTokenPricePerMillion",
            Width = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "F2" }
        });

        _gridModels.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "OutputPrice",
            HeaderText = "Output $/M",
            DataPropertyName = "OutputTokenPricePerMillion",
            Width = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "F2" }
        });

        _gridModels.CellValueChanged += GridModels_CellValueChanged;
        _gridModels.CurrentCellDirtyStateChanged += GridModels_CurrentCellDirtyStateChanged;

        // Button panel on the right side of grid
        var gridButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.TopDown,
            Width = 110,
            Padding = new Padding(5, 0, 0, 0)
        };

        _btnAdd = ButtonHelper.CreateButton("Add Model", 100, 30, ButtonStyle.Primary);
        _btnAdd.Click += BtnAdd_Click;
        gridButtonPanel.Controls.Add(_btnAdd);

        _btnRemove = ButtonHelper.CreateButton("Remove", 100, 30, ButtonStyle.Danger);
        _btnRemove.Click += BtnRemove_Click;
        gridButtonPanel.Controls.Add(_btnRemove);

        _btnResetDefaults = ButtonHelper.CreateButton("Reset Defaults", 100, 30, ButtonStyle.Secondary);
        _btnResetDefaults.Margin = new Padding(0, 20, 0, 0);
        _btnResetDefaults.Click += BtnResetDefaults_Click;
        gridButtonPanel.Controls.Add(_btnResetDefaults);

        gridPanel.Controls.Add(_gridModels);
        gridPanel.Controls.Add(gridButtonPanel);
        mainLayout.Controls.Add(gridPanel, 0, 1);

        // Bottom button panel
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 5, 0, 0)
        };

        _btnCancel = ButtonHelper.CreateButton("Cancel", 80, 30, ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        bottomPanel.Controls.Add(_btnCancel);

        _btnOk = ButtonHelper.CreateButton("OK", 80, 30, ButtonStyle.Primary);
        _btnOk.Margin = new Padding(0, 0, 10, 0);
        _btnOk.Click += BtnOk_Click;
        bottomPanel.Controls.Add(_btnOk);

        mainLayout.Controls.Add(bottomPanel, 0, 2);

        this.Controls.Add(mainLayout);
        this.AcceptButton = _btnOk;
        this.CancelButton = _btnCancel;
    }

    private void GridModels_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        // Commit checkbox changes immediately
        if (_gridModels.IsCurrentCellDirty && _gridModels.CurrentCell is DataGridViewCheckBoxCell)
        {
            _gridModels.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void GridModels_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        // Handle default checkbox - only one can be default
        if (e.ColumnIndex == 0 && e.RowIndex >= 0)
        {
            var currentRow = _gridModels.Rows[e.RowIndex];
            var isDefault = (bool?)currentRow.Cells["IsDefault"].Value ?? false;

            if (isDefault)
            {
                // Uncheck all other rows
                foreach (DataGridViewRow row in _gridModels.Rows)
                {
                    if (row.Index != e.RowIndex)
                    {
                        var model = row.DataBoundItem as AIModelConfig;
                        if (model != null)
                        {
                            model.IsDefault = false;
                        }
                    }
                }
                _gridModels.Refresh();
            }
        }
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        var newModel = new AIModelConfig
        {
            ModelId = "new-model",
            DisplayName = "New Model",
            InputTokenPricePerMillion = 1.00m,
            OutputTokenPricePerMillion = 3.00m,
            IsDefault = false
        };
        Models.Add(newModel);
        _bindingSource.ResetBindings(false);

        // Select the new row
        _gridModels.ClearSelection();
        _gridModels.Rows[_gridModels.Rows.Count - 1].Selected = true;
        _gridModels.CurrentCell = _gridModels.Rows[_gridModels.Rows.Count - 1].Cells["ModelId"];
        _gridModels.BeginEdit(true);
    }

    private void BtnRemove_Click(object? sender, EventArgs e)
    {
        if (_gridModels.SelectedRows.Count > 0)
        {
            var selectedModel = _gridModels.SelectedRows[0].DataBoundItem as AIModelConfig;
            if (selectedModel != null)
            {
                if (Models.Count <= 1)
                {
                    MessageBox.Show("You must have at least one model configured.", "Cannot Remove",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Models.Remove(selectedModel);
                _bindingSource.ResetBindings(false);

                // Ensure at least one model is default
                if (!Models.Any(m => m.IsDefault) && Models.Count > 0)
                {
                    Models[0].IsDefault = true;
                    _bindingSource.ResetBindings(false);
                }
            }
        }
    }

    private void BtnResetDefaults_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Reset all models to default configuration?", "Reset Defaults",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            Models = AIModelConfig.GetDefaultModels();
            _bindingSource.DataSource = Models;
            _bindingSource.ResetBindings(false);
        }
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        // Validate
        var modelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in Models)
        {
            if (string.IsNullOrWhiteSpace(model.ModelId))
            {
                MessageBox.Show("All models must have a Model ID.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!AIModelConfig.IsValidModelId(model.ModelId))
            {
                MessageBox.Show("Model ID contains invalid characters. Use letters, numbers, dots, hyphens, underscores, or colons.",
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!modelIds.Add(model.ModelId.Trim()))
            {
                MessageBox.Show("Model IDs must be unique.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(model.DisplayName))
            {
                MessageBox.Show("All models must have a Display Name.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (model.InputTokenPricePerMillion < 0 || model.OutputTokenPricePerMillion < 0)
            {
                MessageBox.Show("Token pricing must be zero or greater.", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // Ensure exactly one default
        if (!Models.Any(m => m.IsDefault))
        {
            Models[0].IsDefault = true;
        }

        this.DialogResult = DialogResult.OK;
        this.Close();
    }
}
