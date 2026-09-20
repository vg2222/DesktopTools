using System.Reflection;
using System.Windows;

internal static class InstallerMotionChecks
{
    internal static void Run(Assembly installer)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += async (_, _) =>
            {
                try
                {
                    var dismissal = installer.GetType("DesktopTools.Presentation.WindowDismissal", true)!;
                    var attach = dismissal.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic)!;
                    var active = dismissal.GetMethod("IsDismissing", BindingFlags.Static | BindingFlags.NonPublic)!;
                    bool Running(Window window) => (bool)active.Invoke(null, [window])!;
                    async Task Finish(Window window)
                    {
                        for (int i = 0; i < 100 && Running(window); i++) await Task.Delay(20);
                        if (window.IsVisible) throw new Exception("Installer window did not finish closing");
                    }
                    var type = installer.GetType("DesktopTools.Installer.InstallerWindow", true)!;
                    // Uninstall presentation skips release discovery. No action is executed.
                    var setup = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                        null, [true], null)!;
                    attach.Invoke(null, [setup, new Func<bool>(() => true), null]);
                    setup.Show(); await Task.Delay(80);
                    var busy = type.GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    busy.SetValue(setup, true);
                    setup.Close();
                    if (!setup.IsVisible || Running(setup)) throw new Exception("Installer busy guard was bypassed");
                    busy.SetValue(setup, false);
                    setup.Close();
                    if (!setup.IsVisible || !Running(setup)) throw new Exception("Installer skipped closing animation");
                    await Finish(setup);

                    var progress = (Window)Activator.CreateInstance(installer.GetType("DesktopTools.Installer.BackgroundUpdateWindow", true)!, true)!;
                    attach.Invoke(null, [progress, new Func<bool>(() => true), null]);
                    progress.Show(); await Task.Delay(80); progress.Close();
                    if (!progress.IsVisible || !Running(progress)) throw new Exception("Background update notice skipped closing animation");
                    await Finish(progress);
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            };
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception("Installer motion check failed", failure);
        Console.WriteLine("PASS installer closing animation, busy guard and background update notice");
    }
}
