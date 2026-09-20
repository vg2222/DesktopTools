using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools.Extras;

internal static class PresentationHudLifecycleChecks
{
    public static void Run()
    {
        RunStopAllDismissal();
        var type = typeof(DesktopTools.Native.MonitorService).Assembly.GetType("DesktopTools.Extras.PresentationHudService");
        Check(type != null, "Presentation HUD service is missing.");
        using var service = (IDisposable)Activator.CreateInstance(type!, new object[] { (Action<string>)(_ => { }), (Func<bool>)(() => false) })!;
        void Call(string name) => type!.GetMethod(name)!.Invoke(service, null);
        bool State(string name) => (bool)type!.GetProperty(name)!.GetValue(service)!;
        Check(!State("IsClickIndicatorsEnabled") && !State("IsKeystrokesEnabled") && !State("IsTimerVisible"), "HUD starts active.");
        Call("ToggleClickIndicators"); Call("ToggleKeystrokes"); Call("ToggleTimer");
        Check(State("IsClickIndicatorsEnabled") && State("IsKeystrokesEnabled") && State("IsTimerVisible"), "Independent HUD toggles did not enable.");
        var timer = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "DesktopTools Timer");
        Check(timer.AllowsTransparency && timer.WindowStyle == WindowStyle.None && timer.Background == Brushes.Transparent, "Timer outer window is not transparent.");
        Check(timer.Content is Border b && b.CornerRadius.TopLeft > 0, "Timer must use a single rounded card.");
        timer.UpdateLayout();
        var timerCard = (Border)timer.Content;
        var timerStack = (StackPanel)timerCard.Child;
        var buttons = (StackPanel)timerStack.Children[1];
        double bottom = buttons.TransformToAncestor(timer).Transform(new Point(0, buttons.ActualHeight)).Y;
        Check(timer.ActualHeight - bottom <= 19, "Stopwatch has excessive blank space below its controls.");
        Call("StopAll");
        Check(!State("IsClickIndicatorsEnabled") && !State("IsKeystrokesEnabled") && !State("IsTimerVisible"),
            $"StopAll left a HUD active: clicks={State("IsClickIndicatorsEnabled")}, keys={State("IsKeystrokesEnabled")}, timer={State("IsTimerVisible")}.");
        Check(!Application.Current.Windows.Cast<Window>().Any(w => w.Title == "DesktopTools Timer"),
            "StopAll returned before the stopwatch window closed.");
        Call("ToggleCountdown"); Call("ToggleRuler"); Call("ToggleBlackout");
        Check(State("IsCountdownVisible") && State("IsRulerVisible") && State("IsBlackoutVisible"), "New presentation tools did not start.");
        var countdown = Application.Current.Windows.OfType<CountdownWindow>().Single();
        countdown.Minutes = 2; countdown.ToggleRunning(); Check(countdown.IsRunning, "Countdown did not start");
        countdown.ToggleRunning(); Check(!countdown.IsRunning, "Countdown did not pause");
        countdown.Reset(); Check(countdown.Remaining == TimeSpan.FromMinutes(2), "Countdown reset lost duration");
        var ruler = Application.Current.Windows.OfType<ScreenRulerWindow>().Single();
        ruler.Length = 300; ruler.IsVertical = true; Check(ruler.Length == 300 && ruler.IsVertical, "Ruler controls failed");
        Check(Application.Current.Windows.Cast<Window>().Where(w => w.Title == "DesktopTools Blackout").All(w => w.Background == Brushes.Black), "Blackout is not black");
        Call("StopAll");
        Check(!State("IsCountdownVisible") && !State("IsRulerVisible") && !State("IsBlackoutVisible"), "StopAll left new presentation tools active.");
        Check(!Application.Current.Windows.Cast<Window>().Any(w => w is CountdownWindow or ScreenRulerWindow || w.Title == "DesktopTools Blackout"),
            "StopAll returned before the presentation windows closed.");
        Check(!Application.Current.Windows.Cast<Window>().Any(w => w.Title.StartsWith("DesktopTools Timer")), "Timer survived StopAll.");
        service.Dispose(); Call("StopAll");
    }
    public static void RunStopAllDismissal()
    {
        var assembly = typeof(DesktopTools.Native.MonitorService).Assembly;
        var serviceType = assembly.GetType("DesktopTools.Extras.PresentationHudService", throwOnError: true)!;
        var dismissal = assembly.GetType("DesktopTools.Presentation.WindowDismissal", throwOnError: true)!;
        using var service = (IDisposable)Activator.CreateInstance(serviceType,
            new object[] { (Action<string>)(_ => { }), (Func<bool>)(() => false) })!;
        var timerField = serviceType.GetField("_timerWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var window = new Window { Title = "HUD StopAll dismissal check", Width = 100, Height = 100,
            WindowStyle = WindowStyle.None, AllowsTransparency = true };
        window.Closed += (_, _) => timerField.SetValue(service, null);
        dismissal.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { window, (Func<bool>)(() => true), null });
        try
        {
            timerField.SetValue(service, window);
            window.Show();
            serviceType.GetMethod("StopAll")!.Invoke(service, null);
            Check(timerField.GetValue(service) is null && !window.IsVisible,
                "StopAll returned while an animated HUD window was still open.");
        }
        finally { window.Close(); }
    }
    private static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
}
