using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ReelDiscovery.Services;

/// <summary>
/// Handles anonymous usage telemetry (opt-in only).
/// Sends minimal, non-identifying data to help improve the product.
/// </summary>
public static class TelemetryService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private const string WebhookUrl = "https://hooks.slack.com/services/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";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ReelDiscovery",
        "settings.json");

    /// <summary>
    /// Check if user has already made a telemetry choice.
    /// </summary>
    public static bool HasMadeTelemetryChoice()
    {
        return File.Exists(SettingsPath);
    }

    /// <summary>
    /// Get current telemetry opt-in status.
    /// </summary>
    public static bool IsTelemetryEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return false;

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<TelemetrySettings>(json);
            return settings?.TelemetryEnabled ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Save the user's telemetry preference.
    /// </summary>
    public static void SetTelemetryEnabled(bool enabled)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var settings = new TelemetrySettings { TelemetryEnabled = enabled };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently fail - telemetry preference is not critical
        }
    }

    /// <summary>
    /// Send a generation completed event (only if telemetry is enabled).
    /// </summary>
    public static async Task SendGenerationEventAsync(
        string topic,
        int emailCount,
        int threadCount,
        int attachmentCount,
        string model,
        TimeSpan duration)
    {
        if (!IsTelemetryEnabled())
            return;

        try
        {
            var message = $":email: *ReelDiscovery Generation*\n" +
                         $"Topic: `{SanitizeTopic(topic)}`\n" +
                         $"Emails: {emailCount} | Threads: {threadCount} | Attachments: {attachmentCount}\n" +
                         $"Model: {model} | Duration: {duration:mm\\:ss}\n" +
                         $"Version: {GetVersion()}";

            await SendSlackMessageAsync(message);
        }
        catch
        {
            // Silently fail - telemetry should never interrupt user experience
        }
    }

    /// <summary>
    /// Send an app launch event (only if telemetry is enabled).
    /// </summary>
    public static async Task SendLaunchEventAsync()
    {
        if (!IsTelemetryEnabled())
            return;

        try
        {
            var message = $":rocket: *ReelDiscovery Launched*\n" +
                         $"Version: {GetVersion()} | OS: {Environment.OSVersion.Platform}";

            await SendSlackMessageAsync(message);
        }
        catch
        {
            // Silently fail
        }
    }

    private static async Task SendSlackMessageAsync(string text)
    {
        var payload = new { text };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        await _httpClient.PostAsync(WebhookUrl, content);
    }

    private static string SanitizeTopic(string topic)
    {
        // Truncate and remove any potentially sensitive info
        if (string.IsNullOrEmpty(topic))
            return "(empty)";

        // Just take first 50 chars, remove newlines
        var sanitized = topic.Length > 50 ? topic[..50] + "..." : topic;
        return sanitized.Replace("\n", " ").Replace("\r", "");
    }

    private static string GetVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "1.0.0";
    }

    private class TelemetrySettings
    {
        public bool TelemetryEnabled { get; set; }
    }
}
