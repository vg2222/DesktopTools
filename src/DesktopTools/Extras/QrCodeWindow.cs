using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.UI;
using QRCoder;

namespace DesktopTools.Extras;

public sealed class QrCodeWindow : Window
{
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, MinHeight = 150, MaxLength = 7089 };
    private readonly Image preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(22) };
    private readonly Grid previewStage = new();
    private readonly StackPanel emptyPreview = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = Ui.Text(L.T("Enter text or paste a link to create a QR code."), 12, muted: true);
    private readonly Button copy;
    private readonly Button save;
    private readonly Action<string> report;
    private byte[]? png;
    private readonly ComboBox size = new() { ItemsSource = new[] { 256, 512, 1024, 2048 }, SelectedItem = 512, MinWidth = 160 };
    private readonly System.Windows.Threading.DispatcherTimer generateDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public QrCodeWindow(Action<string> report)
    {
        this.report = report;
        Title = L.T("QR code · DesktopTools"); Width = 860; Height = 590; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        var root = new DockPanel();
        var header = UtilityWindowChrome.Header(this, L.T("QR code"), Close, L.T("Close QR code"), 15);
        header.Margin = new Thickness(26, 18, 22, 4); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(28, 12, 28, 28) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var editor = new StackPanel { Margin = new Thickness(0, 0, 32, 0) }; columns.Children.Add(editor);
        var heading = Ui.Text(L.T("Create a QR code"), 28, true); editor.Children.Add(heading);
        var description = Ui.Text(L.T("Add a website, message, Wi-Fi detail, or any short text. Everything is generated on this device."), 13, muted: true);
        description.Margin = new Thickness(0, 9, 0, 26); editor.Children.Add(description);

        var inputLabel = Ui.Text(L.T("Text or link"), 13, true); inputLabel.Margin = new Thickness(0, 0, 0, 9); editor.Children.Add(inputLabel);
        input.ToolTip = L.T("Enter text or paste a link for the QR code");
        System.Windows.Automation.AutomationProperties.SetName(input, L.T("QR code text or link"));
        editor.Children.Add(input);
        var inputHint = Ui.Text(L.T("The preview updates automatically as you type."), 11, muted: true);
        inputHint.Margin = new Thickness(0, 8, 0, 22); editor.Children.Add(inputHint);

        size.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.Children.Add(Ui.Row(L.T("Image size"), L.T("Choose a larger PNG for print or presentation."), size));
        System.Windows.Automation.AutomationProperties.SetName(size, L.T("QR image size"));

        var actions = new Grid { Margin = new Thickness(0, 24, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        copy = Ui.Button(L.T("Copy image"), CopyImage); save = Ui.Button(L.T("Save PNG"), SavePng, true);
        copy.Content = Ui.IconLabel("Copy", L.T("Copy image")); copy.Margin = new Thickness(0, 0, 6, 0);
        save.Content = Ui.IconLabel("Save", L.T("Save PNG"), primary: true); save.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(save, 1);
        copy.IsEnabled = save.IsEnabled = false; actions.Children.Add(copy); actions.Children.Add(save); editor.Children.Add(actions);
        status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 14, 0, 0); editor.Children.Add(status);

        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        System.Windows.Automation.AutomationProperties.SetName(preview, L.T("Generated QR code preview"));
        previewStage.Children.Add(preview);
        var blankIcon = Ui.Icon("QR", 54);
        if (blankIcon is System.Windows.Shapes.Shape iconShape) iconShape.Fill = new SolidColorBrush(Color.FromRgb(88, 98, 112));
        blankIcon.HorizontalAlignment = HorizontalAlignment.Center; emptyPreview.Children.Add(blankIcon);
        var blankTitle = Ui.Text(L.T("Your preview appears here"), 15, true); blankTitle.Foreground = new SolidColorBrush(Color.FromRgb(35, 42, 52));
        blankTitle.HorizontalAlignment = HorizontalAlignment.Center; blankTitle.Margin = new Thickness(0, 18, 0, 0); emptyPreview.Children.Add(blankTitle);
        var blankHint = Ui.Text(L.T("Enter text or paste a link to begin."), 12); blankHint.Foreground = new SolidColorBrush(Color.FromRgb(95, 105, 118));
        blankHint.HorizontalAlignment = HorizontalAlignment.Center; blankHint.Margin = new Thickness(0, 7, 0, 0); emptyPreview.Children.Add(blankHint);
        previewStage.Children.Add(emptyPreview);
        var imageFrame = new Border
        {
            Background = Brushes.White, Child = previewStage, CornerRadius = new CornerRadius(14),
            Height = 300, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var previewColumn = new StackPanel();
        var previewLabel = Ui.Text(L.T("Preview"), 13, true); previewLabel.Margin = new Thickness(0, 0, 0, 9); previewColumn.Children.Add(previewLabel);
        previewColumn.Children.Add(imageFrame);
        var previewNote = Ui.Text(L.T("Scan the result before sharing it."), 11, muted: true); previewNote.Margin = new Thickness(0, 10, 0, 0); previewColumn.Children.Add(previewNote);
        Grid.SetColumn(previewColumn, 1); columns.Children.Add(previewColumn);

        var scroll = new ScrollViewer { Content = columns, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SmoothScroll.Enable(scroll); root.Children.Add(scroll);

        input.TextChanged += (_, _) =>
        {
            generateDelay.Stop(); bool hadPreview = png != null; png = null; preview.Source = null; emptyPreview.Visibility = Visibility.Visible;
            copy.IsEnabled = save.IsEnabled = false;
            status.Text = string.IsNullOrWhiteSpace(input.Text)
                ? L.T("Enter text or paste a link to create a QR code.")
                : L.T("Updating preview…");
            if (hadPreview) Motion.Transition(previewStage);
            if (!string.IsNullOrWhiteSpace(input.Text)) generateDelay.Start();
        };
        generateDelay.Tick += (_, _) => { generateDelay.Stop(); Generate(); }; Closed += (_, _) => generateDelay.Stop();
        size.SelectionChanged += (_, _) => { if (png != null) Generate(); };
        var card = Ui.Card(root, 0); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Stroke"); Content = card;
        Motion.WindowEntrance(this); Loaded += (_, _) => input.Focus();
    }

    public static byte[] CreatePng(string text, int pixels = 512)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(L.T("Enter text or a link first."), nameof(text));
        // Bound work before encoding; even numeric QR payloads cannot exceed 7,089 characters.
        if (text.Length > 7089) throw new ArgumentException(L.T("This text is too long for one QR code. Shorten it and try again."), nameof(text));
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M, forceUtf8: true, eciMode: QRCodeGenerator.EciMode.Utf8);
        if (pixels is not (256 or 512 or 1024 or 2048)) throw new ArgumentOutOfRangeException(nameof(pixels));
        int modules = data.ModuleMatrix.Count, scale = pixels / modules, offset = (pixels - modules * scale) / 2;
        var buffer = new byte[pixels * pixels * 4]; Array.Fill(buffer, (byte)255);
        // Integer modules plus quiet zone, centered without interpolation at the requested pixel size.
        for (int y = 0; y < modules; y++) for (int x = 0; x < modules; x++)
            if (data.ModuleMatrix[y][x])
                for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++)
                {
                    int index = ((offset + y * scale + dy) * pixels + offset + x * scale + dx) * 4;
                    buffer[index] = buffer[index + 1] = buffer[index + 2] = 0;
                }
        var bitmap = BitmapSource.Create(pixels, pixels, 96, 96, PixelFormats.Bgra32, null, buffer, pixels * 4); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    private void Generate()
    {
        try
        {
            png = CreatePng(input.Text, (int)(size.SelectedItem ?? 512));
            using var stream = new MemoryStream(png);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            preview.Source = bitmap; emptyPreview.Visibility = Visibility.Collapsed; copy.IsEnabled = save.IsEnabled = true;
            status.Text = L.F($"Ready · {bitmap.PixelWidth} × {bitmap.PixelHeight} PNG. Scan to check before sharing.");
            Motion.Transition(previewStage); Motion.Transition(status);
        }
        catch (QRCoder.Exceptions.DataTooLongException) { ShowError(L.T("This text is too long for one QR code. Shorten it and try again.")); }
        catch (ArgumentException ex) { ShowError(ex.Message.Split('\n')[0]); }
        catch (Exception ex) { ShowError(L.F($"Could not generate QR code: {ex.Message}")); }
    }

    private void ShowError(string message)
    {
        png = null; preview.Source = null; emptyPreview.Visibility = Visibility.Visible;
        copy.IsEnabled = save.IsEnabled = false; status.Text = message;
        Motion.Transition(previewStage); Motion.Transition(status);
    }

    private void CopyImage()
    {
        if (preview.Source is not BitmapSource bitmap) return;
        try { Clipboard.SetImage(bitmap); status.Text = L.T("QR image copied."); Motion.Transition(status); }
        catch (Exception ex) { status.Text = L.F($"Could not copy image: {ex.Message}"); report(status.Text); }
    }

    private void SavePng()
    {
        if (png is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = L.T("PNG image (*.png)|*.png"), DefaultExt = ".png", AddExtension = true, FileName = L.T("QR code.png") };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, png); status.Text = L.T("QR image saved."); Motion.Transition(status); }
        catch (Exception ex) { status.Text = L.F($"Could not save image: {ex.Message}"); report(status.Text); }
    }
}
