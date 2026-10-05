using DesktopTools.Core;

internal static class DiagnosticReportTests
{
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Diagnostic report assertion failed: " + what); }
        string scrubbed = DiagnosticReport.Scrub(@"Saved to C:\Users\vlad.g\Videos\clip.mp4 by vladg on DESKTOP-ABC from \\nas01\share\a.png at 192.168.1.20 / fe80::1:2:3:4, mail me@example.com",
            "vladg", "DESKTOP-ABC", @"C:\Users\vlad.g");
        Check(!scrubbed.Contains("vlad") && !scrubbed.Contains("DESKTOP-ABC") && !scrubbed.Contains("nas01") && !scrubbed.Contains("192.168") && !scrubbed.Contains("example.com") && !scrubbed.Contains("fe80"), "identifying text survived: " + scrubbed);
        Check(scrubbed.Contains(@"%USERPROFILE%\Videos\clip.mp4"), "profile path should shorten to a variable");
        string other = DiagnosticReport.Scrub(@"D:\backup restored from C:\Users\someone-else\Desktop\x.png", "vladg", "PC", @"C:\Users\vladg");
        Check(other.Contains(@"C:\Users\<user>\Desktop\x.png") && other.Contains(@"D:\backup"), "other user folders are hidden but ordinary paths stay readable: " + other);
        Check(DiagnosticReport.Scrub("version 1.2.7 build 26300", "vladg", "PC", null) == "version 1.2.7 build 26300", "version numbers are not IP addresses");
        Check(DiagnosticReport.Scrub("keep it", "ab", "x", null) == "keep it", "very short names are not replaced inside words");
        string report = DiagnosticReport.Build("1.2.7", new[] { new DiagnosticReport.Section("System", new[] { "Windows 11 on vladg's PC" }) }, new DateTime(2026, 10, 4, 12, 30, 0, DateTimeKind.Utc), "vladg", "PC-1", null);
        Check(report.StartsWith("DesktopTools 1.2.7 diagnostic report") && report.Contains("## System") && report.Contains("- Windows 11 on <user>'s PC") && report.Contains("2026-10-04 12:30 UTC"), "report layout: " + report);
    }
}
