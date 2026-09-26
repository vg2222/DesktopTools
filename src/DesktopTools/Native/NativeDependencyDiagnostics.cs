namespace DesktopTools.Native;

/// <summary>Read-only file checks for the Windows media components used by native tools.</summary>
public static class NativeDependencyDiagnostics
{
    private static readonly string[] MediaFoundationFiles = ["mfplat.dll", "mfreadwrite.dll"];

    public static Uri MediaFeaturePackHelpUri { get; } = new(
        "https://support.microsoft.com/en-us/windows/media-feature-pack-for-windows-10-11-n-february-2023-2aaf89b8-f9d3-4322-98d0-612c9bea9c01");

    public static IReadOnlyList<string> FindMissingMediaFoundationFiles() =>
        FindMissingMediaFoundationFiles(File.Exists, Environment.SystemDirectory);

    internal static IReadOnlyList<string> FindMissingMediaFoundationFiles(
        Func<string, bool> fileExists, string systemDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return MediaFoundationFiles.Where(file => !fileExists(Path.Combine(systemDirectory, file))).ToArray();
    }
}
