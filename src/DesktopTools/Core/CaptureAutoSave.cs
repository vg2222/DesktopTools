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
    public static string UniquePath(string directory, DateTime time, Func<string, bool> exists)
    {
        string stem = $"DesktopTools-{time:yyyyMMdd-HHmmss}";
        string path = Path.Combine(directory, stem + ".png");
        for (int copy = 2; exists(path); copy++)
        {
            if (copy > 9999) throw new IOException("Too many screenshots share this second.");
            path = Path.Combine(directory, $"{stem}-{copy}.png");
        }
        return path;
    }
}
