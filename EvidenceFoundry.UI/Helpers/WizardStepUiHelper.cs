using System.Drawing;
using System.Windows.Forms;

namespace EvidenceFoundry.Helpers;

public static class WizardStepUiHelper
{
    public static IProgress<string> CreateStatusProgress(Label statusLabel)
    {
        ArgumentNullException.ThrowIfNull(statusLabel);

        return new Progress<string>(status =>
        {
            statusLabel.Text = status;
            statusLabel.ForeColor = Color.Blue;
        });
    }

    public static string BuildErrorMessage(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex.InnerException != null
            ? $"{ex.Message} ({ex.InnerException.Message})"
            : ex.Message;
    }

    public static async Task RunWithLoadingStateAsync(
        Control owner,
        Button actionButton,
        Label statusLabel,
        LoadingOverlay overlay,
        Action<bool> setIsLoading,
        Action stateChanged,
        Func<IProgress<string>, Task> action,
        Action? updateEmptyState = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(actionButton);
        ArgumentNullException.ThrowIfNull(statusLabel);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(setIsLoading);
        ArgumentNullException.ThrowIfNull(stateChanged);
        ArgumentNullException.ThrowIfNull(action);

        setIsLoading(true);
        actionButton.Enabled = false;
        statusLabel.Text = string.Empty;
        overlay.Show(owner);
        updateEmptyState?.Invoke();
        stateChanged();

        try
        {
            var progress = CreateStatusProgress(statusLabel);
            await action(progress);
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Error: {BuildErrorMessage(ex)}";
            statusLabel.ForeColor = Color.Red;
        }
        finally
        {
            setIsLoading(false);
            actionButton.Enabled = true;
            overlay.Hide();
            updateEmptyState?.Invoke();
            stateChanged();
        }
    }
}
