namespace EvidenceFoundry.Helpers;

public static class ButtonHelper
{
    /// <summary>
    /// Applies consistent modern styling to a button
    /// </summary>
    public static void StyleButton(Button button, ButtonStyle style = ButtonStyle.Default)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

        switch (style)
        {
            case ButtonStyle.Primary:
                button.BackColor = Color.FromArgb(0, 120, 215);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(0, 100, 180);
                break;

            case ButtonStyle.Success:
                button.BackColor = Color.FromArgb(40, 167, 69);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(30, 140, 55);
                break;

            case ButtonStyle.Danger:
                button.BackColor = Color.FromArgb(220, 53, 69);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(180, 40, 55);
                break;

            case ButtonStyle.Secondary:
                button.BackColor = Color.FromArgb(108, 117, 125);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(90, 98, 105);
                break;

            case ButtonStyle.Default:
            default:
                button.BackColor = Color.FromArgb(240, 240, 240);
                button.ForeColor = Color.FromArgb(33, 33, 33);
                button.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
                break;
        }
    }

    /// <summary>
    /// Creates a styled button with the given text and minimum dimensions.
    /// The button will auto-size to fit the text if needed.
    /// </summary>
    public static Button CreateButton(string text, int minWidth, int height, ButtonStyle style = ButtonStyle.Default)
    {
        var button = new Button
        {
            Text = text,
            Height = height,
            AutoSize = false
        };
        StyleButton(button, style);

        // Measure text and ensure button is wide enough
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        var textSize = g.MeasureString(text, button.Font);
        var requiredWidth = (int)Math.Ceiling(textSize.Width) + 24; // 24px padding (12 each side)

        button.Width = Math.Max(minWidth, requiredWidth);

        return button;
    }
}

public enum ButtonStyle
{
    Default,
    Primary,
    Success,
    Secondary,
    Danger
}
