using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed record RecordingSelection(string Label, MonitorInfo? Monitor = null, Rect? Region = null, RecordingWindowInfo? Window = null);

internal sealed class RecordingSourcePickerWindow : Window
{
    private readonly TextBox search = new();
    private readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly Border stage = new() { CornerRadius = new CornerRadius(10), ClipToBounds = true, Padding = new Thickness(8) };
    private readonly TextBlock caption = Ui.Text("", 13, true), hint = Ui.Text("", 12, muted: true);
    private readonly Button confirm;
    private readonly WindowThumbnail thumbnail = new();
    private readonly Func<IReadOnlyList<RecordingWindowInfo>> getWindows;
    private readonly List<Button> tabs = [];
    private IReadOnlyList<RecordingSelection> sources = [];
    private int mode, revision;
    private bool closed, busy;
    private SelectionWindow? selector;
    private readonly TaskCompletionSource<RecordingSelection?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<RecordingSelection?> Result => completion.Task;
    internal RecordingSelection? Selection { get; private set; }
    internal BitmapSource? SelectedPreview { get; private set; }

    internal RecordingSourcePickerWindow(Func<IReadOnlyList<RecordingWindowInfo>>? windows = null)
    {
        getWindows = windows ?? (() => RecordingWindows.GetAll()); Title = L.T("Recording source"); Width = 820; Height = 530; MinWidth = 720; MinHeight = 480;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        var root = new DockPanel(); var header = UtilityWindowChrome.Header(this, "DesktopTools — " + Title, Close, L.T("Close"), 13); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var top = new StackPanel(); top.Children.Add(Ui.Text(Title, 22, true)); top.Children.Add(Ui.Text(L.T("Choose what to record."), 12, muted: true));
        var strip = new Grid { Margin = new Thickness(0, 14, 0, 14) };
        var labels = new[] { "Display", "Window", "Region" }; var icons = new[] { "Monitor", "Window", "Crop" };
        for (int i = 0; i < 3; i++) { int index = i; strip.ColumnDefinitions.Add(new ColumnDefinition()); var button = Ui.Button(L.T(labels[i]), () => SetMode(index)); button.Content = Ui.IconLabel(icons[i], L.T(labels[i])); Grid.SetColumn(button, i); strip.Children.Add(button); tabs.Add(button); }
        top.Children.Add(strip); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var refresh = Ui.Button(L.T("Refresh"), Reload); refresh.Content = Ui.IconLabel("Refresh", L.T("Refresh")); DockPanel.SetDock(refresh, Dock.Left); bottom.Children.Add(refresh);
        confirm = Ui.Button(L.T("Select source"), async () => await ConfirmAsync(), true); confirm.MinWidth = 150; DockPanel.SetDock(confirm, Dock.Right); bottom.Children.Add(confirm);
        var cancel = Ui.Button(L.T("Cancel"), Close); cancel.Width = 110; DockPanel.SetDock(cancel, Dock.Right); bottom.Children.Add(cancel); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) }; Ui.Tip(search, L.T("Search sources")); System.Windows.Automation.AutomationProperties.SetName(search, L.T("Search sources"));
        var searchBox = new Grid { Margin = new Thickness(0, 0, 0, 10) }; searchBox.Children.Add(search); var searchHint = Ui.IconLabel("Search", L.T("Search sources")); searchHint.Margin = new Thickness(14, 0, 8, 0); searchHint.IsHitTestVisible = false; searchHint.Opacity = .65; searchBox.Children.Add(searchHint); search.TextChanged += (_, _) => searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DockPanel.SetDock(searchBox, Dock.Top); left.Children.Add(searchBox); left.Children.Add(list); columns.Children.Add(left);
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "Frame"; frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(9)); frame.SetValue(Border.PaddingProperty, new Thickness(12)); frame.SetValue(Border.BorderThicknessProperty, new Thickness(1)); frame.SetValue(Border.BorderBrushProperty, Brushes.Transparent); frame.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent }); frame.AppendChild(content);
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame };
        var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Selected"), "Frame")); selectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent"), "Frame")); template.Triggers.Add(selectedTrigger);
        var focusTrigger = new Trigger { Property = ListBoxItem.IsKeyboardFocusWithinProperty, Value = true }; focusTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent"), "Frame")); template.Triggers.Add(focusTrigger);
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.TemplateProperty, template)); list.ItemContainerStyle = itemStyle;
        var right = new DockPanel(); Grid.SetColumn(right, 1); columns.Children.Add(right); var descriptions = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; descriptions.Children.Add(caption); descriptions.Children.Add(hint); DockPanel.SetDock(descriptions, Dock.Bottom); right.Children.Add(descriptions); stage.Child = image; stage.SetResourceReference(Border.BackgroundProperty, "Card"); right.Children.Add(stage);
        var card = Ui.Card(root, 18); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        search.TextChanged += (_, _) => Filter(); list.SelectionChanged += async (_, _) => await PreviewAsync();
        Loaded += (_, _) => SetMode(0); SizeChanged += (_, _) => { if (IsLoaded) thumbnail.Update(this, stage); }; DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => thumbnail.Update(this, stage));
        Closed += (_, _) => { closed = true; revision++; selector?.Close(); thumbnail.Dispose(); image.Source = null; completion.TrySetResult(Selection); };
    }
    internal void SetMode(int value)
    {
        if (busy) return; mode = value; for (int i = 0; i < tabs.Count; i++) tabs[i].SetResourceReference(Control.BackgroundProperty, i == mode ? "Accent" : "Card"); Reload();
    }
    private void Reload()
    {
        if (busy || closed) return;
        sources = mode == 1 ? getWindows().Select(w => new RecordingSelection(w.Title, Window: w)).ToArray() : MonitorService.GetAll().Select((m, i) => new RecordingSelection($"{L.T("Monitor")} {i + 1} · {m.Bounds.Width:0} × {m.Bounds.Height:0}", m)).ToArray(); Filter();
    }
    private void Filter()
    {
        var selected = (list.SelectedItem as ListBoxItem)?.Tag as RecordingSelection;
        list.Items.Clear(); foreach (var source in sources.Where(s => s.Label.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            var text = Ui.Text(source.Label, 12); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis;
            var row = new DockPanel(); var icon = Ui.Icon(source.Window == null ? "Monitor" : "Window", 23); icon.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(icon); row.Children.Add(text);
            var item = new ListBoxItem { Content = row, Tag = source, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 6), ToolTip = source.Label }; item.SetResourceReference(Control.ForegroundProperty, "Text"); list.Items.Add(item);
            if (source == selected) list.SelectedItem = item;
        }
        if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
    }
    private async Task PreviewAsync()
    {
        int current = ++revision; thumbnail.Dispose(); image.Source = null; caption.Text = ""; hint.Text = ""; confirm.IsEnabled = false;
        if ((list.SelectedItem as ListBoxItem)?.Tag is not RecordingSelection source) return;
        caption.Text = source.Label;
        if (source.Window is { } window)
        {
            UpdateLayout(); bool available = RecordingWindows.Available(window); confirm.IsEnabled = available;
            hint.Text = L.T(!available ? "Source is no longer available. Refresh the list." : thumbnail.Show(this, stage, window.Handle) ? "" : "Preview is unavailable for this window."); return;
        }
        await Task.Delay(100);
        try { if (!closed && current == revision) { using var exclusion = new PreviewCaptureScope(); image.Source = CaptureService.Capture(source.Monitor!); confirm.IsEnabled = true; hint.Text = mode == 2 ? L.T("Select, then draw the recording area.") : ""; } }
        catch (Exception ex) { hint.Text = ex.Message; }
    }
    private async Task ConfirmAsync()
    {
        try { await ConfirmCoreAsync(); }
        catch (Exception ex) { busy = false; if (!closed) { hint.Text = ex.Message; Show(); Activate(); } }
    }
    private async Task ConfirmCoreAsync()
    {
        if (busy || (list.SelectedItem as ListBoxItem)?.Tag is not RecordingSelection source) return;
        if (source.Window is { } window && !RecordingWindows.Available(window)) { hint.Text = L.T("Source is no longer available. Refresh the list."); confirm.IsEnabled = false; return; }
        if (source.Monitor is { } monitor && !MonitorService.GetAll().Any(m => m.Id == monitor.Id && m.Bounds == monitor.Bounds)) { Reload(); return; }
        if (mode == 2)
        {
            busy = true; Hide(); thumbnail.Dispose();
            Rect? region;
            // Region selection needs an unobstructed desktop. Ordinary preview refreshes never hide windows.
            using (var hidden = new HiddenWindowsScope())
            {
                selector = new SelectionWindow(source.Monitor!);
                NativeWindowService.ShowForeground(selector);
                region = await selector.Result;
                selector = null;
            }
            busy = false; if (closed) return;
            if (region == null) { NativeWindowService.ShowForeground(this); return; }
            var m = source.Monitor!; var r = region.Value; var pixels = new Rect(Math.Floor(r.X * m.ScaleX), Math.Floor(r.Y * m.ScaleY), Math.Floor(r.Width * m.ScaleX / 2) * 2, Math.Floor(r.Height * m.ScaleY / 2) * 2);
            if (pixels.Width < 2 || pixels.Height < 2) { NativeWindowService.ShowForeground(this); return; }
            source = source with { Region = pixels, Label = source.Label + $" · {pixels.Width:0} × {pixels.Height:0}" };
        }
        SelectedPreview = image.Source as BitmapSource;
        if (SelectedPreview != null && source.Region is Rect crop) { SelectedPreview = new CroppedBitmap(SelectedPreview, new Int32Rect((int)crop.X, (int)crop.Y, (int)crop.Width, (int)crop.Height)); SelectedPreview.Freeze(); }
        Selection = source; Close();
    }
}
