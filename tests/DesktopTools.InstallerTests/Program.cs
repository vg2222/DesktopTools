using System.Reflection;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var installer = Assembly.Load("DesktopTools.Installer");
var program = installer.GetType("DesktopTools.Installer.Program", throwOnError: true)!;
var installerLanguage = installer.GetType("DesktopTools.Localization.L", throwOnError: true)!;
var systemLanguage = installerLanguage.GetMethod("SystemLanguage", BindingFlags.Public | BindingFlags.Static)!;
Check((string)systemLanguage.Invoke(null, [System.Globalization.CultureInfo.GetCultureInfo("de-DE")])! == "de", "Installer did not map the Windows UI language");
var saveInitialLanguage = program.GetMethod("SaveInitialLanguage", BindingFlags.Static | BindingFlags.NonPublic, [typeof(string), typeof(string)])!;
string languageFixture = Path.Combine(Path.GetTempPath(), "DesktopTools installer language", Guid.NewGuid().ToString("N"));
try
{
    saveInitialLanguage.Invoke(null, [languageFixture, "ru"]);
    string settingsPath = Path.Combine(languageFixture, "settings.json");
    Check(File.ReadAllText(settingsPath).Contains("\"ru\""), "Installer language was not passed to the app");
    File.WriteAllText(settingsPath, "{\"Version\":1,\"Language\":\"fr\"}");
    saveInitialLanguage.Invoke(null, [languageFixture, "de"]);
    Check(File.ReadAllText(settingsPath).Contains("\"fr\""), "Installer overwrote an existing app language");
}
finally { if (Directory.Exists(languageFixture)) Directory.Delete(languageFixture, true); }
var runtime = installer.GetType("DesktopTools.Installer.RecordingRuntime", throwOnError: true)!;
var runtimeAvailable = runtime.GetMethod("IsAvailable", BindingFlags.Static | BindingFlags.NonPublic, [typeof(Func<string, bool>)])!;
Check((bool)runtimeAvailable.Invoke(null, [new Func<string, bool>(_ => true)])!, "installed recording runtime was rejected");
foreach (string missing in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" })
    Check(!(bool)runtimeAvailable.Invoke(null, [new Func<string, bool>(name => name != missing)])!, "missing runtime was accepted: " + missing);
if (args.Contains("--render", StringComparer.Ordinal))
{
    RenderInstallerModes(installer, program);
    return;
}
if (args.Contains("--motion", StringComparer.Ordinal))
{
    InstallerMotionChecks.Run(installer);
    return;
}
var matches = program.GetMethod("StartupCommandMatches", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("StartupCommandMatches helper is missing.");
var cleanup = program.GetMethod("RunCleanupActions", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("RunCleanupActions helper is missing.");
var relocate = program.GetMethod("RelocateStartupValue", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("RelocateStartupValue helper is missing.");
var shouldRemove = program.GetMethod("ShouldRemoveStartupValue", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("ShouldRemoveStartupValue helper is missing.");
var restore = program.GetMethod("RestoreStartupValue", BindingFlags.Static | BindingFlags.NonPublic, [typeof(bool), typeof(object), typeof(RegistryValueKind), typeof(Action<object, RegistryValueKind>), typeof(Action)])
    ?? throw new Exception("RestoreStartupValue delegate helper is missing.");
var decide = program.GetMethod("DecideSetupMode", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("DecideSetupMode helper is missing.");
var registeredProcess = program.GetMethod("IsRegisteredApplicationProcess", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("IsRegisteredApplicationProcess helper is missing.");
var readVersion = program.GetMethod("ReadInstalledVersionFromFolder", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("ReadInstalledVersionFromFolder helper is missing.");
var deleteManagedData = program.GetMethod("DeleteManagedData", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("DeleteManagedData helper is missing.");
var normalizeVersion = program.GetMethod("NormalizeVersion", BindingFlags.Static | BindingFlags.NonPublic, [typeof(string)])
    ?? throw new Exception("NormalizeVersion helper is missing.");
var parseSetupArgs = program.GetMethod("ParseSetupArguments", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("ParseSetupArguments helper is missing.");
var validReadyEvent = program.GetMethod("IsValidReadyEventName", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("IsValidReadyEventName helper is missing.");
var deleteTree = program.GetMethod("DeleteDirectoryTreeNoFollow", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("DeleteDirectoryTreeNoFollow helper is missing.");
var checkInstalledTarget = program.GetMethod("CheckInstalledTarget", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("CheckInstalledTarget helper is missing.");
var deleteInstalledTarget = program.GetMethod("DeleteInstalledTarget", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("DeleteInstalledTarget helper is missing.");
var runRollback = program.GetMethod("RunRollbackActions", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("RunRollbackActions helper is missing.");
var shouldReplaceShortcut = program.GetMethod("ShouldReplaceShortcut", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("ShouldReplaceShortcut helper is missing.");
var restoreFileSnapshot = program.GetMethod("RestoreFileSnapshot", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new Exception("RestoreFileSnapshot helper is missing.");
var notificationPrivacy = installer.GetType("DesktopTools.Installer.BackgroundUpdateWindow", true)!
    .GetMethod("NotificationHiddenFromJson", BindingFlags.Static | BindingFlags.NonPublic)!;
bool HideNotification(string json) => (bool)notificationPrivacy.Invoke(null, [json])!;
Check(HideNotification("{}") && HideNotification("invalid") && HideNotification("null"), "installer progress did not default to capture-hidden");
Check(!HideNotification("""{"CaptureVisibilityOverrides":{"Notifications":false}}"""), "installer progress ignored explicit visible preference");
Check(!HideNotification("""{"VisibleCaptureFeatures":["Notifications"]}"""), "installer progress ignored legacy visible preference");
Check(HideNotification("""{"CaptureVisibilityOverrides":{"Notifications":true},"VisibleCaptureFeatures":["Notifications"]}"""), "legacy preference overrode explicit hidden preference");

bool Matches(string command, string folder) => (bool)matches.Invoke(null, [command, folder])!;
string installed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopTools installer tests", "installed"));
string portable = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopTools installer tests", "portable"));
Check(Matches($"\"{Path.Combine(installed, "DesktopTools.exe")}\" --startup", installed), "registered install startup command was not recognized");
Check(Matches($"\"{Path.Combine(installed.ToUpperInvariant(), "DesktopTools.exe")}\" --startup", installed), "startup command path comparison is case-sensitive");
Check(!Matches($"\"{Path.Combine(portable, "DesktopTools.exe")}\" --startup", installed), "portable startup command was claimed by installer");
Check(!Matches($"\"{Path.Combine(installed, "DesktopTools.exe")}\" --other", installed), "different startup arguments were claimed by installer");
bool ShouldRemove(bool exists, object? value, string folder) => (bool)shouldRemove.Invoke(null, [exists, value, folder])!;
Check(!ShouldRemove(false, null, installed), "disabled startup was selected for uninstall cleanup");
Check(ShouldRemove(true, $"\"{Path.Combine(installed, "DesktopTools.exe")}\" --startup", installed), "matching startup was not selected for uninstall cleanup");
Check(!ShouldRemove(true, $"\"{Path.Combine(portable, "DesktopTools.exe")}\" --startup", installed), "portable startup was selected for uninstall cleanup");

var writes = new List<(object Value, RegistryValueKind Kind)>();
var write = new Action<object, RegistryValueKind>((value, kind) => writes.Add((value, kind)));
bool Relocate(bool exists, object? value, RegistryValueKind kind, string previous, string target) =>
    (bool)relocate.Invoke(null, [exists, value, kind, previous, target, write])!;
Check(!Relocate(false, null, RegistryValueKind.None, installed, portable) && writes.Count == 0,
    "disabled startup was enabled during relocation");
Check(!Relocate(true, $"\"{Path.Combine(portable, "DesktopTools.exe")}\" --startup", RegistryValueKind.String, installed, portable) && writes.Count == 0,
    "portable startup entry was overwritten during relocation");
Check(!Relocate(true, $"\"{Path.Combine(installed, "DesktopTools.exe")}\" --startup", RegistryValueKind.String, installed, installed) && writes.Count == 0,
    "same-folder upgrade unnecessarily rewrote startup state");
Check(Relocate(true, $"\"{Path.Combine(installed, "DesktopTools.exe")}\" --startup", RegistryValueKind.ExpandString, installed, portable),
    "matching startup entry was not relocated");
Check(writes.Count == 1 && Equals(writes[0].Value, $"\"{Path.Combine(portable, "DesktopTools.exe")}\" --startup") && writes[0].Kind == RegistryValueKind.ExpandString,
    "relocation did not preserve startup value kind");

object originalValue = $"\"{Path.Combine(installed, "DesktopTools.exe")}\" --startup";
object? restoredValue = null; RegistryValueKind restoredKind = RegistryValueKind.None; bool deleted = false;
var restoreWrite = new Action<object, RegistryValueKind>((value, kind) => { restoredValue = value; restoredKind = kind; });
var restoreDelete = new Action(() => deleted = true);
restore.Invoke(null, [true, originalValue, RegistryValueKind.ExpandString, restoreWrite, restoreDelete]);
Check(Equals(restoredValue, originalValue) && restoredKind == RegistryValueKind.ExpandString && !deleted,
    "rollback did not restore the exact startup value and kind");
restoredValue = null; restoredKind = RegistryValueKind.None;
restore.Invoke(null, [false, null, RegistryValueKind.None, restoreWrite, restoreDelete]);
Check(deleted && restoredValue is null, "rollback did not preserve a disabled startup entry");

var calls = new List<string>();
var actions = new List<(string Name, Action Action)>
{
    ("first", () => { calls.Add("first"); throw new IOException("locked"); }),
    ("second", () => calls.Add("second")),
    ("third", () => calls.Add("third"))
};
try { cleanup.Invoke(null, [actions]); throw new Exception("cleanup failures were not reported"); }
catch (TargetInvocationException ex)
{
    Check(ex.InnerException is InvalidOperationException error && error.Message.Contains("first", StringComparison.Ordinal), "cleanup error did not identify the failed action");
}
Check(calls.SequenceEqual(["first", "second", "third"]), "cleanup stopped after the first failure");

var rollbackCalls = new List<string>();
var rollbackActions = new List<(string Name, Action Action)>
{
    ("remove failed update", () => { rollbackCalls.Add("remove"); throw new IOException("locked update"); }),
    ("restore previous installation", () => rollbackCalls.Add("restore")),
    ("restore registration", () => rollbackCalls.Add("metadata"))
};
try
{
    runRollback.Invoke(null, [new IOException("install failed"), new Func<string?>(() => @"C:\retained\previous"), rollbackActions]);
    throw new Exception("rollback failures were not reported");
}
catch (TargetInvocationException ex)
{
    Check(ex.InnerException is AggregateException error &&
        error.Message.Contains(@"C:\retained\previous", StringComparison.Ordinal) &&
        error.InnerExceptions.Any(inner => inner.Message.Contains("install failed", StringComparison.Ordinal)) &&
        error.InnerExceptions.Any(inner => inner.Message.Contains("remove failed update", StringComparison.Ordinal)),
        "rollback error did not retain the install failure, failed action, and backup path");
}
Check(rollbackCalls.SequenceEqual(["remove", "restore", "metadata"]), "rollback stopped before restoring the previous installation and metadata");

bool ShouldReplaceShortcut(bool exists, string? currentTarget, string? ownedTarget) =>
    (bool)shouldReplaceShortcut.Invoke(null, [exists, currentTarget, ownedTarget])!;
string ownedExe = Path.Combine(installed, "DesktopTools.exe");
Check(ShouldReplaceShortcut(false, null, null), "a missing shortcut was not available for creation");
Check(ShouldReplaceShortcut(true, ownedExe.ToUpperInvariant(), ownedExe), "an owned shortcut target was not recognized case-insensitively");
Check(!ShouldReplaceShortcut(true, Path.Combine(portable, "Other.exe"), ownedExe), "an unrelated same-name shortcut was selected for overwrite or deletion");

string Decide(string? installedVersion, string setupVersion) => decide.Invoke(null, [installedVersion, setupVersion])!.ToString()!;
Check(Decide(null, "1.2.0") == "Install", "fresh setup was not classified as install");
Check(Decide("1.2.0", "1.2.0") == "Maintenance", "matching setup was not classified as maintenance");
Check(Decide("1.1.0", "1.2.0") == "Update", "newer setup was not classified as update");
Check(Decide("1.3.0", "1.2.0") == "OlderSetup", "older setup was allowed to downgrade");
Check(Decide("invalid", "1.2.0") == "Maintenance", "unknown installed version was treated as safe to overwrite");
Check(Equals(normalizeVersion.Invoke(null, ["1.0"]), "1.0.0"), "two-component version was not normalized safely");
Check(Equals(normalizeVersion.Invoke(null, ["2.4.1.9"]), "2.4.1"), "four-component version did not normalize to display version");
Check(normalizeVersion.Invoke(null, ["corrupt"]) is null, "corrupt version marker was accepted");

string eventName = @"Local\DesktopTools.Update.0123456789abcdef0123456789abcdef";
Check((bool)validReadyEvent.Invoke(null, [eventName])!, "valid update ready event was rejected");
Check(!(bool)validReadyEvent.Invoke(null, [@"Global\DesktopTools.Update.0123456789abcdef0123456789abcdef"])!, "global ready event was accepted");
Check(!(bool)validReadyEvent.Invoke(null, [@"Local\DesktopTools.Update.not-a-guid"])!, "malformed ready event was accepted");
object Parse(params string[] args) => parseSetupArgs.Invoke(null, [args])!;
Check(Parse("--background-update", "--wait-for-exit", "42", "--ready-event", eventName).ToString()!.Contains("BackgroundUpdate", StringComparison.Ordinal),
    "background handoff arguments were not parsed");
Check(Parse("--update", "--wait-for-exit", "42", "--ready-event", eventName).ToString()!.Contains("Update", StringComparison.Ordinal),
    "interactive handoff arguments were not parsed");
ExpectInvocationFailure(() => Parse("--background-update", "--wait-for-exit", "42", "--ready-event", "bad"), "malformed ready event was accepted by argument parser");
ExpectInvocationFailure(() => Parse("--update", "--wait-for-exit", "42", "--ready-event", eventName, "extra"), "extra setup argument was accepted");

bool IsRegisteredProcess(string? executable, string folder) => (bool)registeredProcess.Invoke(null, [executable, folder])!;
Check(IsRegisteredProcess(Path.Combine(installed, "DesktopTools.exe"), installed), "registered application process was rejected");
Check(!IsRegisteredProcess(Path.Combine(installed, "Uninstall.exe"), installed), "uninstaller was accepted as application handoff process");
Check(!IsRegisteredProcess(Path.Combine(portable, "DesktopTools.exe"), installed), "portable application was accepted as registered handoff process");

string fixture = Path.Combine(Path.GetTempPath(), "DesktopTools installer tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    File.WriteAllText(Path.Combine(fixture, "VERSION.txt"), " 2.4.1 \r\n");
    Check(Equals(readVersion.Invoke(null, [fixture]), "2.4.1"), "installed version file was not read and normalized");
    File.WriteAllText(Path.Combine(fixture, "VERSION.txt"), "not-a-version");
    Check(readVersion.Invoke(null, [fixture]) is null, "invalid installed version was trusted");
    File.WriteAllText(Path.Combine(fixture, "VERSION.txt"), "1.0");
    Check(Equals(readVersion.Invoke(null, [fixture]), "1.0.0"), "short installed version caused unsafe normalization");
}
finally { Directory.Delete(fixture, true); }

string partialInstall = Path.Combine(Path.GetTempPath(), "DesktopTools installer tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(partialInstall);
try
{
    File.WriteAllText(Path.Combine(partialInstall, "Uninstall.exe"), "test uninstaller");
    checkInstalledTarget.Invoke(null, [partialInstall]);
    File.WriteAllText(Path.Combine(partialInstall, "DesktopTools.exe"), "test app");
    File.WriteAllText(Path.Combine(partialInstall, "VERSION.txt"), "1.0.0");
    string blocked = Path.Combine(partialInstall, "blocked.dat");
    using (var held = new FileStream(blocked, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
    {
        try
        {
            deleteInstalledTarget.Invoke(null, [partialInstall]);
            throw new Exception("Locked installation file did not interrupt deletion");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException or UnauthorizedAccessException) { }
        Check(File.Exists(Path.Combine(partialInstall, "Uninstall.exe")), "failed uninstall removed its retry entry point");
        checkInstalledTarget.Invoke(null, [partialInstall]);
    }
    deleteInstalledTarget.Invoke(null, [partialInstall]);
    Check(!Directory.Exists(partialInstall), "retry left installation files behind");
}
finally { if (Directory.Exists(partialInstall)) Directory.Delete(partialInstall, true); }

string deleteFixture = Path.Combine(Path.GetTempPath(), "DesktopTools installer tests", Guid.NewGuid().ToString("N"));
string managed = Path.Combine(deleteFixture, "DesktopTools");
string external = Path.Combine(deleteFixture, "original.png");
Directory.CreateDirectory(managed);
File.WriteAllText(Path.Combine(managed, "settings.json"), "{}");
File.WriteAllText(external, "original");
deleteManagedData.Invoke(null, [managed]);
Check(!Directory.Exists(managed), "managed data directory was retained after explicit deletion");
Check(File.Exists(external), "external original media was deleted with managed data");
Directory.Delete(deleteFixture, true);

string linkFixture = Path.Combine(Path.GetTempPath(), "DesktopTools installer tests", Guid.NewGuid().ToString("N"));
string deleteRoot = Path.Combine(linkFixture, "managed");
string outsideRoot = Path.Combine(linkFixture, "outside");
Directory.CreateDirectory(deleteRoot);
Directory.CreateDirectory(outsideRoot);
File.WriteAllText(Path.Combine(outsideRoot, "original.txt"), "preserve");
try
{
    Directory.CreateSymbolicLink(Path.Combine(deleteRoot, "linked-originals"), outsideRoot);
    deleteTree.Invoke(null, [deleteRoot]);
    Check(!Directory.Exists(deleteRoot), "managed tree containing a directory link was retained");
    Check(File.Exists(Path.Combine(outsideRoot, "original.txt")), "recursive cleanup traversed a directory link outside its root");
}
catch (UnauthorizedAccessException)
{
    deleteTree.Invoke(null, [deleteRoot]);
}
catch (IOException ex) when ((uint)ex.HResult is 0x80070522 or 0x80070005)
{
    // Creating directory links requires Windows Developer Mode or a privilege on some test hosts.
    deleteTree.Invoke(null, [deleteRoot]);
}
finally { if (Directory.Exists(linkFixture)) Directory.Delete(linkFixture, true); }

string snapshotFixture = Path.Combine(Path.GetTempPath(), "DesktopTools installer tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(snapshotFixture);
try
{
    string shortcutFile = Path.Combine(snapshotFixture, "DesktopTools.lnk");
    byte[] originalShortcut = [1, 7, 4, 9, 3];
    File.WriteAllBytes(shortcutFile, originalShortcut);
    byte[] snapshot = File.ReadAllBytes(shortcutFile);
    File.WriteAllBytes(shortcutFile, [8, 8]);
    restoreFileSnapshot.Invoke(null, [shortcutFile, true, snapshot]);
    Check(File.ReadAllBytes(shortcutFile).SequenceEqual(originalShortcut), "rollback did not restore the exact previous shortcut bytes");
    restoreFileSnapshot.Invoke(null, [shortcutFile, false, null]);
    Check(!File.Exists(shortcutFile), "rollback retained a shortcut created by the failed install");
}
finally { Directory.Delete(snapshotFixture, true); }

Console.WriteLine("PASS installer maintenance decisions, handoff ownership, version detection, safe deletion, startup and shortcut ownership, and best-effort rollback");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void ExpectInvocationFailure(Action action, string message)
{
    try { action(); }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return; }
    throw new Exception(message);
}

static void RenderInstallerModes(Assembly installer, Type program)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Type windowType = installer.GetType("DesktopTools.Installer.InstallerWindow", true)!;
            Type modeType = program.GetNestedType("SetupMode", BindingFlags.NonPublic)!;
            Type releaseType = installer.GetType("DesktopTools.Updates.GitHubRelease", true)!;
            Type latestDelegate = typeof(Func<,>).MakeGenericType(typeof(CancellationToken), typeof(Task<>).MakeGenericType(releaseType));
            Type nullableMode = typeof(Nullable<>).MakeGenericType(modeType);
            ConstructorInfo constructor = windowType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(bool), typeof(bool), nullableMode, typeof(string), typeof(string), latestDelegate, typeof(Func<bool>)], null)
                ?? throw new Exception("Render constructor is missing.");
            string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/integration"));
            Directory.CreateDirectory(output);
            Render("install", false, "Install", false);
            Render("maintenance", false, "Maintenance", false);
            Render("update-more", false, "Update", true);
            Render("older", false, "OlderSetup", false);
            Render("uninstall", true, "Maintenance", false);
            Render("runtime-ready", false, "Install", false, runtimeReady: true);
            Render("compact", false, "Maintenance", false, compact: true);
            Render("update-available", false, "Maintenance", false, releaseVersion: "1.2.0");
            Render("offline", false, "Maintenance", false, offline: true);
            Render("older-release", false, "OlderSetup", false, releaseVersion: "1.0.5");
            Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            Render("install-ru", false, "Install", false);
            Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            RenderBackgroundProgress();

            void Render(string name, bool uninstall, string modeName, bool expand, bool runtimeReady = false, bool compact = false, string? releaseVersion = null, bool offline = false)
            {
                object mode = Enum.Parse(modeType, modeName);
                object? release = releaseVersion is null ? null : Activator.CreateInstance(releaseType, releaseVersion,
                    new Uri("https://github.com/vg2222/DesktopTools/releases"), new Uri("https://github.com/vg2222/DesktopTools/releases/download/test/setup.exe"), "setup.exe", 1024L, new Uri("https://github.com/vg2222/DesktopTools/releases/download/test/SHA256SUMS.txt"));
                var result = offline ? System.Linq.Expressions.Expression.Call(typeof(Task).GetMethods().Single(m => m.Name == "FromException" && m.IsGenericMethod).MakeGenericMethod(releaseType), System.Linq.Expressions.Expression.Constant(new IOException("Offline test"), typeof(Exception)))
                    : System.Linq.Expressions.Expression.Call(typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(releaseType), System.Linq.Expressions.Expression.Constant(release, releaseType));
                var completedCheck = System.Linq.Expressions.Expression.Lambda(latestDelegate, result, System.Linq.Expressions.Expression.Parameter(typeof(CancellationToken))).Compile();
                var window = (Window)constructor.Invoke([uninstall, false, mode, modeName == "OlderSetup" ? "1.3.0" : "1.0.0", modeName == "Update" ? "1.1.0" : "1.0.0", completedCheck, new Func<bool>(() => runtimeReady)]);
                if (!uninstall) ((Task)windowType.GetMethod("CheckLatestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!).GetAwaiter().GetResult();
                if (compact) { window.Width = 740; window.Height = 480; }
                if (expand)
                {
                    var panel = (Border?)windowType.GetField("advancedOptions", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
                    if (panel is not null) panel.Visibility = Visibility.Visible;
                }
                double width = window.Width, height = window.Height;
                window.Measure(new Size(width, height));
                window.Arrange(new Rect(0, 0, width, height));
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                window.UpdateLayout();
                root.UpdateLayout();
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                root.UpdateLayout();
                AssertControlsFit(window);
                object? Field(string field) => windowType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
                if (!uninstall && modeName is "Maintenance" or "OlderSetup")
                {
                    var panel = (Border)Field("advancedOptions")!;
                    var choices = (StackPanel)panel.Child;
                    Check(choices.Children.OfType<Button>().Select(System.Windows.Automation.AutomationProperties.GetName).SequenceEqual(new[] { "Update", "Repair", "Uninstall" }), "maintenance list order or labels differ");
                    Check(((Button)Field("primary")!).Visibility == Visibility.Collapsed, "maintenance still shows a footer action");
                    Check(((Button)Field("repair")!).IsEnabled == (modeName != "OlderSetup"), "older setup offered an unsafe repair");
                    Check(choices.Children.OfType<Button>().All(button => VisualDescendants(button).OfType<System.Windows.Shapes.Path>().Any()), "maintenance list is missing Fluent icons");
                }
                if (!uninstall)
                {
                    var download = (Button)Field("runtimeDownload")!;
                    Check(download.Visibility == (runtimeReady ? Visibility.Collapsed : Visibility.Visible), "runtime guidance does not match prerequisite state");
                    Check(((TextBlock)Field("runtimeDescription")!).Text.Contains(
                        Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? (runtimeReady ? "установлен" : "Остальные инструменты") : (runtimeReady ? "ready" : "Other tools work"), StringComparison.Ordinal), "runtime explanation is missing");
                    runtimeReady = !runtimeReady;
                    windowType.GetMethod("RefreshRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Check(download.Visibility == (runtimeReady ? Visibility.Collapsed : Visibility.Visible), "Check again failed to refresh prerequisite state");
                    runtimeReady = !runtimeReady;
                    windowType.GetMethod("RefreshRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    if (modeName == "OlderSetup") Check(Field("latestRelease") is null && !((Button)Field("repair")!).IsEnabled, "older online release bypassed downgrade protection");
                    if (offline) Check(((Button)Field("githubUpdate")!).IsEnabled && ((Button)Field("repair")!).IsEnabled, "offline check left local maintenance disabled");
                    if (releaseVersion is not null && modeName == "Maintenance") Check(Field("latestRelease") is not null, "newer GitHub release was not actionable");
                }
                if (compact) Check(VisualDescendants(root).OfType<ScrollViewer>().Any(scroll => scroll.ScrollableHeight > 0), "compact setup cannot scroll its content: " + string.Join("; ", VisualDescendants(root).OfType<ScrollViewer>().Select(s => $"actual={s.ActualHeight}, viewport={s.ViewportHeight}, extent={s.ExtentHeight}, content={((FrameworkElement)s.Content).ActualHeight}")));
                var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                AssertNonTransparent(bitmap, name);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream stream = File.Create(Path.Combine(output, $"installer-update-{name}.png"));
                encoder.Save(stream);
                if (name == "install")
                {
                    var selector = VisualDescendants((DependencyObject)window.Content).OfType<ComboBox>().Single(c => Equals(c.Tag, "installer-language"));
                    selector.SelectedItem = "Русский";
                    Check(((TextBlock)Field("headline")!).Text == "Установить DesktopTools", "Installer language selector did not update the first screen");
                }
                window.Close();
            }

            void RenderBackgroundProgress()
            {
                Type progressType = installer.GetType("DesktopTools.Installer.BackgroundUpdateWindow", true)!;
                var window = (Window)Activator.CreateInstance(progressType, nonPublic: true)!;
                window.Measure(new Size(420, 112));
                window.Arrange(new Rect(0, 0, 420, 112));
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(420, 112));
                root.Arrange(new Rect(0, 0, 420, 112));
                window.UpdateLayout();
                AssertControlsFit(window);
                var bitmap = new RenderTargetBitmap(420, 112, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                AssertNonTransparent(bitmap, "background-progress");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream stream = File.Create(Path.Combine(output, "installer-update-background-progress.png"));
                encoder.Save(stream);
                nint handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                bool hidden = (bool)progressType.GetMethod("ReadNotificationHidden", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                Check(InstallerNativeChecks.GetWindowDisplayAffinity(handle, out uint affinity) && affinity == (hidden ? 0x11u : 0u), "installer progress capture preference was not applied to its native window");
                Check((InstallerNativeChecks.GetWindowLongPtr(handle, -20).ToInt64() & 0x080000a0) == 0x080000a0, "installer progress can activate or intercept desktop input");
                window.Close();
            }
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw new Exception("Installer render failed.", failure);
    Console.WriteLine("PASS installer mode renders and bounds checks");
}

static void AssertNonTransparent(BitmapSource bitmap, string name)
{
    int stride = bitmap.PixelWidth * 4;
    byte[] pixels = new byte[stride * bitmap.PixelHeight];
    bitmap.CopyPixels(pixels, stride, 0);
    Check(pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0), $"{name} render is fully transparent");
}

static void AssertControlsFit(Window window)
{
    var root = (FrameworkElement)window.Content;
    foreach (FrameworkElement element in VisualDescendants(root).OfType<FrameworkElement>()
        .Where(element => element.RenderSize.Width > 0 && element.RenderSize.Height > 0 && element is Button or TextBlock or CheckBox))
    {
        Rect bounds = element.TransformToAncestor(root).TransformBounds(new Rect(new Point(), element.RenderSize));
        bool clippedByScroll = false;
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(element); ancestor is not null && ancestor != root; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is ScrollViewer) clippedByScroll = true;
        Check(bounds.Left >= -0.5 && bounds.Right <= root.ActualWidth + 0.5 && (clippedByScroll || bounds.Top >= -0.5 && bounds.Bottom <= root.ActualHeight + 0.5),
            $"control '{(element as ContentControl)?.Content ?? (element as TextBlock)?.Text}' extends outside installer bounds: {bounds}");
    }
}

static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
{
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        DependencyObject child = VisualTreeHelper.GetChild(root, index);
        yield return child;
        foreach (DependencyObject descendant in VisualDescendants(child)) yield return descendant;
    }
}

internal static class InstallerNativeChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint window, int index);
}
