using System.IO;
using DesktopTools.Extras;
using System.Windows.Media;
using System.Windows.Media.Imaging;
internal static class Program
{
    [STAThread] static void Main()
    {
        var input = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, 8);
        byte[] Pixels(BitmapSource source) { var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var bytes = new byte[source.PixelWidth * source.PixelHeight * 4]; converted.CopyPixels(bytes, source.PixelWidth * 4, 0); return bytes; }
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
        var crop = ImageTransforms.Crop(input, new System.Windows.Int32Rect(1,0,1,1)); Check(Pixels(crop)[0] == 255, "Crop pixel location");
        var history = new ImageEditHistory(input); history.Apply(crop); history.Undo(); Check(history.Current == input && history.CanRedo, "Undo crop"); history.Redo(); Check(history.Current == crop, "Redo crop"); history.Undo(); history.Apply(ImageTransforms.Rotate(input)); Check(!history.CanRedo, "Edit drops redo");
        try { ImageTransforms.Crop(input, new System.Windows.Int32Rect(1,0,2,1)); throw new Exception("Crop bounds missing"); } catch (ArgumentException) { }
        Check(ImageTransforms.Encode(input, "JPEG", 30).Length > 0, "JPEG quality encode");
        var noise = new byte[128*128*4]; new Random(73).NextBytes(noise); for (int i=3;i<noise.Length;i+=4) noise[i]=255;
        var detail = BitmapSource.Create(128,128,96,96,PixelFormats.Bgra32,null,noise,128*4);
        Check(ImageTransforms.Encode(detail,"JPEG",20).Length < ImageTransforms.Encode(detail,"JPEG",95).Length, "JPEG quality changes encoded size");
        var highDepth = new WriteableBitmap(2500, 2000, 96, 96, PixelFormats.Rgba64, null); highDepth.Freeze();
        var highDepthHistory = new ImageEditHistory(highDepth); for (int i = 0; i < 4; i++) highDepthHistory.Apply(highDepth);
        int highDepthUndos = 0; while (highDepthHistory.CanUndo) { highDepthHistory.Undo(); highDepthUndos++; } Check(highDepthUndos <= 3, "History byte bound accounts for high-bit-depth pixels");
        for (int i=0;i<30;i++) history.Apply(input); int undoCount=0; while(history.CanUndo) { history.Undo(); undoCount++; } Check(undoCount<=20,"History operation bound");
        var mirror = Pixels(ImageTransforms.Mirror(input, true)); Check(mirror[0] == 255 && mirror[6] == 255, "Horizontal mirror swaps red and blue");
        var rotated = ImageTransforms.Rotate(input); Check(rotated.PixelWidth == 1 && rotated.PixelHeight == 2 && Pixels(rotated)[2] == 255, "Clockwise rotation preserves pixel order");
        var vertical = Pixels(ImageTransforms.Mirror(rotated, false)); Check(vertical[0] == 255, "Vertical mirror reverses rows");
        var resized = ImageTransforms.Resize(input, 4, 2); Check(resized.PixelWidth == 4 && resized.PixelHeight == 2, "Exact resize dimensions");
        var highDpi = BitmapSource.Create(2, 1, 144, 120, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, 8);
        var highDpiRotated = ImageTransforms.Rotate(highDpi); Check(highDpiRotated.DpiX == 120 && highDpiRotated.DpiY == 144, "Quarter rotation swaps DPI axes");
        var highDpiResize = ImageTransforms.Resize(highDpi, 4, 2); Check(highDpiResize.DpiX == 144 && highDpiResize.DpiY == 120, "Resize preserves source DPI");
        Check(Pixels(highDpiResize)[(4 * 2 - 1) * 4 + 3] == 255, "High-DPI resize fills the requested pixel bounds");
        byte[] cornerPixels = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255];
        var anisotropic = BitmapSource.Create(2, 2, 144, 120, PixelFormats.Bgra32, null, cornerPixels, 8);
        Check(Pixels(ImageTransforms.Resize(anisotropic, 2, 2)).SequenceEqual(cornerPixels), "Anisotropic-DPI resize preserves edge and corner pixels");
        Check(Pixels(ImageTransforms.Flatten(anisotropic)).SequenceEqual(cornerPixels), "Anisotropic-DPI JPEG flatten preserves edge and corner pixels");
        var transparent = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        Check(Pixels(ImageTransforms.Flatten(transparent)).All(b => b == 255), "JPEG flatten makes transparent pixels white");
        var highDpiFlatten = ImageTransforms.Flatten(highDpi); Check(highDpiFlatten.DpiX == 144 && highDpiFlatten.DpiY == 120, "JPEG flatten preserves source DPI");
        try { ImageTransforms.ValidateSize(12000, 12000); throw new Exception("Missing image size limit"); } catch (ArgumentException) { }
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(folder);
        try {
            var original = Path.Combine(folder, "original.png"); ImageTransforms.Export(input, original, Path.Combine(folder, "other.png"));
            var loaded = ImageTransforms.Load(original); File.Move(original, original + ".moved"); File.Move(original + ".moved", original);
            Check(Pixels(loaded).SequenceEqual(Pixels(input)), "Lossless PNG load/export and file handle release");
            try { ImageTransforms.Export(input, original, original); throw new Exception("Original overwrite allowed"); } catch (ArgumentException) { }
            foreach (var ext in new[] { "jpg", "bmp" }) { var path = Path.Combine(folder, "copy." + ext); ImageTransforms.Export(input, path, original); Check(ImageTransforms.Load(path).PixelWidth == 2, "Export " + ext); }
        } finally { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
        Console.WriteLine("PASS image transforms, dimensions, flatten, formats, unlocked load and original protection");
    }
}
