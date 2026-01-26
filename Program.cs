using ReelDiscovery.Forms;
using ReelDiscovery.Services;

namespace ReelDiscovery;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Show disclaimer dialog first
        using var disclaimer = new DisclaimerDialog();
        if (disclaimer.ShowDialog() != DialogResult.OK)
        {
            return; // User declined, exit application
        }

        // Show telemetry opt-in dialog on first run
        if (!TelemetryService.HasMadeTelemetryChoice())
        {
            using var telemetryDialog = new TelemetryOptInDialog();
            telemetryDialog.ShowDialog();
            TelemetryService.SetTelemetryEnabled(telemetryDialog.UserOptedIn);
        }

        // Send launch event (only if user opted in)
        _ = TelemetryService.SendLaunchEventAsync();

        Application.Run(new WizardForm());
    }
}
