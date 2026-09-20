namespace DesktopTools.Native;

public static class RecordingPrerequisites
{
    private static readonly string[] VisualCppRuntimeFiles =
    {
        "vcruntime140.dll",
        "vcruntime140_1.dll",
        "msvcp140.dll"
    };

    public static Uri VisualCppRuntimeHelpUri { get; } = new("https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist?view=msvc-170");

    public static IReadOnlyList<string> FindMissingVisualCppRuntimeFiles()
        => FindMissingVisualCppRuntimeFiles(File.Exists, AppContext.BaseDirectory, Environment.SystemDirectory);

    internal static IReadOnlyList<string> FindMissingVisualCppRuntimeFiles(
        Func<string, bool> fileExists,
        string appDirectory,
        string systemDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return VisualCppRuntimeFiles
            .Where(file => !fileExists(Path.Combine(appDirectory, file))
                && !fileExists(Path.Combine(systemDirectory, file)))
            .ToArray();
    }
}
