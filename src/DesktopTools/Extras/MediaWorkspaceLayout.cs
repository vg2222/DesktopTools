using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopTools.Extras;

/// <summary>Small, media-only layout primitives shared by the image and video workspaces.</summary>
internal static class MediaWorkspaceLayout
{
    internal static Border Section(string icon, string title, string detail, UIElement content)
    {
        var stack = new StackPanel();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var glyph = Ui.Icon(icon, 18); glyph.Margin = new Thickness(0, 1, 10, 0);
        DockPanel.SetDock(glyph, Dock.Left); heading.Children.Add(glyph);
        var copy = new StackPanel();
        copy.Children.Add(Ui.Text(L.T(title), 14, true));
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var description = Ui.Text(L.T(detail), 11, muted: true);
            description.Margin = new Thickness(0, 3, 0, 0); description.TextWrapping = TextWrapping.Wrap;
            copy.Children.Add(description);
        }
        heading.Children.Add(copy); stack.Children.Add(heading); stack.Children.Add(content);
        var card = Ui.Card(stack, 14); card.CornerRadius = new CornerRadius(12);
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        card.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        return card;
    }

    internal static Button Tab(string icon, string label, Action action)
    {
        var button = Ui.Button("", action);
        button.Content = Ui.IconLabel(icon, L.T(label), 15);
        button.Height = 34; button.MinHeight = 34; button.MinWidth = 0;
        button.Padding = new Thickness(10, 5, 10, 5); button.Margin = new Thickness(0, 0, 6, 6);
        System.Windows.Automation.AutomationProperties.SetName(button, L.T(label));
        return button;
    }

    internal static void SelectTab(IEnumerable<KeyValuePair<string, Button>> tabs, string selected)
    {
        foreach (var tab in tabs)
        {
            bool active = tab.Key == selected;
            tab.Value.SetResourceReference(Control.BackgroundProperty, active ? "Selected" : "Field");
            tab.Value.SetResourceReference(Control.BorderBrushProperty, active ? "Accent" : "Stroke");
            tab.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    internal static TextBox NumericField(string text = "")
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Control.BorderBrush)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Control.BorderThickness)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        border.AppendChild(host);
        return new TextBox
        {
            Text = text,
            MinHeight = 36,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 12,
            Template = new ControlTemplate(typeof(TextBox)) { VisualTree = border }
        };
    }
    internal static Border Status(UIElement content)
    {
        var surface = new Border { Child = content, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8) };
        surface.SetResourceReference(Border.BackgroundProperty, "Field");
        surface.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        surface.BorderThickness = new Thickness(1);
        return surface;
    }
}
