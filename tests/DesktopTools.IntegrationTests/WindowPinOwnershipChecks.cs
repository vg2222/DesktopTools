using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopTools.Native;
using Forms = System.Windows.Forms;

internal static class WindowPinOwnershipChecks
{
    private sealed record Handles(long Owner, long Owned, long Bystander);

    // Runs in a separate process because WindowPinService rejects app-owned windows.
    internal static int RunTarget(string statePath)
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        using var owner = new Forms.Form { Text = "DesktopTools pin owner fixture", Width = 400, Height = 240 };
        using var owned = new Forms.Form { Text = "DesktopTools pin owned fixture", Width = 300, Height = 180 };
        using var bystander = new Forms.Form { Text = "DesktopTools pin bystander fixture", Width = 300, Height = 180 };
        owner.Shown += (_, _) =>
        {
            owned.Show(owner);
            owned.TopMost = true;
            bystander.Show();
            File.WriteAllText(statePath, JsonSerializer.Serialize(new Handles(
                owner.Handle.ToInt64(), owned.Handle.ToInt64(), bystander.Handle.ToInt64())));
        };
        Forms.Application.Run(owner);
        return 0;
    }

    internal static async Task RunAsync()
    {
        string statePath = Path.Combine(Path.GetTempPath(), "desktoptools-pin-owned-" + Guid.NewGuid().ToString("N") + ".json");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No test executable path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--pin-owned-target");
        start.ArgumentList.Add(statePath);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not launch pin ownership fixture.");
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(statePath) && watch.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (child.HasExited) throw new InvalidOperationException("Pin ownership fixture exited early.");
                await Task.Delay(50);
            }
            Check(File.Exists(statePath), "Pin ownership fixture did not create its windows.");
            Handles handles = JsonSerializer.Deserialize<Handles>(File.ReadAllText(statePath))!;
            nint owner = (nint)handles.Owner, owned = (nint)handles.Owned, bystander = (nint)handles.Bystander;
            Check(!Topmost(owner) && Topmost(owned) && !Topmost(bystander), "Fixture must start with only its owned dialog topmost.");
            using var pins = new WindowPinService();
            Check(pins.ToggleWindow(owner), "Owner did not pin.");
            Check(Topmost(owner) && Topmost(owned) && !Topmost(bystander), "Pin changed an unrelated bystander.");
            Check(!pins.ToggleWindow(owner), "Owner did not unpin.");
            Check(!Topmost(owner) && Topmost(owned) && !Topmost(bystander), "Unpin lost an owned window's original topmost state.");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.CloseMainWindow();
                Task finished = child.WaitForExitAsync();
                if (await Task.WhenAny(finished, Task.Delay(3000)) != finished)
                { child.Kill(); await child.WaitForExitAsync(); }
            }
            File.Delete(statePath);
        }
    }

    private static bool Topmost(nint handle) => (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
}
