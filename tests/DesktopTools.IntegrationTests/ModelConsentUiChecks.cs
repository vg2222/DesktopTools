using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class ModelConsentUiChecks
{
    internal const string EnglishWarning = "This model is not part of DesktopTools. It was not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by its publisher. It is provided by its publisher as is, under the licence above. Results can be wrong; check them before you rely on them.";

    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Animations = false; s.UseCustomBackground = false; s.ThemePreset = "Classic"; });
        var (pack, _) = ModelPackChecks.Fixture();
        pack = pack with { Limitations = ["Not a real model", "Second limitation for the list"] };
        const string license = "Fixture Licence\r\n\r\nPermission is granted to test.\r\n    Indented line stays as is.\r\n";
        string previousTheme = controller.Settings.Theme;
        try
        {
            foreach (string language in L.Languages)
                foreach (string theme in new[] { "Light", "Dark" })
                {
                    L.Use(language); controller.Settings.Theme = theme; controller.ApplyTheme();
                    var window = new ModelConsentWindow(pack, license);
                    try
                    {
                        window.Show(); await Settle(); window.UpdateLayout();
                        var download = Named<Button>(window, "DownloadModel");
                        var accept = Named<CheckBox>(window, "AcceptLicense");
                        var licenseBox = Named<TextBox>(window, "LicenseText");
                        var warning = Named<TextBlock>(window, "NotOursWarning");
                        string where = $" ({language}, {theme})";
                        Check(!download.IsEnabled, "Download enabled before the licence was accepted" + where);
                        Check(accept.IsChecked == false, "Licence checkbox starts ticked" + where);
                        Check(licenseBox.Text == license && licenseBox.IsReadOnly, "Licence text changed or editable" + where);
                        string text = string.Join("\n", Descendants(window).OfType<TextBlock>().Select(t => t.Text)) + "\n" + window.Title;
                        foreach (string expected in new[] { pack.Publisher, pack.Revision, ModelPacks.FormatSize(pack.TotalBytes), "huggingface.co, hf.co" }.Concat(pack.Limitations.Select(L.T)))
                            Check(text.Contains(expected, StringComparison.Ordinal), "Window does not show " + expected + where);
                        var label = Named<TextBlock>(window, "AcceptLicenseLabel");
                        Check(label.Text == L.T("I have read and accept this licence") && label.IsVisible && label.ActualWidth > 40, "Checkbox sentence not visible" + where);
                        var textColor = ((SolidColorBrush)label.Foreground).Color;
                        Check((textColor.R + textColor.G + textColor.B < 300) == (theme == "Light"), "Theme not applied" + where);
                        Check(licenseBox.VerticalContentAlignment == VerticalAlignment.Top, "Licence text not aligned to the top" + where);
                        Check(!string.IsNullOrWhiteSpace(warning.Text), "Warning is empty" + where);
                        if (language == "en") Check(warning.Text == EnglishWarning, "English warning differs from spec 2.3: " + warning.Text);
                        else Check(warning.Text != EnglishWarning, "Warning not translated" + where);
                        Check(window.Title == L.F($"Download {pack.Name}"), "Title" + where);
                        // The warning sits above the checkbox (spec 2.3).
                        Check(warning.TranslatePoint(new Point(), window).Y < accept.TranslatePoint(new Point(), window).Y, "Warning is not above the checkbox" + where);
                        label.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent }); await Settle();
                        Check(download.IsEnabled && accept.IsChecked == true, "Clicking the sentence did not tick the checkbox" + where);
                        Check(download.IsEnabled, "Ticking the checkbox did not enable Download" + where);
                        accept.IsChecked = false; await Settle();
                        Check(!download.IsEnabled, "Unticking did not disable Download" + where);
                        accept.IsChecked = true; await Settle();
                        foreach (var button in Descendants(window).OfType<ButtonBase>().Where(b => b.IsVisible))
                        {
                            var origin = button.TranslatePoint(new Point(), window);
                            Check(origin.X >= 0 && origin.Y >= 0 && origin.X + button.ActualWidth <= window.ActualWidth + 1 && origin.Y + button.ActualHeight <= window.ActualHeight + 1, "Button clipped" + where);
                        }
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var file = File.Create($"model-consent-{language}-{theme.ToLowerInvariant()}.png")) png.Save(file);
                        window.Close();
                        Check(!window.Accepted, "Closing without Download accepted the licence" + where);
                    }
                    finally { if (window.IsVisible) window.Close(); }
                }

            L.Use("en");
            var accepted = new ModelConsentWindow(pack, license);
            accepted.Show(); await Settle();
            Named<CheckBox>(accepted, "AcceptLicense").IsChecked = true; await Settle();
            Named<Button>(accepted, "DownloadModel").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Settle();
            Check(accepted.Accepted && !accepted.IsVisible, "Download did not accept and close");

            var refused = new ModelConsentWindow(pack, license);
            refused.Show(); await Settle();
            var unticked = Named<Button>(refused, "DownloadModel");
            unticked.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await Settle();
            Check(!refused.Accepted, "Download accepted without the checkbox");
            refused.Close();
        }
        finally { L.Use("en"); controller.Settings.Theme = previousTheme; controller.ApplyTheme(); }
    }

    internal static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().SingleOrDefault(e => e.Name == name) ?? throw new Exception("Missing element " + name);
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static Task Settle() => Task.Delay(80);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
