using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DesktopTools;
using DesktopTools.Core;

internal static class DataResetChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    internal static async Task RunAsync()
    {
        string root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "reset-" + Guid.NewGuid().ToString("N")));
        Check(Path.GetDirectoryName(root) == Path.GetFullPath(Environment.CurrentDirectory), "Reset fixture escaped integration output.");
        string data = Path.Combine(root, "DesktopTools");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), "{}");
        string external = Path.Combine(root, "exported.png");
        File.WriteAllText(external, "original");
        try
        {
            using var controller = new AppController(true);
            typeof(AppController).GetField("resetDataOnExit", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, true);
            Check(controller.CompleteDataReset(root, () => { }), "Reset did not complete.");
            Check(!Directory.Exists(data), "DesktopTools data still exists after reset.");
            Check(File.Exists(external), "Reset removed a file outside DesktopTools data.");
            var store = new SettingsStore(data);
            store.Load();
            Check(store.IsNew, "Restart would not see a clean first-run settings state.");
            var start = App.CreateResetRelaunchStartInfo(@"C:\Apps\DesktopTools.exe", 1234);
            Check(start.FileName == @"C:\Apps\DesktopTools.exe" && start.ArgumentList.Single() == "--after-reset=1234",
                "Restart did not pass the parent PID to the same executable.");
            Check(App.ResetParentPid(["--after-reset=1234"]) == 1234
                && App.ResetParentPid(["--after-reset=invalid"]) == null,
                "Restart parent argument parsing changed.");
            var languageStart = App.CreateLanguageRelaunchStartInfo(@"C:\Apps\DesktopTools.exe", 4321);
            Check(languageStart.FileName == @"C:\Apps\DesktopTools.exe" && languageStart.ArgumentList.Single() == "--after-language=4321",
                "Language restart did not wait for the original application process.");
            Check(App.LanguageParentPid(["--after-language=4321"]) == 4321
                && App.LanguageParentPid(["--after-language=invalid"]) == null,
                "Language restart parent argument parsing changed.");
            string nextLanguage = controller.Settings.Language == "ru" ? "de" : "ru";
            Check(await controller.ChangeLanguageAsync(nextLanguage) && controller.RestartForLanguage && controller.Settings.Language == nextLanguage,
                "Changing the interface language did not save it and request an automatic restart.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
