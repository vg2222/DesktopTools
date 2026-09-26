using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class NativeRuntimeFeatureChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        IReadOnlyList<string> missing = ["vcruntime140.dll"];
        var reader = new Func<IReadOnlyList<string>>(() => missing);
        var textConstructor = typeof(TextToolsWindow).GetConstructor([typeof(AppController), typeof(Func<IReadOnlyList<string>>)]);
        Check(textConstructor != null, "Text tools cannot be tested with a missing Visual C++ runtime");
        var text = (TextToolsWindow)textConstructor!.Invoke([controller, reader]);
        try
        {
            text.Show(); text.SetSource("Hello");
            Check(Field<TextBlock>(text, "runtimeWarning").IsVisible &&
                  Field<TextBlock>(text, "runtimeWarning").Text.Contains("Visual C++", StringComparison.Ordinal),
                "Translation does not warn about the missing runtime before use");
            text.ShowMode(true);
            Check(!Field<TextBlock>(text, "runtimeWarning").IsVisible,
                "Missing translation runtime incorrectly blocks the OCR-only mode");
            text.ShowMode(false);
            await text.TranslateAsync();
            Check(!text.IsProcessing && controller.Status == L.T("Local translation is unavailable until Microsoft Visual C++ x64 is installed."),
                "Translation tried to load ONNX or failed without an unavailable notification");
        }
        finally { text.Close(); }

        var imageConstructor = typeof(ImageToolsWindow).GetConstructor([typeof(Action<string>), typeof(Func<IReadOnlyList<string>>)]);
        Check(imageConstructor != null, "Image tools cannot be tested with a missing Visual C++ runtime");
        var reports = new List<string>();
        var image = (ImageToolsWindow)imageConstructor!.Invoke([new Action<string>(reports.Add), reader]);
        try
        {
            image.Show();
            var pixels = Enumerable.Repeat((byte)255, 4 * 4 * 4).ToArray();
            var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
            typeof(ImageToolsWindow).GetMethod("SetImage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(image, [bitmap]);
            var backgroundTab = Walk(image).OfType<Button>().Single(button =>
                System.Windows.Automation.AutomationProperties.GetName(button) == L.T("Background"));
            backgroundTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<TextBlock>(image, "runtimeWarning").IsVisible,
                "Background tab did not show its missing-runtime warning");
            await image.RemoveBackgroundAsync();
            Check(reports.Any(message => message == L.T("Background removal is unavailable until Microsoft Visual C++ x64 is installed.")) &&
                  Field<TextBlock>(image, "status").Text.Contains("Visual C++", StringComparison.Ordinal),
                "Background removal did not stop before loading ONNX with missing runtime");
        }
        finally { image.Close(); }
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
