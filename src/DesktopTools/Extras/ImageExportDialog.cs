using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed record ImageExportRequest(string Filename, string Format, int Width, int Height, int Quality)
{
    internal string Extension => Format == "JPEG" ? ".jpg" : "." + Format.ToLowerInvariant();
}

internal sealed class ImageExportDialog : Window
{
    private readonly BitmapSource source;
    private readonly string original;
    private readonly Func<ImageExportRequest, string?>? choosePath;
    private readonly TextBox filename = new();
    private readonly TextBlock extension = Ui.Text(".png", 13, muted: true), status = Ui.Text("", 12, muted: true), dimensions = Ui.Text("", 12, muted: true);
    private readonly Slider quality = new() { Minimum = 1, Maximum = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly StackPanel controls = new();
    private readonly Button save;
    private readonly FrameworkElement qualityRow;
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBox customWidth = new() { Width = 96 }, customHeight = new() { Width = 96 };
    private readonly FrameworkElement customRow;
    private string format, size = "Original size";
    private bool busy;
    internal string? SavedPath { get; private set; }
    internal string SelectedFormat => format;
    internal int SelectedQuality => (int)quality.Value;

    internal ImageExportDialog(BitmapSource source, string original, string initialFormat = "PNG", int initialQuality = 92, Func<ImageExportRequest, string?>? choosePath = null)
    {
        this.source = source; this.original = original; this.choosePath = choosePath; format = initialFormat;
        Title = L.T("Export image"); Width = 850; Height = 500; MinWidth = 780; MinHeight = 470;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel(); var header = UtilityWindowChrome.Header(this, "DesktopTools — " + Title, Close, L.T("Close"), 13); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var bottom = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 14, 0, 0) };
        save = Ui.Button(L.T("Save copy"), async () => await SaveAsync(), true); save.MinWidth = 150; DockPanel.SetDock(save, Dock.Right); bottom.Children.Add(save);
        var cancel = Ui.Button(L.T("Cancel"), Close); cancel.MinWidth = 110; DockPanel.SetDock(cancel, Dock.Right); bottom.Children.Add(cancel); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(370) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(columns);
        controls.Margin = new Thickness(0, 0, 22, 0); columns.Children.Add(controls); controls.Children.Add(Ui.Text(Title, 22, true)); controls.Children.Add(Ui.Text(L.T("Review the copy before saving."), 12, muted: true));
        filename.Text = Path.GetFileNameWithoutExtension(original) + "-edited"; var nameRow = new DockPanel(); extension.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(extension, Dock.Right); nameRow.Children.Add(extension); nameRow.Children.Add(filename); controls.Children.Add(Label("File name", nameRow));
        var choice = Ui.Choice(new[] { "PNG", "JPEG", "BMP" }, format, value => { format = value; RefreshPreview(); }, translate: false); choice.Width = 210; controls.Children.Add(Label("Format", choice));
        var sizeChoice = Ui.Choice(new[] { "Original size", "75%", "50%", "25%", "Custom size" }, size, value => { size = value; RefreshPreview(); }); sizeChoice.Width = 210; controls.Children.Add(Label("Size", sizeChoice));
        customWidth.Text = source.PixelWidth.ToString(); customHeight.Text = source.PixelHeight.ToString(); var custom = new StackPanel { Orientation = Orientation.Horizontal }; custom.Children.Add(customWidth); var cross = Ui.Text("×", 13); cross.Margin = new Thickness(8, 0, 8, 0); custom.Children.Add(cross); custom.Children.Add(customHeight); customRow = custom; controls.Children.Add(customRow);
        quality.Value = initialQuality; quality.Width = 154; var qualityContent = new StackPanel { Orientation = Orientation.Horizontal }; qualityContent.Children.Add(quality); var qualityValue = Ui.Text(initialQuality + "%", 12); qualityValue.Margin = new Thickness(10, 0, 0, 0); qualityContent.Children.Add(qualityValue); qualityRow = Label("Quality", qualityContent); controls.Children.Add(qualityRow);
        dimensions.Margin = new Thickness(0, 10, 0, 0); controls.Children.Add(dimensions); status.Margin = new Thickness(0, 6, 0, 0); controls.Children.Add(status);
        var originalHint = Ui.Text(L.T("The original image is preserved."), 12, muted: true); originalHint.Margin = new Thickness(0, 10, 0, 0); controls.Children.Add(originalHint);
        var stage = new Border { Child = preview, CornerRadius = new CornerRadius(12), Padding = new Thickness(12) }; stage.SetResourceReference(Border.BackgroundProperty, "Card"); Grid.SetColumn(stage, 1); columns.Children.Add(stage);
        var card = Ui.Card(root, 18); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        quality.ValueChanged += (_, _) => { qualityValue.Text = ((int)quality.Value) + "%"; RefreshPreview(); };
        customWidth.TextChanged += (_, _) => RefreshPreview(); customHeight.TextChanged += (_, _) => RefreshPreview();
        Closing += (_, e) => { if (busy) e.Cancel = true; }; Closed += (_, _) => preview.Source = null;
        RefreshPreview();
    }
    private static FrameworkElement Label(string text, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.Children.Add(Ui.Text(L.T(text), 12)); Grid.SetColumn(control, 1); row.Children.Add(control); return row;
    }
    private (int Width, int Height) OutputSize()
    {
        if (size == "Custom size")
        {
            if (!int.TryParse(customWidth.Text, out int w) || !int.TryParse(customHeight.Text, out int h)) throw new ArgumentException(L.T("Enter whole pixel dimensions."));
            ImageTransforms.ValidateSize(w, h); return (w, h);
        }
        double scale = size switch { "75%" => .75, "50%" => .5, "25%" => .25, _ => 1 };
        return (Math.Max(1, (int)Math.Round(source.PixelWidth * scale)), Math.Max(1, (int)Math.Round(source.PixelHeight * scale)));
    }
    private void RefreshPreview()
    {
        // Choice callbacks can fire while the constructor is still building the rows.
        if (qualityRow == null || customRow == null) return;
        extension.Text = format == "JPEG" ? ".jpg" : "." + format.ToLowerInvariant(); qualityRow.Visibility = format == "JPEG" ? Visibility.Visible : Visibility.Collapsed; customRow.Visibility = size == "Custom size" ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            var output = OutputSize(); dimensions.Text = $"{output.Width} × {output.Height} px";
            double scale = Math.Min(1, 600d / Math.Max(output.Width, output.Height)); var small = ImageTransforms.Resize(source, Math.Max(1, (int)(output.Width * scale)), Math.Max(1, (int)(output.Height * scale)));
            if (format == "JPEG")
            {
                using var stream = new MemoryStream(ImageTransforms.Encode(small, format, (int)quality.Value));
                var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad; decoded.StreamSource = stream; decoded.EndInit(); decoded.Freeze(); preview.Source = decoded;
            }
            else preview.Source = small;
            save.IsEnabled = true; status.Text = "";
        }
        catch (Exception ex) { save.IsEnabled = false; status.Text = ex.Message; }
    }
    private async Task SaveAsync()
    {
        if (busy) return;
        try
        {
            string stem = filename.Text.Trim(); if (stem.Length == 0 || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || stem.EndsWith('.') || stem.EndsWith(' ')) throw new ArgumentException(L.T("Enter a valid file name."));
            var output = OutputSize(); var request = new ImageExportRequest(stem, format, output.Width, output.Height, (int)quality.Value);
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = stem + request.Extension, DefaultExt = request.Extension, AddExtension = true, Filter = format + "|*" + request.Extension, OverwritePrompt = true };
            string? path = choosePath != null ? choosePath(request) : dialog.ShowDialog(this) == true ? dialog.FileName : null; if (path == null) return;
            string actualExtension = Path.GetExtension(path); if (!string.Equals(actualExtension, request.Extension, StringComparison.OrdinalIgnoreCase) && !(format == "JPEG" && actualExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(L.T("The filename extension must match the selected format."));
            busy = true; controls.IsEnabled = save.IsEnabled = false; status.Text = L.T("Saving copy…");
            var prepared = output.Width == source.PixelWidth && output.Height == source.PixelHeight ? source : ImageTransforms.Resize(source, output.Width, output.Height);
            await Task.Run(() => ImageTransforms.Export(prepared, path, original, request.Quality)); SavedPath = path;
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { busy = false; controls.IsEnabled = true; save.IsEnabled = true; if (SavedPath != null) Close(); }
    }
}
