using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;

internal static class GitHubGalleryExtras
{
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Invoke(object target, string name, params object[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    internal static async Task RunAsync(AppController controller, Func<Window, string, Task> capture, string demoDirectory)
    {
        Directory.CreateDirectory(demoDirectory);
        await CaptureNotesAsync(capture, demoDirectory);
        await CapturePrompterAsync(capture);
        await CaptureQrAsync(capture);
        await CaptureSampledColorAsync(capture);
        await CaptureTextToolsAsync(controller, capture);
    }

    private static async Task CaptureNotesAsync(Func<Window, string, Task> capture, string demoDirectory)
    {
        string notesDirectory = Path.Combine(demoDirectory, "gallery-notes");
        var notes = new List<FloatingNote>
        {
            new()
            {
                Title = "Launch checklist",
                Body = "Polish the gallery screenshots\nReview the release notes\nShare the build with the team"
            },
            new()
            {
                Title = "Presentation ideas",
                Body = "Lead with the problem\nShow the workflow\nClose with the result"
            },
            new()
            {
                Title = "Keyboard shortcuts",
                Body = "Ctrl + Alt + D  Draw on screen\nCtrl + Alt + R  Translate selection"
            }
        };
        var store = new NotesStore(notesDirectory);
        store.Load();
        store.Save(notes);

        using var service = new FloatingNotesService(notesDirectory, _ => { }) { DefaultTopmost = false };
        service.Show();
        await Task.Delay(100);
        var manager = Field<Window>(service, "manager");
        manager.Width = 920;
        manager.Height = 580;
        try
        {
            await capture(manager, "11-notes-dark");

            var launchNote = Field<List<FloatingNote>>(service, "notes").First();
            Invoke(service, "Open", launchNote);
            await Task.Delay(80);
            var floating = Field<Dictionary<Guid, Window>>(service, "windows")[launchNote.Id];
            floating.Width = 430;
            floating.Height = 360;
            try { await capture(floating, "12-floating-note-dark"); }
            finally { floating.Close(); }
        }
        finally
        {
            manager.Close();
        }
    }

    private static async Task CapturePrompterAsync(Func<Window, string, Task> capture)
    {
        var settings = new AppSettings
        {
            TeleprompterFontSize = 32,
            TeleprompterSpeed = 34,
            TeleprompterText =
                "Welcome\n\n" +
                "Today I want to show you a calmer way to work on screen.\n\n" +
                "Capture the moment\n\n" +
                "Take a screenshot, draw attention to the detail that matters, and keep the original safe.\n\n" +
                "Stay in the flow\n\n" +
                "Use focused tools for notes, presentations, translation, and everyday desktop tasks.\n\n" +
                "Thank you"
        };
        var window = new TeleprompterWindow(settings, change => { change(settings); return true; })
        {
            Width = 980,
            Height = 650
        };
        window.Show();
        await Task.Delay(100);
        try
        {
            await capture(window, "13-teleprompter-dark");
            window.SetPresentation(true);
            await Task.Delay(80);
            await capture(window, "14-teleprompter-presentation-dark");
        }
        finally
        {
            window.SetPresentation(false);
            window.Close();
        }
    }

    private static async Task CaptureQrAsync(Func<Window, string, Task> capture)
    {
        var window = new QrCodeWindow(_ => { });
        window.Show();
        Field<System.Windows.Controls.TextBox>(window, "input").Text = "https://github.com/vg2222/DesktopTools";
        Invoke(window, "Generate");
        await Task.Delay(80);
        try { await capture(window, "15-qr-dark"); }
        finally { window.Close(); }
    }

    private static async Task CaptureSampledColorAsync(Func<Window, string, Task> capture)
    {
        Color color = Color.FromRgb(47, 128, 237);
        var window = new SampledColorWindow(color, _ => { }, CreateColorPixels(color));
        window.Show();
        await Task.Delay(80);
        try { await capture(window, "16-color-picker-dark"); }
        finally { window.Close(); }
    }

    private static async Task CaptureTextToolsAsync(AppController controller, Func<Window, string, Task> capture)
    {
        var englishOcr = LocalOcr.Languages.FirstOrDefault(language => language.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        controller.UpdateSettings(settings =>
        {
            settings.TranslationEnabled = true;
            settings.ScreenTextEnabled = true;
            settings.TranslationDirection = "en-ru";
            if (englishOcr != null) settings.ScreenTextLanguage = englishOcr.Tag;
        });

        var window = controller.OpenTextTools()!;
        window.SetSource("Thank you for your help. See you tomorrow.");
        await window.TranslateAsync();
        await Task.Delay(60);
        try
        {
            await capture(window, "17-translation-dark");
            if (englishOcr != null)
            {
                await window.RecognizeScreenAsync(CreateOcrFixture());
                await Task.Delay(60);
                await capture(window, "18-text-from-screenshot-dark");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static BitmapSource CreateColorPixels(Color color)
    {
        const int size = 13;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = (y * size + x) * 4;
                int light = (x - 6) * 3 + (6 - y) * 2;
                pixels[offset] = (byte)Math.Clamp(color.B + light, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(color.G + light, 0, 255);
                pixels[offset + 2] = (byte)Math.Clamp(color.R + light, 0, 255);
                pixels[offset + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateOcrFixture()
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 760, 190));
            drawing.DrawText(
                new FormattedText("DESKTOP TOOLS", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), 42, new SolidColorBrush(Color.FromRgb(31, 41, 55)), 1),
                new Point(34, 28));
            drawing.DrawText(
                new FormattedText("CAPTURE  •  CREATE  •  PRESENT", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 24, new SolidColorBrush(Color.FromRgb(47, 128, 237)), 1),
                new Point(36, 105));
        }
        var bitmap = new RenderTargetBitmap(760, 190, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
