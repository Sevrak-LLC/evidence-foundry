using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace EvidenceFoundry.Helpers;

public static class ApiKeyStore
{
    private const string SettingsFileName = "evidencefoundry.apikey";
    private const string LegacySettingsFileName = "reeldiscovery.settings";

    private static readonly Lazy<IDataProtector> Protector = new(() =>
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var keyDirectory = new DirectoryInfo(Path.Combine(appData, "EvidenceFoundry", "keys"));
        Directory.CreateDirectory(keyDirectory.FullName);
        TryHardenKeyDirectoryPermissions(keyDirectory.FullName);
        var services = new ServiceCollection();
        var builder = services.AddDataProtection()
            .SetApplicationName("EvidenceFoundry")
            .PersistKeysToFileSystem(keyDirectory);

        if (OperatingSystem.IsWindows())
        {
            builder.ProtectKeysWithDpapi();
        }

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("EvidenceFoundry.ApiKey.v1");
    });

    public static bool TryLoad(out string apiKey)
    {
        apiKey = string.Empty;

        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            path = GetLegacySettingsPath();
        }

        if (!File.Exists(path))
        {
            return false;
        }

        string stored;
        try
        {
            stored = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Failed to read API key storage at '{path}': {ex.Message}");
            return false;
        }
        if (string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        try
        {
            apiKey = Protector.Value.Unprotect(stored);
            return !string.IsNullOrWhiteSpace(apiKey);
        }
        catch (CryptographicException)
        {
            // Fall through to legacy handling.
        }

        if (TryDecodeLegacyBase64(stored, out var legacyKey))
        {
            apiKey = legacyKey;
            try
            {
                Save(apiKey);
            }
            catch
            {
                // If re-save fails, still return the legacy key for this session.
            }
            return true;
        }

        return false;
    }

    public static void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        var protectedValue = Protector.Value.Protect(apiKey);
        File.WriteAllText(GetSettingsPath(), protectedValue);
    }

    public static void Clear()
    {
        var path = GetSettingsPath();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var legacyPath = GetLegacySettingsPath();
        if (File.Exists(legacyPath))
        {
            File.Delete(legacyPath);
        }
    }

    public static bool HasSavedKey()
    {
        return File.Exists(GetSettingsPath()) || File.Exists(GetLegacySettingsPath());
    }

    private static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "EvidenceFoundry");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, SettingsFileName);
    }

    private static string GetLegacySettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "EvidenceFoundry");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, LegacySettingsFileName);
    }

    private static bool TryDecodeLegacyBase64(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(value);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            decoded = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryHardenKeyDirectoryPermissions(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser == null)
                {
                    return;
                }

                var security = new DirectorySecurity();
                security.SetOwner(currentUser);
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                security.AddAccessRule(new FileSystemAccessRule(
                    systemSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                var dirInfo = new DirectoryInfo(path);
                dirInfo.SetAccessControl(security);
            }
#if !WINDOWS
            else
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
#endif
        }
        catch
        {
            // Best-effort hardening only.
        }
    }
}
