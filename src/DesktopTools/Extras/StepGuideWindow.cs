using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private readonly UniformGrid rows = new() { Columns = 3, VerticalAlignment = VerticalAlignment.Top };
    private readonly WrapPanel recentTiles = new();
    private readonly WrapPanel folderTiles = new();
    private readonly TextBlock folderLabel = Ui.Text("", 11, muted: true);
    private readonly TextBox titleInput = MediaWorkspaceLayout.NumericField(L.T("Step-by-step guide"));
    private readonly TextBlock status = Ui.Text("", 12, muted: true);
    private readonly TextBlock empty = Ui.Text(L.T("Add screenshots to create a numbered guide."), 15, muted: true);
    private readonly Button export;
    private readonly Func<BitmapSource?> latestCapture;
    private readonly Func<IReadOnlyList<CaptureHistoryEntry>> recentCaptures;
    private readonly Action<string> report;
    private int columns = 3;
    private bool darkExport;

    internal StepGuideWindow(Func<BitmapSource?> latestCapture, Func<IReadOnlyList<CaptureHistoryEntry>> recentCaptures, string screenshotFolder, Action<string> report)
    {
        this.latestCapture = latestCapture; this.recentCaptures = recentCaptures; this.report = report;
        darkExport = Application.Current.Resources["Surface"] is SolidColorBrush surface && surface.Color.R < 100;
        titleInput.MaxLength = 120; titleInput.MinHeight = 40;
        Title = L.T("Step-by-step guide"); Tag = "Step-by-step guide";
        Width = 1200; Height = 790; MinWidth = 900; MinHeight = 560;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UtilityWindowChrome.EnableBackdrop(this);
        var layout = new DockPanel();
        var header = UtilityWindowChrome.Header(this, Title, Close, L.T("Close"), 19, allowMinimize: true, allowMaximize: true);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var intro = Ui.Text(L.T("Choose recent captures or a screenshot folder. Drag steps to reorder them; originals stay unchanged."), 13, muted: true);
        intro.Margin = new Thickness(0, 0, 0, 14); DockPanel.SetDock(intro, Dock.Top); layout.Children.Add(intro);
        var settings = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        settings.ColumnDefinitions.Add(new ColumnDefinition());
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
        settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(164) });
        var titleGroup = Labeled(L.T("Guide title"), titleInput); titleGroup.Margin = new Thickness(0, 0, 12, 0); settings.Children.Add(titleGroup);
        var columnChoice = Ui.Choice(new[] { "1", "2", "3" }, "3", value => { columns = int.Parse(value); Refresh(); });
        var columnGroup = Labeled(L.T("Steps per row"), columnChoice); columnGroup.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(columnGroup, 1); settings.Children.Add(columnGroup);
        var backgroundChoice = Ui.Choice(new[] { L.T("Dark"), L.T("Light") }, darkExport ? L.T("Dark") : L.T("Light"), value => darkExport = value == L.T("Dark"));
        var backgroundGroup = Labeled(L.T("Export background"), backgroundChoice);
        Grid.SetColumn(backgroundGroup, 2); settings.Children.Add(backgroundGroup);
        DockPanel.SetDock(settings, Dock.Top); layout.Children.Add(settings);
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        export = Ui.Button(L.T("Export guide PNG"), Export, true); export.Content = Ui.IconLabel("Image", L.T("Export guide PNG"), primary: true);
        DockPanel.SetDock(export, Dock.Right); bottom.Children.Add(export);
        status.VerticalAlignment = VerticalAlignment.Center; bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(242) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        var source = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var sourceHeading = Ui.Text(L.T("Screenshots"), 16, true); sourceHeading.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(sourceHeading, Dock.Top); source.Children.Add(sourceHeading);
        var sourceActions = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var addFiles = Ui.Button(L.T("Add screenshots"), AddFiles, true);
        addFiles.Content = Ui.IconLabel("Plus", L.T("Add screenshots"), primary: true);
        addFiles.Margin = new Thickness(0, 0, 0, 7); sourceActions.Children.Add(addFiles);
        var chooseFolder = Ui.Button(L.T("Choose screenshot folder"), ChooseFolder);
        chooseFolder.Margin = new Thickness(0, 0, 0, 7); sourceActions.Children.Add(chooseFolder);
        sourceActions.Children.Add(Ui.Button(L.T("Add latest capture"), AddLatest));
        DockPanel.SetDock(sourceActions, Dock.Top); source.Children.Add(sourceActions);
        var gallery = new StackPanel();
        var recentHeader = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var refresh = Ui.IconButton("Refresh", L.T("Refresh recent captures"), RefreshRecent);
        DockPanel.SetDock(refresh, Dock.Right); recentHeader.Children.Add(refresh);
        recentHeader.Children.Add(Ui.Text(L.T("Recent DesktopTools captures"), 12, true));
        gallery.Children.Add(recentHeader); gallery.Children.Add(recentTiles);
        var folderHeader = Ui.Text(L.T("Screenshot folder"), 12, true);
        folderHeader.Margin = new Thickness(0, 18, 0, 6); gallery.Children.Add(folderHeader);
        folderLabel.Text = L.T("Choose a folder to browse screenshots.");
        folderLabel.TextWrapping = TextWrapping.Wrap; folderLabel.Margin = new Thickness(0, 0, 0, 8);
        gallery.Children.Add(folderLabel); gallery.Children.Add(folderTiles);
        source.Children.Add(new ScrollViewer { Content = gallery, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        workspace.Children.Add(source);
        var builder = new DockPanel(); Grid.SetColumn(builder, 1); workspace.Children.Add(builder);
        var builderHeading = Ui.Text(L.T("Guide steps"), 16, true);
        builderHeading.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(builderHeading, Dock.Top); builder.Children.Add(builderHeading);
        empty.Margin = new Thickness(20, 44, 20, 24); empty.TextAlignment = TextAlignment.Center;
        DockPanel.SetDock(empty, Dock.Top); builder.Children.Add(empty);
        builder.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        layout.Children.Add(workspace);
        var card = Ui.Card(layout, 22); card.Margin = new Thickness(0);
        card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); Content = card;
        RefreshRecent();
        if (Directory.Exists(screenshotFolder)) PopulateFolder(screenshotFolder);
        Refresh(); Motion.WindowEntrance(this);
    }

    private static StackPanel Labeled(string label, FrameworkElement control)
    {
        var group = new StackPanel();
        var heading = Ui.Text(label, 12, true); heading.Margin = new Thickness(0, 0, 0, 6);
        group.Children.Add(heading); group.Children.Add(control); return group;
    }

    private void RefreshRecent()
    {
        recentTiles.Children.Clear();
        var entries = recentCaptures();
        if (entries.Count == 0)
        {
            recentTiles.Children.Add(Ui.Text(L.T("No recent captures in this session."), 12, muted: true));
            return;
        }
        foreach (var entry in entries)
            recentTiles.Children.Add(SourceTile(entry.Image, entry.CreatedAt.ToLocalTime().ToString("t", L.Culture), () => AddImage(entry.Image)));
    }

    private static Button SourceTile(BitmapSource image, string caption, Action add)
    {
        var tile = Ui.Button(caption, add);
        tile.Width = 104; tile.Margin = new Thickness(0, 0, 8, 8); tile.Padding = new Thickness(5);
        var content = new StackPanel();
        var preview = new Image { Source = image, Height = 62, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
        content.Children.Add(preview);
        var label = Ui.Text(caption, 10, muted: true); label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(label);
        tile.Content = content; ToolTipService.SetToolTip(tile, caption); return tile;
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = L.T("Choose screenshot folder") };
        if (Directory.Exists(folderLabel.Text)) dialog.InitialDirectory = folderLabel.Text;
        if (dialog.ShowDialog(this) != true) return;
        PopulateFolder(dialog.FolderName);
    }

    private void PopulateFolder(string path)
    {
        folderTiles.Children.Clear(); folderLabel.Text = path;
        try
        {
            var paths = Directory.EnumerateFiles(path)
                .Where(path => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(48).ToArray();
            foreach (var file in paths)
            {
                try { folderTiles.Children.Add(SourceTile(LoadFile(file, 192), Path.GetFileName(file), () => AddFile(file))); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }
            }
            if (folderTiles.Children.Count == 0) folderTiles.Children.Add(Ui.Text(L.T("No screenshots found in this folder."), 12, muted: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { status.Text = L.T("Could not open screenshot folder: ") + ex.Message; }
    }

    private static BitmapSource LoadFile(string path, int decodeWidth = 0)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path); if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.EndInit(); image.Freeze(); return image;
    }

    private void AddImage(BitmapSource image)
    {
        if (steps.Count >= 20) { status.Text = L.T("A guide can contain up to 20 screenshots."); return; }
        steps.Add(new Entry(image)); Refresh();
    }

    private void AddFile(string path, bool refresh = true)
    {
        if (steps.Count >= 20) { status.Text = L.T("A guide can contain up to 20 screenshots."); return; }
        try { steps.Add(new Entry(LoadFile(path), sourcePath: Path.GetFullPath(path))); if (refresh) Refresh(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        { status.Text = L.T("Could not open screenshot: ") + Path.GetFileName(path); }
    }

    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Title = L.T("Add screenshots"), Filter = L.T("Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"), Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (string path in dialog.FileNames) AddFile(path, refresh: false);
        Refresh();
    }
    private void AddLatest()
    {
        var image = latestCapture();
        if (image == null) { status.Text = L.T("Capture a screenshot first."); return; }
        AddImage(image);
    }
    private void Move(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= steps.Count) return;
        (steps[index], steps[target]) = (steps[target], steps[index]); Refresh();
    }
    private void Move(Entry step, Entry target)
    {
        int from = steps.IndexOf(step), to = steps.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        steps.RemoveAt(from); steps.Insert(to, step); Refresh();
    }
    private static Button MoveButton(string label, int angle, Action action)
    {
        var button = Ui.IconButton("Chevron", label, action);
        if (button.Content is UIElement glyph)
        {
            glyph.RenderTransformOrigin = new Point(.5, .5);
            glyph.RenderTransform = new RotateTransform(angle);
        }
        return button;
    }
    private void Refresh()
    {
        rows.Columns = columns; rows.Children.Clear(); empty.Visibility = steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        export.IsEnabled = steps.Count > 0;
        for (int i = 0; i < steps.Count; i++)
        {
            int index = i; var step = steps[i];
            var detail = new StackPanel();
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            var up = MoveButton(L.T("Move up"), -90, () => Move(index, -1)); up.IsEnabled = i > 0;
            var down = MoveButton(L.T("Move down"), 90, () => Move(index, 1)); down.IsEnabled = i < steps.Count - 1;
            var remove = Ui.IconButton("Close", L.T("Remove step"), () => { steps.Remove(step); Refresh(); });
            controls.Children.Add(up); controls.Children.Add(down); controls.Children.Add(remove);
            DockPanel.SetDock(controls, Dock.Right); bar.Children.Add(controls);
            bar.Children.Add(Ui.Text(L.T("Step") + " " + (i + 1), 14, true));
            detail.Children.Add(bar);
            var caption = MediaWorkspaceLayout.NumericField(step.Caption);
            caption.MaxLength = 500; caption.MinHeight = 64; caption.TextWrapping = TextWrapping.Wrap;
            caption.AcceptsReturn = true; caption.Margin = new Thickness(0, 8, 0, 0);
            caption.TextChanged += (_, _) => step.Caption = caption.Text;
            System.Windows.Automation.AutomationProperties.SetName(caption, L.T("Step caption") + " " + (i + 1));
            var thumbnail = new Image { Source = step.Image, Stretch = Stretch.Uniform, Height = columns == 1 ? 240 : 140 };
            RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
            var frame = new Border { Child = thumbnail, Padding = new Thickness(5), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            frame.SetResourceReference(Border.BackgroundProperty, "Field"); frame.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            Point? dragStart = null;
            frame.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(frame);
            frame.PreviewMouseMove += (_, e) =>
            {
                if (dragStart == null || e.LeftButton != MouseButtonState.Pressed) return;
                var now = e.GetPosition(frame);
                if (Math.Abs(now.X - dragStart.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(now.Y - dragStart.Value.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                dragStart = null; DragDrop.DoDragDrop(frame, new DataObject("DesktopToolsGuideStep", step), DragDropEffects.Move);
            };
            detail.Children.Add(frame);
            var captionLabel = Ui.Text(L.T("Step caption"), 11, muted: true);
            captionLabel.Margin = new Thickness(0, 10, 0, 0);
            detail.Children.Add(captionLabel); detail.Children.Add(caption);
            var surface = Ui.Card(detail, 12); surface.CornerRadius = new CornerRadius(12);
            surface.Margin = new Thickness(0, 0, 10, 10); surface.AllowDrop = true; surface.VerticalAlignment = VerticalAlignment.Top;
            surface.DragOver += (_, e) => { e.Effects = e.Data.GetData("DesktopToolsGuideStep") is Entry ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            surface.Drop += (_, e) => { if (e.Data.GetData("DesktopToolsGuideStep") is Entry dragged) Move(dragged, step); e.Handled = true; };
            rows.Children.Add(surface);
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
            StepGuideComposer.Save(steps.Select(step => new GuideStep(step.Image, step.Caption)).ToArray(), titleInput.Text, dialog.FileName,
                new GuideLayoutOptions(columns, darkExport));
            status.Text = L.T("Guide saved: ") + dialog.FileName; report(status.Text);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        { status.Text = L.T("Could not export guide: ") + ex.Message; }
        finally { Cursor = null; }
    }
}
