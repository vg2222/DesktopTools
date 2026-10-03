using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>A small note kept above other work. Its title lives in the header; the body uses the whole sheet.</summary>
internal sealed class FloatingNoteWindow : Window
{
    private readonly TextBlock status = Ui.Text("", 11, muted: true);
    private readonly Button retry;
    private readonly FloatingNote note;
    private readonly DockPanel footer = new() { Margin = new Thickness(10, 10, 10, 0), Visibility = Visibility.Collapsed };
    private bool removing;

    internal FloatingNoteWindow(FloatingNote note, bool topmost, Action openLibrary, Func<bool> flush)
    {
        this.note = note;
        Tag = "Floating notes"; Width = 410; Height = 380; MinWidth = 320; MinHeight = 260;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = topmost; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var editor = new NoteEditorView { ShowTitle = false }; editor.SetNote(note);
        var layout = new DockPanel();
        var header = UtilityWindowChrome.Header(this, L.T("Note"), Close, L.T("Close"), 14);
        header.Margin = new Thickness(0, 0, 0, 4);
        var options = UtilityWindowChrome.CaptionButton("More", L.T("Note options"), () => { });
        DockPanel.SetDock(options, Dock.Right); header.Children.Insert(1, options);
        var library = UtilityWindowChrome.CaptionButton("Notes", L.T("All notes"), openLibrary);
        DockPanel.SetDock(library, Dock.Right); header.Children.Insert(2, library);

        // The note's own title replaces the fixed "Note" caption and stays editable in place.
        var titleBox = new TextBox
        {
            Text = note.Title, MaxLength = NotesStore.MaximumTitleLength, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(2, 4, 8, 4),
            MinHeight = 0, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Template = BareTextBox()
        };
        titleBox.SetResourceReference(Control.ForegroundProperty, "Text"); titleBox.SetResourceReference(TextBoxBase.CaretBrushProperty, "Text");
        System.Windows.Automation.AutomationProperties.SetName(titleBox, L.T("Note title"));
        var titleHint = Ui.Text(L.T("Untitled note"), 15, true, muted: true);
        titleHint.Margin = new Thickness(4, 0, 8, 0); titleHint.IsHitTestVisible = false; titleHint.TextTrimming = TextTrimming.CharacterEllipsis;
        titleHint.VerticalAlignment = VerticalAlignment.Center;
        var titleHost = new Grid(); titleHost.Children.Add(titleBox); titleHost.Children.Add(titleHint);
        bool syncingTitle = false;
        void ShowHint() => titleHint.Visibility = titleBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        titleBox.TextChanged += (_, _) => { ShowHint(); if (!syncingTitle) { syncingTitle = true; note.Title = titleBox.Text; syncingTitle = false; } };
        void FromNote(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(FloatingNote.Title) || syncingTitle || titleBox.Text == note.Title) return;
            syncingTitle = true; titleBox.Text = note.Title; syncingTitle = false;
        }
        note.PropertyChanged += FromNote; ShowHint();
        if (header.Children[^1] is Border dragArea) dragArea.Child = titleHost;

        var menu = new ContextMenu();
        var sizes = new MenuItem { Header = L.T("Text size"), Icon = Ui.Icon("Text", 16) };
        foreach (double size in new[] { 12d, 15d, 18d, 22d })
        {
            var choice = new MenuItem { Header = L.F($"{size:0} px"), IsCheckable = true, IsChecked = size == editor.BodyEditor.FontSize };
            choice.Click += (_, _) => { editor.BodyEditor.FontSize = size; foreach (MenuItem item in sizes.Items) item.IsChecked = item == choice; };
            sizes.Items.Add(choice);
        }
        menu.Items.Add(sizes);
        var wrap = new MenuItem { Header = L.T("Wrap text"), IsCheckable = true, IsChecked = true };
        wrap.Click += (_, _) =>
        {
            editor.BodyEditor.TextWrapping = wrap.IsChecked ? TextWrapping.Wrap : TextWrapping.NoWrap;
            editor.BodyEditor.HorizontalScrollBarVisibility = wrap.IsChecked ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        };
        menu.Items.Add(wrap); options.ContextMenu = menu;
        options.Click += (_, _) => { menu.PlacementTarget = options; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);

        // Saving is automatic, so the footer only appears when something needs attention.
        retry = Ui.Button(L.T("Retry saving"), () => { flush(); }); retry.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(retry, Dock.Right); footer.Children.Add(retry); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(editor);
        var card = Ui.Card(layout, 14); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        void UpdateTitle(object? sender, PropertyChangedEventArgs e) => Title = string.IsNullOrWhiteSpace(note.Title) ? L.T("Note · DesktopTools") : note.Title + " · DesktopTools";
        UpdateTitle(null, new(nameof(FloatingNote.Title))); note.PropertyChanged += UpdateTitle;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { flush(); e.Handled = true; } };
        Closing += (_, e) => { if (!removing && !flush()) e.Cancel = true; };
        Closed += (_, _) => { menu.IsOpen = false; editor.Dispose(); note.PropertyChanged -= UpdateTitle; note.PropertyChanged -= FromNote; };
        Loaded += (_, _) => editor.BodyEditor.Focus();
        Motion.WindowEntrance(this);
    }
    private static ControlTemplate BareTextBox()
    {
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FocusableProperty, false);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = host };
    }
    internal void SetSaved(bool saved, bool failed = false)
    {
        status.Text = failed ? L.T("Could not save your changes") : "";
        retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        footer.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void CloseForRemoval() { removing = true; Close(); }
}
