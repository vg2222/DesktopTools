using DesktopTools.Localization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopTools.Native;

internal sealed record ClipboardResult(bool Success, string Error = "");

/// <summary>
/// Puts text on the clipboard in the shortest possible time and waits patiently when another program (a clipboard manager, Windows clipboard
/// history, a remote desktop) has it open. The framework's clipboard class holds the clipboard open while it flushes the data and gives up after
/// about a second with "Unable to open clipboard"; here the clipboard is opened only for the moment the text is written, retries back off for
/// a few seconds and a failure names the program that was in the way.
/// </summary>
internal static class ClipboardService
{
    private const uint UnicodeText = 13, MoveableMemory = 0x0002;
    private static HwndSource? owner;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr newOwner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);

    // SetClipboardData needs a clipboard owner window; a hidden message-only window serves every caller.
    private static IntPtr Owner() => (owner ??= new HwndSource(new HwndSourceParameters("DesktopToolsClipboardOwner") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 })).Handle;

    /// <summary>One attempt. Returns false with the Win32 error when the clipboard could not be opened or written.</summary>
    private static bool TryWrite(IReadOnlyList<(uint Format, byte[] Data)> formats, out int error)
    {
        error = 0;
        if (!OpenClipboard(Owner())) { error = Marshal.GetLastWin32Error(); return false; }
        try
        {
            if (!EmptyClipboard()) { error = Marshal.GetLastWin32Error(); return false; }
            foreach (var (format, data) in formats)
            {
                var memory = GlobalAlloc(MoveableMemory, (UIntPtr)data.Length);
                if (memory == IntPtr.Zero) { error = Marshal.GetLastWin32Error(); return false; }
                var target = GlobalLock(memory);
                if (target == IntPtr.Zero) { error = Marshal.GetLastWin32Error(); GlobalFree(memory); return false; }
                try { Marshal.Copy(data, 0, target, data.Length); } finally { GlobalUnlock(memory); }
                if (SetClipboardData(format, memory) == IntPtr.Zero) { error = Marshal.GetLastWin32Error(); GlobalFree(memory); return false; }   // on success the system owns the memory
            }
            return true;
        }
        finally { CloseClipboard(); }
    }

    private static IReadOnlyList<(uint, byte[])> TextFormats(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
        return [(UnicodeText, bytes)];
    }

    /// <summary>A bottom-up 32-bit DIB with an alpha channel (BITMAPV5HEADER) plus the PNG format, which is how screenshot tools put pictures on the clipboard.</summary>
    private static IReadOnlyList<(uint, byte[])> ImageFormats(System.Windows.Media.Imaging.BitmapSource image)
    {
        var straight = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int width = straight.PixelWidth, height = straight.PixelHeight, stride = width * 4;
        var top = new byte[stride * height]; straight.CopyPixels(top, stride, 0);
        var dib = new byte[124 + top.Length];
        using (var writer = new BinaryWriter(new MemoryStream(dib)))
        {
            writer.Write(124); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)32);
            writer.Write(3); writer.Write(top.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);          // BI_BITFIELDS, size, resolution, colour counts
            writer.Write(0x00FF0000); writer.Write(0x0000FF00); writer.Write(0x000000FF); writer.Write(unchecked((int)0xFF000000));   // red, green, blue, alpha masks
            writer.Write(0x73524742);                                                                                                 // 'sRGB'
            writer.Write(new byte[36 + 12]); writer.Write(4); writer.Write(0); writer.Write(0); writer.Write(0);                      // endpoints, gamma, intent, profile, reserved
        }
        for (int row = 0; row < height; row++) Buffer.BlockCopy(top, (height - 1 - row) * stride, dib, 124 + row * stride, stride);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var png = new MemoryStream(); encoder.Save(png);
        return [(PngFormat.Value, png.ToArray()), (DibV5, dib)];
    }
    private const uint DibV5 = 17;
    private static readonly Lazy<uint> PngFormat = new(() => RegisterClipboardFormat("PNG"));
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterClipboardFormat(string name);

    /// <summary>The program that has the clipboard open right now, if Windows can tell.</summary>
    private static string? Holder()
    {
        try
        {
            var window = GetOpenClipboardWindow();
            if (window == IntPtr.Zero) return null;
            GetWindowThreadProcessId(window, out uint processId);
            return processId == 0 ? null : Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception) { return null; }
    }

    private static ClipboardResult Busy(int error)
    {
        string? holder = Holder();
        return new ClipboardResult(false, holder != null ? L.F($"The clipboard is in use by {holder}. Try again in a moment.")
            : L.T("The clipboard is busy. Try again in a moment.") + (error != 0 ? " (" + new System.ComponentModel.Win32Exception(error).Message + ")" : ""));
    }

    private static async Task<ClipboardResult> WriteAsync(IReadOnlyList<(uint, byte[])> formats, int timeoutMs, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew(); int delay = 10, error;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryWrite(formats, out error)) return new ClipboardResult(true);
            if (clock.ElapsedMilliseconds >= timeoutMs) return Busy(error);
            await Task.Delay(delay, cancellationToken); delay = Math.Min(150, delay * 2);
        }
    }

    private static ClipboardResult Write(IReadOnlyList<(uint, byte[])> formats, int timeoutMs)
    {
        var clock = Stopwatch.StartNew(); int delay = 10, error;
        while (true)
        {
            if (TryWrite(formats, out error)) return new ClipboardResult(true);
            if (clock.ElapsedMilliseconds >= timeoutMs) return Busy(error);
            Thread.Sleep(delay); delay = Math.Min(100, delay * 2);
        }
    }

    public static Task<ClipboardResult> SetTextAsync(string text, int timeoutMs = 3000, CancellationToken cancellationToken = default) => WriteAsync(TextFormats(text), timeoutMs, cancellationToken);
    public static Task<ClipboardResult> SetImageAsync(System.Windows.Media.Imaging.BitmapSource image, int timeoutMs = 3000, CancellationToken cancellationToken = default) => WriteAsync(ImageFormats(image), timeoutMs, cancellationToken);

    /// <summary>For callers that cannot await. Waits at most <paramref name="timeoutMs"/> on the calling thread.</summary>
    public static ClipboardResult SetText(string text, int timeoutMs = 1200) => Write(TextFormats(text), timeoutMs);
    public static ClipboardResult SetImage(System.Windows.Media.Imaging.BitmapSource image, int timeoutMs = 1200) => Write(ImageFormats(image), timeoutMs);
}
