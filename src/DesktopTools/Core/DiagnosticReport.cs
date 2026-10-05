using System.Text;
using System.Text.RegularExpressions;

namespace DesktopTools.Core;

/// <summary>
/// A plain-text summary of this installation for a bug report. Nothing is sent anywhere: the Diagnostics page copies it to the
/// clipboard so the user can read it before pasting. Personal details are replaced before the text leaves the app.
/// </summary>
public static class DiagnosticReport
{
    public sealed record Section(string Title, IReadOnlyList<string> Lines);

    public static string Build(string appVersion, IEnumerable<Section> sections, DateTime generatedUtc, string? userName, string? machineName, string? userProfile)
    {
        var text = new StringBuilder();
        text.AppendLine($"DesktopTools {appVersion} diagnostic report");
        text.AppendLine($"Created {generatedUtc:yyyy-MM-dd HH:mm} UTC");
        text.AppendLine("User name, computer name, file paths, e-mail addresses and IP addresses are replaced.");
        foreach (var section in sections)
        {
            text.AppendLine().AppendLine("## " + section.Title);
            foreach (var line in section.Lines) text.AppendLine("- " + line);
        }
        return Scrub(text.ToString(), userName, machineName, userProfile);
    }

    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);
    // Candidate IPv6 text; only replaced when it has "::" or a hex letter, so clock times such as 12:30:15 survive.
    private static readonly Regex Ipv6 = new(@"(?i)(?<![\w:])(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}(?![\w:])", RegexOptions.Compiled);
    private static readonly Regex UserFolder = new(@"(?i)\b([A-Z]:\\Users\\)[^\\/\s""'<>|]+", RegexOptions.Compiled);
    private static readonly Regex Unc = new(@"\\\\[^\\\s""'<>|]+(?=\\)", RegexOptions.Compiled);

    /// <summary>Replaces details that identify a person or a network. Applies to the whole text, so every caller gets the same protection.</summary>
    public static string Scrub(string text, string? userName, string? machineName, string? userProfile)
    {
        if (!string.IsNullOrWhiteSpace(userProfile)) text = Replace(text, userProfile!.TrimEnd('\\', '/'), @"%USERPROFILE%");
        text = UserFolder.Replace(text, "$1<user>");
        text = Unc.Replace(text, @"\\<server>");
        if (!string.IsNullOrWhiteSpace(machineName) && machineName!.Length >= 3) text = Replace(text, machineName, "<pc>");
        if (!string.IsNullOrWhiteSpace(userName) && userName!.Length >= 3) text = Replace(text, userName, "<user>");
        text = Email.Replace(text, "<email>");
        text = Ipv4.Replace(text, "<ip>");
        return Ipv6.Replace(text, m => m.Value.Contains("::") || m.Value.Any(c => char.IsLetter(c)) ? "<ip>" : m.Value);
    }

    private static string Replace(string text, string value, string with) => Regex.Replace(text, Regex.Escape(value), with.Replace("$", "$$"), RegexOptions.IgnoreCase);
}
