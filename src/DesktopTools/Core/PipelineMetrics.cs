using System.Diagnostics;

namespace DesktopTools.Core;

// Explicit benchmark scope only: timings contain no image, OCR text or user paths.
// No observer is installed by the application and no measurements are persisted here.
internal static class PipelineMetrics
{
    internal readonly record struct Sample(string Stage, TimeSpan Elapsed);
    private static readonly AsyncLocal<Action<Sample>?> Observer = new();
    internal static IDisposable Capture(Action<Sample> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var previous = Observer.Value; Observer.Value = observer;
        return new Lease(previous);
    }
    private sealed class Lease(Action<Sample>? previous) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; Observer.Value = previous; }
    }
    internal static Timing Measure(string stage) => new(stage, Observer.Value);
    internal readonly struct Timing : IDisposable
    {
        private readonly string stage;
        private readonly Action<Sample>? observer;
        private readonly long started;
        internal Timing(string stage, Action<Sample>? observer)
        { this.stage = stage; this.observer = observer; started = observer != null ? Stopwatch.GetTimestamp() : 0; }
        public void Dispose() { if (observer != null) observer(new(stage, Stopwatch.GetElapsedTime(started))); }
    }
}