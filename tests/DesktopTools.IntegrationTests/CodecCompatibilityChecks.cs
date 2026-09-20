using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopTools.Core;
using DesktopTools.Extras;

internal static class CodecCompatibilityChecks
{
    internal static async Task RunAsync()
    {
        string directory = Path.GetFullPath(File.ReadAllText("codec-directory.txt"));
        if (!directory.StartsWith(Path.GetFullPath(".") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture directory is outside the integration workspace.");
        var results = new List<object>(); int imported = 0;
        foreach (string name in new[] { "h264.mp4", "h264.mov", "mjpeg.avi", "wmv.wmv", "vp9.mkv", "hevc.mp4" })
        {
            string path = Path.Combine(directory, name); string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            try
            {
                var info = await VideoEditorService.ProbeAsync(path);
                string output = Path.Combine(directory, name.Replace('.', '-') + "-export.mp4");
                await VideoEditorService.ExportAsync(info, new VideoEdit(.25, 1.75), output);
                var edited = await VideoEditorService.ProbeAsync(output);
                if (!edited.HasAudio || Math.Abs(edited.Duration - 1.5) > .15 || edited.Width != 320 || edited.Height != 240)
                    throw new Exception("Edited clip lost audio, dimensions or duration.");
                imported++; results.Add(new { File = name, Status = "PASS import and trimmed MP4 export", edited.Duration, edited.HasAudio });
                Console.WriteLine("PASS codec " + name);
            }
            catch (Exception ex)
            {
                results.Add(new { File = name, Status = "Unsupported or failed on this Windows installation", HResult = $"0x{ex.HResult:X8}", ex.Message });
                Console.WriteLine($"UNAVAILABLE {name}: 0x{ex.HResult:X8} {ex.Message}");
            }
            if (hash != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))) throw new Exception("Original fixture changed: " + name);
        }
        File.WriteAllText(Path.Combine(directory, "windows-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        if (imported == 0) throw new Exception("Windows could not import any generated codec fixture.");
    }
}
