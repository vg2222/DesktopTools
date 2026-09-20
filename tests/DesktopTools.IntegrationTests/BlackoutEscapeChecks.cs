using System;
using System.Threading.Tasks;
using DesktopTools;

internal static class BlackoutEscapeChecks
{
    internal static Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => s.StopwatchEnabled = s.CountdownEnabled = s.ScreenRulerEnabled = s.ScreenBlackoutEnabled = true);
        try
        {
            controller.Hud.ToggleTimer(); controller.Hud.ToggleCountdown(); controller.Hud.ToggleRuler(); controller.Hud.ToggleBlackout();
            if (!controller.Hud.IsTimerVisible || !controller.Hud.IsCountdownVisible || !controller.Hud.IsRulerVisible || !controller.Hud.IsBlackoutVisible)
                throw new Exception("Required aids did not open before Escape");
            controller.CancelActiveTool();
            if (controller.Hud.IsBlackoutVisible) throw new Exception("Escape retained blackout");
            controller.CancelActiveTool();
            if (!controller.Hud.IsTimerVisible || !controller.Hud.IsCountdownVisible || !controller.Hud.IsRulerVisible)
                throw new Exception("Escape stopped independent aids");
        }
        finally { controller.Hud.StopAll(); }
        return Task.CompletedTask;
    }
}
