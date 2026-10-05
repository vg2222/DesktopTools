using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>Search every saved screenshot by the words in it. Cards open the editor, copy the image or remove it from the library.</summary>
internal sealed class ScreenshotLibraryWindow : Window
{
    private readonly AppController controller;
    private readonly ScreenshotLibrary library;
    private readonly TextBox search = new() { MinHeight = 38, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 0, 12, 0) };
    private readonly WrapPanel results = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock summary = Ui.Text("", 12, muted: true);
    private readonly TextBlock empty = Ui.Text("", 13, muted: true);
    private readonly System.Windows.Threading.DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private int shown;
    private const int Page = 36;

    internal ScreenshotLibraryWindow(AppController controller, ScreenshotLibrary library)
    {
        this.controller = controller; this.library = library;
        Title = L.T("Screenshot library"); Width = 980; Height = 700; MinWidth = 640; MinHeight = 440;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; Background = Brushes.Transparent; ShowInTaskbar = true;
        UtilityWindowChrome.EnableBackdrop(this); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel();
        var header = UtilityWindowChrome.Header(this, "DesktopTools — " + L.T("Screenshot library"), Close, L.T("Close"), 13, allowMinimize: true);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var top = new DockPanel { Margin = new Thickness(0, 6, 0, 8) };
        var clear = Ui.Button(L.T("Clear library"), ClearLibrary, ButtonKind.Danger);
        DockPanel.SetDock(clear, Dock.Right); clear.Margin = new Thickness(10, 0, 0, 0); top.Children.Add(clear);
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("Search text in screenshots"));
        search.SetResourceReference(BackgroundProperty, "Field"); search.SetResourceReference(ForegroundProperty, "Text"); search.SetResourceReference(BorderBrushProperty, "Stroke");
        search.ToolTip = L.T("Type words that appear on a screenshot"); top.Children.Add(search);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        summary.Margin = new Thickness(2, 0, 0, 4); DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary);
        var note = Ui.Text(L.T("Text from your screenshots is read and stored only on this PC. Turn the library off or clear it at any time."), 11, muted: true);
        note.Margin = new Thickness(2, 8, 0, 0); note.TextWrapping = TextWrapping.Wrap; DockPanel.SetDock(note, Dock.Bottom); root.Children.Add(note);

        var content = new StackPanel(); content.Children.Add(empty); content.Children.Add(results);
        var more = Ui.Button(L.T("Show more"), () => { Render(more: true); }); more.Margin = new Thickness(0, 12, 0, 0); more.HorizontalAlignment = HorizontalAlignment.Center; more.Name = "ShowMore";
        content.Children.Add(more);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(scroll);
        Content = Ui.Card(root, 16);

        debounce.Tick += (_, _) => { debounce.Stop(); Render(); };
        search.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        library.Changed += OnChanged; Closed += (_, _) => { library.Changed -= OnChanged; debounce.Stop(); };
        Loaded += (_, _) => { Render(); search.Focus(); };
        Motion.WindowEntrance(this);
        void OnChanged() => Dispatcher.BeginInvoke(() => Render());
        this.more = more;
    }
    private readonly Button more;

    private void Render(bool more = false)
    {
        var found = library.Index.Search(search.Text);
        if (!more) { shown = Page; results.Children.Clear(); } else shown += Page;
        int total = library.Index.Count;
        summary.Text = string.IsNullOrWhiteSpace(search.Text) ? L.F($"{total} screenshots") : L.F($"{found.Count} of {total} screenshots match");
        empty.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        empty.Text = total == 0 ? L.T("Nothing here yet. Turn on the library in Capture settings; new screenshots are added from then on.") : L.T("No screenshot contains these words.");
        empty.Margin = new Thickness(2, 30, 0, 0);
        results.Children.Clear();
        foreach (var entry in found.Take(shown)) results.Children.Add(Card(entry, search.Text));
        this.more.Visibility = found.Count > shown ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement Card(LibraryEntry entry, string query)
    {
        var card = new StackPanel { Width = 232 };
        System.Windows.Controls.Image picture;
        try { picture = new System.Windows.Controls.Image { Source = ScreenshotLibrary.LoadImage(library.Index.ThumbnailPath(entry.Id), 232), Height = 130, Stretch = Stretch.UniformToFill, ClipToBounds = true }; }
        catch (Exception) { picture = new System.Windows.Controls.Image { Height = 130 }; }
        var frame = new Border { Child = picture, CornerRadius = new CornerRadius(8), ClipToBounds = true, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "Stroke"); card.Children.Add(frame);
        var when = Ui.Text(entry.CreatedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) + $"  ·  {entry.Width} × {entry.Height}", 11, muted: true); when.Margin = new Thickness(2, 8, 0, 0); card.Children.Add(when);
        var snippet = Ui.Text(entry.Text.Length == 0 ? L.T("No text found") : ScreenshotLibraryIndex.Snippet(entry, query), 12); snippet.TextWrapping = TextWrapping.Wrap; snippet.MaxHeight = 34; snippet.Margin = new Thickness(2, 3, 0, 0); card.Children.Add(snippet);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Ui.ActionButton("Pen", L.T("Open"), () => OpenEntry(entry), ButtonKind.Secondary, 12));
        actions.Children.Add(Ui.IconButton("Copy", L.T("Copy image"), () => _ = CopyEntry(entry)));
        actions.Children.Add(Ui.IconButton("Clear", L.T("Remove from library"), () => library.Remove(entry.Id)));
        card.Children.Add(actions);
        var host = new Border { Child = card, Padding = new Thickness(8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand };
        host.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not (System.Windows.Documents.Run or Button) && !IsInsideButton(e.OriginalSource as DependencyObject)) OpenEntry(entry); };
        return host;
    }
    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source != null) { if (source is Button) return true; source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source); }
        return false;
    }

    private void OpenEntry(LibraryEntry entry)
    {
        try { controller.OpenRecentCapture(ScreenshotLibrary.LoadImage(library.Index.ImagePath(entry.Id))); }
        catch (Exception ex) { controller.Report(L.T("Could not open the screenshot: ") + ex.Message, NotificationKind.Warning); }
    }
    private async Task CopyEntry(LibraryEntry entry)
    {
        try { await controller.CopyAsync(ScreenshotLibrary.LoadImage(library.Index.ImagePath(entry.Id))); controller.Report(L.T("Screenshot copied to clipboard.")); }
        catch (Exception ex) { controller.Report(L.T("Could not copy the screenshot: ") + ex.Message, NotificationKind.Warning); }
    }
    private void ClearLibrary()
    {
        if (library.Index.Count == 0) return;
        if (ConfirmationDialog.Ask(this, L.T("Clear library"), L.T("Delete every screenshot and its searchable text from the library? Files you saved elsewhere are not touched."), L.T("Clear library"))) library.Clear();
    }
}
