using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;

internal static class QrChecks
{
    public static void Run()
    {
        foreach (var text in new[] { "https://example.com/path?q=one", "Привет 🌍\nこんにちは", "first line\nsecond line" })
        {
            var bytes = QrCodeWindow.CreatePng(text);
            using var stream = new System.IO.MemoryStream(bytes);
            var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoded.PixelWidth != decoded.PixelHeight || decoded.PixelWidth < 232) throw new Exception("QR image must be square and retain quiet zones.");
            var rgba = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[rgba.PixelWidth * rgba.PixelHeight * 4]; rgba.CopyPixels(pixels, rgba.PixelWidth * 4, 0);
            for (var y = 0; y < rgba.PixelHeight; y++)
            for (var x = 0; x < rgba.PixelWidth; x++)
            {
                if (x >= 32 && x < rgba.PixelWidth - 32 && y >= 32 && y < rgba.PixelHeight - 32) continue;
                var i = (y * rgba.PixelWidth + x) * 4;
                if (pixels[i] != 255 || pixels[i + 1] != 255 || pixels[i + 2] != 255 || pixels[i + 3] != 255) throw new Exception("QR quiet zone must stay opaque white for four modules.");
            }
        }
        foreach (int size in new[] { 256, 512, 1024, 2048 })
        {
            using var stream = new System.IO.MemoryStream(QrCodeWindow.CreatePng("Размер QR • Unicode", size));
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (frame.PixelWidth != size || frame.PixelHeight != size) throw new Exception("Requested QR pixel size not preserved");
        }
        foreach (var invalid in new[] { "", "  ", new string('x', 8000) })
        {
            try { QrCodeWindow.CreatePng(invalid); throw new Exception("Expected rejected QR input."); }
            catch (ArgumentException) { }
        }
        try { QrCodeWindow.CreatePng(new string('x', 3000)); throw new Exception("Expected QR capacity rejection."); }
        catch (QRCoder.Exceptions.DataTooLongException) { }
        Console.WriteLine("PASS QR PNG generation, Unicode, multiline, quiet zones and invalid input");
    }
}
