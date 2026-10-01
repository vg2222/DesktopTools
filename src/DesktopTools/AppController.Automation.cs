using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;
namespace DesktopTools;
internal sealed partial class AppController
{
    private AutomationService? automation;
    public void OpenAutomation()
    {
        nint target=NativeWindowService.GetForegroundWindowHandle();
        InitializeAutomation();
        if(AutomationInput.External(target))automation!.Target=target;
        OpenUtility("Automation",()=>new AutomationWindow(automation!));
    }
    private void InitializeAutomation()
    {
        automation??=new AutomationService(smoke?Path.Combine(Environment.CurrentDirectory,"artifacts","smoke-automation"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopTools"),
            ()=>!IsBusy && main?.HasModal!=true && State is not (OverlayState.Draw or OverlayState.Interact),Report);
    }
}
