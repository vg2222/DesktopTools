using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

public sealed record CaptureHistoryEntry(Guid Id, DateTimeOffset CreatedAt, BitmapSource Image);

/// <summary>Session-only history. The byte budget counts decoded pixels, not compressed PNG size.</summary>
public sealed class CaptureHistory
{
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly List<CaptureHistoryEntry> _entries = [];
    private readonly IReadOnlyList<CaptureHistoryEntry> _view;
    private long _bytes;
    public IReadOnlyList<CaptureHistoryEntry> Entries => _view;
    public CaptureHistory(int maxEntries = 8, long maxBytes = 64L * 1024 * 1024)
    {
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxEntries = maxEntries; _maxBytes = maxBytes; _view = _entries.AsReadOnly();
    }
    public void Add(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        long size = Size(image);
        // An oversized capture remains usable by its caller but is not retained here.
        if (size > _maxBytes) return;
        var retained = image.IsFrozen ? image : image.CloneCurrentValue();
        if (!retained.IsFrozen) retained.Freeze();
        while (_entries.Count > 0 && (_entries.Count >= _maxEntries || _bytes + size > _maxBytes))
        {
            _bytes -= Size(_entries[^1].Image); _entries.RemoveAt(_entries.Count - 1);
        }
        _entries.Insert(0, new(Guid.NewGuid(), DateTimeOffset.Now, retained)); _bytes += size;
    }
    public void Clear() { _entries.Clear(); _bytes = 0; }
    private static long Size(BitmapSource image) => checked((((long)image.PixelWidth * Math.Max(32, image.Format.BitsPerPixel) + 7) / 8) * image.PixelHeight);
}
