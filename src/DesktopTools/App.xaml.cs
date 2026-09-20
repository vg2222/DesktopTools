using DesktopTools.Localization;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using DesktopTools.UI;

namespace DesktopTools;

public partial class App : Application
{
    private const string AfterResetPrefix = "--after-reset=";
    private Mutex? mutex;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? wait;
    internal AppController? Controller { get; private set; }

    internal static int? ResetParentPid(string[] args)
    {
        string? argument = args.FirstOrDefault(value => value.StartsWith(AfterResetPrefix, StringComparison.Ordinal));
        return argument != null && int.TryParse(argument[AfterResetPrefix.Length..], out int pid) && pid > 0 ? pid : null;
    }

    internal static ProcessStartInfo CreateResetRelaunchStartInfo(string executable, int parentPid)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true };
        start.ArgumentList.Add(AfterResetPrefix + parentPid);
        return start;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (ResetParentPid(e.Args) is int parentPid)
        {
            try
            {
                using var parent = Process.GetProcessById(parentPid);
                if (!parent.WaitForExit(30_000))
                {
                    MessageBox.Show(L.T("DesktopTools could not finish restarting after the data reset. Please open it again."),
                        "DesktopTools", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1); return;
                }
            }
            catch (ArgumentException) { } // The prior process has already exited.
        }

        bool smoke = e.Args.Contains("--smoke");
        if (!smoke)
        {
            mutex = new Mutex(true, "Local\\DesktopTools.App", out bool first);
            if (!first)
            {
                try { using var existing = EventWaitHandle.OpenExisting("Local\\DesktopTools.Activate"); existing.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                Shutdown(); return;
            }
            activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopTools.Activate");
            wait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Dispatcher.BeginInvoke(() => Controller?.OpenMain()), null, -1, false);
        }
        try
        {
            Controller = new AppController(smoke);
            if (e.Args.Contains("--update-error") && !smoke) Controller.ShowBackgroundUpdateError();
            else
            {
                if (!e.Args.Contains("--startup")) Controller.OpenMain();
                if (!smoke) Controller.OpenSetup(restart: e.Args.Contains("--setup"));
            }
            if (!smoke) Controller.StartUpdateChecks();
            if (smoke) _ = Controller.RunSmokeAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, L.T("DesktopTools could not start"), MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Controller?.Dispose();
        bool restart = Controller?.CompleteDataReset() == true;
        wait?.Unregister(null);
        activation?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
        if (!restart) return;
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("DesktopTools executable path is unavailable.");
            var start = CreateResetRelaunchStartInfo(executable, Environment.ProcessId);
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Insert(0, Assembly.GetEntryAssembly()?.Location
                    ?? throw new InvalidOperationException("DesktopTools entry assembly path is unavailable."));
            _ = Process.Start(start) ?? throw new InvalidOperationException("Windows could not reopen DesktopTools.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.T("DesktopTools data was reset, but the app could not reopen automatically. Open DesktopTools from the Start menu.") + "\n\n" + ex.Message,
                "DesktopTools", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}