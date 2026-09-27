using DesktopTools.Localization;

namespace DesktopTools.Extras;

internal static class RecordingReadiness
{
    internal const long MinimumFreeBytes = 512L * 1024 * 1024;

    internal static string? CheckDisk(string destination)
    {
        string fullPath = Path.GetFullPath(destination);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root)) return null;
        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return null;
            if (drive.AvailableFreeSpace < MinimumFreeBytes)
                return L.T("Recording needs at least 512 MB of free space on the destination drive.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Network and virtual destinations may not expose a meaningful free-space figure.
        }
        return null;
    }
}
