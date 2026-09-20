using System.Windows.Media.Imaging;
namespace DesktopTools.Extras;
internal sealed class ImageEditHistory
{
    private readonly List<BitmapSource> undo = new(), redo = new();
    internal BitmapSource Current { get; private set; }
    internal bool CanUndo => undo.Count > 0;
    internal bool CanRedo => redo.Count > 0;
    internal ImageEditHistory(BitmapSource image) => Current = image;
    private static long ByteSize(BitmapSource image)
    {
        int bitsPerPixel = Math.Max(1, image.Format.BitsPerPixel);
        long stride = ((long)image.PixelWidth * bitsPerPixel + 31) / 32 * 4;
        return stride * image.PixelHeight;
    }
    private static void Bound(List<BitmapSource> list)
    {
        long bytes = list.Sum(ByteSize);
        while (list.Count > 0 && (list.Count > 20 || bytes > 128_000_000)) { bytes -= ByteSize(list[0]); list.RemoveAt(0); }
    }
    internal void Apply(BitmapSource image) { undo.Add(Current); Bound(undo); redo.Clear(); Current = image; }
    internal void Undo() { if (!CanUndo) return; redo.Add(Current); Bound(redo); Current = undo[^1]; undo.RemoveAt(undo.Count-1); }
    internal void Redo() { if (!CanRedo) return; undo.Add(Current); Bound(undo); Current = redo[^1]; redo.RemoveAt(redo.Count-1); }
}
