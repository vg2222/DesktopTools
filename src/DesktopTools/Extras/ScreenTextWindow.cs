using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

/// <summary>"Scan screen text": the captured area with its text outlined and selectable, in a large window of its own.</summary>
internal sealed class ScreenTextWindow : Window
{
    private readonly TextSelectionView view;

    public ScreenTextWindow(BitmapSource image, Action<string> report, Action<string>? translate)
    {
        Title = L.T("Extract text") + " · DesktopTools"; Width = 1100; Height = 720; MinWidth = 720; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip;
        SetResourceReference(ForegroundProperty, "Text");
        view = new TextSelectionView(image, report, translate);
        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
        var header = UtilityWindowChrome.Header(this, L.T("Extract text"), Close, L.T("Close"), 20, allowMinimize: true, allowMaximize: true);
        header.Margin = new Thickness(0, 0, 0, 12);
        grid.Children.Add(header); System.Windows.Controls.Grid.SetRow(view, 1); grid.Children.Add(view);
        var card = Ui.Card(grid, 18); card.Margin = new Thickness(0); card.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "GlassRim");
        Content = card;
        Loaded += InitializeSize;
        Loaded += async (_, _) => await view.RecognizeAsync();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && view.Model?.HasSelection != true && Keyboard.FocusedElement is not System.Windows.Controls.TextBox) { Close(); e.Handled = true; } };
        Closed += (_, _) => view.Cancel();
        Motion.WindowEntrance(this);
    }

    private void InitializeSize(object sender, RoutedEventArgs args)
    {
        Loaded -= InitializeSize;
        var monitor = MonitorService.GetForWindow(this); var area = monitor.WorkingArea;
        int width = Math.Max(1, (int)Math.Round(area.Width * .8)), height = Math.Max(1, (int)Math.Round(area.Height * .8));
        MinWidth = Math.Min(720, width / monitor.ScaleX); MinHeight = Math.Min(480, height / monitor.ScaleY);
        Width = width / monitor.ScaleX; Height = height / monitor.ScaleY;
        NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(area.Left + (area.Width - width) / 2), (int)Math.Round(area.Top + (area.Height - height) / 2), width, height, 0x0004 | 0x0010);
    }
}
