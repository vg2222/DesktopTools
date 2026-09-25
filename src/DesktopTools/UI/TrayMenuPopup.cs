using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopTools.UI;

internal sealed class TrayMenuPopup : Popup
{
    internal TrayMenuPopup(Action open, Action recorder, Action pinScreenshot, Action quickActions, Action quit)
    {
        AllowsTransparency = true;
        StaysOpen = false;
        Placement = PlacementMode.MousePoint;
        PopupAnimation = PopupAnimation.Fade;

        var content = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 1, 0, 12) };
        title.Children.Add(Ui.AppIcon(26));
        var name = Ui.Text("DesktopTools", 15, true); name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(9, 0, 0, 0); title.Children.Add(name); content.Children.Add(title);

        AddAction("Home", "Open DesktopTools", open, true);
        AddAction("Record", "Screen recorder", recorder);
        AddAction("Pin", "Pin screenshot", pinScreenshot);
        AddAction("More", "Quick actions", quickActions);

        var divider = new Border { Height = 1, Margin = new Thickness(4, 9, 4, 8) };
        divider.SetResourceReference(Border.BackgroundProperty, "Stroke"); content.Children.Add(divider);
        AddAction("Close", "Quit", quit);

        var card = new Border { Child = content, Width = 268, Padding = new Thickness(12), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
        card.SetResourceReference(Border.BackgroundProperty, "Card"); card.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        Child = card;
        Opened += (_, _) => Ui.ExcludePopup(card);
        card.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { IsOpen = false; e.Handled = true; } };

        void AddAction(string icon, string label, Action action, bool primary = false)
        {
            var button = Ui.Button(L.T(label), () =>
            {
                IsOpen = false;
                Application.Current.Dispatcher.BeginInvoke(action);
            }, primary);
            button.Content = Ui.IconLabel(icon, L.T(label), 17, primary);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 2, 0, 2);
            button.MinHeight = 39;
            content.Children.Add(button);
        }
    }
}
