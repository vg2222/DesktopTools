using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DesktopTools.Native;

namespace DesktopTools.UI;

internal sealed class ShortcutRecorderWindow : Window
{
    private readonly ContentControl preview = new();
    private readonly TextBlock status = Ui.Text(L.T("Press your new shortcut now."), 13, muted: true);
    private readonly TextBlock error = Ui.Text("", 12);
    private readonly Button saveButton;
    private readonly bool allowUnmodified;
    private bool recording = true;
    private string? candidate;
    private readonly Func<string, string?>? validate;
    internal bool Embedded { get; set; }
    private void Complete(bool saved) { if (Embedded) Close(); else DialogResult = saved; }

    public ShortcutRecorderWindow(string current, Func<string, bool> save, Func<string?>? lastError = null, bool allowUnmodified = false, Func<string, string?>? validate = null)
    {
        this.allowUnmodified = allowUnmodified;
        this.validate = validate;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent;
        Title = L.T("Record shortcut"); Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Motion.WindowEntrance(this);

        var panel = new StackPanel { Margin = new Thickness(4) };
        var heading = new DockPanel();
        var close = Ui.IconButton("Close", L.T("Close shortcut recorder"), Close); close.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        var title = Ui.Text(L.T("Record shortcut"), 24, true);
        title.MouseLeftButtonDown += (_, e) => { if (!Embedded && e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        heading.Children.Add(title); panel.Children.Add(heading);
        var currentLabel = Ui.Text(L.T("Current shortcut"), 12, muted: true); currentLabel.Margin = new Thickness(0, 16, 0, 6); panel.Children.Add(currentLabel);
        panel.Children.Add(Ui.Shortcut(current));
        if (!allowUnmodified && HotkeyGesture.TryParse(current, out var gesture, out _))
        {
            var choices = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var key = KeyInterop.KeyFromVirtualKey((int)gesture.VirtualKey);
            foreach (var modifiers in new[] { ModifierKeys.Control | ModifierKeys.Alt, ModifierKeys.Control | ModifierKeys.Shift, ModifierKeys.Alt | ModifierKeys.Shift })
            {
                if (!ShortcutRecording.TryFormat(key, modifiers, out string label, out _)) continue;
                var choice = Ui.Button(label, () => { recording = true; RecordGesture(key, modifiers); });
                choice.Tag = "suggest-" + label; choice.Margin = new Thickness(0, 0, 6, 6);
                choices.Children.Add(choice);
            }
            panel.Children.Add(choices);
        }
        status.Margin = new Thickness(0, 22, 0, 12); panel.Children.Add(status);
        preview.Content = Ui.Text(L.T("Waiting for keys…"), 16, true); preview.MinHeight = 44; panel.Children.Add(preview);
        error.Margin = new Thickness(0, 8, 0, 12); error.Visibility = Visibility.Collapsed; panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("Record again"), () =>
        {
            recording = true; candidate = null; saveButton!.IsEnabled = false; error.Visibility = Visibility.Collapsed;
            status.Text = L.T("Press your new shortcut now."); preview.Content = Ui.Text(L.T("Waiting for keys…"), 16, true); Focus();
        }));
        saveButton = Ui.Button(L.T("Save"), () =>
        {
            if (recording || candidate == null) return;
            if (save(candidate)) { Complete(true); return; }
            error.Text = lastError?.Invoke() ?? L.T("This shortcut could not be saved. Your current shortcut is unchanged.");
            error.Visibility = Visibility.Visible;
        }, true);
        saveButton.IsEnabled = false; saveButton.IsDefault = false; actions.Children.Add(saveButton);
        actions.Children.Add(Ui.Button(L.T("Cancel"), () => Complete(false))); panel.Children.Add(actions);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; SmoothScroll.Enable(scroll);
        var card = Ui.Card(scroll, 28); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        card.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            var modifiers = Keyboard.Modifiers;
            if (key == Key.Escape && modifiers == ModifierKeys.None) { e.Handled = true; Complete(false); return; }
            if (key == Key.Tab && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)) return;
            if (Keyboard.FocusedElement is Button && modifiers == ModifierKeys.None && key is Key.Enter or Key.Space) return;
            if (!recording) return;
            e.Handled = true;
            if (e.IsRepeat || ShortcutRecording.IsModifier(key)) return;
            RecordGesture(key, modifiers);
        };
        Loaded += (_, _) => Focus();
    }
    internal void RecordGesture(Key key, ModifierKeys modifiers)
    {
        if (!recording || ShortcutRecording.IsModifier(key)) return;
        bool valid = ShortcutRecording.TryFormat(key, modifiers, out var recorded, out var message, allowUnmodified);
        preview.Content = Ui.Shortcut(recorded);
        if (valid && validate?.Invoke(recorded) is string conflict) { valid = false; message = conflict; }
        candidate = null; saveButton.IsEnabled = false;
        if (!valid) { error.Text = message ?? L.T("Press a key together with a modifier."); error.Visibility = Visibility.Visible; return; }
        candidate = recorded; recording = false; error.Visibility = Visibility.Collapsed; saveButton.IsEnabled = true;
        status.Text = L.T("Review the shortcut, then choose Save.");
    }
}
