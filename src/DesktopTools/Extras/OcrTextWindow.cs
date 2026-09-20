using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed class OcrTextWindow : Window
{
    public OcrTextWindow(BitmapSource image, Action<string> report)
    {
        Title = L.T("Extract text · DesktopTools"); Width = 860; Height = 520; MinWidth = 660; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = System.Windows.Media.Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "Text");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(UtilityWindowChrome.Header(this, L.T("Extract text"), Close, L.T("Close"), 20));
        header.Children.Add(Ui.Text(L.T("Recognized locally on this device. Review the text before copying."), 13, muted: true));
        var languages = LocalOcr.Languages;
        var choice = new ComboBox { ItemsSource = languages, SelectedIndex = languages.Count > 0 ? 0 : -1, Margin = new Thickness(0, 12, 0, 12), MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(choice, L.T("OCR language")); header.Children.Add(choice); grid.Children.Add(header);
        var output = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        System.Windows.Automation.AutomationProperties.SetName(output, L.T("Recognized text")); var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var preview = new Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 16, 0) };
        System.Windows.Automation.AutomationProperties.SetName(preview, L.T("Selected area")); columns.Children.Add(preview); Grid.SetColumn(output, 1); columns.Children.Add(output); Grid.SetRow(columns, 1); grid.Children.Add(columns);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var status = Ui.Text(L.T("Choose a language and extract text."), 12, muted: true); status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(status);
        var actions = new WrapPanel();
        var extract = Ui.Button(L.T("Extract"), () => { });
        var copy = Ui.Button(L.T("Copy text"), () => { try { Clipboard.SetText(output.Text); status.Text = L.T("Text copied."); } catch (Exception ex) { report(L.T("Could not copy text: ") + ex.Message); } }, true); copy.IsEnabled = false;
        extract.Click += async (_, _) =>
        {
            if (choice.SelectedItem is not OcrLanguage language) return;
            extract.IsEnabled = false; choice.IsEnabled = false; copy.IsEnabled = false; output.Text = ""; status.Text = L.T("Recognizing text…");
            try
            {
                var text = await LocalOcr.RecognizeAsync(image, language.Tag);
                if (!IsVisible) return;
                output.Text = text; status.Text = string.IsNullOrWhiteSpace(text) ? L.T("No text found. Try another language or a tighter crop.") : L.T("Text ready. Review and edit it before copying.");
            }
            catch (Exception ex) { if (IsVisible) status.Text = L.T("Text recognition unavailable: ") + ex.Message; }
            finally { extract.IsEnabled = true; choice.IsEnabled = true; copy.IsEnabled = !string.IsNullOrWhiteSpace(output.Text); }
        };
        output.TextChanged += (_, _) => copy.IsEnabled = !string.IsNullOrWhiteSpace(output.Text);
        actions.Children.Add(extract); actions.Children.Add(copy); actions.Children.Add(Ui.Button(L.T("Close"), Close)); footer.Children.Add(actions);
        if (languages.Count == 0) { extract.IsEnabled = false; choice.IsEnabled = false; status.Text = L.T("No Windows OCR languages are installed. Add a language pack in Windows Settings → Time & language → Language & region, then reopen this window."); }
        Grid.SetRow(footer, 2); grid.Children.Add(footer);
        var card = Ui.Card(grid, 24); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
    }
}
