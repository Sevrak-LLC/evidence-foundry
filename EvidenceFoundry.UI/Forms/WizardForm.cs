using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.UserControls;

namespace EvidenceFoundry.Forms;

public partial class WizardForm : Form
{
    private readonly WizardState _state = new();
    private readonly List<UserControl> _steps = new();
    private int _currentStepIndex = 0;

    private Panel _contentPanel = null!;
    private Panel _navigationPanel = null!;
    private Panel _stepIndicatorPanel = null!;
    private Panel _footerPanel = null!;
    private Button _btnBack = null!;
    private Button _btnNext = null!;
    private Button _btnCancel = null!;
    private Label _lblStepTitle = null!;
    private Label _lblCostTracker = null!;
    private System.Windows.Forms.Timer _costUpdateTimer = null!;

    public WizardForm()
    {
        InitializeComponent();
        InitializeWizardUI();
        LoadSteps();
        InitializeCostTracker();
    }

    private void InitializeCostTracker()
    {
        _costUpdateTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000 // Update every second
        };
        _costUpdateTimer.Tick += (s, e) => UpdateCostDisplay();
        _costUpdateTimer.Start();
    }

    private void UpdateCostDisplay()
    {
        var tracker = _state.UsageTracker;
        var totalTokens = tracker.TotalInputTokens + tracker.TotalOutputTokens;
        if (totalTokens > 0)
        {
            _lblCostTracker.Text = $"Tokens: {totalTokens:N0} | Cost: ${tracker.TotalCost:F4}";
        }
        else
        {
            _lblCostTracker.Text = "API Cost: $0.0000";
        }
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(900, 600);
        this.MinimumSize = new Size(800, 500);
        this.Name = "WizardForm";
        this.Text = "Evidence Foundry - E-Discovery Dataset Generator";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.ResumeLayout(false);
    }

    private void InitializeWizardUI()
    {
        // Step indicator panel at top
        _stepIndicatorPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(45, 45, 48),
            Padding = new Padding(20, 10, 20, 10)
        };

        _lblStepTitle = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _stepIndicatorPanel.Controls.Add(_lblStepTitle);

        // Cost tracker panel on the right side - more prominent
        var costPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 320,
            BackColor = Color.FromArgb(60, 60, 65),
            Padding = new Padding(10, 5, 10, 5)
        };

        _lblCostTracker = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(100, 200, 100),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "API Cost: $0.0000"
        };
        costPanel.Controls.Add(_lblCostTracker);
        _stepIndicatorPanel.Controls.Add(costPanel);

        // Promotional footer at very bottom
        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = Color.FromArgb(45, 45, 48)
        };

        var lblFooter = new LinkLabel
        {
            Text = "Provided for free distribution by Sevrak LLC  |  Visit www.sevrak.com for more information",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(180, 180, 180),
            LinkColor = Color.FromArgb(100, 180, 255),
            ActiveLinkColor = Color.FromArgb(150, 200, 255),
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleCenter
        };
        lblFooter.Links.Add(53, 16, "https://www.sevrak.com");
        lblFooter.LinkClicked += (s, e) =>
        {
            if (e.Link?.LinkData is string url)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        };
        _footerPanel.Controls.Add(lblFooter);

        // Navigation panel at bottom
        _navigationPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            Padding = new Padding(20, 10, 20, 10)
        };

        _btnCancel = ButtonHelper.CreateButton("Cancel", 100, 35, ButtonStyle.Default);
        _btnCancel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _btnCancel.Click += BtnCancel_Click;

        _btnBack = ButtonHelper.CreateButton("< Back", 100, 35, ButtonStyle.Secondary);
        _btnBack.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _btnBack.Click += BtnBack_Click;

        _btnNext = ButtonHelper.CreateButton("Next >", 100, 35, ButtonStyle.Primary);
        _btnNext.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _btnNext.Click += BtnNext_Click;

        _navigationPanel.Controls.Add(_btnCancel);
        _navigationPanel.Controls.Add(_btnBack);
        _navigationPanel.Controls.Add(_btnNext);

        // Position buttons
        _navigationPanel.Resize += (s, e) =>
        {
            _btnCancel.Location = new Point(20, 12);
            _btnNext.Location = new Point(_navigationPanel.Width - _btnNext.Width - 20, 12);
            _btnBack.Location = new Point(_btnNext.Left - _btnBack.Width - 10, 12);
        };

        // Content panel in center
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15, 10, 15, 10)
        };

        // Add panels in correct docking order:
        // For Bottom dock: LAST added ends up at the very bottom
        // For Top dock: LAST added ends up at the very top
        // Fill should be added LAST to take remaining space
        this.Controls.Add(_contentPanel);      // Fill - takes remaining space
        this.Controls.Add(_stepIndicatorPanel); // Top - header
        this.Controls.Add(_navigationPanel);   // Bottom - buttons (added before footer, so above it)
        this.Controls.Add(_footerPanel);       // Bottom - at very bottom (added last)
    }

    private void LoadSteps()
    {
        _steps.Add(new StepApiConfiguration());
        _steps.Add(new StepTopicInput());
        _steps.Add(new StepWorldModel());
        _steps.Add(new StepStorylines());
        _steps.Add(new StepCharacters());
        _steps.Add(new StepStoryBeats());
        _steps.Add(new StepGenerationConfig());
        _steps.Add(new StepGenerationSummary());
        _steps.Add(new StepGeneration());
        _steps.Add(new StepComplete());

        foreach (var step in _steps)
        {
            if (step is IWizardStep wizardStep)
            {
                wizardStep.BindState(_state);
                wizardStep.StateChanged += (s, e) => UpdateNavigationButtons();
            }
            step.Dock = DockStyle.Fill;
            step.Visible = false;
            _contentPanel.Controls.Add(step);
        }

        ShowStep(0);
    }

    private async void ShowStep(int index)
    {
        if (index < 0 || index >= _steps.Count) return;

        // Hide current step
        if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
        {
            var currentStep = _steps[_currentStepIndex];
            if (currentStep is IWizardStep current)
            {
                await current.OnLeaveStepAsync();
            }
            currentStep.Visible = false;
        }

        _currentStepIndex = index;
        var newStep = _steps[index];
        newStep.Visible = true;

        if (newStep is IWizardStep wizard)
        {
            _lblStepTitle.Text = $"Step {index + 1} of {_steps.Count}: {wizard.StepTitle}";
            _btnNext.Text = wizard.NextButtonText;
            _btnBack.Enabled = wizard.CanMoveBack && index > 0;

            await wizard.OnEnterStepAsync();
        }

        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        _btnBack.Enabled = _currentStepIndex > 0;

        if (_steps[_currentStepIndex] is IWizardStep wizard)
        {
            _btnBack.Enabled = wizard.CanMoveBack && _currentStepIndex > 0;
            _btnNext.Enabled = wizard.CanMoveNext;
        }

        // Last step shows "Finish" (handled by StepComplete)
        if (_currentStepIndex == _steps.Count - 1)
        {
            _btnNext.Text = "Finish";
        }
    }

    private async void BtnNext_Click(object? sender, EventArgs e)
    {
        if (_currentStepIndex >= _steps.Count - 1)
        {
            // On last step, close the form
            this.Close();
            return;
        }

        var currentStep = _steps[_currentStepIndex];
        if (currentStep is IWizardStep wizard)
        {
            _btnNext.Enabled = false;
            try
            {
                if (!await wizard.ValidateStepAsync())
                {
                    _btnNext.Enabled = true;
                    return;
                }
            }
            finally
            {
                _btnNext.Enabled = true;
            }
        }

        ShowStep(_currentStepIndex + 1);
    }

    private void BtnBack_Click(object? sender, EventArgs e)
    {
        if (_currentStepIndex > 0)
        {
            ShowStep(_currentStepIndex - 1);
        }
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Are you sure you want to cancel?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            this.Close();
        }
    }

    public void NavigateToStep(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < _steps.Count)
        {
            ShowStep(stepIndex);
        }
    }

    public void EnableNavigation(bool enabled)
    {
        _btnBack.Enabled = enabled && _currentStepIndex > 0;
        _btnNext.Enabled = enabled;
        _btnCancel.Enabled = enabled;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _costUpdateTimer?.Stop();
        _costUpdateTimer?.Dispose();
        base.OnFormClosing(e);
    }
}
