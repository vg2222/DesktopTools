using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed class SharingWelcomeWindow : Window
{
    internal SharingWelcomeWindow(System.Func<bool> acknowledge)
    {
        Title = L.T("Screen sharing"); Width = 480; Height = 300;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Background = Brushes.Transparent;
        UtilityWindowChrome.EnableBackdrop(this);
        var body = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        body.Children.Add(Ui.Text(L.T("Your tools can appear in screen shares"), 21, true));
        var explanation = Ui.Text(L.T("Drawing and audience effects stay visible. Recorder controls, drawing controls, notes, notifications and the teleprompter start hidden. Change each choice in Settings → Privacy."), 14);
        explanation.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(explanation);
        var done = Ui.Button(L.T("Got it"), () => { if (acknowledge()) Close(); }, true);
        done.MinWidth = 104; done.HorizontalAlignment = HorizontalAlignment.Right; done.IsDefault = true;
        var card = UtilityWindowChrome.DialogCard(this, UtilityWindowChrome.Header(this, Title, Close, L.T("Close")), body, done, 24);
        card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        Loaded += (_, _) => done.Focus();
        Motion.WindowEntrance(this);
    }
}
