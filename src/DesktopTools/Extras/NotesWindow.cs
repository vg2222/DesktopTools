using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>The collection shares note objects with floating editors and owns no storage.</summary>
internal sealed class NotesWindow : Window
{
    private readonly IReadOnlyList<FloatingNote> notes;
    private readonly TextBox search = new() { Name = "NotesSearch", MinWidth = 80 };
    private readonly StackPanel entries = new();
    private readonly Dictionary<Guid, (Button Row, TextBlock Title, TextBlock Preview)> items = new();
    private readonly NoteEditorView editor = new();
    private readonly TextBlock status = Ui.Text("", 12, muted: true);
    private readonly TextBlock listStatus = Ui.Text("", 12, muted: true);
    private readonly Button retry, floating;
    private readonly Border sheet;
    private readonly StackPanel empty;
    private readonly MenuItem delete;
    private FloatingNote? selected;

    internal NotesWindow(IReadOnlyList<FloatingNote> notes, Action create, Action<FloatingNote> remove,
        Action<FloatingNote> openFloating, Func<bool> flush, bool topmost)
    {
        this.notes = notes;
        Title = L.T("Floating notes"); Tag = "Floating notes";
        Width = 920; Height = 620; MinWidth = 700; MinHeight = 430;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = topmost;
        var layout = new DockPanel();
        var header = UtilityWindowChrome.Header(this, L.T("Notes"), Close, L.T("Close"), 20);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var subtitle = Ui.Text(L.T("Your notes, saved automatically on this device."), 12, muted: true);
        subtitle.Margin = new Thickness(0, 0, 0, 18); DockPanel.SetDock(subtitle, Dock.Top); layout.Children.Add(subtitle);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        var controls = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        void Create() { search.Clear(); create(); }
        var add = Ui.Button(L.T("New note"), Create, true); add.Content = Ui.IconLabel("Plus", L.T("New note"), primary: true);
        add.Name = "NewNote"; add.Margin = new Thickness(0, 0, 0, 10); add.HorizontalContentAlignment = HorizontalAlignment.Center;
        controls.Children.Add(add); AutomationProperties.SetName(search, L.T("Search notes"));
        var searchField = new Grid(); searchField.Children.Add(search);
        var hint = Ui.Text(L.T("Search notes"), 12, muted: true); hint.Margin = new Thickness(12, 0, 0, 0); hint.IsHitTestVisible = false;
        search.TextChanged += (_, _) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchField.Children.Add(hint); controls.Children.Add(searchField);
        DockPanel.SetDock(controls, Dock.Top); left.Children.Add(controls);
        listStatus.Margin = new Thickness(4, 10, 0, 0); DockPanel.SetDock(listStatus, Dock.Bottom); left.Children.Add(listStatus);
        left.Children.Add(new ScrollViewer { Content = entries, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        columns.Children.Add(left);
        var right = new Grid(); Grid.SetColumn(right, 1); columns.Children.Add(right);
        var content = new DockPanel();
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var options = Ui.IconButton("More", L.T("Note options"), () => { }); DockPanel.SetDock(options, Dock.Right); toolbar.Children.Add(options);
        floating = Ui.Button(L.T("Open as floating note"), () => { if (selected != null) openFloating(selected); });
        floating.Content = Ui.IconLabel("Pin", L.T("Open as floating note")); floating.HorizontalAlignment = HorizontalAlignment.Left;
        floating.BorderThickness = new Thickness(0); floating.Background = Brushes.Transparent; toolbar.Children.Add(floating);
        var menu = new ContextMenu();
        var pin = new MenuItem { Header = L.T("Always on top"), IsCheckable = true, IsChecked = Topmost };
        pin.Click += (_, _) => Topmost = pin.IsChecked;
        delete = new MenuItem { Header = L.T("Delete note") }; delete.Click += (_, _) => { if (selected != null) remove(selected); };
        menu.Items.Add(pin); menu.Items.Add(new Separator()); menu.Items.Add(delete);
        options.ContextMenu = menu; options.Click += (_, _) => { menu.PlacementTarget = options; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        DockPanel.SetDock(toolbar, Dock.Top); content.Children.Add(toolbar);
        var footer = new DockPanel { Margin = new Thickness(10, 16, 0, 0) };
        retry = Ui.Button(L.T("Retry saving"), () => { flush(); }); retry.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(retry, Dock.Right); footer.Children.Add(retry); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer); content.Children.Add(editor);
        sheet = new Border { Child = content, Padding = new Thickness(18), CornerRadius = new CornerRadius(12) };
        sheet.SetResourceReference(Border.BackgroundProperty, "Field"); right.Children.Add(sheet);
        empty = new StackPanel { MaxWidth = 340, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var symbol = Ui.Icon("Notes", 44); symbol.HorizontalAlignment = HorizontalAlignment.Left; symbol.Margin = new Thickness(0, 0, 0, 20); empty.Children.Add(symbol);
        empty.Children.Add(Ui.Text(L.T("A place for your next thought"), 22, true));
        var detail = Ui.Text(L.T("Create a note and start typing. Changes save automatically. Keep any note on your desktop with Open as floating note."), 13, muted: true);
        detail.Margin = new Thickness(0, 12, 0, 20); empty.Children.Add(detail);
        var first = Ui.Button(L.T("Create a note"), Create, true); first.HorizontalAlignment = HorizontalAlignment.Left; empty.Children.Add(first); right.Children.Add(empty);
        layout.Children.Add(columns);
        var card = Ui.Card(layout, 22); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        search.TextChanged += (_, _) => RefreshList();
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.N) { Create(); e.Handled = true; }
            else if (e.Key == Key.F) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.S) { flush(); e.Handled = true; }
        };
        Closing += (_, e) => { if (!flush()) e.Cancel = true; };
        Closed += (_, _) => { menu.IsOpen = false; editor.Dispose(); };
        Refresh(); Motion.WindowEntrance(this);
    }
    internal void Select(FloatingNote note) { selected = note; Refresh(); editor.BodyEditor.Focus(); }
    internal void SetSaved(bool saved, bool failed = false)
    {
        status.Text = L.T(saved ? "Saved on this device" : failed ? "Could not save your changes" : "Saving…");
        retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void Refresh()
    {
        if (selected == null || !notes.Contains(selected)) selected = notes.FirstOrDefault();
        editor.SetNote(selected); delete.IsEnabled = floating.IsEnabled = selected != null;
        sheet.Visibility = selected != null ? Visibility.Visible : Visibility.Collapsed;
        empty.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
        RefreshList();
    }
    private void RefreshList()
    {
        foreach (var id in items.Keys.Where(id => !notes.Any(n => n.Id == id)).ToArray())
        { entries.Children.Remove(items[id].Row); items.Remove(id); }
        string query = search.Text.Trim(); int visible = 0;
        foreach (var note in notes)
        {
            if (!items.TryGetValue(note.Id, out var item))
            {
                var heading = Ui.Text("", 14, true); heading.TextWrapping = TextWrapping.NoWrap; heading.TextTrimming = TextTrimming.CharacterEllipsis;
                var preview = Ui.Text("", 12, muted: true); preview.TextWrapping = TextWrapping.NoWrap; preview.TextTrimming = TextTrimming.CharacterEllipsis; preview.Margin = new Thickness(0, 7, 0, 0);
                var panel = new StackPanel(); panel.Children.Add(heading); panel.Children.Add(preview);
                var row = Ui.Button("", () => Select(note)); row.Content = panel; row.Tag = note.Id;
                row.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.Margin = new Thickness(0, 0, 0, 6); row.Padding = new Thickness(14);
                row.BorderThickness = new Thickness(0); item = (row, heading, preview); items.Add(note.Id, item);
            }
            item.Title.Text = string.IsNullOrWhiteSpace(note.Title) ? L.T("Untitled note") : note.Title;
            string previewText = note.Body.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            item.Preview.Text = string.IsNullOrWhiteSpace(previewText) ? L.T("Empty note") : previewText[..Math.Min(100, previewText.Length)];
            item.Row.SetResourceReference(BackgroundProperty, note == selected ? "Selected" : "GlassSurface");
            AutomationProperties.SetName(item.Row, item.Title.Text);
            bool matches = note.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || note.Body.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            if (!matches) { entries.Children.Remove(item.Row); continue; }
            if (entries.Children.IndexOf(item.Row) != visible) { entries.Children.Remove(item.Row); entries.Children.Insert(visible, item.Row); }
            visible++;
        }
        listStatus.Text = visible == 0 ? L.T(notes.Count == 0 ? "No notes yet" : "No notes found") : L.F($"{visible} notes");
    }
}
