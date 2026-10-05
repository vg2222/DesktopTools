using System.Diagnostics;
using System.IO;

internal static class PackagingChecks
{
    public static void Run()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "scripts", "publish.ps1")))
            repository = repository.Parent;
        if (repository is null) throw new Exception("Cannot locate the package verification script");
        var fixtureRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopTools-package-checks"));
        var fixture = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fixture, "docs"));
        try
        {
            File.WriteAllText(Path.Combine(fixture, "README.md"), "[Offline languages](docs/offline-languages.md#packs)");
            File.WriteAllText(Path.Combine(fixture, "docs", "offline-languages.md"), "# Packs\n[README](../README.md)\n[Website](https://example.com)");
            Verify(true, "A package with complete relative documentation links was rejected");
            var temporary = Path.Combine(fixture, "unfinished.onnx.download");
            File.WriteAllText(temporary, "unfinished download");
            Verify(false, "An unfinished model download was accepted in the package");
            File.Delete(temporary);
            File.Delete(Path.Combine(fixture, "docs", "offline-languages.md"));
            Verify(false, "A broken relative documentation link was accepted in the package");
        }
        finally
        {
            if (!Path.GetFullPath(fixture).StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new Exception("Package fixture cleanup escaped its temporary directory");
            Directory.Delete(fixture, true);
        }
        Console.WriteLine("PASS package temporary-file and documentation-link checks");

        void Verify(bool expectedSuccess, string message)
        {
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(repository.FullName, "scripts", "verify-publish.ps1"), "-Directory", fixture })
                start.ArgumentList.Add(argument);
            Process launched;
            try { launched = Process.Start(start) ?? throw new Exception("Package verifier did not start"); }
            catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 2)
            {
                start.FileName = "powershell.exe";
                launched = Process.Start(start) ?? throw new Exception("Package verifier did not start");
            }
            using var process = launched;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) { process.Kill(true); throw new Exception("Package verifier timed out"); }
            if ((process.ExitCode == 0) != expectedSuccess)
                throw new Exception(message + Environment.NewLine + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }
    }
}
