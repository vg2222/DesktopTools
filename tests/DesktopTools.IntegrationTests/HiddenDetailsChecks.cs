using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;

/// <summary>
/// Hidden file details (location, device, date, author): what today's exports keep, and the opt-in removal.
/// Every fixture is generated here in a temp folder; no photo or screenshot is committed.
/// </summary>
internal static class HiddenDetailsChecks
{
    internal static readonly string[] Formats = ["JPEG", "PNG", "TIFF"];
    private static string? folder;
    private static string Folder => folder ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "DesktopTools-hidden-details-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>A small generated image with GPS position, camera make and model, date taken, author and a comment. Returns its path.</summary>
    internal static string Fixture(string format)
    {
        var pixels = new byte[32 * 24 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i % 251); pixels[i + 1] = (byte)(i * 7 % 253); pixels[i + 2] = (byte)(i * 13 % 255); pixels[i + 3] = 255; }
        var image = BitmapSource.Create(32, 24, 72, 72, PixelFormats.Bgra32, null, pixels, 32 * 4);
        BitmapEncoder encoder; BitmapMetadata metadata; string extension;
        switch (format)
        {
            case "JPEG":
                encoder = new JpegBitmapEncoder { QualityLevel = 95 }; extension = ".jpg";
                metadata = new BitmapMetadata("jpg");
                metadata.SetQuery("/app1/ifd/{ushort=271}", "Contoso");
                metadata.SetQuery("/app1/ifd/{ushort=272}", "Phone X");
                metadata.SetQuery("/app1/ifd/{ushort=315}", "Anna Example");
                metadata.SetQuery("/app1/ifd/exif/{ushort=36867}", "2026:11:11 09:05:07");
                metadata.SetQuery("/app1/ifd/gps/{ushort=1}", "N");
                metadata.SetQuery("/app1/ifd/gps/{ushort=2}", new ulong[] { Rational(48, 1), Rational(8, 1), Rational(1234, 100) });
                metadata.Comment = "private comment";
                image = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
                break;
            case "TIFF":
                encoder = new TiffBitmapEncoder(); extension = ".tif";
                metadata = new BitmapMetadata("tiff");
                metadata.SetQuery("/ifd/{ushort=271}", "Contoso");
                metadata.SetQuery("/ifd/{ushort=272}", "Phone X");
                metadata.SetQuery("/ifd/{ushort=315}", "Anna Example");
                metadata.SetQuery("/ifd/{ushort=306}", "2026:11:11 09:05:07");
                metadata.SetQuery("/ifd/{ushort=270}", "private comment");
                metadata.SetQuery("/ifd/gps/{ushort=1}", "N");
                metadata.SetQuery("/ifd/gps/{ushort=2}", new ulong[] { Rational(48, 1), Rational(8, 1), Rational(1234, 100) });
                break;
            default:
                encoder = new PngBitmapEncoder(); extension = ".png";
                metadata = new BitmapMetadata("png");
                metadata.SetQuery("/[0]tEXt/{str=Author}", "Anna Example");
                metadata.SetQuery("/[1]tEXt/{str=Software}", "Phone X camera");
                metadata.SetQuery("/[2]tEXt/{str=Creation Time}", "2026-11-11 09:05:07");
                metadata.SetQuery("/[3]tEXt/{str=Comment}", "private comment");
                metadata.SetQuery("/[4]tEXt/{str=GPSPosition}", "48.1, 8.2");
                break;
        }
        encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
        string path = Path.Combine(Folder, "fixture-" + format.ToLowerInvariant() + extension);
        using (var stream = File.Create(path)) encoder.Save(stream);
        return path;
    }

    private static ulong Rational(uint numerator, uint denominator) => ((ulong)denominator << 32) | numerator;

    /// <summary>Task 4: prints which details survive each of today's export paths. It measures; it does not assert.</summary>
    internal static async Task RunAsync(Action<string> log)
    {
        try
        {
            await MeasureAsync(log);
            DescribeAndStrip(log);
            await ExportDialogAsync(log);
            await PanelAsync(log);
        }
        finally
        {
            try { if (folder != null) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            folder = null;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().SingleOrDefault(e => e.Name == name) ?? throw new Exception($"Hidden details: no {typeof(T).Name} named {name}");

    private static string ExpectedSummary(params string[] categories) =>
        L.F($"This file contains hidden details: {string.Join(", ", categories.Select(L.T))}.");

    /// <summary>The text is fully visible: not cut vertically, not off the window, and above <paramref name="limitBottom"/> when given.</summary>
    private static void NotClipped(Window window, FrameworkElement element, string what, double? limitBottom = null)
    {
        window.UpdateLayout();
        Check(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0, what + " is not shown");
        var margin = element.Margin;
        element.Measure(new Size(element.ActualWidth + margin.Left + margin.Right, double.PositiveInfinity));
        double needed = element.DesiredSize.Height - margin.Top - margin.Bottom;
        Check(needed <= element.ActualHeight + 1, $"{what} is cut vertically ({needed:0} > {element.ActualHeight:0})");
        var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        Check(bounds.Right <= window.ActualWidth + 0.5 && bounds.Left >= -0.5, $"{what} runs off the window");
        if (limitBottom is { } bottom) Check(bounds.Bottom <= bottom + 0.5, $"{what} overlaps the buttons ({bounds.Bottom:0} > {bottom:0})");
    }

    private static void Render(Window window, string name)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create("hidden-details-" + name + ".png"); encoder.Save(stream);
    }

    /// <summary>Task 6: the export dialog's checkbox, summary, both export results and the untouched source, in every language and theme.</summary>
    private static async Task ExportDialogAsync(Action<string> log)
    {
        using var controller = new AppController(true);
        string png = Fixture("PNG"); byte[] before = File.ReadAllBytes(png); var written = File.GetLastWriteTimeUtc(png);
        var loaded = ImageTransforms.Load(png);
        try
        {
            foreach (string theme in new[] { "Light", "Dark" })
                foreach (string language in L.Languages)
                {
                    controller.Settings.Theme = theme; controller.ApplyTheme(); L.Use(language);
                    var dialog = new ImageExportDialog(loaded, png, "PNG", choosePath: _ => null); dialog.Show(); await Task.Delay(40); dialog.UpdateLayout();
                    var box = Named<CheckBox>(dialog, "RemoveHiddenDetails"); var summary = Named<TextBlock>(dialog, "HiddenDetailsSummary");
                    Check(box.IsChecked == false && box.IsEnabled, $"{language}/{theme}: the checkbox does not open unticked and available");
                    Check(summary.Text == ExpectedSummary("location", "device", "date", "author", "other"), $"{language}/{theme}: summary reads '{summary.Text}'");
                    var save = Named<Button>(dialog, "SaveCopy");
                    double buttonsTop = save.TransformToAncestor(dialog).TransformBounds(new Rect(save.RenderSize)).Top;
                    NotClipped(dialog, summary, $"{language}/{theme} summary", buttonsTop);
                    NotClipped(dialog, Named<TextBlock>(dialog, "RemoveHiddenDetailsLabel"), $"{language}/{theme} checkbox label", buttonsTop);
                    if (language is "en" or "de" or "ja") Render(dialog, $"dialog-{language}-{theme}");
                    dialog.Close();
                }
        }
        finally { L.Use("en"); controller.Settings.Theme = "Light"; controller.ApplyTheme(); }

        // No details: say so and disable the box. Unreadable: say so and keep the box available.
        string plain = Path.Combine(Folder, "plain.png");
        var plainDialog = new ImageExportDialog(loaded, plain, "PNG", choosePath: _ => null); plainDialog.Show(); await Task.Delay(30);
        Check(Named<TextBlock>(plainDialog, "HiddenDetailsSummary").Text == L.T("This file has no hidden details.") && !Named<CheckBox>(plainDialog, "RemoveHiddenDetails").IsEnabled,
            "a file without details is not described as such, or the box is available");
        plainDialog.Close();
        string garbage = Path.Combine(Folder, "garbage.png");
        var unreadable = new ImageExportDialog(loaded, garbage, "PNG", choosePath: _ => null); unreadable.Show(); await Task.Delay(30);
        Check(Named<TextBlock>(unreadable, "HiddenDetailsSummary").Text == L.T("Hidden details could not be read. Tick the box to save a copy without any.")
            && Named<CheckBox>(unreadable, "RemoveHiddenDetails").IsEnabled, "unreadable details are not reported, or the box is unavailable");
        unreadable.Close();

        // Unticked: exactly today's behaviour (Task 4: an unedited PNG keeps everything). Ticked: nothing.
        foreach (bool remove in new[] { false, true })
        {
            string output = Path.Combine(Folder, remove ? "export-ticked.png" : "export-unticked.png");
            var dialog = new ImageExportDialog(loaded, png, "PNG", choosePath: _ => output); dialog.Show(); await Task.Delay(30);
            Named<CheckBox>(dialog, "RemoveHiddenDetails").IsChecked = remove;
            var closed = new TaskCompletionSource(); dialog.Closed += (_, _) => closed.TrySetResult();
            Named<Button>(dialog, "SaveCopy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (await Task.WhenAny(closed.Task, Task.Delay(10000)) != closed.Task)
                throw new Exception($"Hidden details: export ({(remove ? "ticked" : "unticked")}) did not finish: " + string.Join(" | ", Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).TakeLast(4)));
            Check(dialog.SavedPath == output && dialog.RemoveHiddenDetails == remove, "export did not save, or did not report the choice");
            string survived = Survivors(output);
            Check(survived == (remove ? "none" : "location, device, date, author, comment"), $"{(remove ? "ticked" : "unticked")} export keeps {survived}");
            if (remove) Check(!ImageHiddenDetails.Describe(output).Any, "the ticked export still describes details");
            var copy = ImageTransforms.Load(output);
            Check(copy.PixelWidth == loaded.PixelWidth && copy.DpiX == loaded.DpiX, "the exported copy changed size or DPI");
        }
        var reopened = new ImageExportDialog(loaded, png, "PNG", choosePath: _ => null); reopened.Show(); await Task.Delay(30);
        Check(Named<CheckBox>(reopened, "RemoveHiddenDetails").IsChecked == false, "the box stayed ticked in a new dialog");
        reopened.Close();
        var carried = new ImageExportDialog(loaded, png, "PNG", choosePath: _ => null, removeHiddenDetails: true); carried.Show(); await Task.Delay(30);
        Check(Named<CheckBox>(carried, "RemoveHiddenDetails").IsChecked == true, "a tick from the Hide private data panel did not reach the export dialog");
        carried.Close();
        Check(File.ReadAllBytes(png).AsSpan().SequenceEqual(before) && File.GetLastWriteTimeUtc(png) == written, "the source file changed");
        log("PASS export dialog: summary, unticked = today, ticked = none, source untouched, 12 languages x 2 themes");
    }

    /// <summary>Task 6: the Hide private data panel in Image tools (an imported file) and in the screenshot editor (no source file).</summary>
    private static async Task PanelAsync(Action<string> log)
    {
        using var controller = new AppController(true);
        string png = Fixture("PNG"); byte[] before = File.ReadAllBytes(png);
        var loaded = ImageTransforms.Load(png);
        var tools = new ImageToolsWindow(_ => { }) { Width = 1280, Height = 820 }; tools.Show(); await Task.Delay(40);
        try
        {
            tools.LoadImage(png, loaded);
            foreach (string theme in new[] { "Light", "Dark" })
                foreach (string language in L.Languages)
                {
                    controller.Settings.Theme = theme; controller.ApplyTheme(); L.Use(language);
                    Invoke(tools, "EnterAnnotate"); await Task.Delay(40); tools.UpdateLayout();
                    ShowPrivacyTab(tools); await Task.Delay(20); tools.UpdateLayout();
                    var box = Named<CheckBox>(tools, "RemoveHiddenDetails"); var summary = Named<TextBlock>(tools, "HiddenDetailsSummary");
                    Check(box.IsChecked == false && box.IsEnabled, $"{language}/{theme}: panel box not unticked and available");
                    Check(summary.Text == ExpectedSummary("location", "device", "date", "author", "other"), $"{language}/{theme}: panel summary reads '{summary.Text}'");
                    box.BringIntoView(); await Task.Delay(20);
                    NotClipped(tools, summary, $"{language}/{theme} panel summary");
                    NotClipped(tools, Named<TextBlock>(tools, "RemoveHiddenDetailsLabel"), $"{language}/{theme} panel checkbox label");
                    if (language is "en" or "de" or "ja") Render(tools, $"panel-{language}-{theme}");
                    Invoke(tools, "LeaveAnnotate"); await Task.Delay(20);
                }
        }
        finally { L.Use("en"); controller.Settings.Theme = "Light"; controller.ApplyTheme(); }
        // A tick in the panel is the same choice the export dialog starts from; a newly opened image starts unticked again.
        Invoke(tools, "EnterAnnotate"); await Task.Delay(30); ShowPrivacyTab(tools); await Task.Delay(20);
        Named<CheckBox>(tools, "RemoveHiddenDetails").IsChecked = true;
        Invoke(tools, "LeaveAnnotate"); await Task.Delay(20);
        Check(tools.RemoveHiddenDetails, "the panel tick was not kept for this image's export");
        Invoke(tools, "EnterAnnotate"); await Task.Delay(30); ShowPrivacyTab(tools); await Task.Delay(20);
        Check(Named<CheckBox>(tools, "RemoveHiddenDetails").IsChecked == true, "reopening the panel lost the tick for the same image");
        Invoke(tools, "LeaveAnnotate"); await Task.Delay(20);
        tools.LoadImage(png, loaded);
        Check(!tools.RemoveHiddenDetails, "a newly opened image started ticked");
        tools.Close();

        // A DesktopTools screenshot has no source file: nothing to remove.
        var pixels = new byte[64 * 48 * 4]; Array.Fill(pixels, (byte)180);
        var shot = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4); shot.Freeze();
        var editor = new ScreenshotEditorWindow(shot, _ => { }, _ => { }); editor.Show(); await Task.Delay(40);
        ShowPrivacyTab(editor); await Task.Delay(20);
        Check(Named<TextBlock>(editor, "HiddenDetailsSummary").Text == L.T("This file has no hidden details.") && !Named<CheckBox>(editor, "RemoveHiddenDetails").IsEnabled,
            "a screenshot offers to remove details it does not have");
        editor.Close();
        Check(File.ReadAllBytes(png).AsSpan().SequenceEqual(before), "the source file changed");
        log("PASS Hide private data panel: Image tools file and screenshot, 12 languages x 2 themes");
    }

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, null);

    private static void ShowPrivacyTab(Window window)
    {
        var tab = Descendants(window).OfType<RadioButton>().First(r => System.Windows.Automation.AutomationProperties.GetName(r) == L.T("Hide data"));
        tab.IsChecked = true; window.UpdateLayout();
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception("Hidden details: " + message); }

    /// <summary>Task 5: Describe never throws and names the categories; Strip keeps pixels, size, DPI, format and colour profile and drops every detail.</summary>
    internal static void DescribeAndStrip(Action<string> log)
    {
        foreach (string format in Formats)
        {
            string path = Fixture(format);
            byte[] before = File.ReadAllBytes(path); var written = File.GetLastWriteTimeUtc(path);
            var described = ImageHiddenDetails.Describe(path);
            Check(described.Readable && described.Location && described.Device && described.Date && described.Author && described.Other > 0, $"{format} fixture described as {described}");
            var frame = Decode(path);
            Check(ImageHiddenDetails.Describe(frame) == described, $"{format} in-memory description differs: {ImageHiddenDetails.Describe(frame)}");
            var stripped = ImageHiddenDetails.Strip(frame);
            Check(stripped.PixelWidth == frame.PixelWidth && stripped.PixelHeight == frame.PixelHeight && stripped.Format == frame.Format
                && stripped.DpiX == frame.DpiX && stripped.DpiY == frame.DpiY, $"{format} strip changed size, DPI or format");
            Check(Pixels(stripped).AsSpan().SequenceEqual(Pixels(frame)), $"{format} strip changed pixels");
            Check(!ImageHiddenDetails.Describe(stripped).Any, $"{format} stripped image still describes details");
            string output = Path.Combine(Folder, $"stripped-{format.ToLowerInvariant()}{Path.GetExtension(path)}");
            Encode(stripped, format, output);
            var after = ImageHiddenDetails.Describe(output);
            Check(after.Readable && !after.Any, $"{format} stripped file still has {after}; reader says {Survivors(output)}");
            Check(Survivors(output) == "none", $"{format} stripped file keeps {Survivors(output)}");
            Check(File.ReadAllBytes(path).AsSpan().SequenceEqual(before) && File.GetLastWriteTimeUtc(path) == written, $"{format} source file changed");
            log($"PASS {format}: described {described}; stripped copy has none");
        }

        // A plain DesktopTools PNG has nothing to report, and that is a successful reading.
        var plainPixels = new byte[16 * 4]; Array.Fill(plainPixels, (byte)200);
        string plain = Path.Combine(Folder, "plain.png");
        Encode(BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, plainPixels, 16), "PNG", plain);
        var none = ImageHiddenDetails.Describe(plain);
        Check(none.Readable && !none.Any, "a plain PNG reports " + none);

        // A damaged file: no exception, and "could not be read" instead of "none".
        string truncated = Path.Combine(Folder, "truncated.jpg");
        File.WriteAllBytes(truncated, File.ReadAllBytes(Fixture("JPEG")).Take(60).ToArray());
        Check(!ImageHiddenDetails.Describe(truncated).Readable, "a truncated file claims readable metadata");
        string garbage = Path.Combine(Folder, "garbage.png"); File.WriteAllBytes(garbage, Enumerable.Range(0, 300).Select(i => (byte)(i * 31)).ToArray());
        Check(!ImageHiddenDetails.Describe(garbage).Readable, "random bytes claim readable metadata");
        Check(!ImageHiddenDetails.Describe(Path.Combine(Folder, "missing.png")).Readable, "a missing file claims readable metadata");

        // Review Focus 4: a profiled image must look the same after export, ticked or unticked. WPF converts profiled
        // pixels to sRGB on decode, so what the app shows is the decoded pixels; the exported file, decoded the same
        // way, must give the same pixels. SA3200.icc is a non-sRGB profile that ships with Windows.
        string colourFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "drivers", "color");
        foreach (string profileName in new[] { "sRGB Color Space Profile.icm", "SA3200.icc" })
        {
            string profilePath = Path.Combine(colourFolder, profileName);
            if (!File.Exists(profilePath)) { log("NOT TESTED colour profile " + profileName + ": missing from the Windows colour folder"); continue; }
            var profiled = FixtureFrame("PNG", new ColorContext(new Uri(profilePath)));
            byte[] shown = Pixels(profiled);
            foreach (var (label, ready) in new[] { ("ticked", ImageHiddenDetails.Strip(profiled)), ("unticked", ImageTransforms.ForEncoding(profiled)) })
            {
                string exported = Path.Combine(Folder, "profiled-" + label + ".png");
                File.WriteAllBytes(exported, ImageTransforms.Encode(ready, ".png", 92));
                byte[] after = Pixels(Decode(exported));
                int differing = 0; for (int i = 0; i < Math.Min(after.Length, shown.Length); i++) if (Math.Abs(after[i] - shown[i]) > 1) differing++;
                Check(after.Length == shown.Length && differing == 0, $"an image with the {profileName} profile changed colour when exported {label}: {differing} of {shown.Length} bytes differ");
                if (label == "ticked") Check(!ImageHiddenDetails.Describe(exported).Any, "a stripped profiled export still has hidden details");
            }
        }
        log("PASS profiled images (sRGB and SA3200) look the same after export, ticked and unticked");

        // Review Focus 5: an image whose EXIF block is damaged still exports, ticked and unticked.
        byte[] jpeg = File.ReadAllBytes(Fixture("JPEG"));
        int exif = jpeg.AsSpan().IndexOf("Exif\0\0"u8);
        Check(exif > 0, "the JPEG fixture has no EXIF block to damage");
        int segmentEnd = exif - 2 + ((jpeg[exif - 2] << 8) | jpeg[exif - 1]); // the APP1 length covers its own two bytes
        for (int i = exif + 16; i < segmentEnd; i++) jpeg[i] = 0xFF; // IFD entries become garbage; the JPEG itself stays intact
        string damaged = Path.Combine(Folder, "damaged-exif.jpg"); File.WriteAllBytes(damaged, jpeg);
        var damagedDetails = ImageHiddenDetails.Describe(damaged); // must not throw
        Check(damagedDetails.Any || !damagedDetails.Readable, "a damaged EXIF block is described as no hidden details: " + damagedDetails);
        BitmapSource damagedImage;
        try { damagedImage = ImageTransforms.Load(damaged); }
        catch (Exception ex) { log("NOTE damaged-EXIF fixture is refused on import (" + ex.GetType().Name + "), so it never reaches export"); return; }
        foreach (string extension in new[] { ".png", ".jpg", ".bmp" })
        {
            Check(ImageTransforms.Encode(ImageTransforms.ForEncoding(damagedImage), extension).Length > 0, "a damaged-EXIF image failed to export unticked as " + extension);
            Check(ImageTransforms.Encode(ImageHiddenDetails.Strip(damagedImage), extension).Length > 0, "a damaged-EXIF image failed to export ticked as " + extension);
        }
        log($"PASS damaged-EXIF image exports ticked and unticked (described as {damagedDetails})");
    }

    private static BitmapFrame FixtureFrame(string format, ColorContext profile)
    {
        var source = Decode(Fixture(format));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source, null, source.Metadata as BitmapMetadata, new ReadOnlyCollection<ColorContext>([profile])));
        string path = Path.Combine(Folder, "fixture-profile.png");
        using (var stream = File.Create(path)) encoder.Save(stream);
        var frame = Decode(path);
        Check(frame.ColorContexts?.Count == 1, "the profile fixture has no colour profile");
        Check(ImageHiddenDetails.Describe(frame).Any, "the profile fixture lost its details");
        return frame;
    }

    private static BitmapFrame Decode(string path)
    {
        using var stream = File.OpenRead(path);
        return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static byte[] Pixels(BitmapSource image)
    {
        int stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var bytes = new byte[stride * image.PixelHeight]; image.CopyPixels(bytes, stride, 0); return bytes;
    }

    private static void Encode(BitmapSource image, string format, string path)
    {
        BitmapEncoder encoder = format switch { "JPEG" => new JpegBitmapEncoder { QualityLevel = 95 }, "TIFF" => new TiffBitmapEncoder(), _ => new PngBitmapEncoder() };
        // The same frame rule as ImageTransforms.Encode.
        encoder.Frames.Add(image is BitmapFrame { Decoder: null } built ? built : BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    internal static Task MeasureAsync(Action<string> log)
    {
        log("MEASURE format | path | survived (location, device, date, author, comment)");
        foreach (string format in Formats)
        {
            string source = Fixture(format);
            log($"MEASURE {format} | fixture | {Survivors(source)}");
            BitmapSource loaded;
            try { loaded = ImageTransforms.Load(source); }
            catch (Exception ex) { log($"MEASURE {format} | Image tools import | refused: {ex.Message}"); continue; }
            foreach (string target in new[] { "png", "jpg", "bmp" })
            {
                string output = Path.Combine(Folder, $"{format}-export-unedited.{target}");
                try { ImageTransforms.Export(loaded, output, source); log($"MEASURE {format} | export dialog, unedited → {target} | {Survivors(output)}"); }
                catch (Exception ex) { log($"MEASURE {format} | export dialog, unedited → {target} | failed: {ex.GetType().Name}: {ex.Message}"); }
                string rotated = Path.Combine(Folder, $"{format}-export-rotated.{target}");
                try { ImageTransforms.Export(ImageTransforms.Rotate(loaded), rotated, source); log($"MEASURE {format} | export dialog, rotated → {target} | {Survivors(rotated)}"); }
                catch (Exception ex) { log($"MEASURE {format} | export dialog, rotated → {target} | failed: {ex.GetType().Name}: {ex.Message}"); }
            }
            // The dialog encodes on a worker thread, as ImageExportDialog.SaveAsync does.
            foreach (string target in new[] { "png", "jpg" })
            {
                string worker = Path.Combine(Folder, $"{format}-export-worker.{target}");
                try { Task.Run(() => ImageTransforms.Export(loaded, worker, source)).GetAwaiter().GetResult(); log($"MEASURE {format} | export dialog on its worker thread, unedited → {target} | {Survivors(worker)}"); }
                catch (Exception ex) { log($"MEASURE {format} | export dialog on its worker thread, unedited → {target} | failed: {ex.GetType().Name}: {ex.Message}"); }
            }
            string saved = Path.Combine(Folder, $"{format}-image-output.png");
            try { SavePng(loaded, saved); log($"MEASURE {format} | ImageOutput.Save encoder | {Survivors(saved)}"); }
            catch (Exception ex) { log($"MEASURE {format} | ImageOutput.Save encoder | failed: {ex.GetType().Name}: {ex.Message}"); }
            string edited = Path.Combine(Folder, $"{format}-editor-save.png");
            try { SavePng(new ScreenshotEditDocument(loaded).Export(), edited); log($"MEASURE {format} | editor Save (no edits) | {Survivors(edited)}"); }
            catch (Exception ex) { log($"MEASURE {format} | editor Save (no edits) | failed: {ex.GetType().Name}: {ex.Message}"); }
        }
        log("MEASURE WebP | Image tools import | not offered: Image tools opens PNG, JPEG and BMP only");
        return Task.CompletedTask;
    }

    /// <summary>The encoder path of <c>ImageOutput.Save</c> and the controller's Save PNG.</summary>
    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    /// <summary>A test-side reader, independent of the code under test: lists the categories whose fixture values are still in the file.</summary>
    internal static string Survivors(string path)
    {
        var values = new List<(string Query, string Value)>();
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (frame.Metadata is BitmapMetadata metadata) Walk(metadata, "", values);
        }
        catch (Exception ex) { return "unreadable: " + ex.Message; }
        bool Has(Func<string, string, bool> match) => values.Any(v => match(v.Query.ToLowerInvariant(), v.Value));
        var found = new List<string>();
        if (Has((q, v) => q.Contains("/gps/") || q.Contains("{ushort=34853}") || q.Contains("gpsposition"))) found.Add("location");
        if (Has((q, v) => v.Contains("Contoso") || v.Contains("Phone X"))) found.Add("device");
        if (Has((q, v) => v.Contains("2026:11:11") || v.Contains("2026-11-11"))) found.Add("date");
        if (Has((q, v) => v.Contains("Anna Example"))) found.Add("author");
        if (Has((q, v) => v.Contains("private comment"))) found.Add("comment");
        return found.Count == 0 ? "none" : string.Join(", ", found);
    }

    internal static void Walk(BitmapMetadata metadata, string prefix, List<(string, string)> values)
    {
        foreach (string query in metadata)
        {
            object? value;
            try { value = metadata.GetQuery(query); } catch (Exception) { continue; }
            if (value is BitmapMetadata nested) Walk(nested, prefix + query, values);
            else values.Add((prefix + query, value switch { null => "", string s => s, byte[] bytes => System.Text.Encoding.Unicode.GetString(bytes).TrimEnd((char)0), Array a => string.Join(",", a.Cast<object>()), _ => value.ToString() ?? "" }));
        }
    }
}
