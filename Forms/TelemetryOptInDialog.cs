using ReelDiscovery.Helpers;

namespace ReelDiscovery.Forms;

/// <summary>
/// First-run dialog asking user to opt-in to anonymous telemetry.
/// </summary>
public class TelemetryOptInDialog : Form
{
    public bool UserOptedIn { get; private set; }

    public TelemetryOptInDialog()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Text = "Help Improve ReelDiscovery";
        this.Size = new Size(520, 420);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20)
        };

        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Header
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Content
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // Link
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));  // Buttons

        // Header
        var lblHeader = new Label
        {
            Text = "Help Improve ReelDiscovery",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        mainPanel.Controls.Add(lblHeader, 0, 0);

        // Content - use a panel with auto-scroll to ensure all content is visible
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        var lblContent = new Label
        {
            Text = "Would you like to help improve ReelDiscovery by sending anonymous usage statistics?\n\n" +
                   "What we collect:\n" +
                   "  \u2022 Topic entered (first 50 characters only)\n" +
                   "  \u2022 Number of emails/threads generated\n" +
                   "  \u2022 AI model used\n" +
                   "  \u2022 App version\n\n" +
                   "What we DON'T collect:\n" +
                   "  \u2022 Your name, email, or any personal info\n" +
                   "  \u2022 Your OpenAI API key\n" +
                   "  \u2022 Generated email content\n" +
                   "  \u2022 IP address or device identifiers",
            Font = new Font("Segoe UI", 10F),
            AutoSize = true,
            MaximumSize = new Size(450, 0),
            Location = new Point(0, 0)
        };
        contentPanel.Controls.Add(lblContent);
        mainPanel.Controls.Add(contentPanel, 0, 1);

        // Privacy link
        var linkPanel = new Panel { Dock = DockStyle.Fill };
        var lblPrivacy = new LinkLabel
        {
            Text = "You can change this anytime in Generation Settings.",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            Location = new Point(0, 5)
        };
        linkPanel.Controls.Add(lblPrivacy);
        mainPanel.Controls.Add(linkPanel, 0, 2);

        // Buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var btnNo = ButtonHelper.CreateButton("No Thanks", 120, 35, ButtonStyle.Secondary);
        btnNo.Click += (s, e) =>
        {
            UserOptedIn = false;
            this.DialogResult = DialogResult.OK;
            this.Close();
        };

        var btnYes = ButtonHelper.CreateButton("Yes, I'll Help", 120, 35, ButtonStyle.Success);
        btnYes.Click += (s, e) =>
        {
            UserOptedIn = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        };

        buttonPanel.Controls.Add(btnNo);
        buttonPanel.Controls.Add(btnYes);
        mainPanel.Controls.Add(buttonPanel, 0, 3);

        this.Controls.Add(mainPanel);
        this.AcceptButton = btnYes;
    }
}
