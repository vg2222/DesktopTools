using System.IO;

namespace DesktopTools.Core;

/// <summary>Where a screenshot goes when it is saved without a dialog ("Clipboard and folder").</summary>
public static class CaptureAutoSave
{
    public const string Mode = "Clipboard and folder";

    /// <summary>The chosen save location, or Pictures\DesktopTools until one is chosen.</summary>
    public static string ResolveDirectory(string? chosen, string picturesFolder) =>
        string.IsNullOrWhiteSpace(chosen) ? Path.Combine(picturesFolder, "DesktopTools") : chosen;

    /// <summary>
    /// A timestamped PNG name that is not taken. Two captures in the same second get -2, -3 and so on; nothing is ever overwritten.
    /// </summary>
    public static string UniquePath(string directory, DateTime time, Func<string, bool> exists) =>
        UniquePath(directory, SmartFileNames.Fallback(time), exists);

    /// <summary>A PNG name built from <paramref name="stem"/> that is not taken: stem.png, then stem-2.png, stem-3.png and so on.</summary>
    public static string UniquePath(string directory, string stem, Func<string, bool> exists)
    {
        string path = Path.Combine(directory, stem + ".png");
        for (int copy = 2; exists(path); copy++)
        {
            if (copy > 9999) throw new IOException("Too many screenshots share this name.");
            path = Path.Combine(directory, $"{stem}-{copy}.png");
        }
        return path;
    }
}
