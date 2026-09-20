using System.IO;
using System.Windows;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools;

internal sealed partial class AppController
{
    private bool resetDataOnExit;

    internal void RequestDataReset(Window owner)
    {
        if (!ConfirmationDialog.Ask(owner, L.T("Reset all app data"),
                L.T("Delete all local DesktopTools settings, profiles and notes and restart the app? This cannot be undone. Files you exported elsewhere will remain."),
                L.T("Delete app data"))) return;
        resetDataOnExit = true;
        Application.Current.Shutdown();
    }

    internal bool CompleteDataReset(string? localDataRoot = null, Action? disableStartup = null)
    {
        if (!resetDataOnExit) return false;
        try
        {
            (disableStartup ?? (() => StartupService.SetEnabled(false)))();
            string local = Path.GetFullPath(localDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            string directory = Path.GetFullPath(Path.Combine(local, "DesktopTools"));
            if (!string.Equals(Path.GetDirectoryName(directory), local, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(directory) != "DesktopTools")
                throw new IOException("Unexpected app-data location.");
            if (Directory.Exists(directory))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("App-data folder is a link and was not removed.");
                Directory.Delete(directory, true);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.T("Could not remove all app data: ") + ex.Message,
                L.T("DesktopTools"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}