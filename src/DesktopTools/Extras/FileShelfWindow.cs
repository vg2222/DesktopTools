using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Specialized;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed class FileShelfWindow : Window
{
    private readonly List<string> paths = [];
    private readonly StackPanel items = new();
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Action<string> report;
    public FileShelfWindow(Action<string> report)
    {
        this.report = report;
        Title = L.T("File shelf"); Width = 500; Height = 450; MinWidth = 400; MinHeight = 340;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResize; Topmost = true; AllowDrop = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel();
        var header = UtilityWindowChrome.Header(this, L.T("File shelf"), () => DesktopTools.Presentation.WindowDismissal.Hide(this, () => Motion.Enabled), L.T("Hide file shelf"));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var options = UtilityWindowChrome.CaptionButton("More", L.T("File shelf options"), () => { });
        DockPanel.SetDock(options, Dock.Right); header.Children.Insert(1, options);
        var menu = new ContextMenu();
        var pin = new MenuItem { Header = L.T("Always on top"), IsCheckable = true, IsChecked = Topmost };
        pin.Click += (_, _) => Topmost = pin.IsChecked; menu.Items.Add(pin);
        var copy = new MenuItem { Header = L.T("Copy all") };
        copy.Click += (_, _) => { try { var files = new StringCollection(); files.AddRange(Existing()); if (files.Count > 0) Clipboard.SetFileDropList(files); } catch (Exception ex) { report(L.F($"Could not copy files: {ex.Message}")); } }; menu.Items.Add(copy);
        var clear = new MenuItem { Header = L.T("Clear shelf") }; clear.Click += (_, _) => { paths.Clear(); Refresh(); }; menu.Items.Add(clear);
        options.ContextMenu = menu; options.Click += (_, _) => { pin.IsChecked = Topmost; menu.PlacementTarget = options; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) menu.IsOpen = false; };
        var dropContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        dropContent.Children.Add(Ui.Icon("Folder", 30));
        var dropTitle = Ui.Text(L.T("Drop files or folders"), 16, true); dropTitle.Margin = new Thickness(0, 8, 0, 0); dropContent.Children.Add(dropTitle);
        var target = Ui.Card(dropContent, 16); target.Margin = new Thickness(0, 0, 0, 12); target.SetResourceReference(Border.BackgroundProperty, "Field");
        DockPanel.SetDock(target, Dock.Top); root.Children.Add(target);
        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var addFiles = Ui.Button(L.T("Add files"), () => { var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true }; if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames); });
        addFiles.Content = Ui.IconLabel("File", L.T("Add files")); addFiles.Margin = new Thickness(0, 0, 6, 0); actions.Children.Add(addFiles);
        var addFolder = Ui.Button(L.T("Add folder"), () => { var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true }; if (dialog.ShowDialog(this) == true) AddPaths(dialog.FolderNames); });
        addFolder.Content = Ui.IconLabel("Folder", L.T("Add folder")); addFolder.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(addFolder, 1); actions.Children.Add(addFolder);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        root.Children.Add(scroll);
        var card = Ui.Card(root, 20); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        PreviewDragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddPaths(files); e.Handled = true; };
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; DesktopTools.Presentation.WindowDismissal.Hide(this, () => Motion.Enabled); } };
        Motion.WindowEntrance(this); Refresh();
    }
    private bool quitting;
    public void Shutdown() { quitting = true; Close(); paths.Clear(); }
    internal IReadOnlyList<string> Paths => paths;
    internal void AddPaths(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (paths.Count >= 100) { report(L.T("File shelf holds up to 100 items.")); break; }
            try
            {
                var path = Path.GetFullPath(file);
                if (!File.Exists(path) && !Directory.Exists(path)) report(L.F($"File unavailable: {path}"));
                else if (paths.Contains(path, StringComparer.OrdinalIgnoreCase)) report(L.F($"Already on the shelf: {Path.GetFileName(path)}"));
                else paths.Add(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { report(L.F($"Could not add file: {ex.Message}")); }
        }
        Refresh(); Motion.Transition(items);
    }
    private string[] Existing() => paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
    private void Refresh()
    {
        items.Children.Clear();
        scroll.Content = items;
        if (paths.Count == 0) items.Children.Add(Ui.Text(L.T("Drag them out whenever you need them."), 12, muted: true));
        foreach (var path in paths)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var remove = Ui.IconButton("Close", L.F($"Remove {Path.GetFileName(path)} from shelf"), () => { paths.Remove(path); Refresh(); }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var file = Ui.Button(Path.GetFileName(path), () => { });
            var content = new DockPanel();
            var icon = Ui.Icon(Directory.Exists(path) ? "Folder" : "File", 34); icon.Margin = new Thickness(0, 0, 12, 0); icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left); content.Children.Add(icon);
            var label = new StackPanel();
            var name = Ui.Text(Path.GetFileName(path), 14, true); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            label.Children.Add(name);
            var detail = Ui.Text(Directory.Exists(path) ? L.T("Folder · drag to copy") : L.F($"{Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} file · drag to copy"), 11, muted: true); detail.Margin = new Thickness(0, 4, 0, 0); label.Children.Add(detail);
            content.Children.Add(label); file.Content = content; file.Padding = new Thickness(12, 10, 12, 10); file.ToolTip = path; file.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Point start = default; bool pressed = false;
            file.PreviewMouseLeftButtonDown += (_, e) => { start = e.GetPosition(file); pressed = true; };
            file.PreviewMouseLeftButtonUp += (_, _) => pressed = false;
            file.PreviewMouseMove += (_, e) =>
            {
                if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
                var now = e.GetPosition(file); if (Math.Abs(now.X-start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y-start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pressed = false;
                if (!File.Exists(path) && !Directory.Exists(path)) { report(L.F($"File unavailable: {path}")); return; }
                try { DragDrop.DoDragDrop(file, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy); }
                catch (Exception ex) { report(L.F($"Could not drag file: {ex.Message}")); }
            };
            row.Children.Add(file); items.Children.Add(row);
        }
    }
}
