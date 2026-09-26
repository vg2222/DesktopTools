using DesktopTools.Localization;
using DesktopTools.Native;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed class ImageToolsWindow : Window, IUnsavedWork
{
    private readonly Action<string> report;
    private readonly Func<IReadOnlyList<string>> readMissingRuntime;
    private readonly TextBlock runtimeWarning = Ui.Text("", 12);
    private readonly Button runtimeHelp;
    private readonly Image preview;
    private readonly ImageEditCanvas editCanvas = new() { Margin = new Thickness(12) };
    private readonly Slider previewZoom = new() { Minimum = 1, Maximum = 4, Value = 1, Width = 112, SmallChange = .1, LargeChange = .5 };
    private readonly ScrollViewer imageScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly TextBlock status = Ui.Text(L.T("Open a PNG, JPEG, or BMP to begin."), 12, muted: true);
    private readonly TextBox width = MediaWorkspaceLayout.NumericField();
    private readonly TextBox height = MediaWorkspaceLayout.NumericField();
    private readonly CheckBox aspect = Ui.Toggle(true, _ => { });
    private readonly DockPanel edits = new();
    private BitmapSource? bitmap;
    private string original = "";
    private bool updating;
    private ImageEditHistory? history;
    private BitmapSource? originalImage;
    private BitmapSource? savedImage;
    private BitmapSource? backgroundBefore;
    private BitmapSource? backgroundAfter;
    private bool IsComparisonAvailable => bitmap != null && ReferenceEquals(bitmap, backgroundAfter) && backgroundBefore != null;
    public bool HasUnsavedChanges => removal != null || (bitmap != null && !ReferenceEquals(bitmap, savedImage ?? originalImage));
    public Task<bool> SaveCopyAsync(Window owner)
    {
        if (removal != null) return Task.FromResult(false);
        Export(owner); return Task.FromResult(!HasUnsavedChanges);
    }
    private BitmapSource? encodedPreview;
    private readonly Button undoButton, redoButton;
    private readonly TextBox cropX = MediaWorkspaceLayout.NumericField("0"), cropY = MediaWorkspaceLayout.NumericField("0");
    private readonly TextBox cropWidth = MediaWorkspaceLayout.NumericField(), cropHeight = MediaWorkspaceLayout.NumericField();
    private string outputFormat = "PNG";
    private readonly Slider quality = new() { Minimum = 1, Maximum = 100, Value = 92, Width = 100, IsSnapToTickEnabled = true, TickFrequency = 1, IsEnabled = false };
    private readonly TextBlock sizeInfo = Ui.Text(L.T("Estimate export size before saving."), 12, muted: true);
    private bool estimating;
    private CheckBox? compare;
    private int revision;
    private CancellationTokenSource? removal;
    private bool closed;
    private readonly Button removeBackground, cancelRemoval;
    private readonly ComboBox outputChoice;
    private readonly ComboBox cropRatio;
    private readonly Slider reductionSlider;
    private readonly WrapPanel openControls;
    private string activeMode = "Size";
    private readonly Button emptyImport;


    public ImageToolsWindow(Action<string> report, Func<IReadOnlyList<string>>? readMissingRuntime = null)
    {
        this.report = report; this.readMissingRuntime = readMissingRuntime ?? VisualCppRuntime.FindMissingFiles; preview = editCanvas.Image;
        Title = L.T("Image tools"); Width = 1120; Height = 740; MinWidth = 900; MinHeight = 560;
        ResizeMode = ResizeMode.CanResizeWithGrip; WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        Button Icon(string symbol, string label, Action action)
        {
            var button = Ui.IconButton(symbol, L.T(label), action); button.Width = button.Height = 34; button.MinHeight = 34; button.Margin = new Thickness(3, 0, 3, 0);
            button.BorderThickness = new Thickness(1); button.SetResourceReference(Control.BorderBrushProperty, "Stroke"); button.SetResourceReference(Control.BackgroundProperty, "Field"); return button;
        }
        var header = UtilityWindowChrome.Header(this, "DesktopTools — " + L.T("Image tools"), Close, L.T("Close image tools"), 13);
        header.Margin = new Thickness(0, 0, 0, 12);
        openControls = new WrapPanel();
        var openImage = Ui.Button(L.T("Open image"), Open); openImage.Content = Ui.IconLabel("Folder", L.T("Open image")); openImage.Tag = "import-image"; openControls.Children.Add(openImage);
        undoButton = Icon("Undo", "Undo", () => { history?.Undo(); if (history != null) SetImage(history.Current); });
        redoButton = Icon("Redo", "Redo", () => { history?.Redo(); if (history != null) SetImage(history.Current); });
        openControls.Children.Add(undoButton); openControls.Children.Add(redoButton);
        var more = Icon("More", "Image tools", () => { }); var menu = new ContextMenu();
        var reset = new MenuItem { Header = L.T("Reset") }; reset.Click += (_, _) => Edit(() => originalImage!); menu.Items.Add(reset);
        more.ContextMenu = menu; more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; }; openControls.Children.Add(more);
        DockPanel.SetDock(openControls, Dock.Right); header.Children.Insert(1, openControls);
        root.Children.Add(header);

        var sizeRow = new StackPanel();
        foreach (var field in new[] { width, height, cropX, cropY, cropWidth, cropHeight }) field.Margin = new Thickness(0);
        var dimensions = new Grid(); dimensions.ColumnDefinitions.Add(new ColumnDefinition()); dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        void Dimension(TextBox input, string label, int column)
        {
            input.Width = double.NaN;
            var field = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 0 ? 5 : 0, 0) };
            var caption = Ui.Text(L.T(label), 12, muted: true); caption.Margin = new Thickness(0, 0, 0, 6); field.Children.Add(caption); field.Children.Add(input);
            Grid.SetColumn(field, column); dimensions.Children.Add(field);
        }
        Dimension(width, "Width", 0); Dimension(height, "Height", 1); sizeRow.Children.Add(dimensions);
        var ratio = Ui.Button(L.T("Keep aspect ratio"), () => aspect.IsChecked = aspect.IsChecked != true);
        ratio.Content = Ui.IconLabel("Lock", L.T("Keep aspect ratio")); ratio.Margin = new Thickness(0, 10, 0, 14);
        void UpdateRatio() { ratio.SetResourceReference(Control.BackgroundProperty, aspect.IsChecked == true ? "Selected" : "Field"); ratio.SetResourceReference(Control.BorderBrushProperty, aspect.IsChecked == true ? "Accent" : "Stroke"); editCanvas.KeepRatio = aspect.IsChecked == true; }
        aspect.Checked += (_, _) => UpdateRatio(); aspect.Unchecked += (_, _) => UpdateRatio(); UpdateRatio(); sizeRow.Children.Add(ratio);
        void TransformRow(string label, Button first, Button second)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var controls = new StackPanel { Orientation = Orientation.Horizontal }; controls.Children.Add(first); controls.Children.Add(second);
            DockPanel.SetDock(controls, Dock.Right); row.Children.Add(controls); row.Children.Add(Ui.Text(L.T(label), 12)); sizeRow.Children.Add(row);
        }
        TransformRow("Rotation", Icon("RotateLeft", "Rotate left", () => Edit(() => ImageTransforms.Rotate(ImageTransforms.Rotate(ImageTransforms.Rotate(bitmap!))))),
            Icon("RotateRight", "Rotate 90°", () => Edit(() => ImageTransforms.Rotate(bitmap!))));
        TransformRow("Mirror", Icon("FlipHorizontal", "Mirror horizontal", () => Edit(() => ImageTransforms.Mirror(bitmap!, true))),
            Icon("FlipVertical", "Mirror vertical", () => Edit(() => ImageTransforms.Mirror(bitmap!, false))));
        var applySize = Ui.Button(L.T("Apply size"), ResizeImage); applySize.Content = Ui.IconLabel("Check", L.T("Apply size")); applySize.Margin = new Thickness(0, 4, 0, 0); sizeRow.Children.Add(applySize);
        foreach (var field in new[] { width, height }) field.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { ResizeImage(); e.Handled = true; } };
        var cropRow = new StackPanel();
        var cropGuide = Ui.Text(L.T("Drag the corners to keep an area. Drag inside the frame to move it."), 12, muted: true);
        cropGuide.Margin = new Thickness(0, 0, 0, 12); cropRow.Children.Add(cropGuide);
        cropRow.Children.Add(Ui.Text(L.T("Aspect ratio"), 12, muted: true));
        cropRatio = Ui.Choice(new[] { "Free", "Original", "1:1", "4:3", "16:9", "9:16" }, "Free", value =>
        {
            if (bitmap == null) return;
            editCanvas.KeepRatio = value != "Free";
            if (value == "Free") return;
            double ratioValue = value switch { "1:1" => 1, "4:3" => 4d / 3, "16:9" => 16d / 9, "9:16" => 9d / 16, _ => bitmap.PixelWidth / (double)bitmap.PixelHeight };
            int w = bitmap.PixelWidth, h = Math.Max(1, (int)Math.Round(w / ratioValue));
            if (h > bitmap.PixelHeight) { h = bitmap.PixelHeight; w = Math.Max(1, (int)Math.Round(h * ratioValue)); }
            editCanvas.ResizePreview = false;
            editCanvas.SetSelection(new Int32Rect((bitmap.PixelWidth - w) / 2, (bitmap.PixelHeight - h) / 2, w, h)); editCanvas.ShowHandles(true);
        });
        cropRatio.Tag = "crop-ratio"; cropRatio.Margin = new Thickness(0, 6, 0, 14); cropRow.Children.Add(cropRatio);
        var cropGrid = new Grid(); cropGrid.ColumnDefinitions.Add(new ColumnDefinition()); cropGrid.ColumnDefinitions.Add(new ColumnDefinition());
        cropGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); cropGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void CropField(TextBox input, string label, int row, int column)
        {
            var group = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 0 ? 5 : 0, 10) };
            var caption = Ui.Text(L.T(label), 12, muted: true); caption.Margin = new Thickness(0, 0, 0, 5);
            group.Children.Add(caption); group.Children.Add(input);
            Grid.SetRow(group, row); Grid.SetColumn(group, column); cropGrid.Children.Add(group);
        }
        CropField(cropX, "Crop X", 0, 0); CropField(cropY, "Crop Y", 0, 1);
        CropField(cropWidth, "Crop width", 1, 0); CropField(cropHeight, "Crop height", 1, 1);
        cropRow.Children.Add(cropGrid);
        var cropActions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        var applyCrop = Ui.Button(L.T("Apply crop"), CropImage, true); applyCrop.Content = Ui.IconLabel("Check", L.T("Apply crop"), primary: true);
        cropActions.Children.Add(applyCrop);
        var resetCrop = Ui.Button(L.T("Reset crop"), ResetCrop); resetCrop.Tag = "reset-image-crop"; cropActions.Children.Add(resetCrop);
        cropRow.Children.Add(cropActions);
        var sizePanel = new StackPanel(); sizePanel.Children.Add(sizeRow);
        var reduction = new DockPanel { Margin = new Thickness(0, 14, 0, 0) }; var reductionCaption = Ui.Text(L.T("Reduce size"), 12); reductionCaption.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(reductionCaption, Dock.Top); reduction.Children.Add(reductionCaption);
        var reductionValue = Ui.Text("100%", 12);
        reductionSlider = new Slider { Minimum = 10, Maximum = 100, Value = 100, MinWidth = 40, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 12, 0), Tag = "image-reduction" };
        System.Windows.Automation.AutomationProperties.SetName(reductionSlider, L.T("Reduce size"));
        reductionSlider.ValueChanged += (_, _) =>
        {
            reductionValue.Text = $"{reductionSlider.Value:0}%";
            if (bitmap == null || updating) return;
            editCanvas.ResizePreview = true;
            editCanvas.SetSelection(new Int32Rect(0, 0, Math.Max(1, (int)Math.Round(bitmap.PixelWidth * reductionSlider.Value / 100)), Math.Max(1, (int)Math.Round(bitmap.PixelHeight * reductionSlider.Value / 100)))); editCanvas.ShowHandles(true);
        };
        DockPanel.SetDock(reductionValue, Dock.Right); reduction.Children.Add(reductionValue); reduction.Children.Add(reductionSlider); sizePanel.Children.Add(reduction);
        var formatRow = new StackPanel();
        outputChoice = Ui.Choice(new[] { "PNG", "JPEG", "BMP" }, outputFormat, v => { outputFormat = v; quality.IsEnabled = v == "JPEG"; ChangedOutput(); }); outputChoice.Width = double.NaN;
        formatRow.Children.Add(outputChoice); var qualityLabel = Ui.Text(L.T("JPEG quality"), 12); qualityLabel.Margin = new Thickness(0, 16, 0, 8); formatRow.Children.Add(qualityLabel); var qualityRow = new DockPanel(); formatRow.Children.Add(qualityRow);
        var qualityValue = Ui.Text("92", 12); DockPanel.SetDock(qualityValue, Dock.Right); qualityRow.Children.Add(qualityValue); quality.Width = double.NaN; quality.Margin = new Thickness(0, 0, 12, 0); qualityRow.Children.Add(quality);
        quality.ValueChanged += (_, _) => { qualityValue.Text = ((int)quality.Value).ToString(); ChangedOutput(); };
        var estimateSize = Ui.Button(L.T("Estimate size"), async () => await Estimate()); estimateSize.Content = Ui.IconLabel("Search", L.T("Estimate size")); estimateSize.Margin = new Thickness(0, 16, 0, 0); formatRow.Children.Add(estimateSize);
        var backgroundRow = new StackPanel();
        runtimeWarning.TextWrapping = TextWrapping.Wrap;
        runtimeWarning.Margin = new Thickness(0, 0, 0, 8);
        backgroundRow.Children.Add(runtimeWarning);
        runtimeHelp = Ui.Button(L.T("Open Microsoft Visual C++ Runtime download page"), OpenRuntimeHelp);
        runtimeHelp.HorizontalAlignment = HorizontalAlignment.Left;
        runtimeHelp.Margin = new Thickness(0, 0, 0, 10);
        backgroundRow.Children.Add(runtimeHelp);
        removeBackground = Ui.Button(L.T("Remove background"), async () => await RemoveBackgroundAsync());
        var removeLabel = new StackPanel { Orientation = Orientation.Horizontal }; removeLabel.Children.Add(Ui.Icon("Background", 17)); var removeText = Ui.Text(L.T("Remove background"), 12); removeText.Margin = new Thickness(8, 0, 0, 0); removeLabel.Children.Add(removeText); removeBackground.Content = removeLabel;
        backgroundRow.Children.Add(removeBackground);
        cancelRemoval = Icon("Close", "Cancel", () => removal?.Cancel()); cancelRemoval.Visibility = Visibility.Collapsed;
        var comparisonPosition = new Slider { Minimum = 0, Maximum = 1, Value = .5, Width = 160, Margin = new Thickness(12, 0, 0, 0) };
        compare = Ui.Toggle(false, v =>
        {
            bool active = v && IsComparisonAvailable;
            preview.Source = active ? bitmap : encodedPreview ?? bitmap;
            editCanvas.SetComparison(active ? backgroundBefore : null, comparisonPosition.Value, L.T("Before"), L.T("After"));
            editCanvas.ShowHandles(!active && activeMode == "Crop");
        });
        compare.IsEnabled = false;
        comparisonPosition.ValueChanged += (_, _) => { if (compare.IsChecked == true && IsComparisonAvailable) editCanvas.SetComparison(backgroundBefore, comparisonPosition.Value, L.T("Before"), L.T("After")); };
        editCanvas.ComparisonMoved += value => comparisonPosition.Value = value;
        var compareRow = new DockPanel { Margin = new Thickness(0, 16, 0, 8) }; DockPanel.SetDock(compare, Dock.Right); compareRow.Children.Add(compare); compareRow.Children.Add(Ui.Text(L.T("Compare before and after"), 12)); backgroundRow.Children.Add(compareRow);
        var compareHelp = Ui.Text(L.T("Drag the divider. Before is on the left; after is on the right."), 11, muted: true); compareHelp.TextWrapping = TextWrapping.Wrap; backgroundRow.Children.Add(compareHelp);
        comparisonPosition.Width = double.NaN; comparisonPosition.Margin = new Thickness(0, 8, 0, 0); backgroundRow.Children.Add(comparisonPosition);

        var tabButtons = new Dictionary<string, Button>();
        var rowHost = new Grid { VerticalAlignment = VerticalAlignment.Top };
        var sizeSection = MediaWorkspaceLayout.Section("Utilities", "Resize and transform", "Set exact dimensions or adjust the image with reversible transforms.", sizePanel);
        var cropSection = MediaWorkspaceLayout.Section("Crop", "Crop", "Choose a ratio, then drag the frame or enter exact coordinates.", cropRow);
        var formatContent = new StackPanel(); formatContent.Children.Add(formatRow); sizeInfo.Margin = new Thickness(0, 10, 0, 0); formatContent.Children.Add(sizeInfo);
        var formatSection = MediaWorkspaceLayout.Section("File", "Export settings", "Choose the file type and review its estimated size.", formatContent);
        var backgroundSection = MediaWorkspaceLayout.Section("Background", "Background", "Remove the background locally and compare the result with the source.", backgroundRow);
        rowHost.Children.Add(sizeSection); rowHost.Children.Add(cropSection); rowHost.Children.Add(formatSection); rowHost.Children.Add(backgroundSection);
        var export = Ui.Button(L.T("Export image"), Export, true); export.Content = Ui.IconLabel("Save", L.T("Export image"), primary: true); export.Height = 40; export.Margin = new Thickness(0, 12, 0, 0);
        var guide = FeatureTourButton.Create(this, "image-editor", () => new GuidedTour.Step[]
        {
            new(() => openControls, "Open image", "Open an image to edit a copy. Undo and redo keep your changes reversible."),
            new(() => editCanvas, "Image canvas", "Use the handles to adjust the crop or size, then apply the change."),
            new(() => rowHost, "Image tools", "Choose size, format or background options. The pen opens annotation tools."),
            new(() => export, "Export image", "Review the name, format, size and preview before saving a separate copy.")
        }); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(1, guide);
        void Mode(string mode)
        {
            activeMode = mode;
            if (mode == "Background") RefreshRuntimeWarning();
            sizeSection.Visibility = mode == "Size" ? Visibility.Visible : Visibility.Collapsed; formatSection.Visibility = mode == "Format" ? Visibility.Visible : Visibility.Collapsed;
            backgroundSection.Visibility = mode == "Background" ? Visibility.Visible : Visibility.Collapsed; cropSection.Visibility = mode == "Crop" ? Visibility.Visible : Visibility.Collapsed;
            MediaWorkspaceLayout.SelectTab(tabButtons, mode);
            if (compare != null && mode != "Background") compare.IsChecked = false;
            editCanvas.ResizePreview = mode == "Size"; editCanvas.KeepRatio = mode == "Size" && aspect.IsChecked == true; editCanvas.ResetSelection(); editCanvas.ShowHandles(mode is "Size" or "Crop");
            if (bitmap != null) editCanvas.SetSelection(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            if (mode == "Crop") { cropRatio.SelectedItem = "Free"; editCanvas.KeepRatio = false; }
        }
        var tabStrip = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (key, symbol, label) in new[] { ("Size", "Utilities", "Resize"), ("Crop", "Crop", "Crop"), ("Format", "File", "Output"), ("Background", "Background", "Background") })
        {
            string selected = key; var button = MediaWorkspaceLayout.Tab(symbol, label, () => Mode(selected));
            System.Windows.Automation.AutomationProperties.SetName(button, L.T(key));
            tabButtons[key] = button; tabStrip.Children.Add(button);
        }
        var annotate = Ui.Button(L.T("Annotate"), () => { if (bitmap != null && removal == null) new ScreenshotEditorWindow(bitmap, result => Edit(() => result), report, applyToImage: true) { Owner = this }.ShowDialog(); });
        annotate.Content = Ui.IconLabel("Pen", L.T("Annotate")); annotate.Margin = new Thickness(0, 8, 0, 0);
        var exportActions = new StackPanel(); exportActions.Children.Add(annotate); exportActions.Children.Add(export);
        DockPanel.SetDock(exportActions, Dock.Bottom); edits.Children.Add(exportActions);
        DockPanel.SetDock(tabStrip, Dock.Top); edits.Children.Add(tabStrip);
        var inspectorScroll = new ScrollViewer { Content = rowHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        rowHost.Margin = new Thickness(0, 0, 6, 0); edits.Children.Add(inspectorScroll); edits.IsEnabled = false; Mode("Size");
        var stage = new Grid(); imageScroll.Content = editCanvas;
        var canvas = new Border { Child = imageScroll, Background = Checkerboard(), CornerRadius = new CornerRadius(10), ClipToBounds = true }; stage.Children.Add(canvas);
        emptyImport = Ui.ImportPrompt("Image", L.T("Open image"), L.T("Choose a PNG, JPEG or BMP to edit a copy."), Open);
        emptyImport.Tag = "import-image-empty"; stage.Children.Add(emptyImport);
        void Zoom()
        {
            editCanvas.Handles.EndDrag(true);
            bool enlarged = previewZoom.Value > 1.001;
            imageScroll.HorizontalScrollBarVisibility = imageScroll.VerticalScrollBarVisibility = enlarged ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            editCanvas.Width = Math.Max(1, canvas.ActualWidth * previewZoom.Value);
            editCanvas.Height = Math.Max(1, canvas.ActualHeight * previewZoom.Value);
        }
        canvas.SizeChanged += (_, _) => Zoom(); previewZoom.ValueChanged += (_, _) => Zoom();
        var zoomTools = new StackPanel { Orientation = Orientation.Horizontal };
        var fit = Ui.IconButton("Maximize", L.T("Fit preview"), () => { previewZoom.Value = 1; imageScroll.ScrollToHome(); }); zoomTools.Children.Add(fit);
        zoomTools.Children.Add(previewZoom); var zoomValue = Ui.Text("1×", 12); zoomValue.Margin = new Thickness(8, 0, 0, 0); zoomValue.Width = 34; zoomTools.Children.Add(zoomValue);
        previewZoom.ValueChanged += (_, _) => zoomValue.Text = $"{previewZoom.Value:0.0}×";
        System.Windows.Automation.AutomationProperties.SetName(previewZoom, L.T("Preview zoom"));
        var zoomCard = Ui.Card(zoomTools, 5); zoomCard.HorizontalAlignment = HorizontalAlignment.Right; zoomCard.VerticalAlignment = VerticalAlignment.Top; zoomCard.Margin = new Thickness(10); stage.Children.Add(zoomCard);
        cancelRemoval.HorizontalAlignment = HorizontalAlignment.Right; cancelRemoval.VerticalAlignment = VerticalAlignment.Bottom; cancelRemoval.Margin = new Thickness(12); stage.Children.Add(cancelRemoval);
        var viewer = new Grid { Margin = new Thickness(0, 0, 16, 0) }; viewer.RowDefinitions.Add(new RowDefinition()); viewer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        viewer.Children.Add(stage); status.TextWrapping = TextWrapping.Wrap; var statusSurface = MediaWorkspaceLayout.Status(status); statusSurface.Margin = new Thickness(0, 10, 0, 0); Grid.SetRow(statusSurface, 1); viewer.Children.Add(statusSurface);
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        workspace.Children.Add(viewer); Grid.SetColumn(edits, 1); workspace.Children.Add(edits);
        Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        editCanvas.Margin = new Thickness(0);
        editCanvas.SelectionChanged += (rect, _) =>
        {
            updating = true;
            if (activeMode == "Crop")
            {
                cropX.Text = rect.X.ToString(); cropY.Text = rect.Y.ToString();
                cropWidth.Text = rect.Width.ToString(); cropHeight.Text = rect.Height.ToString();
            }
            else if (activeMode == "Size")
            {
                width.Text = rect.Width.ToString(); height.Text = rect.Height.ToString();
                if (editCanvas.ResizePreview && bitmap != null) reductionSlider.Value = Math.Clamp(rect.Width * 100d / bitmap.PixelWidth, 10, 100);
            }
            updating = false; status.Text = L.F($"{rect.Width} × {rect.Height} px");
        };
        var card = Ui.Card(root, 14); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        sizeInfo.TextWrapping = TextWrapping.Wrap;
        Closed += (_, _) => menu.IsOpen = false;
        System.Windows.Automation.AutomationProperties.SetName(width, L.T("Image width in pixels"));
        System.Windows.Automation.AutomationProperties.SetName(height, L.T("Image height in pixels"));
        System.Windows.Automation.AutomationProperties.SetName(aspect, L.T("Keep aspect ratio"));
        System.Windows.Automation.AutomationProperties.SetName(preview, L.T("Image preview"));
        width.TextChanged += (_, _) => SyncRatio(true); height.TextChanged += (_, _) => SyncRatio(false);
        aspect.Checked += (_, _) => SyncRatio(true);
        Motion.WindowEntrance(this);
        removeBackground.IsEnabled = false;
        Closed += (_, _) => { closed = true; removal?.Cancel(); revision++; preview.Source = null; bitmap = null; originalImage = null; savedImage = null; backgroundBefore = null; backgroundAfter = null; editCanvas.SetComparison(null, .5); encodedPreview = null; history = null; };
    }
    private void SyncRatio(bool fromWidth)
    {
        if (updating || bitmap == null || aspect.IsChecked != true) return;
        if (!int.TryParse(fromWidth ? width.Text : height.Text, out var value) || value < 1 || value > ImageTransforms.MaxDimension) return;
        updating = true;
        if (fromWidth) height.Text = Math.Max(1, (int)Math.Round((double)value * bitmap.PixelHeight / bitmap.PixelWidth)).ToString();
        else width.Text = Math.Max(1, (int)Math.Round((double)value * bitmap.PixelWidth / bitmap.PixelHeight)).ToString();
        updating = false;
    }
    private void Open()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Images|*.png;*.jpg;*.jpeg;*.bmp") };
        if (dialog.ShowDialog(this) != true) return;
        try { var loaded = ImageTransforms.Load(dialog.FileName); original = dialog.FileName; originalImage = savedImage = loaded; backgroundBefore = backgroundAfter = null; history = new ImageEditHistory(loaded); SetImage(loaded); }
        catch (Exception ex) { Error(ex); }
    }
    private void SetImage(BitmapSource image)
    {
        originalImage ??= image; history ??= new ImageEditHistory(image); previewZoom.Value = 1; imageScroll.ScrollToHome();
        undoButton.IsEnabled = history.CanUndo; redoButton.IsEnabled = history.CanRedo; ChangedOutput();
        bitmap = image; if (compare != null) { compare.IsChecked = false; compare.IsEnabled = ReferenceEquals(image, backgroundAfter) && backgroundBefore != null; } preview.Source = image; emptyImport.Visibility = Visibility.Collapsed; edits.IsEnabled = true; updating = true;
        removeBackground.IsEnabled = removal == null;
        if (activeMode == "Crop") cropRatio.SelectedItem = "Free";
        width.Text = image.PixelWidth.ToString(); height.Text = image.PixelHeight.ToString(); cropX.Text = cropY.Text = "0"; cropWidth.Text = image.PixelWidth.ToString(); cropHeight.Text = image.PixelHeight.ToString(); reductionSlider.Value = 100; updating = false; editCanvas.ResetSelection();
        status.Text = L.F($"{Path.GetFileName(original)} · {image.PixelWidth} × {image.PixelHeight} px · Original preserved");
    }
    private void Edit(Func<BitmapSource> transform)
    {
        if (bitmap == null) return;
        try { var next = transform(); history!.Apply(next); SetImage(next); } catch (Exception ex) { Error(ex); }
    }
    private void ResizeImage()
    {
        if (!int.TryParse(width.Text, out int w) || !int.TryParse(height.Text, out int h)) { status.Text = L.T("Enter whole pixel dimensions."); return; }
        Edit(() => ImageTransforms.Resize(bitmap!, w, h));
    }
    private void Export() => Export(this);
    private void Export(Window owner)
    {
        if (bitmap == null) return;
        var dialog = new ImageExportDialog(bitmap, original, outputFormat, (int)quality.Value) { Owner = owner }; dialog.ShowDialog();
        if (dialog.SavedPath == null) return;
        savedImage = bitmap; status.Text = L.T("Saved copy: ") + dialog.SavedPath; report(L.T("Image copy saved."));
    }
    private void ResetCrop()
    {
        if (bitmap == null) return;
        cropRatio.SelectedItem = "Free"; editCanvas.KeepRatio = false; editCanvas.ResizePreview = false;
        editCanvas.SetSelection(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight)); editCanvas.ShowHandles(true);
    }
    private void CropImage()
    {
        if (!int.TryParse(cropX.Text, out int x) || !int.TryParse(cropY.Text, out int y) || !int.TryParse(cropWidth.Text, out int w) || !int.TryParse(cropHeight.Text, out int h)) { status.Text = L.T("Enter whole pixel crop coordinates and dimensions."); return; }
        Edit(() => ImageTransforms.Crop(bitmap!, new Int32Rect(x, y, w, h)));
    }
    private void ChangedOutput() { revision++; encodedPreview = null; if (compare?.IsChecked != true) preview.Source = bitmap; sizeInfo.Text = L.T("Export changed. Choose Estimate size."); }
    private async Task Estimate()
    {
        if (bitmap == null || estimating) return;
        estimating = true; int version = revision; var image = bitmap; string format = outputFormat; int q = (int)quality.Value;
        sizeInfo.Text = L.T("Calculating…");
        try
        {
            long originalBytes = File.Exists(original) ? new FileInfo(original).Length : 0;
            byte[] encoded = await Task.Run(() => ImageTransforms.Encode(image, format, q));
            long bytes = encoded.LongLength;
            if (version == revision)
            {
                using var stream = new MemoryStream(encoded); var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad; decoded.StreamSource = stream; decoded.EndInit(); decoded.Freeze(); encodedPreview = decoded;
                if (compare?.IsChecked != true) preview.Source = decoded;
            }
            if (version == revision) sizeInfo.Text = L.F($"Export: {bytes / 1024d:0.0} KB") + (originalBytes > 0 ? L.F($" · Original: {originalBytes / 1024d:0.0} KB · {(bytes < originalBytes ? L.T("smaller") : L.T("not smaller"))}") : "");
        }
        catch (Exception ex) { if (version == revision) sizeInfo.Text = L.T("Estimate failed: ") + ex.Message; }
        finally { estimating = false; }
    }
    internal async Task RemoveBackgroundAsync()
    {
        if (bitmap == null || removal != null || closed) return;
        if (RefreshRuntimeWarning().Count > 0)
        {
            status.Text = L.T("Background removal is unavailable until Microsoft Visual C++ x64 is installed.");
            report(status.Text);
            return;
        }
        var input = bitmap; int version = ++revision;
        var cancellation = new CancellationTokenSource(); removal = cancellation;
        edits.IsEnabled = false; openControls.IsEnabled = false; removeBackground.IsEnabled = false; cancelRemoval.Visibility = Visibility.Visible;
        status.Text = L.T("Removing background locally…");
        try
        {
            var result = await BackgroundRemovalService.RemoveAsync(input, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed || revision != version) return;
            backgroundBefore = input; backgroundAfter = result; history!.Apply(result); SetImage(result); outputChoice.SelectedItem = "PNG";
            status.Text = L.T("Background removed. Check fine edges; Undo restores the image. Export PNG keeps transparency.");
        }
        catch (OperationCanceledException) { if (!closed) status.Text = L.T("Background removal canceled. The image is unchanged."); }
        catch (Exception ex) { if (!closed) Error(ex); }
        finally
        {
            removal = null; cancellation.Dispose();
            if (!closed) { edits.IsEnabled = bitmap != null; openControls.IsEnabled = true; removeBackground.IsEnabled = bitmap != null; cancelRemoval.Visibility = Visibility.Collapsed; }
        }
    }
    private static Brush Checkerboard()
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(220, 223, 228)), null, new Rect(0, 0, 16, 16));
            var darker = new SolidColorBrush(Color.FromRgb(180, 185, 193));
            context.DrawRectangle(darker, null, new Rect(0, 0, 8, 8)); context.DrawRectangle(darker, null, new Rect(8, 8, 8, 8));
        }
        drawing.Freeze(); var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16) }; brush.Freeze(); return brush;
    }
    private void Error(Exception ex) { status.Text = L.T("Image operation failed: ") + ex.Message; }
    private IReadOnlyList<string> RefreshRuntimeWarning()
    {
        var missing = readMissingRuntime();
        bool unavailable = missing.Count > 0;
        runtimeWarning.Text = unavailable
            ? L.T("Background removal needs Microsoft Visual C++ x64. Other image editing tools still work.")
                + "\n" + L.T("Missing files: ") + string.Join(", ", missing)
            : "";
        runtimeWarning.Visibility = runtimeHelp.Visibility = unavailable ? Visibility.Visible : Visibility.Collapsed;
        return missing;
    }
    private void OpenRuntimeHelp()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VisualCppRuntime.HelpUri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { status.Text = L.T("Could not open the Microsoft download page. Use this link: ") + VisualCppRuntime.HelpUri.AbsoluteUri + "\n" + ex.Message; }
    }
}
