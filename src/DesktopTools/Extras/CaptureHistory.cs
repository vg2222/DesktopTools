using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

/// <summary>
/// One recent screenshot. The full picture is kept PNG-compressed and decoded only when it is opened;
/// lists show the small <see cref="Thumbnail"/>, so idle memory does not grow by a raw bitmap per capture.
/// </summary>
public sealed class CaptureHistoryEntry
{
    private readonly object gate = new();
    private BitmapSource? raw;
    private byte[]? png;
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public BitmapSource Thumbnail { get; }
    internal long DecodedBytes { get; }
    internal CaptureHistoryEntry(BitmapSource frozen, long decodedBytes)
    {
        raw = frozen; PixelWidth = frozen.PixelWidth; PixelHeight = frozen.PixelHeight; DecodedBytes = decodedBytes;
        Thumbnail = MakeThumbnail(frozen);
    }
    /// <summary>The full screenshot. Decoded from compressed storage on each call; callers keep it only while they use it.</summary>
    public BitmapSource Image
    {
        get
        {
            byte[]? compressed;
            lock (gate) { if (raw != null) return raw; compressed = png; }
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = new MemoryStream(compressed!, writable: false); image.EndInit();
            image.Freeze();
            return image;
        }
    }
    /// <summary>Replaces the raw pixels with a PNG copy. Runs off the UI thread; the raw bitmap stays in use if encoding fails.</summary>
    internal void Compress()
    {
        BitmapSource? source;
        lock (gate) source = raw;
        if (source == null) return;
        try
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); encoder.Save(stream);
            lock (gate) { png = stream.ToArray(); raw = null; }
        }
        catch (Exception) { }
    }
    private static BitmapSource MakeThumbnail(BitmapSource source)
    {
        const double maxWidth = 360, maxHeight = 200;
        double scale = Math.Min(1, Math.Min(maxWidth / source.PixelWidth, maxHeight / source.PixelHeight));
        if (scale >= 1) return source;
        var thumbnail = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        // Copy into its own small buffer so the thumbnail does not keep the full bitmap alive.
        var copy = new WriteableBitmap(thumbnail);
        copy.Freeze();
        return copy;
    }
}

/// <summary>Session-only history. The byte budget counts decoded pixels, not compressed PNG size.</summary>
public sealed class CaptureHistory
{
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly List<CaptureHistoryEntry> _entries = [];
    private readonly IReadOnlyList<CaptureHistoryEntry> _view;
    private long _bytes;
    public IReadOnlyList<CaptureHistoryEntry> Entries => _view;
    /// <summary>When false (tests), compression runs inline instead of on the thread pool.</summary>
    internal bool CompressInBackground { get; init; } = true;
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
            _bytes -= _entries[^1].DecodedBytes; _entries.RemoveAt(_entries.Count - 1);
        }
        var entry = new CaptureHistoryEntry(retained, size);
        _entries.Insert(0, entry); _bytes += size;
        if (CompressInBackground) _ = Task.Run(entry.Compress);
        else entry.Compress();
    }
    public void Clear() { _entries.Clear(); _bytes = 0; }
    private static long Size(BitmapSource image) => checked((((long)image.PixelWidth * Math.Max(32, image.Format.BitsPerPixel) + 7) / 8) * image.PixelHeight);
}
