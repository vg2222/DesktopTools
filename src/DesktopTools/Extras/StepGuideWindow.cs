using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using Microsoft.Win32;

namespace DesktopTools.Extras;

internal sealed class StepGuideWindow : Window
{
    private sealed class Entry(BitmapSource image, string caption = "", string? sourcePath = null)
    {
        internal BitmapSource Image { get; } = image;
        internal string Caption { get; set; } = caption;
        internal string? SourcePath { get; } = sourcePath;
    }
    private readonly List<Entry> steps = new();
    private readonly StackPanel rows = new();
    private readonly TextBox titleInput = MediaWorkspaceLayout.NumericField(L.T("Step-by-step guide"));
    private readonly TextBlock status = Ui.Text("", 12, muted: true);
    private readonly TextBlock empty = Ui.Text(L.T("Add screenshots to create a numbered guide."), 15, muted: true);
    private readonly Button export;
    private readonly Func<BitmapSource?> latestCapture;
    private readonly Action<string> report;

    internal StepGuideWindow(Func<BitmapSource?> latestCapture, Action<string> report)
    {
        this.latestCapture = latestCapture; this.report = report;
        titleInput.MaxLength = 120; titleInput.MinHeight = 40;
        Title = L.T("Step-by-step guide"); Tag = "Step-by-step guide";
        Width = 960; Height = 720; MinWidth = 720; MinHeight = 500;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UtilityWindowChrome.EnableBackdrop(this);
        var layout = new DockPanel();
        var header = UtilityWindowChrome.Header(this, Title, Close, L.T("Close"), 19, allowMinimize: true, allowMaximize: true);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var intro = Ui.Text(L.T("Put screenshots in order, explain each step, then save one long PNG. Original screenshots stay unchanged."), 13, muted: true);
        intro.Margin = new Thickness(0, 0, 0, 18); DockPanel.SetDock(intro, Dock.Top); layout.Children.Add(intro);
        var titleLabel = Ui.Text(L.T("Guide title"), 13, true); titleLabel.Margin = new Thickness(0, 0, 0, 6);
        var titleGroup = new StackPanel { Margin = new Thickness(0, 0, 0, 16) }; titleGroup.Children.Add(titleLabel); titleGroup.Children.Add(titleInput);
        DockPanel.SetDock(titleGroup, Dock.Top); layout.Children.Add(titleGroup);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var addFiles = Ui.Button(L.T("Add screenshots"), AddFiles, true); addFiles.Content = Ui.IconLabel("Plus", L.T("Add screenshots"), primary: true);
        var addLatest = Ui.Button(L.T("Add latest capture"), AddLatest); addLatest.Content = Ui.IconLabel("Capture", L.T("Add latest capture"));
        actions.Children.Add(addFiles); actions.Children.Add(addLatest);
        DockPanel.SetDock(actions, Dock.Top); layout.Children.Add(actions);
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        export = Ui.Button(L.T("Export guide PNG"), Export, true); export.Content = Ui.IconLabel("Image", L.T("Export guide PNG"), primary: true);
        DockPanel.SetDock(export, Dock.Right); bottom.Children.Add(export);
        status.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        var list = new DockPanel();
        empty.Margin = new Thickness(20, 30, 20, 30); empty.TextAlignment = TextAlignment.Center;
        DockPanel.SetDock(empty, Dock.Top); list.Children.Add(empty);
        list.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        layout.Children.Add(list);
        var card = Ui.Card(layout, 22); card.Margin = new Thickness(0);
        card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); Content = card;
        Refresh(); Motion.WindowEntrance(this);
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Title = L.T("Add screenshots"), Filter = L.T("Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"), Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (string path in dialog.FileNames)
        {
            if (steps.Count >= 20) { status.Text = L.T("A guide can contain up to 20 screenshots."); break; }
            try
            {
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
                steps.Add(new Entry(image, sourcePath: Path.GetFullPath(path)));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
            { status.Text = L.T("Could not open screenshot: ") + Path.GetFileName(path); }
        }
        Refresh();
    }
    private void AddLatest()
    {
        if (steps.Count >= 20) { status.Text = L.T("A guide can contain up to 20 screenshots."); return; }
        var image = latestCapture();
        if (image == null) { status.Text = L.T("Capture a screenshot first."); return; }
        steps.Add(new Entry(image)); Refresh();
    }
    private void Move(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= steps.Count) return;
        (steps[index], steps[target]) = (steps[target], steps[index]); Refresh();
    }
    private void Refresh()
    {
        rows.Children.Clear(); empty.Visibility = steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        export.IsEnabled = steps.Count > 0;
        for (int i = 0; i < steps.Count; i++)
        {
            int index = i; var step = steps[i];
            var line = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); line.ColumnDefinitions.Add(new ColumnDefinition());
            var thumbnail = new Image { Source = step.Image, Stretch = Stretch.Uniform, Width = 190, Height = 122 };
            RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
            var frame = new Border { Child = thumbnail, Padding = new Thickness(4), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 0) };
            frame.SetResourceReference(Border.BackgroundProperty, "Field"); frame.SetResourceReference(Border.BorderBrushProperty, "Stroke"); line.Children.Add(frame);
            var detail = new StackPanel(); Grid.SetColumn(detail, 1); line.Children.Add(detail);
            detail.Children.Add(Ui.Text(L.T("Step") + " " + (i + 1), 15, true));
            var captionLabel = Ui.Text(L.T("Step caption"), 11, muted: true); captionLabel.Margin = new Thickness(0, 7, 0, 4); detail.Children.Add(captionLabel);
            var caption = MediaWorkspaceLayout.NumericField(step.Caption);
            caption.MaxLength = 500; caption.MinHeight = 48; caption.TextWrapping = TextWrapping.Wrap;
            caption.AcceptsReturn = true; caption.Margin = new Thickness(0, 0, 0, 7);
            caption.TextChanged += (_, _) => step.Caption = caption.Text;
            System.Windows.Automation.AutomationProperties.SetName(caption, L.T("Step caption") + " " + (i + 1));
            detail.Children.Add(caption);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            var up = Ui.IconButton("Chevron", L.T("Move up"), () => Move(index, -1)); up.IsEnabled = i > 0;
            var down = Ui.IconButton("Chevron", L.T("Move down"), () => Move(index, 1)); down.IsEnabled = i < steps.Count - 1;
            up.RenderTransform = new RotateTransform(90); down.RenderTransform = new RotateTransform(-90);
            var remove = Ui.IconButton("Close", L.T("Remove step"), () => { steps.RemoveAt(index); Refresh(); });
            controls.Children.Add(up); controls.Children.Add(down); controls.Children.Add(remove); detail.Children.Add(controls);
            var surface = Ui.Card(line, 14); surface.CornerRadius = new CornerRadius(12); rows.Children.Add(surface);
        }
        status.Text = steps.Count == 0 ? "" : steps.Count + " / 20 " + L.T("steps");
    }
    private void Export()
    {
        if (steps.Count == 0) return;
        var dialog = new SaveFileDialog { Title = L.T("Export guide PNG"), Filter = "PNG image|*.png", DefaultExt = ".png", AddExtension = true, FileName = "DesktopTools-guide" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (steps.Any(step => step.SourcePath != null && string.Equals(step.SourcePath, Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)))
            { status.Text = L.T("Choose a different file to keep the original screenshot."); return; }
            Cursor = System.Windows.Input.Cursors.Wait;
            StepGuideComposer.Save(steps.Select(step => new GuideStep(step.Image, step.Caption)).ToArray(), titleInput.Text, dialog.FileName);
            status.Text = L.T("Guide saved: ") + dialog.FileName; report(status.Text);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        { status.Text = L.T("Could not export guide: ") + ex.Message; }
        finally { Cursor = null; }
    }
}
