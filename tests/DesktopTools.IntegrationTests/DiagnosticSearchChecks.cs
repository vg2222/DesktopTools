using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class DiagnosticSearchChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    internal static Task RunAsync()
    {
        var system = @"C:\Windows\System32";
        var missing = NativeDependencyDiagnostics.FindMissingMediaFoundationFiles(
            path => !path.EndsWith("mfplat.dll", StringComparison.OrdinalIgnoreCase), system);
        Check(missing.SequenceEqual(["mfplat.dll"]), "Media Foundation file check missed an absent DLL");

        using var controller = new AppController(true);
        controller.UpdateSettings(settings => settings.Animations = false);
        controller.OpenMain();
        var main = Field<MainWindow>(controller, "main");
        try
        {
            main.Navigate("About"); main.UpdateLayout();
            var aboutPage = Field<StackPanel>(main, "page");
            Check(Walk(aboutPage).OfType<Image>().Any(item =>
                    item.Source is BitmapImage bitmap && bitmap.UriSource?.ToString().Contains("AppIcon.png", StringComparison.OrdinalIgnoreCase) == true),
                "About page should show the actual DesktopTools app icon");
            Render(main, "about-app-icon-preview.png");
            main.Navigate("Home"); main.UpdateLayout();
            var homeSearch = Field<TextBox>(main, "dashboardSearch");
            CheckEnteredTextVisible(main, homeSearch, "record", "capture");
            Render(main, "home-search-input-preview.png");
            homeSearch.Text = "zzzz-no-match"; main.UpdateLayout();
            Render(main, "home-search-empty-preview.png");
            main.Navigate("Help"); main.UpdateLayout();
            var helpSearch = Walk(main).OfType<TextBox>().Single(box => box.Tag as string == "help-search");
            CheckEnteredTextVisible(main, helpSearch, "recorder", "image");
            helpSearch.Text = "zzzz-no-match"; main.UpdateLayout();
            Check(Walk(main).OfType<FrameworkElement>().Any(item => item.Tag as string == "search-empty-help" && item.IsVisible),
                "Help search needs a visible centered empty state");
            Render(main, "help-search-empty-preview.png");
            Walk(main).OfType<Button>().Single(button => button.Tag as string == "search-clear-help")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(helpSearch.Text.Length == 0, "Help search clear action did not clear the field");
            Render(main, "help-search-input-preview.png");
            main.Navigate("Shortcuts"); main.UpdateLayout();
            var shortcutSearch = Walk(main).OfType<TextBox>().Single(box => box.Tag as string == "shortcut-search");
            var shortcutHint = Walk((DependencyObject)shortcutSearch.Parent).OfType<TextBlock>()
                .Single(block => block.Text == L.T("Find a shortcut"));
            Render(main, "shortcut-search-placeholder-preview.png");
            double hintX = shortcutHint.TransformToAncestor(main).Transform(new Point()).X;
            shortcutSearch.Text = "record"; main.UpdateLayout();
            Render(main, "shortcut-search-typed-preview.png");
            var caret = shortcutSearch.GetRectFromCharacterIndex(0);
            double typedX = shortcutSearch.TransformToAncestor(main).Transform(caret.TopLeft).X;
            Check(Math.Abs(hintX - typedX) <= 1.5 && Math.Abs(shortcutHint.FontSize - shortcutSearch.FontSize) < .1,
                $"Shortcut search placeholder and entered text do not align (placeholder {hintX:F1}, typed {typedX:F1}; fonts {shortcutHint.FontSize:F1}/{shortcutSearch.FontSize:F1})");
            shortcutSearch.Text = "zzzz-no-match"; main.UpdateLayout();
            Check(Walk(main).OfType<FrameworkElement>().Any(item => item.Tag as string == "search-empty-shortcuts" && item.IsVisible),
                "Shortcut search needs a visible centered empty state");
            Render(main, "shortcut-search-empty-preview.png");
            Walk(main).OfType<Button>().Single(button => button.Tag as string == "search-clear-shortcuts")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(shortcutSearch.Text.Length == 0 && Walk(main).OfType<FrameworkElement>().Any(item => item.Tag as string == "feature-shortcut-row" && item.IsVisible),
                "Clearing shortcut search did not restore the list");
            main.Navigate("Diagnostics"); main.UpdateLayout();
            Check(Walk(main).OfType<Button>().Any(button => button.Tag as string == "diagnostics-rerun") &&
                  Walk(main).OfType<Button>().Any(button => button.Tag as string == "diagnostics-shortcuts"),
                "Diagnostics page is missing its check and shortcut controls");
            Render(main, "diagnostics-preview.png");

            main.Navigate("Settings"); main.UpdateLayout();
            var search = Walk(main).OfType<TextBox>().Single(box => box.Tag as string == "settings-feature-search");
            CheckEnteredTextVisible(main, search, "screen recorder", "floating notes");
            search.Text = "zzzz-no-match"; main.UpdateLayout();
            Check(Walk(main).OfType<FrameworkElement>().Any(item => item.Tag as string == "search-empty-settings" && item.IsVisible),
                "Settings search needs a visible centered empty state");
            Render(main, "settings-search-empty-preview.png");
            search.Text = "Ctrl+Alt+Shift+R"; main.UpdateLayout();
            var result = Walk(main).OfType<Button>().Single(button => button.Tag as string == "settings-result-record");
            Render(main, "settings-search-preview.png");
            result.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
            Check(Field<string>(main, "currentPage") == "Feature:record", "Settings search did not open the recorder's dedicated page");

            L.Use("ru"); main.Navigate("Settings"); main.UpdateLayout();
            search = Walk(main).OfType<TextBox>().Single(box => box.Tag as string == "settings-feature-search");
            search.Text = L.T("Floating notes"); main.UpdateLayout();
            Check(Walk(main).OfType<Button>().Any(button => button.Tag as string == "settings-result-notes"),
                "Settings search did not find a localized feature title");
        }
        finally { main.Close(); }
        return Task.CompletedTask;
    }

    private static void Render(MainWindow window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static void CheckEnteredTextVisible(MainWindow window, TextBox box, string first, string second)
    {
        byte[] Pixels(string value)
        {
            box.Text = value;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            return pixels;
        }

        var before = Pixels(first);
        var after = Pixels(second);
        var origin = box.TransformToAncestor(window).Transform(new Point());
        int x0 = (int)origin.X + 8, x1 = Math.Min((int)(origin.X + 240), (int)window.ActualWidth);
        int y0 = (int)origin.Y + 2, y1 = Math.Min((int)(origin.Y + box.ActualHeight - 2), (int)window.ActualHeight);
        int changed = 0, stride = (int)window.ActualWidth * 4;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int offset = y * stride + x * 4;
                if (Math.Abs(before[offset] - after[offset]) + Math.Abs(before[offset + 1] - after[offset + 1]) +
                    Math.Abs(before[offset + 2] - after[offset + 2]) > 48) changed++;
            }
        Check(changed > 30, $"Entered text is not visibly rendered in {box.Tag ?? "Home search"} ({changed} changed pixels)");
    }
}
