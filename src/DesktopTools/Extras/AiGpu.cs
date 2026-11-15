using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopTools.Extras;

/// <summary>
/// Graphics-card support for optional AI models through DirectML. DesktopTools does not ship DirectML: it uses the copy built into
/// Windows (System32), and offers the graphics card only when that copy is new enough for the bundled ONNX Runtime.
/// </summary>
internal static class AiGpu
{
    internal const string Processor = "Processor";
    internal const string GraphicsCard = "GraphicsCard";
    /// <summary>The DirectML version the bundled ONNX Runtime 1.24 build was made against.</summary>
    internal static readonly Version MinimumDirectML = new(1, 15, 0);
    private static readonly Lazy<Version?> systemVersion = new(ReadSystemVersion);
    private static readonly object gate = new();
    private static bool loaded;
    /// <summary>Test seam: overrides the detected Windows DirectML version (null = not present).</summary>
    internal static Func<Version?>? VersionOverride { get; set; }

    internal static string SystemDirectMLPath => Path.Combine(Environment.SystemDirectory, "DirectML.dll");
    internal static Version? SystemVersion => VersionOverride != null ? VersionOverride() : systemVersion.Value;
    /// <summary>True when Windows has a DirectML new enough to run models on the graphics card.</summary>
    internal static bool IsSupported => SystemVersion is { } version && version >= MinimumDirectML;

    private static Version? ReadSystemVersion()
    {
        try
        {
            if (!File.Exists(SystemDirectMLPath)) return null;
            var info = FileVersionInfo.GetVersionInfo(SystemDirectMLPath);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Loads Windows' DirectML before ONNX Runtime asks for it, so the runtime's delayed import binds to that exact file
    /// and never to a DirectML.dll found elsewhere on the search path.
    /// </summary>
    internal static bool EnsureLoaded()
    {
        lock (gate)
        {
            if (loaded) return true;
            if (!IsSupported) return false;
            loaded = NativeLibrary.TryLoad(SystemDirectMLPath, out _);
            return loaded;
        }
    }
}
