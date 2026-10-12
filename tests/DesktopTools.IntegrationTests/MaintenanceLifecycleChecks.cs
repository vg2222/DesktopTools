using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using Microsoft.Win32;

internal static class MaintenanceLifecycleChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        foreach (var test in new (string Name, Func<Task> Run)[]
        {
            ("Automation polling follows armed automatic triggers", AutomationPollingAsync),
            ("Queued controller callbacks stop at disposal", QueuedCallbacksAsync),
            ("Shutdown preparation awaits the owned recorder session", ShutdownPreparationAsync)
        })
        {
            try { await test.Run(); Console.WriteLine("PASS maintenance: " + test.Name); }
            catch (Exception ex) { failures.Add(new InvalidOperationException(test.Name, ex)); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static Task AutomationPollingAsync()
    {
        string directory = Path.GetFullPath("maintenance-automation-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var service = new AutomationService(directory, () => false, _ => { }))
            {
                var timer = Field<DispatcherTimer>(service, "timer");
                Check(!timer.IsEnabled, "Empty automation polls once a second");
                var script = new AutomationScript { Armed = true };
                service.Save([script]);
                Check(!timer.IsEnabled, "An armed workflow without automatic triggers starts polling");
                script.IntervalMinutes = 1; service.Save([script]);
                Check(timer.IsEnabled, "Saving an armed interval did not start scheduling");
                script.Armed = false; service.Save([script]);
                Check(!timer.IsEnabled, "Disarming the last automatic workflow retained polling");
                script.Armed = true; script.IntervalMinutes = 0; script.WindowTrigger = "DesktopTools owned missing trigger"; service.Save([script]);
                Check(timer.IsEnabled, "Window appearance trigger lost its scheduler");
                script.WindowTrigger = ""; script.DailyTime = "12:34"; service.Save([script]);
                Check(timer.IsEnabled, "Daily schedule lost its scheduler");
                service.Save([]);
                Check(!timer.IsEnabled, "Removing the last scheduled workflow retained polling");
            }

            var store = new AutomationStore(directory); store.Load();
            var startupScript = new AutomationScript { Armed = true, RunOnStartup = true };
            store.Save([startupScript]);
            using (var startup = new AutomationService(directory, () => false, _ => { }))
            {
                var timer = Field<DispatcherTimer>(startup, "timer");
                Check(timer.IsEnabled, "Startup-only workflow lost its first available dispatch");
                typeof(AutomationService).GetMethod("Tick", PrivateInstance)!.Invoke(startup, null);
                Check(timer.IsEnabled && !startup.IsActive, "Busy startup dispatch was dropped or run");
                // Consume only the real trigger state; this fixture never executes injected input.
                var state = Field<Dictionary<Guid, (string Key, AutomationTriggerState State)>>(startup, "triggers")[startupScript.Id].State;
                Check(state.Take(startupScript, DateTime.Now, false, true), "Deferred startup trigger was not available for dispatch");
                typeof(AutomationService).GetMethod("Tick", PrivateInstance)!.Invoke(startup, null);
                Check(!timer.IsEnabled, "Consumed startup-only workflow retained polling");
                startup.Save([new AutomationScript { Armed = true, RunOnStartup = true }]);
                Check(!timer.IsEnabled, "Editing a startup-only workflow replayed its startup trigger");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    }

    private static async Task QueuedCallbacksAsync()
    {
        var controller = new AppController(true);
        string before = controller.Status;
        int statusEvents = 0; controller.StatusChanged += _ => statusEvents++;
        object accent = Application.Current.Resources["Accent"];
        typeof(AppController).GetMethod("DisplayChanged", PrivateInstance)!.Invoke(controller, [null, EventArgs.Empty]);
        typeof(AppController).GetMethod("PreferenceChanged", PrivateInstance)!.Invoke(controller,
            [null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color)]);
        controller.Dispose();
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(controller.Status == before && statusEvents == 0, "Queued display callback reported after disposal");
        Check(ReferenceEquals(accent, Application.Current.Resources["Accent"]), "Queued preference callback reapplied theme after disposal");
    }

    private static async Task ShutdownPreparationAsync()
    {
        using var controller = new AppController(true);
        var recorder = new ScreenRecorderWindow(controller, readMissingRuntime: () => Array.Empty<string>());
        var ownedSession = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionField = typeof(ScreenRecorderWindow).GetField("session", PrivateInstance)!;
        sessionField.SetValue(recorder, ownedSession.Task);
        Field<Dictionary<string, Window>>(controller, "utilityWindows")["Recorder"] = recorder;
        try
        {
            var prepare = typeof(AppController).GetMethod("PrepareForShutdownAsync", PrivateInstance);
            Check(prepare != null, "Restart paths have no shared recorder finalization boundary");
            var pending = (Task)prepare!.Invoke(controller, null)!;
            Check(!pending.IsCompleted && Field<bool>(recorder, "stopping"), "Shutdown preparation returned before the recording session finalized");
            ownedSession.SetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            sessionField.SetValue(recorder, null);
            await ((Task)prepare.Invoke(controller, null)!).WaitAsync(TimeSpan.FromSeconds(3));
            Check(!Field<bool>(controller, "resetDataOnExit") && Field<string?>(controller, "importArchive") == null,
                "Preparing shutdown requested a data operation");

            string originalLanguage = controller.Settings.Language;
            string nextLanguage = originalLanguage == "en" ? "de" : "en";
            ownedSession = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sessionField.SetValue(recorder, ownedSession.Task);
            var changingLanguage = controller.ChangeLanguageAsync(nextLanguage);
            Check(!changingLanguage.IsCompleted && controller.Settings.Language == originalLanguage && !controller.RestartForLanguage,
                "Language restart changed settings before recorder finalization");
            ownedSession.SetResult();
            Check(await changingLanguage.WaitAsync(TimeSpan.FromSeconds(3)) && controller.RestartForLanguage && controller.Settings.Language == nextLanguage,
                "Language restart did not resume after recorder finalization");
            sessionField.SetValue(recorder, null);
        }
        finally { ownedSession.TrySetResult(); sessionField.SetValue(recorder, null); recorder.Close(); }
    }
}
