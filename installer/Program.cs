using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;

namespace DesktopTools.Installer;

internal static class Program
{
    internal enum SetupMode { Install, Maintenance, Update, OlderSetup }
    internal enum SetupCommand { None, Update, BackgroundUpdate }
    internal readonly record struct SetupArguments(SetupCommand Command, int ProcessId, string? ReadyEventName);
    private const int MoveFileDelayUntilReboot = 0x4;
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFile, string? newFile, int flags);

    private const string Product = "DesktopTools";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopTools";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "DesktopTools";
    private static readonly string ProgramsRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));
    private static readonly string DefaultInstallDir = Path.Combine(ProgramsRoot, Product);
    private static readonly string DataDir = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product));
    internal static string InstallDir => GetRegisteredInstallDir() ?? DefaultInstallDir;
    private static readonly string StartShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DesktopTools.lnk");
    private static readonly string DesktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DesktopTools.lnk");
    internal static readonly string Version = NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version) ?? "1.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
#if SETUP
            SetupArguments setupArguments = ParseSetupArguments(args);
            if (setupArguments.Command == SetupCommand.BackgroundUpdate)
            {
                RunBackgroundUpdate(setupArguments);
                return 0;
            }
            bool automaticUpdate = setupArguments.Command == SetupCommand.Update;
            if (automaticUpdate) WaitForRegisteredApplication(setupArguments, requireNewerVersion: true);
            return new Application().Run(new InstallerWindow(false, automaticUpdate));
#else
            if (args.Length == 0 || args[0] != "--uninstall-worker")
            {
                string installed = GetRegisteredInstallDir() ?? throw new InvalidOperationException("DesktopTools is not installed for this user.");
                CheckInstalledTarget(installed);
                if (!SamePath(Path.GetDirectoryName(Environment.ProcessPath!)!, installed))
                    throw new InvalidOperationException("Run the uninstaller from the registered application folder.");
                string temporary = Path.Combine(Path.GetTempPath(), "DesktopTools-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Environment.ProcessPath!, temporary);
                Process.Start(new ProcessStartInfo(temporary) { ArgumentList = { "--uninstall-worker", installed }, UseShellExecute = false, CreateNoWindow = true });
                return 0;
            }
            if (args.Length != 2 || !SamePath(args[1], GetRegisteredInstallDir()))
                throw new InvalidOperationException("The registered installation location changed. Restart uninstall from the application folder.");
            CheckInstalledTarget(args[1]);
            // Windows keeps the running executable locked. Remove the temporary GUI copy on restart.
            MoveFileEx(Environment.ProcessPath!, null, MoveFileDelayUntilReboot);
            return new Application().Run(new InstallerWindow(true));
#endif
        }
        catch (Exception ex)
        {
#if SETUP
            if (args.Length >= 4 && args.Contains("--ready-event", StringComparer.Ordinal) &&
                args[0] is "--background-update" or "--update")
            {
                Console.Error.WriteLine(ex.GetBaseException().Message);
                return 1;
            }
#endif
            MessageBox.Show(ex.Message, "DesktopTools Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    internal static string? NormalizeVersion(string? value) =>
        System.Version.TryParse(value, out var parsed) ? NormalizeVersion(parsed) : null;

    private static string? NormalizeVersion(System.Version? value)
    {
        if (value is null || value.Major < 0 || value.Minor < 0) return null;
        return new System.Version(value.Major, value.Minor, Math.Max(0, value.Build)).ToString(3);
    }

    internal static bool IsValidReadyEventName(string? value)
    {
        const string prefix = @"Local\DesktopTools.Update.";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 32) return false;
        return Guid.TryParseExact(value[prefix.Length..], "N", out _);
    }

    internal static SetupArguments ParseSetupArguments(string[] args)
    {
        if (args.Length == 0) return new SetupArguments(SetupCommand.None, 0, null);
        if (args.Length is not (3 or 5) || args[1] != "--wait-for-exit" ||
            !int.TryParse(args[2], out int processId) || processId <= 0)
            throw new InvalidOperationException("Setup command-line arguments are invalid.");
        SetupCommand command = args[0] switch
        {
            "--update" => SetupCommand.Update,
            "--background-update" => SetupCommand.BackgroundUpdate,
            _ => throw new InvalidOperationException("Setup command-line arguments are invalid.")
        };
        string? readyEventName = null;
        if (args.Length == 5)
        {
            if (args[3] != "--ready-event" || !IsValidReadyEventName(args[4]))
                throw new InvalidOperationException("The update ready-event argument is invalid.");
            readyEventName = args[4];
        }
        return new SetupArguments(command, processId, readyEventName);
    }

    private static string? GetRegisteredInstallDir()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        string? location = key?.GetValue("InstallLocation") as string;
        return string.IsNullOrWhiteSpace(location) ? null : ValidatePath(location);
    }

    internal static string? ReadInstalledVersionFromFolder(string folder)
    {
        try
        {
            string value = File.ReadAllText(Path.Combine(folder, "VERSION.txt")).Trim();
            return NormalizeVersion(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static string? InstalledVersion
    {
        get
        {
            string? folder = GetRegisteredInstallDir();
            if (folder is null) return null;
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            string? registryVersion = key?.GetValue("DisplayVersion") as string;
            return NormalizeVersion(registryVersion) ?? ReadInstalledVersionFromFolder(folder);
        }
    }

    internal static SetupMode DecideSetupMode(string? installedVersion, string setupVersion)
    {
        if (installedVersion is null) return SetupMode.Install;
        if (!System.Version.TryParse(installedVersion, out var installed) || !System.Version.TryParse(setupVersion, out var setup))
            return SetupMode.Maintenance;
        int comparison = setup.CompareTo(installed);
        return comparison > 0 ? SetupMode.Update : comparison < 0 ? SetupMode.OlderSetup : SetupMode.Maintenance;
    }

    internal static bool IsRegisteredApplicationProcess(string? executablePath, string installDir) =>
        executablePath is not null && SamePath(executablePath, Path.Combine(installDir, "DesktopTools.exe"));

    private static void WaitForRegisteredApplication(SetupArguments arguments, bool requireNewerVersion, Action? onReady = null)
    {
        string installed = GetRegisteredInstallDir() ?? throw new InvalidOperationException("DesktopTools is not installed for this user.");
        Process process;
        try { process = Process.GetProcessById(arguments.ProcessId); }
        catch (ArgumentException) { throw new InvalidOperationException("The DesktopTools update handoff has expired. Start the update again from the app."); }
        using (process)
        {
            string? executable;
            try { executable = process.MainModule?.FileName; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { executable = null; }
            if (!IsRegisteredApplicationProcess(executable, installed))
                throw new InvalidOperationException("Update handoff was not started by the registered DesktopTools application.");
            if (requireNewerVersion)
            {
                string? installedVersion = InstalledVersion;
                if (installedVersion is null || DecideSetupMode(installedVersion, Version) != SetupMode.Update)
                    throw new InvalidOperationException("This update is not newer than the installed DesktopTools version.");
            }
            if (arguments.ReadyEventName is not null)
            {
                using EventWaitHandle ready = EventWaitHandle.OpenExisting(arguments.ReadyEventName);
                ready.Set();
            }
            onReady?.Invoke();
            if (!process.WaitForExit(60_000))
                throw new UpdateSourceStillRunningException("DesktopTools did not exit in time. Close it and start the update again.");
        }
    }

    private static void RunBackgroundUpdate(SetupArguments arguments)
    {
        string target = GetRegisteredInstallDir() ?? throw new InvalidOperationException("DesktopTools is not installed for this user.");
        string errorFile = Path.Combine(DataDir, "Updates", "update-error.txt");
        bool handoffReady = false;
        try
        {
            WaitForRegisteredApplication(arguments, requireNewerVersion: true, () => handoffReady = true);
            if (File.Exists(errorFile)) File.Delete(errorFile);
            RunBackgroundInstall(target);
            LaunchInstalledApplication(target, "--updated");
        }
        catch (Exception ex)
        {
            if (!handoffReady) throw;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(errorFile)!);
                string text = ex.GetBaseException().Message;
                File.WriteAllText(errorFile, text[..Math.Min(text.Length, 4096)]);
            }
            catch { /* The relaunch or fallback dialog still reports the original error. */ }
            try
            {
                if (ex is not UpdateSourceStillRunningException) LaunchInstalledApplication(target, "--update-error");
            }
            catch
            {
                MessageBox.Show("DesktopTools could not be updated or restarted. " + ex.GetBaseException().Message,
                    "DesktopTools Update", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private sealed class UpdateSourceStillRunningException(string message) : InvalidOperationException(message);

    private static void RunBackgroundInstall(string target)
    {
        Exception? failure = null;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new BackgroundUpdateWindow();
        window.Closed += (_, _) => application.Shutdown();
        window.Loaded += async (_, _) =>
        {
            try { await Task.Run(() => Install(target, window.Progress)); }
            catch (Exception ex) { failure = ex; }
            finally
            {
                window.Close();
            }
        };
        application.Run(window);
        if (failure is not null) throw failure;
    }

    private static void LaunchInstalledApplication(string target, string argument)
    {
        string executable = Path.Combine(target, "DesktopTools.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("DesktopTools.exe is missing after the update attempt.", executable);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = target, UseShellExecute = true };
        start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start DesktopTools.");
    }

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null && string.Equals(ValidatePath(left), ValidatePath(right), StringComparison.OrdinalIgnoreCase);

    private readonly record struct RegistryValueSnapshot(bool Exists, object? Value, RegistryValueKind Kind);

    private static string StartupCommand(string installDir) =>
        $"\"{Path.Combine(Path.GetFullPath(installDir), "DesktopTools.exe")}\" --startup";

    internal static bool StartupCommandMatches(string? command, string installDir) =>
        command is not null && string.Equals(command, StartupCommand(installDir), StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldRemoveStartupValue(bool exists, object? value, string installDir) =>
        exists && value is string command && StartupCommandMatches(command, installDir);

    private static RegistryValueSnapshot ReadStartupValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        object? value = key?.GetValue(RunValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is null ? default : new RegistryValueSnapshot(true, value, key!.GetValueKind(RunValue));
    }

    private static void WriteStartupValue(string command, RegistryValueKind kind)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("Cannot update DesktopTools startup at login.");
        key.SetValue(RunValue, command, kind);
    }

    internal static bool RelocateStartupValue(bool exists, object? value, RegistryValueKind kind,
        string previousInstallDir, string targetInstallDir, Action<object, RegistryValueKind> write)
    {
        if (!exists || value is not string command || SamePath(previousInstallDir, targetInstallDir) ||
            !StartupCommandMatches(command, previousInstallDir)) return false;
        write(StartupCommand(targetInstallDir), kind);
        return true;
    }

    internal static void RestoreStartupValue(bool exists, object? value, RegistryValueKind kind,
        Action<object, RegistryValueKind> write, Action delete)
    {
        if (exists) write(value!, kind);
        else delete();
    }

    private static void RestoreStartupRegistry(RegistryValueSnapshot snapshot)
    {
        RestoreStartupValue(snapshot.Exists, snapshot.Value, snapshot.Kind,
            (value, kind) =>
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true)
                    ?? throw new InvalidOperationException("Cannot restore DesktopTools startup at login.");
                key.SetValue(RunValue, value, kind);
            }, DeleteStartupValue);
    }

    private static void DeleteStartupValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(RunValue, false);
    }

    internal static void RunCleanupActions(IReadOnlyList<(string Name, Action Action)> actions)
    {
        var failures = new List<string>();
        foreach (var (name, action) in actions)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        if (failures.Count != 0)
            throw new InvalidOperationException("DesktopTools files were removed, but some cleanup could not be completed: " + string.Join("; ", failures));
    }

    internal static void RunRollbackActions(Exception installError, Func<string?> retainedBackupPath,
        IReadOnlyList<(string Name, Action Action)> actions)
    {
        var failures = new List<Exception> { installError };
        foreach (var (name, action) in actions)
        {
            try { action(); }
            catch (Exception ex) { failures.Add(new InvalidOperationException(name + ": " + ex.Message, ex)); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(installError).Throw();
        string? retained = retainedBackupPath();
        string message = "Setup failed and could not fully restore the previous installation.";
        if (retained is not null) message += " Previous application files were retained at: " + retained;
        throw new AggregateException(message, failures);
    }

    internal static bool ShouldReplaceShortcut(bool exists, string? currentTarget, string? ownedTarget)
    {
        if (!exists) return true;
        return currentTarget is not null && ownedTarget is not null &&
            string.Equals(Path.GetFullPath(currentTarget), Path.GetFullPath(ownedTarget), StringComparison.OrdinalIgnoreCase);
    }

    internal static void RestoreFileSnapshot(string path, bool existed, byte[]? contents)
    {
        if (existed)
        {
            if (contents is null) throw new InvalidOperationException("The previous shortcut snapshot is missing.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, contents);
        }
        else if (File.Exists(path)) File.Delete(path);
    }

    internal static void DeleteManagedData(string dataDirectory)
    {
        string full = Path.GetFullPath(dataDirectory);
        if (Directory.Exists(full)) DeleteDirectoryTreeNoFollow(full);
    }

    internal static void DeleteDirectoryTreeNoFollow(string root)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(root));
        if (!directory.Exists) return;
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refusing to recursively delete a linked directory.");
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo child && (child.Attributes & FileAttributes.ReparsePoint) == 0)
                DeleteDirectoryTreeNoFollow(child.FullName);
            else if (entry is DirectoryInfo linkedDirectory)
                linkedDirectory.Delete(false);
            else
                entry.Delete();
        }
        directory.Delete(false);
    }

    internal static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Choose an installation folder.");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string? parent = Path.GetDirectoryName(full);
        if (parent is null || Path.GetPathRoot(full) == full ||
            SameOrWithin(full, DataDir) || SameOrWithin(DataDir, full) ||
            SameOrWithin(full, Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
            throw new InvalidOperationException("Choose a dedicated application folder outside Windows and your DesktopTools data folder.");
        for (string? part = full; part is not null; part = Path.GetDirectoryName(part))
        {
            if (Directory.Exists(part) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Installation folders cannot contain directory links or junctions.");
        }
        return full;
    }

    private static bool SameOrWithin(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void CheckTarget(string target)
    {
        if (!Environment.Is64BitOperatingSystem) throw new InvalidOperationException("DesktopTools requires 64-bit Windows.");
        ValidatePath(target);
        if (Process.GetProcessesByName(Product).Length != 0)
            throw new InvalidOperationException("DesktopTools is running. Choose Quit from its tray menu, then try again.");
    }

    private static void CheckInstalledTarget(string target)
    {
        ValidatePath(target);
        // Keep the uninstaller as the retry entry point if an earlier removal stopped
        // after deleting the application or version marker.
        if (!File.Exists(Path.Combine(target, "Uninstall.exe")))
            throw new InvalidOperationException("The registered folder no longer contains the DesktopTools uninstaller. No files were removed.");
    }

    private static void DeleteInstalledTarget(string root)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(root));
        if (!directory.Exists) return;
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refusing to recursively delete a linked directory.");

        // A locked file can interrupt removal after other files are gone. Leave the
        // registered uninstaller in place until every other entry has been removed.
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is FileInfo && entry.Name.Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry is DirectoryInfo child && (child.Attributes & FileAttributes.ReparsePoint) == 0)
                DeleteDirectoryTreeNoFollow(child.FullName);
            else if (entry is DirectoryInfo linkedDirectory)
                linkedDirectory.Delete(false);
            else
                entry.Delete();
        }
        if (directory.EnumerateFileSystemInfos().Any(entry =>
                !entry.Name.Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Installation files changed during removal. Try again.");

        File.Delete(Path.Combine(directory.FullName, "Uninstall.exe"));
        try { directory.Delete(false); }
        catch (IOException) when (Directory.Exists(directory.FullName) &&
                                  !Directory.EnumerateFileSystemEntries(directory.FullName).Any())
        {
            // An empty folder held open by another process can be reused on reinstall.
        }
    }

    internal static void Install(string requestedPath, IProgress<(int, string)> progress) =>
        RunExclusiveTransaction(() => InstallCore(requestedPath, progress));

    private static void InstallCore(string requestedPath, IProgress<(int, string)> progress)
    {
        string target = ValidatePath(requestedPath);
        CheckTarget(target);
        string? previous = GetRegisteredInstallDir();
        string? previousVersion = InstalledVersion;
        if (DecideSetupMode(previousVersion, Version) == SetupMode.OlderSetup)
            throw new InvalidOperationException("This setup cannot replace a newer DesktopTools installation. Download the latest setup from GitHub.");
        // A registered installation may be incomplete; replacing it is the repair path.
        RegistryValueSnapshot startupBefore = ReadStartupValue();
        if (previous is not null && !SamePath(previous, target) &&
            (SameOrWithin(target, previous) || SameOrWithin(previous, target)))
            throw new InvalidOperationException("Choose a folder separate from the current DesktopTools installation.");
        if (Directory.Exists(target) && (previous is null || !SamePath(previous, target)) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new InvalidOperationException("Choose an empty folder or the current DesktopTools installation folder.");
        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        string workspace = Path.Combine(parent, ".DesktopTools-setup-" + Guid.NewGuid().ToString("N"));
        string extracted = Path.Combine(workspace, "extracted");
        string staged = Path.Combine(workspace, "staged");
        string? backup = null;
        bool installedNew = false;
        bool startupChanged = false;
        bool startShortcutChanged = false;
        bool desktopShortcutChanged = false;
        bool startShortcutExisted = File.Exists(StartShortcut);
        byte[]? startShortcutBefore = startShortcutExisted ? File.ReadAllBytes(StartShortcut) : null;
        bool desktopShortcutExisted = File.Exists(DesktopShortcut);
        byte[]? desktopShortcutBefore = desktopShortcutExisted ? File.ReadAllBytes(DesktopShortcut) : null;
        try
        {
            Directory.CreateDirectory(extracted);
            progress.Report((15, "Unpacking application files…"));
            using (Stream source = OpenResource("DesktopTools.Payload.zip"))
                ZipFile.ExtractToDirectory(source, extracted);
            string payload = Path.Combine(extracted, "DesktopTools-win-x64");
            if (!File.Exists(Path.Combine(payload, "DesktopTools.exe"))) payload = extracted;
            if (!File.Exists(Path.Combine(payload, "DesktopTools.exe"))) throw new InvalidDataException("Setup payload is missing DesktopTools.exe.");
            Directory.Move(payload, staged);
            CopyResource("DesktopTools.Uninstall.exe", Path.Combine(staged, "Uninstall.exe"));
            File.WriteAllText(Path.Combine(staged, "VERSION.txt"), Version);
            progress.Report((65, "Updating application files…"));
            if (Directory.Exists(target) && (previous is null || !SamePath(previous, target))) DeleteDirectoryTreeNoFollow(target);
            if (previous is not null && Directory.Exists(previous))
            {
                backup = Path.Combine(Path.GetDirectoryName(previous)!, ".DesktopTools-backup-" + Guid.NewGuid().ToString("N"));
                Directory.Move(previous, backup);
            }
            try
            {
                Directory.Move(staged, target);
                installedNew = true;
                progress.Report((82, "Creating shortcuts and uninstall entry…"));
                string? previouslyOwnedExecutable = previous is null ? null : Path.Combine(previous, "DesktopTools.exe");
                CreateShortcutIfOwned(StartShortcut, target, previouslyOwnedExecutable, () => startShortcutChanged = true);
                CreateShortcutIfOwned(DesktopShortcut, target, previouslyOwnedExecutable, () => desktopShortcutChanged = true);
                RegisterUninstall(target);
                if (previous is not null)
                    RelocateStartupValue(startupBefore.Exists, startupBefore.Value, startupBefore.Kind,
                        previous, target, (value, kind) =>
                        {
                            // Restore the snapshot even when a registry write throws after starting.
                            startupChanged = true;
                            WriteStartupValue((string)value, kind);
                        });
            }
            catch (Exception installError)
            {
                var rollback = new List<(string Name, Action Action)>();
                if (startupChanged) rollback.Add(("Restore startup entry", () => RestoreStartupRegistry(startupBefore)));
                if (installedNew) rollback.Add(("Remove failed update", () =>
                {
                    if (Directory.Exists(target)) Directory.Move(target, staged);
                }));
                if (backup is not null) rollback.Add(("Restore previous application files", () =>
                {
                    if (Directory.Exists(backup)) Directory.Move(backup, previous!);
                }));
                if (startShortcutChanged) rollback.Add(("Restore Start menu shortcut", () =>
                    RestoreFileSnapshot(StartShortcut, startShortcutExisted, startShortcutBefore)));
                if (desktopShortcutChanged) rollback.Add(("Restore desktop shortcut", () =>
                    RestoreFileSnapshot(DesktopShortcut, desktopShortcutExisted, desktopShortcutBefore)));
                rollback.Add(("Restore uninstall entry", () =>
                {
                    if (previous is not null) RegisterUninstall(previous, previousVersion);
                    else Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                }));
                RunRollbackActions(installError,
                    () => backup is not null && Directory.Exists(backup) ? backup : null, rollback);
            }
            progress.Report((95, "Finishing…"));
            if (backup is not null)
            {
                try { DeleteDirectoryTreeNoFollow(backup); }
                catch (IOException) { /* A locked old file can be removed later; the new installation is usable. */ }
                catch (UnauthorizedAccessException) { /* Preserve the backup rather than report a successful install as failed. */ }
            }
        }
        finally
        {
            // A failed rollback must never be followed by deleting the only remaining previous copy.
            if (Directory.Exists(workspace) && (backup is null || !Directory.Exists(backup)))
            {
                try { DeleteDirectoryTreeNoFollow(workspace); }
                catch (IOException) { /* A transient lock must not turn a completed install or its original failure into another result. */ }
                catch (UnauthorizedAccessException) { /* The GUID-scoped staging folder is safe to leave for later cleanup. */ }
            }
        }
    }

    internal static void Remove(IProgress<(int, string)> progress, bool deleteManagedData = false) =>
        RunExclusiveTransaction(() => RemoveCore(progress, deleteManagedData));

    private static void RemoveCore(IProgress<(int, string)> progress, bool deleteManagedData)
    {
        string target = GetRegisteredInstallDir() ?? throw new InvalidOperationException("The uninstall entry is missing.");
        CheckTarget(target);
        CheckInstalledTarget(target);
        RegistryValueSnapshot startup = ReadStartupValue();
        bool removeStartup = ShouldRemoveStartupValue(startup.Exists, startup.Value, target);
        progress.Report((25, "Removing application files…"));
        DeleteInstalledTarget(target);
        progress.Report((75, "Removing shortcuts and uninstall entry…"));
        var cleanup = new List<(string Name, Action Action)>
        {
            ("Start menu shortcut", () => DeleteShortcutIfOwned(StartShortcut, target)),
            ("Desktop shortcut", () => DeleteShortcutIfOwned(DesktopShortcut, target))
        };
        if (removeStartup) cleanup.Add(("Startup entry", DeleteStartupValue));
        cleanup.Add(("Uninstall entry", () => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false)));
        if (deleteManagedData) cleanup.Add(("DesktopTools settings and notes", () => DeleteManagedData(DataDir)));
        RunCleanupActions(cleanup);
        progress.Report((95, "Finishing…"));
    }

    internal static void RunExclusiveTransaction(Action action)
    {
        using var mutex = new Mutex(false, @"Local\DesktopTools.Installer.Transaction");
        bool entered;
        try { entered = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { entered = true; }
        if (!entered) throw new InvalidOperationException("Another DesktopTools setup or uninstall is already running.");
        try { action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static Stream OpenResource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidDataException("Setup resource is missing: " + name);

    internal static void StartBundledUninstall()
    {
        string target = GetRegisteredInstallDir() ?? throw new InvalidOperationException("DesktopTools is not installed for this user.");
        ValidatePath(target);
        if (!File.Exists(Path.Combine(target, "DesktopTools.exe")) || !File.Exists(Path.Combine(target, "VERSION.txt")))
            throw new InvalidOperationException("The registered folder is not a valid DesktopTools installation. No files were removed.");
        string temporary = Path.Combine(Path.GetTempPath(), "DesktopTools-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
        CopyResource("DesktopTools.Uninstall.exe", temporary);
        var start = new ProcessStartInfo(temporary) { UseShellExecute = false };
        start.ArgumentList.Add("--uninstall-worker");
        start.ArgumentList.Add(target);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start DesktopTools uninstall.");
    }

    private static void CopyResource(string name, string path)
    {
        using Stream source = OpenResource(name);
        using FileStream destination = File.Create(path);
        source.CopyTo(destination);
    }

    private static void CreateShortcut(string path, string target)
    {
        string exe = Path.Combine(target, "DesktopTools.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(path);
        try
        {
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = target;
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = Product;
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void CreateShortcutIfOwned(string path, string target, string? previouslyOwnedExecutable, Action beforeChange)
    {
        bool exists = File.Exists(path);
        string? currentTarget = exists ? ReadShortcutTarget(path) : null;
        if (!ShouldReplaceShortcut(exists, currentTarget, previouslyOwnedExecutable)) return;
        // Restore the snapshot even if Save writes the link and then throws.
        beforeChange();
        CreateShortcut(path, target);
    }

    private static void DeleteShortcutIfOwned(string path, string installDir)
    {
        if (!File.Exists(path)) return;
        string ownedExecutable = Path.Combine(installDir, "DesktopTools.exe");
        if (ShouldReplaceShortcut(true, ReadShortcutTarget(path), ownedExecutable)) File.Delete(path);
    }

    private static string? ReadShortcutTarget(string path)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path]);
            return shortcut?.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void RegisterUninstall(string target, string? displayVersion = null)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey)!
            ?? throw new InvalidOperationException("Cannot create the uninstall entry.");
        string exe = Path.Combine(target, "DesktopTools.exe");
        long size = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        key.SetValue("DisplayName", Product);
        key.SetValue("DisplayVersion", displayVersion ?? Version);
        key.SetValue("Publisher", "DesktopTools contributors");
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", target);
        key.SetValue("URLInfoAbout", "https://github.com/vg2222/DesktopTools");
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, (size + 1023) / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("UninstallString", "\"" + Path.Combine(target, "Uninstall.exe") + "\"");
    }
}
