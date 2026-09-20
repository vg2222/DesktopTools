using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed class FloatingNoteWindow : Window
{
    private readonly TextBlock status = Ui.Text("", 11, muted: true);
    private readonly Button retry;
    private bool removing;

    internal FloatingNoteWindow(FloatingNote note, bool topmost, Action openLibrary, Func<bool> flush)
    {
        Tag = "Floating notes"; Width = 410; Height = 380; MinWidth = 320; MinHeight = 260;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = topmost; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var editor = new NoteEditorView(); editor.TitleEditor.FontSize = 22; editor.SetNote(note);
        var layout = new DockPanel();
        var header = UtilityWindowChrome.Header(this, L.T("Note"), Close, L.T("Close"), 14);
        var options = UtilityWindowChrome.CaptionButton("More", L.T("Note options"), () => { });
        DockPanel.SetDock(options, Dock.Right); header.Children.Insert(1, options);
        var library = UtilityWindowChrome.CaptionButton("Notes", L.T("All notes"), openLibrary);
        DockPanel.SetDock(library, Dock.Right); header.Children.Insert(2, library);
        var menu = new ContextMenu();
        var pin = new MenuItem { Header = L.T("Always on top"), IsCheckable = true, IsChecked = Topmost };
        pin.Click += (_, _) => Topmost = pin.IsChecked; menu.Items.Add(pin);
        var sizes = new MenuItem { Header = L.T("Text size") };
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
        var footer = new DockPanel { Margin = new Thickness(10, 12, 10, 0) };
        retry = Ui.Button(L.T("Retry saving"), () => { flush(); }); retry.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(retry, Dock.Right); footer.Children.Add(retry); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(editor);
        var card = Ui.Card(layout, 16); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        void UpdateTitle(object? sender, PropertyChangedEventArgs e) => Title = string.IsNullOrWhiteSpace(note.Title) ? L.T("Note · DesktopTools") : note.Title + " · DesktopTools";
        UpdateTitle(null, new(nameof(FloatingNote.Title))); note.PropertyChanged += UpdateTitle;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { flush(); e.Handled = true; } };
        Closing += (_, e) => { if (!removing && !flush()) e.Cancel = true; };
        Closed += (_, _) => { menu.IsOpen = false; editor.Dispose(); note.PropertyChanged -= UpdateTitle; };
        Loaded += (_, _) => editor.BodyEditor.Focus(); Motion.WindowEntrance(this);
    }
    internal void SetSaved(bool saved, bool failed = false)
    {
        status.Text = L.T(saved ? "Saved on this device" : failed ? "Could not save your changes" : "Saving…");
        retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void CloseForRemoval() { removing = true; Close(); }
}
