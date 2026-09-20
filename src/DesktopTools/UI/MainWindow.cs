using DesktopTools.Localization;
using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Shell;

using DesktopTools.Core;

using DesktopTools.Native;



namespace DesktopTools.UI;



internal sealed partial class MainWindow : Window

{

    private readonly AppController controller;

    private readonly StackPanel page = new();

    private readonly Dictionary<string, Button> navigation = [];

    private readonly ScrollViewer scroller;

    internal static string VersionLabel => "DesktopTools " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    private string currentPage = "Home";

    private string? selectedProfile;
    private string settingsTab = "Appearance";

    private static readonly string[] AidNames = ["Click indicators", "Shortcut display", "Stopwatch", "Countdown", "Screen ruler", "Screen blackout"];

    private string? presentationSection;

    public MainWindow(AppController controller)

    {

        this.controller = controller; Title = "DesktopTools"; Width = 1120; Height = 800; MinWidth = 860; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterScreen;

        WindowStyle = WindowStyle.None; WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 52, ResizeBorderThickness = new Thickness(6, 0, 6, 6), CornerRadius = new CornerRadius(8), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false });

        SetResourceReference(BackgroundProperty, "Surface");

        var root = new Grid(); root.SetResourceReference(Panel.BackgroundProperty, "Shell"); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) }); root.RowDefinitions.Add(new RowDefinition());

        var title = new Grid { Margin = new Thickness(20, 5, 8, 0) }; title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal }; brand.Children.Add(Ui.AppIcon(26)); var name = Ui.Text(L.T("DesktopTools"), 15, true); name.Margin = new Thickness(10, 0, 0, 0); brand.Children.Add(name); title.Children.Add(brand);

        var caption = new StackPanel { Orientation = Orientation.Horizontal }; var min = Ui.IconButton("Minimize",L.T("Minimize"), () => WindowState = WindowState.Minimized); var max = Ui.IconButton("Maximize",L.T("Maximize or restore"), () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized); var close = Ui.IconButton("Close",L.T("Close to system tray"), CloseToTray);

        foreach (var b in new[] { min, max, close }) { b.Width = DesignTokens.CaptionButtonWidth; b.Height = DesignTokens.CaptionButtonHeight; b.MinHeight = DesignTokens.CaptionButtonHeight; b.Padding = new Thickness(9); b.Margin = new Thickness(2, 0, 0, 0); b.VerticalAlignment = VerticalAlignment.Center; b.Content = b == min ? Ui.Icon("Minimize", 14) : b == max ? Ui.Icon("Maximize", 14) : Ui.Icon("Close", 14); WindowChrome.SetIsHitTestVisibleInChrome(b, true); caption.Children.Add(b); } Grid.SetColumn(caption, 1); title.Children.Add(caption); root.Children.Add(title);

        var body = new Grid { Margin = new Thickness(12, 8, 14, 14) }; body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(206) }); body.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(body, 1); root.Children.Add(body);

        var sidebar = new DockPanel { Margin = new Thickness(0, 0, 14, 0), LastChildFill = true };

        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);

        var navItems = new StackPanel(); var navScroll = new ScrollViewer { Content = navItems, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; SmoothScroll.Enable(navScroll); sidebar.Children.Add(navScroll);

        foreach (var item in new[] { "Home", "Capture tools", "Presentation tools", "Media tools", "Text tools", "Desktop utilities", "Profiles", "Shortcuts", "Settings", "Help", "About" })

        {

            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new ColumnDefinition()); content.Children.Add(Ui.Icon(NavigationIcon(item), 18)); var label = Ui.Text(L.T(item), 13, true); label.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(label, 1); content.Children.Add(label);

            var b = Ui.Button(L.T(item), () => Navigate(item)); b.Content = content; b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.MinHeight = 41; b.Padding = new Thickness(14, 7, 14, 7); b.Margin = new Thickness(0, 0, 0, 4); b.BorderThickness = new Thickness(0);

            navigation[item] = b; (item is "Shortcuts" or "Settings" or "Help" or "About" ? footer : navItems).Children.Add(b);

        }

        AddUpdateIndicator(footer);
        var version = Ui.Text(VersionLabel, 11, muted: true); version.Name = "AppVersion"; version.Margin = new Thickness(14, 17, 0, 7); footer.Children.Add(version); body.Children.Add(sidebar);

        scroller = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(26, 25, 24, 22) };

        scroller.SetResourceReference(StyleProperty, "PageScroller");
        SmoothScroll.Enable(scroller);

        var frame = new Border { Child = scroller, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1) }; frame.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); frame.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); Grid.SetColumn(frame, 1); body.Children.Add(frame);

        Content = root;
        Loaded += (_, _) => UtilityWindowChrome.ClipContent(this);
        SizeChanged += (_, _) => UtilityWindowChrome.ClipContent(this);
        StateChanged += (_, _) => UtilityWindowChrome.ClipContent(this);

        controller.Hud.Changed += OnAidChanged;

        Closing += (_, e) => { if (!Application.Current.Dispatcher.HasShutdownStarted) { e.Cancel = true; CloseToTray(); } };

        SourceInitialized += (_, _) => NativeWindowService.ApplyBackdrop(this, controller.Dark, controller.Settings.Transparency);
        Loaded += (_, _) => DesktopTools.Presentation.WindowDismissal.Attach(this, () => Motion.Enabled);

        Closed += (_, _) => { controller.Hud.Changed -= OnAidChanged; };

        IsVisibleChanged += (_, _) => { if (IsVisible) Navigate(currentPage); };
        PreviewKeyDown += (_, e) => { if (modalLayers.Count == 0 && e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { Navigate("Home"); dashboardSearch?.Focus(); e.Handled = true; } };
        Navigate("Home");

    }



    private void CloseToTray() { DesktopTools.Presentation.WindowDismissal.Hide(this, () => Motion.Enabled); controller.ShowFirstTrayHint(); }
    private void HideImmediatelyForCapture()
    {
        using (DesktopTools.Presentation.WindowDismissal.Suppress())
            DesktopTools.Presentation.WindowDismissal.Hide(this, () => Motion.Enabled);
    }
    private readonly List<Action> stateRefreshers = [];
    private void RefreshState() { foreach (var refresh in stateRefreshers.ToArray()) refresh(); }
    private void OnAidChanged() => Dispatcher.BeginInvoke(RefreshState);

    public void Navigate(string destination)

    {

        if (destination == "Present") destination = "Presentation tools";

        double previousOffset = scroller.VerticalOffset;

        bool refreshing = currentPage == destination;
        bool pageChanged = !refreshing;

        try

        {

            presentationSection = null; dashboardSearch = null;

            currentPage = destination; stateRefreshers.Clear(); page.Children.Clear(); if (pageChanged) SmoothScroll.ScrollToOffset(scroller, 0);

            foreach (var (name, button) in navigation) { if (button.Content is Grid navGrid && navGrid.Children.OfType<TextBlock>().FirstOrDefault() is { } navLabel) navLabel.Text = L.T(name); if (name == destination) button.SetResourceReference(BackgroundProperty, "Selected"); else button.Background = Brushes.Transparent; }

            if (AidNames.Contains(destination))

            {

                navigation["Presentation tools"].SetResourceReference(BackgroundProperty, "Selected");

                var back = Ui.Button(L.T("‹  Presentation tools"), () => Navigate("Presentation tools")); back.HorizontalAlignment = HorizontalAlignment.Left; back.Margin = new Thickness(0, 0, 0, 18); page.Children.Add(back);

                PageBanner(destination, "Presentation aids", destination is "Countdown" or "Stopwatch" ? "Timer" : "Present"); AidSettings(destination); return;

            }

            if (destination is "Laser pointer" or "Cursor spotlight" or "Freeze frame")

            {

                navigation["Presentation tools"].SetResourceReference(BackgroundProperty, "Selected");

                var back = Ui.Button(L.T("‹  Presentation tools"), () => Navigate("Presentation tools")); back.HorizontalAlignment = HorizontalAlignment.Left; back.Margin = new Thickness(0, 0, 0, 18); page.Children.Add(back);

                PageBanner(destination, "Presentation aids", destination == "Laser pointer" ? "Laser" : destination == "Cursor spotlight" ? "Spotlight" : "Freeze"); presentationSection = L.T(destination); Present(); return;

            }

            if (destination == "Home") { Home(); return; }
            if (DashboardGroups.Contains(destination)) { DashboardCategory(destination); return; }

            PageBanner(destination, destination switch { "Draw" => "Set up your drawing tools and floating palette.", "Capture" => "Choose what to capture and what happens next.", "Utilities" => "Files, notes, and application audio in one place.", "Profiles" => "Save tool preferences for the way you work.", "Shortcuts" => "Set global shortcuts and learn the drawing controls.", "Settings" => "Appearance, startup, and display preferences.", "Help" => "Help and learning", _ => "An open-source toolkit for Windows." }, destination);

            switch (destination) { case "Utilities": Utilities(); break; case "Home": Home(); break; case "Draw": Draw(); break; case "Capture": Capture(); break; case "Profiles": Profiles(); break; case "Shortcuts": ShortcutsCatalog(); break; case "Settings": General(); break; case "Help": Help(); break; case "About": About(); break; }

        }

        finally

        {

            if (refreshing) { scroller.UpdateLayout(); SmoothScroll.ScrollToOffset(scroller, previousOffset); }
            else if (pageChanged) Motion.PageTransition(page);

        }

    }

    private void Change(Action<AppSettings> change) { if (!controller.UpdateSettings(change)) Navigate(currentPage); else RefreshState(); }

    private Button UtilityButton(string label, Action action, Func<bool> enabled) { var button = Ui.Button(L.T(label), action); void Refresh() => button.IsEnabled = enabled(); stateRefreshers.Add(Refresh); Refresh(); return button; }

    private void Utilities()

    {

        var s = controller.Settings;

        Group(L.T("Text tools"),
            Ui.Row(L.T("Open text tools"), L.T("Review, copy or translate recognized text."), UtilityButton(L.T("Open"), controller.OpenTextToolsWindow, () => controller.Settings.TranslationEnabled || controller.Settings.ScreenTextEnabled)),
            Ui.Row(L.T("Translation direction"), null, Ui.Choice(new[] { "English → Russian", "Russian → English" }, s.TranslationDirection == "ru-en" ? "Russian → English" : "English → Russian", v => Change(x => x.TranslationDirection = v == "Russian → English" ? "ru-en" : "en-ru"))));
        Group(L.T("Screen recorder"), Ui.Row(L.T("Screen recorder"), L.T("Record a monitor or region to MP4."), Ui.Button(L.T("Open"), controller.OpenScreenRecorder)));
        var wheelRows = new List<UIElement> { Ui.Row(L.T("Using the wheel"), L.T("Hold the shortcut, point to an action, then release. Escape or the center cancels."), Ui.Button(L.T("Open"), () => controller.OpenQuickWheel())) };
        for (int index=0;index<8;index++) { int slot=index; wheelRows.Add(Ui.Row(L.F($"Slot {index+1}"),L.T(index==0 ? "Clockwise from the top" : null), Ui.Choice(QuickWheelActions.Available, s.QuickWheelItems[index], value => Change(x => x.QuickWheelItems[slot]=value)))); }
        Group(L.T("Quick actions wheel"), wheelRows.ToArray());
        Group(L.T("Teleprompter"), Ui.Row(L.T("Open script"), L.T("Floating script with playback and adjustable speed."), Ui.Button(L.T("Open teleprompter"), controller.OpenTeleprompter)));
        Group(L.T("Pin active window"), Ui.Row(L.T("How it works"), L.T("Focus any application window and use the shortcut to pin or unpin it."), Ui.Text(L.T("Original state is restored when DesktopTools closes."), 12, muted: true)));
        Group(L.T("QR codes"), Ui.Row(L.T("Generator"), L.T("Generate locally from text or links."), Ui.Button(L.T("Open QR codes"), controller.OpenQrCodes)));
        Group(L.T("Screen eyedropper"), Ui.Row(L.T("Sample"), L.T("Click selects; Escape cancels."), Ui.Button(L.T("Pick screen color"), () => _ = controller.PickScreenColorAsync())));
        Group(L.T("Video editor"), Ui.Row(L.T("Editor"), L.T("Trim, cut, crop, rotate and export MP4."), Ui.Button(L.T("Open video editor"), controller.OpenVideoEditor)));
        Group(L.T("Image tools"), Ui.Row(L.T("Editor"), L.T("Resize, rotate, mirror and export PNG, JPEG or BMP."), Ui.Button(L.T("Open image tools"), controller.OpenImageTools)));
        Group(L.T("File shelf"), Ui.Row(L.T("Keep above other windows"), null, Ui.Toggle(s.FileShelfTopmost, v => Change(x => x.FileShelfTopmost = v))), Ui.Row(L.T("Open shelf"), L.T("Drop files and folders into a temporary shelf. Originals stay in place."), Ui.Button(L.T("Open file shelf"), controller.OpenFileShelf)));

        Group(L.T("Floating notes"), Ui.Row(L.T("New notes stay above other windows"), null, Ui.Toggle(s.NotesTopmost, v => Change(x => x.NotesTopmost = v))), Ui.Row(L.T("Manage notes"), L.T("Closing a note keeps its text saved."), Ui.Button(L.T("Open notes"), controller.OpenFloatingNotes)));

        Group(L.T("Audio controls"), Ui.Row(L.T("Application mixer"), L.T("Apps appear when they create an audio session."), Ui.Button(L.T("Open audio controls"), controller.OpenAudioControls)));

    }

    private void Group(string title, params UIElement[] rows)

    {

        if (presentationSection != null && presentationSection != title) return;

        var p = new StackPanel();
        var groupHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var groupIcon = Ui.Icon(SectionIcon(title), 20); groupIcon.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(groupIcon, Dock.Left); groupHeader.Children.Add(groupIcon);
        groupHeader.Children.Add(Ui.Text(title, 17, true)); p.Children.Add(groupHeader);

        var shortcutRows = FeatureShortcutRows(title).ToArray();
        for (int i = 0; i < shortcutRows.Length; i++)
        {
            if (i > 0) p.Children.Add(SettingsDivider(false));
            p.Children.Add(shortcutRows[i]);
        }
        if (shortcutRows.Length > 0 && rows.Length > 0) p.Children.Add(SettingsDivider(true));

        for (int i = 0; i < rows.Length; i++)

        {

            if (i > 0) p.Children.Add(SettingsDivider(rows[i - 1] is FrameworkElement previous && Equals(previous.Tag, "feature-shortcut-row")));

            p.Children.Add(rows[i]);

        }

        var section = Ui.Card(p, 20); section.Margin = new Thickness(0, 0, 0, 18); page.Children.Add(section);

    }

    private static Border SettingsDivider(bool afterShortcut)
    {
        var line = new Border
        {
            Tag = afterShortcut ? "settings-shortcut-divider" : "settings-row-divider",
            Height = afterShortcut ? 2 : 1,
            Margin = afterShortcut ? new Thickness(0, 10, 0, 8) : new Thickness(0, 5, 0, 5),
            Opacity = afterShortcut ? 1 : .72,
            IsHitTestVisible = false
        };
        line.SetResourceReference(Border.BackgroundProperty, "Divider");
        return line;
    }

    private CheckBox AidToggle(string name)
    {
        bool synchronizing = false;
        var toggle = Ui.Toggle(AidActive(name), _ => { if (!synchronizing) ToggleAid(name); });
        toggle.IsEnabled = FeatureAvailability.IsAvailable(controller.Settings, "aid-" + name);
        stateRefreshers.Add(() => { synchronizing = true; try { toggle.IsChecked = AidActive(name); toggle.IsEnabled = FeatureAvailability.IsAvailable(controller.Settings, "aid-" + name); } finally { synchronizing = false; } });
        return toggle;
    }

    private bool AidActive(string name) => name switch { "Click indicators" => controller.Hud.IsClickIndicatorsEnabled, "Shortcut display" => controller.Hud.IsKeystrokesEnabled, "Stopwatch" => controller.Hud.IsTimerVisible, "Countdown" => controller.Hud.IsCountdownVisible, "Screen ruler" => controller.Hud.IsRulerVisible, _ => controller.Hud.IsBlackoutVisible };

    private void ToggleAid(string name)

    {

        switch (name)

        {

            case "Click indicators": controller.Hud.ToggleClickIndicators(); break;

            case "Shortcut display": controller.Hud.ToggleKeystrokes(); break;

            case "Stopwatch": controller.Hud.ToggleTimer(); break;

            case "Countdown": controller.Hud.ToggleCountdown(); break;

            case "Screen ruler": controller.Hud.ToggleRuler(); break;

            case "Screen blackout": if (!controller.Hud.IsBlackoutVisible) Hide(); controller.Hud.ToggleBlackout(); break;

        }

    }

    private void AidRows()

    {

        foreach (var name in AidNames)

        {

            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var open = Ui.Button(L.F($"{L.T(name)} settings"), () => Navigate(name)); open.Background = Brushes.Transparent; open.BorderThickness = new Thickness(0); open.Padding = new Thickness(8, 12, 14, 12); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;

            var content = new DockPanel(); var arrow = Ui.Icon("Chevron", 13); DockPanel.SetDock(arrow, Dock.Right); content.Children.Add(arrow); content.Children.Add(Ui.Text(L.T(name), 14, true)); open.Content = content; row.Children.Add(open);

            var toggle = AidToggle(name); System.Windows.Automation.AutomationProperties.SetName(toggle, L.F($"Activate {L.T(name)}")); Grid.SetColumn(toggle, 1); row.Children.Add(toggle); page.Children.Add(row);

        }

    }

    private void BlackoutSettings()
    {
        Group(L.T("Screen blackout"),
            Ui.Row(L.T("Only hide from screen sharing (experimental)"), L.T("Keep your desktop visible locally. Off: cover all displays in black for you too. Changing this stops active blackout."),
                Ui.Toggle(controller.Settings.BlackoutSharingOnly, value => Change(s => s.BlackoutSharingOnly = value))),
            Ui.Text(L.T("Warning: your screen may still be visible in screen-sharing apps. We recommend checking the viewer's output before use. Sharing an individual application window may bypass this mode."), 13, true),
            Ui.Text(L.T("Applies to all connected displays. Press Escape to stop. This is not a security guarantee."), 12, muted: true));
    }

    private void AidSettings(string name)

    {

        var s = controller.Settings;

        var feature = FeatureAvailability.All.Single(f => f.Id == "aid-" + name);
        Group(L.T("Controls"),
            ShortcutCatalogView.Row(controller, FeatureShortcutCatalog.All.Single(entry => entry.FeatureId == feature.Id)),
            Ui.Row(L.T("Active"),L.T(name == "Screen blackout" ? "Press Escape to restore your desktop." : "Use this switch or the tool’s close button to stop it."), AidToggle(name)));

        switch (name)

        {

            case "Shortcut display":

                Group(L.T("Display"), Ui.Row(L.T("Visible duration"),L.T("Seconds after the last shortcut"), Slider(s.ShortcutDisplaySeconds, .5, 5, v => Change(x => x.ShortcutDisplaySeconds = v))), Ui.Row(L.T("Animate shortcuts"),L.T("Entrance, replacement, and dismissal; respects reduced motion."), Ui.Toggle(s.ShortcutAnimations, v => Change(x => x.ShortcutAnimations = v))), Ui.Text(L.T("Only shortcuts and navigation keys are displayed. Ordinary typing stays private."), 13, muted: true)); break;

            case "Click indicators":

                Group(L.T("Appearance"), Ui.Row(L.T("Color"), null, Ui.ColorButton(s.ClickColor, () => { controller.ShowControlDialog(new ColorPickerWindow(s.ClickColor, value => controller.UpdateSettings(x => x.ClickColor = value)) { Owner = this }); Navigate(name); })), Ui.Row(L.T("Diameter"),L.T("Logical pixels"), Slider(s.ClickSize, 24, 128, v => Change(x => x.ClickSize = v))), Ui.Row(L.T("Fade duration"),L.T("Milliseconds"), Slider(s.ClickDurationMilliseconds, 150, 1500, v => Change(x => x.ClickDurationMilliseconds = v)))); break;

            case "Countdown": Group(L.T("Defaults"), Ui.Row(L.T("Duration"),L.T("Minutes; changing this resets an active countdown."), Slider(s.CountdownMinutes, 1, 120, v => Change(x => x.CountdownMinutes = Math.Round(v))))); break;

            case "Screen ruler": Group(L.T("Defaults"), Ui.Row(L.T("Length"),L.T("Logical pixels, limited to the available display area."), Slider(s.RulerLength, 100, 1600, v => Change(x => x.RulerLength = Math.Round(v))))); break;

            case "Stopwatch": Group(L.T("Using the stopwatch"), Ui.Text(L.T("Start, pause, or reset using its floating controls. Drag the time display to move it."), 13)); break;

            case "Screen blackout": BlackoutSettings(); break;

        }

        Group(L.T("Profiles"), Ui.Row(L.T("Save these preferences"),L.T("Profiles include presentation preferences without starting tools automatically."), Ui.Button(L.T("Open profiles"), () => Navigate("Profiles"))));

    }

    private static FrameworkElement Slider(double value, double min, double max, Action<double> change)

    {

        var p = new StackPanel { Width = 165 }; var label = Ui.Text(value.ToString("0.##"), 12, muted: true); label.HorizontalAlignment = HorizontalAlignment.Right;

        var slider = new System.Windows.Controls.Slider { Minimum = min, Maximum = max, Value = value }; slider.ValueChanged += (_, _) => label.Text = slider.Value.ToString("0.##");

        slider.PreviewMouseLeftButtonUp += (_, _) => change(slider.Value); slider.KeyUp += (_, _) => change(slider.Value); p.Children.Add(label); p.Children.Add(slider); return p;

    }

    private void Draw()

    {

        var s = controller.Settings;

        Group(L.T("Drawing defaults"),

            Ui.Row(L.T("Default tool"), null, Ui.Choice(new[] { "Pen", "Highlighter", "Arrow", "Line", "Rectangle", "Ellipse", "Text", "Number", "Select", "Eraser" }, s.DefaultTool, v => { Change(x => x.DefaultTool = v); controller.SetTool(v); })),

            Ui.Row(L.T("Color"), null, Ui.ColorButton(s.Color, () => { controller.PickColor(); Navigate("Draw"); })),

            Ui.Row(L.T("Arrowhead size"),L.T("Relative to the stroke width"), Slider(s.ArrowHeadSize, .5, 3, v => Change(x => x.ArrowHeadSize = v))),

            Ui.Row(L.T("Stroke thickness"),L.T("Logical pixels"), Slider(s.Thickness, 1, 24, v => Change(x => x.Thickness = v))),

            Ui.Row(L.T("Opacity"), null, Slider(s.Opacity, .1, 1, v => Change(x => x.Opacity = v))),

            Ui.Row(L.T("Text size"),L.T(AnnotationTypography.DefaultFamily), Slider(s.FontSize, 12, 96, v => Change(x => x.FontSize = v))),

            Ui.Row(L.T("Filled shapes"),L.T("Fill rectangles and ellipses with the selected color."), Ui.Toggle(s.FillShapes, v => Change(x => x.FillShapes = v))),

            Ui.Row(L.T("Shape snapping"),L.T("Hold Shift for straight angles and equal sides."), Ui.Toggle(s.ShapeSnapping, v => Change(x => x.ShapeSnapping = v))));

        Group(L.T("Drawing controls"), Ui.Row(L.T("Hide controls from supported captures"),L.T("Support varies by capture method. Use the hide-palette shortcut as a fallback."), Ui.Toggle(AppCapturePrivacy.ShouldHideFeature("Drawing palette", s), v => Change(x => x.CaptureVisibilityOverrides["Drawing palette"] = v))),

            Ui.Row(L.T("Palette position"), null, Ui.Button(L.T("Reset position"), () => { Change(x => { x.PaletteX = null; x.PaletteY = null; }); controller.Report("Palette position resets on the next drawing session."); })));

    }

    private void Capture()

    {

        var s = controller.Settings;

        Group(L.T("Capture behavior"),

            Ui.Row(L.T("Freeze screen before selecting"), L.T("Capture a still when you start region capture, then choose from that frame."), Ui.Toggle(s.FreezeRegionBeforeSelection, v => Change(x => x.FreezeRegionBeforeSelection = v))),

            Ui.Row(L.T("Editor layout"), null, Ui.Choice(new[] { "B", "A" }, s.ScreenshotEditorLayout, v => Change(x => x.ScreenshotEditorLayout = v), translate: false)),

            Ui.Row(L.T("Capture displays"),L.T("All spans the desktop. Selected uses your app monitor preference."), Ui.Choice(new[] { "All", "Selected" }, s.CaptureMonitorMode, v => Change(x => x.CaptureMonitorMode = v))),

            Ui.Row(L.T("Capture delay"),L.T("After selecting a region; Escape cancels the countdown."), Ui.Choice(new[] { "0", "3", "5", "10" }, s.CaptureDelaySeconds.ToString(), v => Change(x => x.CaptureDelaySeconds = int.Parse(v)))),

            Ui.Row(L.T("Include annotations"),L.T("Composite your current drawing into the screenshot."), Ui.Toggle(s.IncludeAnnotations, v => Change(x => x.IncludeAnnotations = v))),

            Ui.Row(L.T("After capture"), null, Ui.Choice(new[] { "Clipboard", "Save" }, s.CaptureOutput, v => Change(x => x.CaptureOutput = v))),

            Ui.Row(L.T("Save location"),L.T(string.IsNullOrEmpty(s.SaveDirectory) ? "Chosen when you save your first screenshot." : s.SaveDirectory), Ui.Button(L.T("Choose folder"), () => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog(this) == true) Change(x => x.SaveDirectory = dialog.FolderName); Navigate("Capture"); })));

        Group(L.T("Latest screenshot"), Ui.Row(L.T("Pin above applications"),L.T("Resize it or adjust its opacity."), Ui.Button(L.T("Pin screenshot"), controller.PinLast)), Ui.Row(L.T("Edit screenshot"),L.T("Annotate, crop, or cover private details before sharing."), Ui.Button(L.T("Open editor"), controller.RedactLast)), Ui.Row(L.T("Save a copy"), null, Ui.Button(L.T("Save PNG"), controller.SaveLast)));

        var repeat = Ui.Button(L.T("Repeat region"), () => { HideImmediatelyForCapture(); _ = controller.CaptureAsync(true); }); repeat.IsEnabled = controller.CanRepeatCapture && s.CaptureEnabled;

        Group(L.T("Capture again"), Ui.Row(L.T("Last selected region"),L.T("Use the same area while the display layout stays unchanged."), repeat));

        var recent = new StackPanel();

        foreach (var entry in controller.CaptureHistory.Entries)

        {

            var button = Ui.Button(L.T("Open screenshot"), () => controller.OpenRecentCapture(entry.Image));

            var preview = new Image { Source = entry.Image, Width = 72, Height = 44, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) };

            var row = Ui.Row(L.T(entry.CreatedAt.ToLocalTime().ToString("HH:mm:ss")),L.F($"{entry.Image.PixelWidth} × {entry.Image.PixelHeight}"), button);

            var layout = new DockPanel(); DockPanel.SetDock(preview, Dock.Left); layout.Children.Add(preview); layout.Children.Add(row); recent.Children.Add(layout);

        }

        if (controller.CaptureHistory.Entries.Count == 0) recent.Children.Add(Ui.Text(L.T("Your recent screenshots appear here until you quit."), 13, muted: true));

        else recent.Children.Add(Ui.Button(L.T("Clear recent screenshots"), () => { controller.CaptureHistory.Clear(); Navigate("Capture"); }));

        Group(L.T("Recent screenshots"), recent);

    }

    private void Present()

    {

        var s = controller.Settings;

        Group(L.T("Laser pointer"), Ui.Row(L.T("Color"), null, Ui.ColorButton(s.LaserColor, () => { controller.PickLaserColor(); Navigate(currentPage); })), Ui.Row(L.T("Displays"),L.T("Follow across all monitors, or use the app monitor preference."), Ui.Choice(new[] { "All", "Selected" }, s.LaserMonitorMode, v => Change(x => x.LaserMonitorMode = v))), Ui.Row(L.T("Size"), null, Slider(s.LaserSize, 4, 40, v => Change(x => x.LaserSize = v))), Ui.Row(L.T("Fade duration"),L.T("Seconds"), Slider(s.LaserFadeSeconds, .1, 3, v => Change(x => x.LaserFadeSeconds = v))), Ui.Row(L.T("Start presenting"), null, Ui.Button(L.T("Laser pointer"), () => { Hide(); controller.TogglePresentation("Laser"); })));

        Group(L.T("Cursor spotlight"), Ui.Row(L.T("Displays"),L.T("Follow across all monitors, or use the app monitor preference."), Ui.Choice(new[] { "All", "Selected" }, s.SpotlightMonitorMode, v => Change(x => x.SpotlightMonitorMode = v))), Ui.Row(L.T("Radius"), null, Slider(s.SpotlightRadius, 40, 350, v => Change(x => x.SpotlightRadius = v))), Ui.Row(L.T("Background dimming"), null, Slider(s.SpotlightDim, .1, .9, v => Change(x => x.SpotlightDim = v))), Ui.Row(L.T("Start presenting"), null, Ui.Button(L.T("Spotlight"), () => { Hide(); controller.TogglePresentation("Spotlight"); })));

        if (presentationSection == null) { var heading = Ui.Text(L.T("Presentation aids"), 16, true); heading.Margin = new Thickness(0, 0, 0, 10); page.Children.Add(heading); AidRows(); }

        Group(L.T("Freeze frame"), Ui.Row(L.T("Toggle frozen desktop"), L.T("Annotate a still image; applications keep running underneath."), Ui.Button(L.T("Freeze / unfreeze"), () => _ = controller.FreezeAsync())));

    }

    private void Profiles()

    {

        var names = controller.Profiles.ListNames();

        if (selectedProfile == null || !names.Contains(selectedProfile)) selectedProfile = controller.ActiveProfile ?? names.First();

        var profileLayout = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        profileLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        profileLayout.ColumnDefinitions.Add(new ColumnDefinition());
        var choices = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        var selectedDetails = new StackPanel(); Grid.SetColumn(selectedDetails, 1);
        profileLayout.Children.Add(choices); profileLayout.Children.Add(selectedDetails);
        foreach (string profileName in names)
        {
            var profile = profileName;
            var contents = new StackPanel();
            var label = Ui.Text(ProfileStore.IsBuiltin(profile) ? L.T(profile) : profile, 14, true); label.Margin = new Thickness(0, 0, 0, 5); contents.Children.Add(label);
            if (controller.ActiveProfile == profile) contents.Children.Add(Ui.Text(L.T("Active"), 12, muted: true));
            var button = Ui.Button(profile, () => { selectedProfile = profile; Navigate("Profiles"); }); button.Content = contents; button.Padding = new Thickness(16); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0, 0, 10, 10);
            if (profile == selectedProfile) button.SetResourceReference(BackgroundProperty, "Selected"); choices.Children.Add(button);
        }
        page.Children.Add(profileLayout);

        string name = selectedProfile;

        try

        {

            var saved = controller.Profiles.Load(name);

            var details = new StackPanel();
            details.Children.Add(Ui.Text(ProfileStore.IsBuiltin(name) ? L.T(name) : name, 20, true));

            details.Children.Add(Ui.Row(L.T("Drawing"),L.F($"{L.T(saved.DefaultTool)} · {saved.Thickness:0.#} px · text {saved.FontSize:0} px"), new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(saved.Color)) }));

            details.Children.Add(Ui.Row(L.T("Draw shortcut"), null, Ui.Shortcut(saved.DrawShortcut)));

            details.Children.Add(Ui.Row(L.T("Capture shortcut"), null, Ui.Shortcut(saved.CaptureShortcut)));

            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };

            actions.Children.Add(Ui.Button(L.T("Apply profile"), () => { controller.ApplyProfile(name); Navigate("Profiles"); }, true));

            actions.Children.Add(Ui.Button(L.T("Save current tools"), () => { try { controller.Profiles.Save(name, controller.Settings); controller.Report(L.F($"Updated {name}.")); Navigate("Profiles"); } catch (Exception ex) { controller.Report(ex.Message); } }));

            if (controller.Profiles.HasSaved(name)) actions.Children.Add(Ui.Button(L.T(ProfileStore.IsBuiltin(name) ? "Restore preset" : "Delete profile"), () => { if (ConfirmationDialog.Ask(this, L.T("Remove profile"), L.F($"Remove saved preferences for {name}?"), L.T("Remove profile"))) { controller.Profiles.Delete(name); selectedProfile = null; Navigate("Profiles"); } }));

            details.Children.Add(actions); selectedDetails.Children.Add(Ui.Card(details, 18));

        }

        catch (Exception ex) { selectedDetails.Children.Add(Ui.Card(Ui.Text(ex.Message, 13, muted: true))); }

        var input = new TextBox { Width = 180, Margin = new Thickness(0, 0, 8, 0) }; System.Windows.Automation.AutomationProperties.SetName(input, L.T("New profile name"));

        var add = new WrapPanel(); add.Children.Add(input); add.Children.Add(Ui.Button(L.T("Create profile"), () => { try { if (controller.Profiles.ListNames().Contains(input.Text, StringComparer.OrdinalIgnoreCase)) { controller.Report(L.T("That profile already exists. Choose another name.")); return; } controller.Profiles.Save(input.Text, controller.Settings); selectedProfile = input.Text; Navigate("Profiles"); } catch (Exception ex) { controller.Report(ex.Message); } })); Group(L.T("Save as a new profile"), add);

        var help = Ui.Text(L.T("Profiles keep your appearance, startup and save location unchanged. Shortcut conflicts leave the current profile active."), 12, muted: true); help.Margin = new Thickness(0, 16, 0, 0); page.Children.Add(help);

    }

    private void Shortcuts()

    {

        var s = controller.Settings;

        var search = new TextBox { Tag = "shortcut-search", Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("Find a shortcut"));
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        page.Children.Add(search); page.Children.Add(tabs);
        var rows = new List<UIElement>();
        var searchable = new Dictionary<UIElement, string>();
        Grid SearchableRow(string title, string? description, FrameworkElement control)
        {
            var row = Ui.Row(title, description, control); searchable.Add(row, title); return row;
        }

        foreach (var (name, value, set) in new (string, string, Action<AppSettings, string>)[] { ("Translate selected text", s.TranslationShortcut, (x,v) => x.TranslationShortcut=v), ("Scan screen text", s.ScreenTextShortcut, (x,v) => x.ScreenTextShortcut=v), ("Quick actions wheel", s.QuickWheelShortcut, (x,v) => x.QuickWheelShortcut=v), ("Teleprompter play / pause", s.TeleprompterShortcut, (x,v) => x.TeleprompterShortcut=v), ("Pin active window", s.WindowPinShortcut, (x,v) => x.WindowPinShortcut=v), ("Screen eyedropper", s.EyedropperShortcut, (x,v) => x.EyedropperShortcut=v), ("Draw / interact", s.DrawShortcut, (x,v) => x.DrawShortcut=v), ("Capture region", s.CaptureShortcut, (x,v) => x.CaptureShortcut=v), ("Hide / show palette", s.HidePaletteShortcut, (x,v) => x.HidePaletteShortcut=v), ("Laser pointer", s.LaserShortcut, (x,v) => x.LaserShortcut=v), ("Spotlight", s.SpotlightShortcut, (x,v) => x.SpotlightShortcut=v), ("Freeze frame", s.FreezeShortcut, (x,v) => x.FreezeShortcut=v) })

        {

            var change = Ui.Button(L.T("Change shortcut"), () => { controller.RecordShortcut(value, set); Navigate("Shortcuts"); });

            change.Content = Ui.Shortcut(value); change.Padding = new Thickness(10, 6, 6, 6);

            rows.Add(SearchableRow(L.T(name),L.T("Click to record a key combination."), change));

        }

        var global = new StackPanel(); foreach (var row in rows) global.Children.Add(row);
        var globalCard = Ui.Card(global); page.Children.Add(globalCard);

        rows.Clear();

        foreach (var (action, value) in s.DrawingShortcuts)

        {

            var change = Ui.Button(L.T("Change shortcut"), () => { controller.RecordDrawingShortcut(action); Navigate("Shortcuts"); });

            change.Content = Ui.Shortcut(value); change.Padding = new Thickness(10, 6, 10, 6);

            rows.Add(SearchableRow(L.T(action == "FinishText" ? "Finish text" : action),L.T(action == "Delete" ? "Delete selected annotation, or clear the canvas." : null), change));

        }

        rows.Add(SearchableRow(L.T("Cancel / hide"),L.T("Always available as an emergency exit."), Ui.Shortcut("Esc")));

        rows.Add(SearchableRow(L.T("Snap shapes"),L.T("Hold while drawing a shape."), Ui.Shortcut("Shift")));

        var drawing = new StackPanel(); foreach (var row in rows) drawing.Children.Add(row);
        var drawingCard = Ui.Card(drawing); page.Children.Add(drawingCard);
        var empty = Ui.Text(L.T("No shortcuts found."), 13, muted: true); page.Children.Add(empty);
        bool drawingSelected = false;
        var globalTab = Ui.Button(L.T("Global shortcuts"), () => { });
        var drawingTab = Ui.Button(L.T("While drawing"), () => { });
        globalTab.Click += (_, _) => { drawingSelected = false; RefreshShortcuts(); };
        drawingTab.Click += (_, _) => { drawingSelected = true; RefreshShortcuts(); };
        globalTab.Tag = "shortcut-tab-global"; drawingTab.Tag = "shortcut-tab-drawing";
        tabs.Children.Add(globalTab); tabs.Children.Add(drawingTab);
        void RefreshShortcuts()
        {
            globalCard.Visibility = drawingSelected ? Visibility.Collapsed : Visibility.Visible;
            drawingCard.Visibility = drawingSelected ? Visibility.Visible : Visibility.Collapsed;
            globalTab.SetResourceReference(BackgroundProperty, drawingSelected ? "Field" : "Selected");
            drawingTab.SetResourceReference(BackgroundProperty, drawingSelected ? "Selected" : "Field");
            foreach (var (row, title) in searchable) row.Visibility = title.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            var active = drawingSelected ? drawing : global;
            empty.Visibility = active.Children.Cast<UIElement>().Any(row => row.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }
        search.TextChanged += (_, _) => RefreshShortcuts(); RefreshShortcuts();

    }

    private void General()

    {

        var s = controller.Settings;

        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
        foreach (var name in new[] { "Appearance", "Behavior", "Privacy", "Notifications", "Updates" })
        {
            var tab = Ui.Button(L.T(name), () => { if (settingsTab == name) return; settingsTab = name; Navigate("Settings"); SmoothScroll.ScrollToOffset(scroller, 0); Motion.Transition(page); });
            var tabContent = new StackPanel { Orientation = Orientation.Horizontal }; var tabIcon = Ui.Icon(name switch { "Appearance" => "Color", "Privacy" => "Shield", "Notifications" => "Notifications", "Updates" => "Refresh", _ => "Behavior" }, 18); tabIcon.Margin = new Thickness(0,0,9,0); tabContent.Children.Add(tabIcon); tabContent.Children.Add(Ui.Text(L.T(name),13,true)); tab.Content = tabContent; tab.Padding = new Thickness(16,10,16,10);
            tab.Margin = new Thickness(0, 0, 7, 7);
            if (name == settingsTab) { tab.SetResourceReference(BackgroundProperty, "Selected"); tab.SetResourceReference(BorderBrushProperty, "Accent"); tab.BorderThickness = new Thickness(1); }
            tabs.Children.Add(tab);
        }
        page.Children.Add(tabs);
        if (settingsTab == "Behavior") Group(L.T("Language"), Ui.Row(L.T("Interface language"), L.T("Applies after restarting DesktopTools. Your open work stays untouched."),
            Ui.Choice(L.LanguageNames, L.LanguageNames[Array.IndexOf(L.Languages, s.Language)], name => Change(x => x.Language = L.Languages[Array.IndexOf(L.LanguageNames, name)]))));
        if (settingsTab == "Appearance")
        {
            ThemePreviews();
            Group(L.T("Theme"),
                Ui.Row(L.T("Palette"),L.T("Presets set the primary color. Custom keeps your chosen colors."), Ui.Choice(new[] { "Classic", "Ocean", "Forest", "Violet", "Custom" }, s.ThemePreset, v => { Change(x => x.ThemePreset = v); Navigate("Settings"); })),
                Ui.Row(L.T("Primary color"), null, Ui.ColorButton(s.PrimaryColor, () => PickAppearance(false))),
                Ui.Row(L.T("Background color"),L.T("Choose a background to enable a custom palette."), Ui.ColorButton(s.BackgroundColor, () => PickAppearance(true))),
                Ui.Row(L.T("Use custom background"), null, Ui.Toggle(s.UseCustomBackground, v => Change(x => x.UseCustomBackground = v))));
            Group(L.T("Effects"), Ui.Row(L.T("Transparency"),L.T("Use opaque surfaces when disabled."), Ui.Toggle(s.Transparency, v => Change(x => x.Transparency = v))),
                Ui.Row(L.T("Animations"),L.T("Also respects the Windows animation preference."), Ui.Toggle(s.Animations, v => Change(x => x.Animations = v))));
        }
        else if (settingsTab == "Updates")
        {
            page.Children.Add(UpdateCard(preferences: true));
        }
        else if (settingsTab == "Notifications")
        {
            NotificationSettings();
        }
        else if (settingsTab == "Privacy")
        {
            Group(L.T("Screen sharing"),
                Ui.Row(L.T("Hide main app"), L.T("Hide the DesktopTools main window from supported screen-sharing apps."), Ui.Toggle(s.HideMainWindowFromCapture ?? false, v => Change(x => x.HideMainWindowFromCapture = v))),
                Ui.Text(L.T("Screen-sharing support varies. Test the viewer output before presenting."), 12, muted: true));
            var featureRows = new List<UIElement>();
            foreach (string feature in AppCapturePrivacy.FeatureGroups)
            {
                var key = feature;
                var toggle = Ui.Toggle(AppCapturePrivacy.ShouldHideFeature(key, s), hide => Change(x => x.CaptureVisibilityOverrides[key] = hide));
                toggle.Tag = "capture-privacy-" + key;
                featureRows.Add(Ui.Row(L.T(key), L.T("Hide this tool from supported screen sharing and recordings."), toggle));
            }
            var hiddenList = new StackPanel(); foreach (var row in featureRows) hiddenList.Children.Add(row);
            var hiddenHeader = Ui.Text("", 14, true);
            void RefreshHiddenCount() => hiddenHeader.Text = L.T("Hidden features") + " · " +
                AppCapturePrivacy.FeatureGroups.Count(feature => AppCapturePrivacy.ShouldHideFeature(feature, controller.Settings)) + " / " + AppCapturePrivacy.FeatureGroups.Length;
            stateRefreshers.Add(RefreshHiddenCount); RefreshHiddenCount();
            var hidden = new Expander { Header = hiddenHeader, Content = hiddenList, Tag = "privacy-features", IsExpanded = true };
            page.Children.Add(Ui.Card(hidden));
            var individual = new List<UIElement>();
            foreach (string tool in AppCapturePrivacy.IndividualTools)
            {
                var key = tool;
                individual.Add(Ui.Row(L.T(key), L.T("Hide this tool from supported screen sharing and recordings."), Ui.Toggle(AppCapturePrivacy.ShouldHideFeature(key, s), hide => Change(x => x.CaptureVisibilityOverrides[key] = hide))));
            }
            Group(L.T("Individual tool visibility"), individual.ToArray());
            page.Children.Add(Ui.Text(L.T("Drawing, laser, spotlight, click indicators and shortcut labels are always visible to your audience. Their controls can be hidden separately."), 12, muted: true));
            BlackoutSettings();
        }
        else
        {
            Group(L.T("App behavior"), Ui.Row(L.T("Start at login"),L.T("Launch quietly in the system tray."), Ui.Toggle(s.StartAtLogin, v => Change(x => x.StartAtLogin = v))), Ui.Row(L.T("Active monitor"),L.T("Changing this ends the current drawing session."), Ui.Choice(new[] { "Cursor", "Primary" }, s.MonitorMode, v => Change(x => x.MonitorMode = v))), Ui.Row(L.T("Close to system tray"),L.T("Use Quit in the tray menu to exit completely."), Ui.Text(L.T("Always"), 13, muted: true)));
            Group(L.T("Preferences"),
                Ui.Row(L.T("Restore defaults"), L.T("Resets app settings and shortcuts."), Ui.Button(L.T("Reset preferences"), () => { if (!ConfirmationDialog.Ask(this, L.T("Reset preferences"), L.T("Reset all preferences? Your current drawing will be cleared."), L.T("Reset preferences"))) return; Change(x => { var defaults = new AppSettings(); foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite)) p.SetValue(x, p.GetValue(defaults)); }); Navigate("Settings"); })),
                Ui.Row(L.T("Reset all app data"), L.T("Deletes local settings, profiles and notes, then restarts DesktopTools with first-run setup. Exported files stay where you saved them."), Ui.Button(L.T("Delete app data"), () => controller.RequestDataReset(this))));
        }
    }
    private void PickAppearance(bool background)
    {
        var s = controller.Settings;
        controller.ShowControlDialog(new ColorPickerWindow(background ? s.BackgroundColor : s.PrimaryColor, value => controller.UpdateSettings(x => { x.ThemePreset = "Custom"; if (background) { x.BackgroundColor = value; x.UseCustomBackground = true; } else x.PrimaryColor = value; })) { Owner = this });
        Navigate("Settings");
    }



}
