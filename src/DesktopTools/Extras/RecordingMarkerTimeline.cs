namespace DesktopTools.Extras;

internal sealed record RecordingMarker(int Number, TimeSpan Position);

internal sealed class RecordingMarkerTimeline
{
    private readonly List<RecordingMarker> markers = new();
    internal IReadOnlyList<RecordingMarker> Markers => markers;

    internal bool Add(TimeSpan elapsed, bool recording)
    {
        if (!recording || elapsed < TimeSpan.Zero || markers.Count >= 100) return false;
        markers.Add(new RecordingMarker(markers.Count + 1, elapsed));
        return true;
    }

    internal void Clear() => markers.Clear();
}
