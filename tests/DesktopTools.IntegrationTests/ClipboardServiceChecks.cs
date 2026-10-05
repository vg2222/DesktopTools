using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using DesktopTools.Native;

/// <summary>Copying text while another program is using the clipboard (--clipboard-only). The user's clipboard text is put back afterwards.</summary>
internal static class ClipboardServiceChecks
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    /// <summary>Reads the clipboard text; the framework reader competes with clipboard managers too, so ask again for up to a second.</summary>
    private static string ReadText(string expected)
    {
        string text = "";
        for (int i = 0; i < 20; i++)
        {
            try { text = Clipboard.GetText(); } catch (Exception) { text = ""; }
            if (text == expected) break;
            Thread.Sleep(50);
        }
        return text;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    /// <summary>Another thread opens the clipboard for <paramref name="holdMs"/> milliseconds, the way a clipboard manager does.</summary>
    private static Task Hold(IntPtr owner, int holdMs)
    {
        var opened = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            for (int i = 0; i < 200 && !OpenClipboard(owner); i++) Thread.Sleep(5);
            opened.TrySetResult();
            Thread.Sleep(holdMs); CloseClipboard();
        }) { IsBackground = true };
        thread.Start();
        return opened.Task;
    }

    public static async Task RunAsync()
    {
        string? saved = null;
        if (Clipboard.ContainsText()) saved = Clipboard.GetText();
        else if (Clipboard.GetDataObject()?.GetFormats().Length is > 0) { Console.WriteLine("SKIP clipboard checks: the clipboard holds data other than text and is preserved"); return; }
        using var holder = new HwndSource(new HwndSourceParameters("ClipboardHolder") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        try
        {
            // Plain copy keeps every character: new lines, Cyrillic, symbols outside the basic plane.
            string sample = "Первая строка\r\nSecond line — 日本語 😀 \"quotes\"";
            var plain = await ClipboardService.SetTextAsync(sample);
            Check(plain.Success && ReadText(sample) == sample, "Text did not round-trip through the clipboard: " + plain.Error);

            // A clipboard manager holds the clipboard for a moment: copying waits for it instead of failing.
            await Hold(holder.Handle, 450);
            var watch = Stopwatch.StartNew();
            var waited = await ClipboardService.SetTextAsync("after the wait");
            Check(waited.Success && watch.ElapsedMilliseconds >= 300 && ReadText("after the wait") == "after the wait", $"Copy did not wait for a busy clipboard: success={waited.Success}, {watch.ElapsedMilliseconds} ms, {waited.Error}");

            // Held much longer than we are willing to wait: a clear message that names the program, and the old content is untouched.
            await Task.Delay(100);
            await Hold(holder.Handle, 1500);
            var refused = await ClipboardService.SetTextAsync("never written", timeoutMs: 300);
            string me = Process.GetCurrentProcess().ProcessName;
            Check(!refused.Success && refused.Error.Contains(me, StringComparison.OrdinalIgnoreCase), "A long-held clipboard should fail with a message naming the program: '" + refused.Error + "'");
            await Task.Delay(1600);
            Check(ReadText("after the wait") == "after the wait", "A failed copy changed the clipboard");

            // Back to back copies: no failures and the last one wins.
            for (int i = 0; i < 120; i++)
            {
                var result = await ClipboardService.SetTextAsync("copy " + i);
                Check(result.Success, $"Copy {i} failed: {result.Error}");
            }
            Check(ReadText("copy 119") == "copy 119", "The last of many quick copies did not win");

            // A picture: bitmap formats for ordinary programs, PNG for the ones that keep transparency; exact pixels, and the same patience.
            var picture = new System.Windows.Media.Imaging.WriteableBitmap(37, 21, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            var pixels = new byte[37 * 21 * 4];
            for (int y = 0; y < 21; y++) for (int x = 0; x < 37; x++) { int i = (y * 37 + x) * 4; pixels[i] = (byte)(x * 6); pixels[i + 1] = (byte)(y * 12); pixels[i + 2] = (byte)((x + y) * 4); pixels[i + 3] = 255; }
            picture.WritePixels(new Int32Rect(0, 0, 37, 21), pixels, 37 * 4, 0); picture.Freeze();
            await Hold(holder.Handle, 300);
            var imageResult = await ClipboardService.SetImageAsync(picture);
            Check(imageResult.Success, "Image copy failed: " + imageResult.Error);
            Check(Clipboard.ContainsImage(), "The clipboard has no image after copying one");
            var back = Clipboard.GetImage()!; var backPixels = new byte[37 * 21 * 4];
            new System.Windows.Media.Imaging.FormatConvertedBitmap(back, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(backPixels, 37 * 4, 0);
            Check(back.PixelWidth == 37 && back.PixelHeight == 21, $"Image size changed: {back.PixelWidth}x{back.PixelHeight}");
            int wrong = 0; for (int i = 0; i < pixels.Length; i += 4) if (Math.Abs(pixels[i] - backPixels[i]) > 1 || Math.Abs(pixels[i + 1] - backPixels[i + 1]) > 1 || Math.Abs(pixels[i + 2] - backPixels[i + 2]) > 1) wrong++;
            Check(wrong == 0, $"{wrong} pixels differ after the clipboard round trip (rows upside down or channels swapped?)");
            Check(Clipboard.GetData("PNG") is System.IO.MemoryStream { Length: > 50 }, "The PNG format is missing from the clipboard");

            // The synchronous variant (used by older windows) behaves the same way.
            var sync = ClipboardService.SetText("sync copy");
            Check(sync.Success && ReadText("sync copy") == "sync copy", $"Synchronous copy failed: success={sync.Success} error={sync.Error} text='{Clipboard.GetText()}'");
        }
        finally { if (saved != null) { try { Clipboard.SetText(saved); } catch (Exception) { } } else { try { Clipboard.Clear(); } catch (Exception) { } } }
        Console.WriteLine("PASS Clipboard: text and picture round trips, waiting for a busy clipboard, clear refusal naming the holder, quick copies");
    }
}
