using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Automation;
using DesktopTools.Core;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed partial class AnnotationTextEditor
{
    private readonly Popup formatting = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Top, VerticalOffset = -8 };
    private static void RefreshStyle(Button button, bool selected)
    {
        button.SetResourceReference(Control.BackgroundProperty, selected ? "Accent" : "Field");
        button.SetResourceReference(Control.ForegroundProperty, selected ? "AccentText" : "Text");
    }
    partial void InitializeFormatting()
    {
        double fontSize = FontSize;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        string currentFamily = FontFamily.Source.Contains("#Inter") ? "Inter" : FontFamily.Source;
        var family = Ui.Choice(new[] { currentFamily, "Inter", "Segoe UI", "Arial" }.Distinct(), currentFamily, value => { FontFamily = AnnotationTypography.Resolve(value == "Inter" ? AnnotationTypography.BundledFamily : value); Focus(); }, translate: false); family.MinWidth = 120; family.Width = 130; family.Margin = new Thickness(0, 0, 8, 0); toolbar.Children.Add(family);
        var size = Ui.Choice(new[] { 12d, 16, 20, 24, 32, 48, 64, 96 }.Append(fontSize).Distinct().OrderBy(v => v).Select(v => v.ToString()), fontSize.ToString(), value => { FontSize = double.Parse(value); Focus(); }, translate: false); size.MinWidth = size.Width = 65; size.Margin = new Thickness(0, 0, 8, 0); toolbar.Children.Add(size);
        var bold = Ui.Button("B", () => { FontWeight = FontWeight == FontWeights.Bold ? FontWeights.Normal : FontWeights.Bold; Focus(); }); bold.FontWeight = FontWeights.Bold; bold.Click += (_, _) => RefreshStyle(bold, FontWeight == FontWeights.Bold); bold.Width = 36; Ui.Tip(bold, L.T("Bold")); AutomationProperties.SetName(bold, L.T("Bold")); toolbar.Children.Add(bold);
        var italic = Ui.Button("I", () => { FontStyle = FontStyle == FontStyles.Italic ? FontStyles.Normal : FontStyles.Italic; Focus(); }); italic.FontStyle = FontStyles.Italic; italic.Click += (_, _) => RefreshStyle(italic, FontStyle == FontStyles.Italic); italic.Width = 36; Ui.Tip(italic, L.T("Italic")); AutomationProperties.SetName(italic, L.T("Italic")); toolbar.Children.Add(italic);
        var colorButton = Ui.IconButton("Color", L.T("Color"), () =>
        {
            var owner = Window.GetWindow(this); formatting.IsOpen = false;
            var picker = new ColorPickerWindow(((SolidColorBrush)Foreground).Color.ToString(), value => { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); CaretBrush = Foreground; var color = ((SolidColorBrush)Foreground).Color; Background = new SolidColorBrush(color.R * .2126 + color.G * .7152 + color.B * .0722 > 155 ? Color.FromRgb(28, 32, 40) : Color.FromRgb(250, 251, 254)); return true; });
            if (owner != null) picker.Owner = owner;
            if (owner is OverlayWindow drawing) drawing.ShowControlDialog(picker); else picker.ShowDialog();
            if (IsLoaded && IsVisible) { formatting.IsOpen = true; Focus(); }
        }); toolbar.Children.Add(colorButton);
        toolbar.Children.Add(Ui.IconButton("Check", L.T("Apply"), RequestCommit)); toolbar.Children.Add(Ui.IconButton("Close", L.T("Cancel"), RequestCancel));
        var card = Ui.Card(toolbar, 8); card.Margin = new Thickness(0); formatting.Child = card; formatting.PlacementTarget = this;
        System.Windows.Documents.TextElement.SetFontFamily(card, AnnotationTypography.Resolve(AnnotationTypography.BundledFamily));
        System.Windows.Documents.TextElement.SetFontStyle(card, FontStyles.Normal);
        System.Windows.Documents.TextElement.SetFontWeight(card, FontWeights.Normal);
        card.PreviewKeyDown += (_, e) =>
        {
            if (family.IsDropDownOpen || size.IsDropDownOpen) return;
            if (e.Key == System.Windows.Input.Key.Escape) { RequestCancel(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { RequestCommit(); e.Handled = true; }
        };
        formatting.Opened += (_, _) => Ui.ExcludePopup(card, Window.GetWindow(this));
        Loaded += (_, _) => formatting.IsOpen = true; Unloaded += (_, _) => { formatting.IsOpen = false; formatting.Child = null; formatting.PlacementTarget = null; };
        IsVisibleChanged += (_, _) => { if (!IsVisible) formatting.IsOpen = false; };
    }
}
