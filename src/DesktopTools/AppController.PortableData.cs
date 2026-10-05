using System.IO;
using System.Windows;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools;

internal sealed partial class AppController
{
    private string? importArchive;

    private static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools");

    internal void RequestSettingsExport(Window owner)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Export settings"), Filter = L.T("DesktopTools data") + " (*.zip)|*.zip", DefaultExt = ".zip", AddExtension = true, OverwritePrompt = true,
            FileName = $"DesktopTools-data-{DateTime.Now:yyyyMMdd}.zip"
        };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName); // the save dialog already asked about replacing it
            PortableData.Export(DataRoot, dialog.FileName, typeof(AppController).Assembly.GetName().Version?.ToString(3) ?? "unknown");
            Report(L.T("Settings exported to ") + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { Report(L.T("Could not export settings: ") + ex.Message, NotificationKind.Warning); }
    }

    internal void RequestSettingsImport(Window owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = L.T("Import settings"), Filter = L.T("DesktopTools data") + " (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(owner) != true) return;
        PortableData.Summary summary;
        try { summary = PortableData.Validate(dialog.FileName); }
        catch (InvalidDataException ex) { Report(L.T("This file cannot be imported: ") + ex.Message, NotificationKind.Warning); return; }
        string contents = string.Join(", ", new[] { summary.HasSettings ? L.T("settings and shortcuts") : null, summary.Profiles > 0 ? L.F($"{summary.Profiles} profiles") : null,
            summary.HasAutomation ? L.T("workflows") : null, summary.Notes > 0 ? L.T("notes") : null }.Where(part => part != null));
        if (!ConfirmationDialog.Ask(owner, L.T("Import settings"),
                L.F($"Replace your current data ({contents}) with the data in this file? DesktopTools restarts to apply it. A backup of the current data stays in the DesktopTools data folder."),
                L.T("Import and restart"))) return;
        importArchive = dialog.FileName;
        Application.Current.Shutdown();
    }

    /// <summary>Runs after the app has stopped using its files. Returns true when the app should reopen.</summary>
    internal bool CompleteDataImport(string? dataRoot = null)
    {
        if (importArchive == null) return false;
        try { PortableData.Import(importArchive, dataRoot ?? DataRoot); return true; }
        catch (Exception ex)
        {
            MessageBox.Show(L.T("Could not import the settings. Your current data was not changed: ") + ex.Message, L.T("DesktopTools"), MessageBoxButton.OK, MessageBoxImage.Error);
            return true; // reopen with the data that is still in place
        }
    }
}
