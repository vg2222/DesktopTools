using DesktopTools.Core;
using DesktopTools.UI;

namespace DesktopTools;

internal sealed partial class AppController
{
    private SetupWindow? setupWindow;
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
