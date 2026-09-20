using DesktopTools.Localization;
using Microsoft.Win32;

namespace DesktopTools.Native;

public static class StartupService
{
    public static void SetEnabled(bool enabled)
    {
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(runPath, true)
                ?? throw new IOException(L.T("Windows did not open your startup settings."));
            if (!enabled) { run.DeleteValue("DesktopTools", false); return; }
            string executable = Environment.ProcessPath ?? throw new IOException(L.T("The application executable path is unavailable."));
            if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "DesktopTools", StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.T("Start DesktopTools.exe directly before enabling startup at login."));
            run.SetValue("DesktopTools", $"\"{executable}\" --startup", RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InvalidOperationException(L.F($"Could not update DesktopTools startup at login. {ex.Message}"), ex);
        }
    }
}
