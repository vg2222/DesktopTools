using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using WinRT;

namespace DesktopTools.Native;

internal static class SoftwareBitmapFactory
{
    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out nint buffer, out uint capacity);
    }

    // Copies into a single owned native bitmap. Its lock/reference never escape,
    // and the caller must dispose the returned bitmap after the OCR reading.
    internal static SoftwareBitmap Create(BitmapSource source)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, source.PixelWidth, source.PixelHeight, BitmapAlphaMode.Ignore);
        try
        {
            using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Write);
            using var reference = buffer.CreateReference();
            reference.As<IMemoryBufferByteAccess>().GetBuffer(out nint pointer, out uint capacity);
            var plane = buffer.GetPlaneDescription(0);
            int rowBytes = checked(source.PixelWidth * 4);
            if (pointer == 0 || plane.StartIndex < 0 || plane.Stride < rowBytes || plane.Width < source.PixelWidth || plane.Height < source.PixelHeight ||
                (long)plane.StartIndex + (long)plane.Stride * (source.PixelHeight - 1) + rowBytes > capacity || capacity > int.MaxValue)
                throw new InvalidOperationException("Invalid native bitmap buffer layout.");
            BitmapSource converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(Int32Rect.Empty, nint.Add(pointer, plane.StartIndex), checked((int)capacity - plane.StartIndex), plane.Stride);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}