using DesktopTools.Core;
using DesktopTools.UI;

namespace DesktopTools;

internal sealed partial class AppController
{
    private SetupWindow? setupWindow;
    internal bool RestartForLanguage { get; private set; }
    internal async Task<bool> ChangeLanguageAsync(string language)
    {
        language = DesktopTools.Localization.L.Normalize(language);
        if (Settings.Language == language) return true;
        if (IsBusy) { Report(DesktopTools.Localization.L.T("Finish the current operation before changing the language.")); return false; }
        if (utilityWindows.TryGetValue("Recorder", out var recorder))
        {
            try { await ((DesktopTools.Extras.ScreenRecorderWindow)recorder).StopAsync(); }
            catch (Exception ex) { Report(ex.Message); return false; }
        }
        if (!UpdateSettings(settings => settings.Language = language)) return false;
        RestartForLanguage = true;
        if (!smoke) System.Windows.Application.Current.Shutdown();
        return true;
    }
    internal void OpenSetup(bool restart = false)
    {
        if (setupWindow != null) { if (main != null) DesktopTools.Native.NativeWindowService.ShowForeground(main); return; }
        bool updatedSetup = Settings.SetupExperienceVersion < 3;
        if (!restart && !updatedSetup && !Settings.Setup.ShouldResume) return;
        if (main?.IsVisible != true) OpenMain();
        if (!UpdateSettings(s =>
        {
            s.Setup.Status = SetupStatus.InProgress;
            if (restart || updatedSetup) s.Setup.Step = 0;
            s.SetupExperienceVersion = 3;
        })) return;
        setupWindow = new SetupWindow(this) { Owner = main };
        setupWindow.Closed += (_, _) =>
        {
            setupWindow = null;
            // Let the modal host restore its layer before opening the next notice.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed) return;
                if (main?.IsVisible == true) DesktopTools.Native.NativeWindowService.ShowForeground(main);
                OpenSharingWelcome();
            }));
        };
        main!.ShowEmbeddedDialog(setupWindow);
    }
}
