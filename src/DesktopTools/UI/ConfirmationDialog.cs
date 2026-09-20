using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Localization;
using DesktopTools.Native;

namespace DesktopTools.UI;

/// <summary>Explicit confirmation; closing, Escape and default Enter all cancel.</summary>
internal sealed class ConfirmationDialog : Window
{
    internal bool Accepted { get; private set; }
    internal ConfirmationDialog(string title, string message, string confirmLabel)
    {
        Title = title; Width = 460; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new StackPanel();
        var header = UtilityWindowChrome.Header(this, title, Close, L.T("Cancel"), 20);
        var description = Ui.Text(message); description.Margin = new Thickness(0, 8, 0, 22);
        content.Children.Add(description);
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = Ui.Button(L.T("Cancel"), Close);
        cancel.Name = "CancelConfirmation"; cancel.IsCancel = true; cancel.IsDefault = true;
        cancel.Margin = new Thickness(0, 0, 6, 0);
        var confirm = Ui.Button(confirmLabel, () => { Accepted = true; Close(); }, true);
        confirm.Name = "AcceptConfirmation"; confirm.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(confirm, 1); actions.Children.Add(cancel); actions.Children.Add(confirm);
        Content = UtilityWindowChrome.DialogCard(this, header, content, actions, 24);
        SourceInitialized += (_, _) => NativeWindowService.TryExcludeFromCapture(this, true, out _);
        Loaded += (_, _) => cancel.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Motion.WindowEntrance(this);
    }

    internal static bool Ask(Window owner, string title, string message, string confirmLabel)
    {
        var previousFocus = Keyboard.FocusedElement;
        var dialog = new ConfirmationDialog(title, message, confirmLabel) { Owner = owner };
        try { dialog.ShowDialog(); return dialog.Accepted; }
        finally { if (previousFocus is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus(); }
    }
}
