using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

/// <summary>
/// The screenshot editor as a window: chrome, title, size, feature tour and lifetime around <see cref="ScreenshotEditorView"/>, which holds the
/// canvas, tools, Hide data and Style panels so the same editor can also live inside Image tools.
/// </summary>
public sealed class ScreenshotEditorWindow : Window
{
    internal ScreenshotEditorView View { get; }

    public ScreenshotEditorWindow(BitmapSource image, Action<BitmapSource> onExport, Action<string> report, bool applyToImage = false, string? editorLayout = null,
        string? applyLabel = null, bool offerOriginal = false, AutoRedactOptions? autoRedact = null, bool reviewBeforeOutput = false,
        Func<BitmapSource, ScreenshotExportAction, Task<bool>>? exportAsync = null, Action<string>? translate = null)
    {
        View = new ScreenshotEditorView(image, onExport, report, applyToImage, editorLayout, applyLabel, offerOriginal, autoRedact, reviewBeforeOutput, exportAsync);
        if (translate != null) View.TranslateRequested += translate;
        Title = L.T(applyLabel == null ? "Edit screenshot · DesktopTools" : "Edit guide image"); Width = 1060; Height = 760; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(ForegroundProperty, "Text");
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 860; MinHeight = 500;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        var header = UtilityWindowChrome.Header(this, "DesktopTools — " + L.T(applyLabel == null ? "Edit screenshot" : "Edit guide image"), Close, L.T("Close"), 13, allowMinimize: true);
        layout.Children.Add(header); Grid.SetRow(View, 1); layout.Children.Add(View);
        var guide = FeatureTourButton.Create(this, "screenshot-editor", () => new GuidedTour.Step[]
        {
            new(() => View.ToolCard, "Annotation toolbar", "Choose a drawing tool, selection, eraser or crop. More tools are available in the menu."),
            new(() => View.Backdrop, "Image canvas", "Draw on the image. Select moves an annotation; Undo restores the previous change."),
            new(() => View.PropertyCard, "Properties", "Choose color, thickness and opacity for your annotations."),
            new(() => View.Actions, "Export image", applyLabel != null ? "Use the edited copy in the guide; the source image stays unchanged." : applyToImage ? "Apply returns the edited image to the image editor without saving a file." : "Copy sends the result to the clipboard. Save creates a PNG file.")
        }); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(header.Children.Count - 1, guide);
        var shell = Ui.Card(layout, 14); shell.Margin = new Thickness(0); shell.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); shell.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = shell;
        Loaded += InitializeEditorSize;
        View.CloseRequested += Close;
        Closed += (_, _) => View.Dispose();
    }

    // Forwarded so callers and checks keep addressing the window.
    internal Task FindSensitiveDataAsync() => View.FindSensitiveDataAsync();
    internal Func<BitmapSource, string, IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<SensitiveFinding>>> AnalyzeSensitiveData
    {
        get => View.AnalyzeSensitiveData; set => View.AnalyzeSensitiveData = value;
    }
    internal CancellationToken ExportCancellationToken => View.ExportCancellationToken;

    private void InitializeEditorSize(object sender, RoutedEventArgs args)
    {
        Loaded -= InitializeEditorSize;
        var monitor = MonitorService.GetForWindow(this);
        var area = monitor.WorkingArea;
        int width = Math.Max(1, (int)Math.Round(area.Width * .75)), height = Math.Max(1, (int)Math.Round(area.Height * .75));
        MinWidth = Math.Min(860, width / monitor.ScaleX); MinHeight = Math.Min(500, height / monitor.ScaleY);
        Width = width / monitor.ScaleX; Height = height / monitor.ScaleY;
        // Work area and HWND placement use physical pixels, including monitors
        // with negative origins; WPF dimensions above remain monitor-local DIPs.
        NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(area.Left + (area.Width - width) / 2), (int)Math.Round(area.Top + (area.Height - height) / 2),
            width, height, 0x0004 | 0x0010);
    }
}
