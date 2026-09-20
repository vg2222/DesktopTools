using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools;

internal sealed partial class AppController
{
    private SharingWelcomeWindow? sharingWelcome;
    internal void OpenSharingWelcome()
    {
        if (disposed || Settings.SharingWelcomeSeen || sharingWelcome != null || setupWindow != null || main?.IsVisible != true) return;
        sharingWelcome = new SharingWelcomeWindow(() => UpdateSettings(s => s.SharingWelcomeSeen = true)) { Owner = main };
        sharingWelcome.Closed += (_, _) =>
        {
            sharingWelcome = null;
            if (!disposed && main?.IsVisible == true) NativeWindowService.ShowForeground(main);
        };
        main.ShowEmbeddedDialog(sharingWelcome);
    }
}
