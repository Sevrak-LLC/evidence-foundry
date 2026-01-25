using System.Reflection;
using ReelDiscovery.Helpers;

namespace ReelDiscovery.Forms;

public class DisclaimerDialog : Form
{
    private CheckBox _chkAccept = null!;
    private Button _btnAccept = null!;
    private Button _btnDecline = null!;

    public DisclaimerDialog()
    {
        InitializeUI();
        LoadIcon();
    }

    private void LoadIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "ReelDiscovery.Resources.QD_Logo_Color_236x256.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                this.Icon = new Icon(stream);
            }
        }
        catch
        {
            // Icon loading failed, use default
        }
    }

    private static void LoadLogoImage(PictureBox pictureBox)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "ReelDiscovery.Resources.98c804f5-9d4f-4d4b-a654-a9b33c92781f.png";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                pictureBox.Image = Image.FromStream(stream);
            }
        }
        catch
        {
            // Logo loading failed, leave empty
        }
    }

    private void InitializeUI()
    {
        this.Text = "ReelDiscovery - Terms of Use";
        this.Size = new Size(700, 750);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = true;
        this.BackColor = Color.White;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(25)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));  // Logo/Header (sized for banner image)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));   // Subheader
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Content
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));   // Checkbox
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));   // Buttons

        // Logo and title header
        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(45, 45, 48)
        };

        // Load and display the logo image - scaled to fit header
        var logoBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(45, 45, 48)
        };
        LoadLogoImage(logoBox);
        headerPanel.Controls.Add(logoBox);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // Subheader
        var lblSubheader = new Label
        {
            Text = "PLEASE READ THE FOLLOWING TERMS BEFORE PROCEEDING",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false
        };
        mainLayout.Controls.Add(lblSubheader, 0, 1);

        // Scrollable content panel with RichTextBox for better formatting
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(252, 252, 252),
            Padding = new Padding(2)
        };

        var rtbContent = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(252, 252, 252),
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(40, 40, 40),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        rtbContent.Rtf = GetDisclaimerRtf();
        contentPanel.Controls.Add(rtbContent);
        mainLayout.Controls.Add(contentPanel, 0, 2);

        // Checkbox panel
        var checkPanel = new Panel
        {
            Dock = DockStyle.Fill
        };

        _chkAccept = new CheckBox
        {
            Text = "I have read and agree to the terms above",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 60, 60),
            Location = new Point(0, 12)
        };
        _chkAccept.CheckedChanged += (s, e) => _btnAccept.Enabled = _chkAccept.Checked;
        checkPanel.Controls.Add(_chkAccept);
        mainLayout.Controls.Add(checkPanel, 0, 3);

        // Buttons panel
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Fill
        };

        _btnDecline = ButtonHelper.CreateButton("Decline", 120, 40, ButtonStyle.Default);
        _btnDecline.Font = new Font("Segoe UI", 10F);
        _btnDecline.Click += (s, e) =>
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        };

        _btnAccept = ButtonHelper.CreateButton("I Accept", 120, 40, ButtonStyle.Success);
        _btnAccept.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _btnAccept.Enabled = false;
        _btnAccept.Click += (s, e) =>
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        };

        // Position buttons - center them
        buttonPanel.Resize += (s, e) =>
        {
            var totalWidth = _btnAccept.Width + _btnDecline.Width + 20;
            var startX = (buttonPanel.Width - totalWidth) / 2;
            _btnAccept.Location = new Point(startX, 8);
            _btnDecline.Location = new Point(startX + _btnAccept.Width + 20, 8);
        };

        buttonPanel.Controls.Add(_btnAccept);
        buttonPanel.Controls.Add(_btnDecline);
        mainLayout.Controls.Add(buttonPanel, 0, 4);

        this.Controls.Add(mainLayout);
        this.AcceptButton = _btnAccept;
        this.CancelButton = _btnDecline;
    }

    private static string GetDisclaimerRtf()
    {
        // RTF formatted disclaimer text
        return @"{\rtf1\ansi\deff0
{\fonttbl{\f0\fswiss\fcharset0 Segoe UI;}}
{\colortbl;\red40\green40\blue40;\red0\green100\blue180;\red80\green80\blue80;}
\viewkind4\uc1\pard\f0\fs20

\cf2\b ABOUT THIS SOFTWARE\cf1\b0\par
\par
This software generates synthetic email content using artificial intelligence for e-discovery demonstrations. By clicking ""I Accept,"" you agree to the following terms:\par
\par

\cf2\b NO WARRANTY\cf1\b0\par
\par
This software is provided ""as is"" without warranty of any kind, express or implied. QuikData makes no representations or warranties regarding the accuracy, completeness, or suitability of the generated content for any purpose.\par
\par

\cf2\b LIMITATION OF LIABILITY\cf1\b0\par
\par
QuikData shall not be liable for any direct, indirect, incidental, special, consequential, or punitive damages arising from the use of this software or its generated content, including but not limited to damages for loss of profits, data, or other intangible losses.\par
\par

\cf2\b USER RESPONSIBILITY\cf1\b0\par
\par
You assume full responsibility for reviewing, validating, and appropriately using any content generated by this software. You acknowledge that AI-generated content may contain errors, inaccuracies, or inappropriate material.\par
\par

\cf2\b INTELLECTUAL PROPERTY\cf1\b0\par
\par
Any characters, names, trademarks, or copyrighted material referenced in generated content remain the sole property of their respective owners. The use of such references is for illustrative purposes only and does not imply endorsement, affiliation, or sponsorship by the rights holders.\par
\par

\cf2\b AI-GENERATED CONTENT\cf1\b0\par
\par
All emails, conversations, scenarios, and attachments created by this software are entirely fictional and generated by artificial intelligence. None of the content represents real communications, events, or persons.\par
\par

\cf2\b INTENDED USE\cf1\b0\par
\par
This tool is intended solely for e-discovery software demonstrations, training, and testing purposes. You agree not to use the generated content for any unlawful, deceptive, or harmful purpose.\par
\par

\cf2\b INDEMNIFICATION\cf1\b0\par
\par
You agree to indemnify and hold harmless QuikData and its affiliates from any claims, damages, or expenses arising from your use of this software or the generated content.\par
\par
}";
    }
}
