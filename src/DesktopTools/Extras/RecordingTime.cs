namespace DesktopTools.Extras;

internal static class RecordingTime
{
    internal static string Format(TimeSpan elapsed, bool compact = false) => compact && elapsed.TotalHours < 1
        ? elapsed.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
        : FormattableString.Invariant($"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
}
