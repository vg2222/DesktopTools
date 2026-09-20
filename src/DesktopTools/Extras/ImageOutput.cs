using DesktopTools.Localization;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace DesktopTools.Extras;

internal static class ImageOutput
{
    internal static void Copy(BitmapSource image, Action<string> report)
    {
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
            var data = new DataObject(); data.SetImage(image); data.SetData("PNG", stream);
            Clipboard.SetDataObject(data, true);
            report(L.T("Image copied to the clipboard."));
        }
        catch (Exception ex) { report(L.T("Could not copy the image. Try again: ") + ex.Message); }
    }

    internal static bool Save(Window owner, BitmapSource image, Action<string> report)
    {
        try
        {
            var dialog = new SaveFileDialog { Filter = L.T("PNG image (*.png)|*.png"), DefaultExt = ".png", AddExtension = true, FileName = $"DesktopTools-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
            if (dialog.ShowDialog(owner) != true) return false;
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write); encoder.Save(stream);
            report(L.T("PNG saved.")); return true;
        }
        catch (Exception ex) { report(L.T("Could not save the image: ") + ex.Message); return false; }
    }
}
