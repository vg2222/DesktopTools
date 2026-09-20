using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace DesktopTools.UI;

internal sealed class ColorPickerWindow : Window
{
    internal static readonly string[] Swatches = ["#2563EB", "#60A5FA", "#7C3AED", "#C084FC", "#DB2777", "#FB7185", "#EF4444", "#F97316", "#FBBF24", "#FDE047", "#16A34A", "#4ADE80", "#0D9488", "#22D3EE", "#FFFFFF", "#94A3B8", "#475569", "#17181C"];
    public ColorPickerWindow(string current, Func<string, bool> save)
    {
        Title = L.T("Choose color"); Width = 364; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Motion.WindowEntrance(this);
        var content = new StackPanel();
        var title = Ui.Text(L.T("Choose color"), 22, true);
        title.PreviewMouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        content.Children.Add(title);
        var spectrum = new ColorSpectrum { Margin = new Thickness(0, 14, 0, 8) };
        var hue = new Slider { Minimum = 0, Maximum = 359.99, Margin = new Thickness(0, 5, 0, 4) };
        AutomationProperties.SetName(hue, L.T("Color hue"));
        content.Children.Add(spectrum); content.Children.Add(Ui.Text(L.T("Hue"), 12, muted: true)); content.Children.Add(hue);
        var palette = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6, Margin = new Thickness(0, 16, 0, 14) };
        var preview = new Border { Width = 42, Height = 38, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) }; preview.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var input = new TextBox { Text = current, MinWidth = 160 }; AutomationProperties.SetName(input, L.T("Hex color"));
        var error = Ui.Text("", 12, muted: true); error.Margin = new Thickness(0, 6, 0, 8);
        foreach (var color in Swatches)
        {
            var button = Ui.Button("", () => input.Text = color); button.Padding = new Thickness(5); button.Margin = new Thickness(3); button.Height = 40;
            button.Content = new Border { CornerRadius = new CornerRadius(7), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Width = 30, Height = 26 };
            Ui.Tip(button, color); AutomationProperties.SetName(button, L.F($"Choose {color}")); palette.Children.Add(button);
        }
        content.Children.Add(palette);
        var field = new DockPanel(); DockPanel.SetDock(preview, Dock.Left); field.Children.Add(preview); field.Children.Add(input); content.Children.Add(field); content.Children.Add(error);
        var apply = Ui.Button(L.T("Use color"), () => { if (save(input.Text)) Close(); }, true); apply.Margin = new Thickness(0); content.Children.Add(apply);
        void Validate()
        {
            try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(input.Text)); apply.IsEnabled = true; error.Text = ""; }
            catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException) { apply.IsEnabled = false; error.Text = L.T("Use a color like #2563EB or #FF2563EB."); }
        }
        bool syncing = false;
        void SyncFromText()
        {
            Validate(); if (syncing || !apply.IsEnabled) return;
            syncing = true; spectrum.SetColor((Color)ColorConverter.ConvertFromString(input.Text)); hue.Value = spectrum.Hue; syncing = false;
        }
        spectrum.Changed += color => { if (syncing) return; syncing = true; input.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}"; syncing = false; };
        hue.ValueChanged += (_, _) => { if (!syncing) spectrum.Hue = hue.Value; };
        input.TextChanged += (_, _) => SyncFromText(); SyncFromText();
        var card = Ui.Card(content, 22); card.Margin = new Thickness(0); card.CornerRadius = new CornerRadius(16); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
    }
}
