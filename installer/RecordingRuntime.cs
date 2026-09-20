using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTools.Installer;

internal static class RecordingRuntime
{
    // Open Microsoft's page so the user chooses the x64 download and accepts
    // Microsoft's own installer terms. We do not redistribute their binaries.
    internal const string DownloadPage = "https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist";
    internal static bool IsAvailable() => IsAvailable(CanLoadSystemLibrary);
    internal static bool IsAvailable(Func<string, bool> canLoad) =>
        new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" }.All(canLoad);

    private static bool CanLoadSystemLibrary(string name)
    {
        if (!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, name), out nint handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }
}
