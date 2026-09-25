using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;

namespace DesktopTools.UI;

internal sealed class SetupWindow : Window
{
    private static readonly string[] Steps = ["Appearance", "Tools", "Shortcuts", "Ready"];
    private readonly AppController controller;
    private StackPanel content = new();
    private readonly Grid steps = new();
    private readonly Grid body = new();
    private readonly Grid pageHost = new() { ClipToBounds = true };
    private readonly StackPanel preview = new();
    private readonly ScrollViewer scroll;
    private readonly Border previewCard;
    private readonly Button back, next;
    private int renderedStep = -1;
    private int transitionVersion;
    internal SetupWindow(AppController controller)
    {
        this.controller = controller;
        Title = L.T("Initial setup"); Width = 940; Height = 650;
        Motion.WindowEntrance(this);
        WindowStyle = WindowStyle.None; Background = Brushes.Transparent;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var close = Ui.IconButton("Close", L.T("Close setup"), Close); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var brand = Ui.AppIcon(40); brand.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(brand, Dock.Left); header.Children.Add(brand);
        var heading = new StackPanel(); heading.Children.Add(Ui.Text(L.T("Make DesktopTools yours"), 24, true));
        heading.Children.Add(Ui.Text(L.T("A few choices to help you get started. You can change everything later."), 12, muted: true));
        header.Children.Add(heading); root.Children.Add(header);
        steps.Margin = new Thickness(0, 0, 0, 22); Grid.SetRow(steps, 1); root.Children.Add(steps);
        body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        scroll = new ScrollViewer { Content = pageHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
        SmoothScroll.Enable(scroll); body.Children.Add(scroll);
        previewCard = Ui.Card(preview, 22); previewCard.Width = 272; previewCard.Margin = new Thickness(18, 0, 0, 0);
        previewCard.VerticalAlignment = VerticalAlignment.Top; previewCard.SetResourceReference(Border.BackgroundProperty, "Field");
        Grid.SetColumn(previewCard, 1); body.Children.Add(previewCard); Grid.SetRow(body, 2); root.Children.Add(body);
        var footer = new DockPanel { Margin = new Thickness(0, 22, 0, 0) }; Grid.SetRow(footer, 3); root.Children.Add(footer);
        var skip = Ui.Button(L.T("Set up later"), () => { if (controller.UpdateSettings(s => s.Setup.Status = SetupStatus.Skipped)) Close(); });
        skip.Tag = "setup-skip"; DockPanel.SetDock(skip, Dock.Left); footer.Children.Add(skip);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        back = Ui.Button(L.T("Back"), () => Move(-1)); back.Tag = "setup-back";
        next = Ui.Button(L.T("Next"), () => Move(1), true); next.Tag = "setup-next"; next.MinWidth = 120;
        navigation.Children.Add(back); navigation.Children.Add(next); footer.Children.Add(navigation);
        var card = Ui.Card(root, 28); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        RenderStep();
    }
    private void Move(int direction)
    {
        int step = controller.Settings.Setup.Step;
        if (step == 3 && direction > 0)
        {
            if (controller.UpdateSettings(s => s.Setup.Status = SetupStatus.Completed)) Close();
            return;
        }
        if (controller.UpdateSettings(s => s.Setup.Step = Math.Clamp(step + direction, 0, 3))) RenderStep(direction);
    }
    private void Change(Action<AppSettings> change)
    {
        if (!controller.UpdateSettings(change)) RenderStep(); else RenderPreview();
    }
    private void RenderStep(int direction = 0)
    {
        int step = controller.Settings.Setup.Step; var s = controller.Settings;
        bool pageChanged = renderedStep >= 0 && step != renderedStep;
        StackPanel outgoing = content;
        CancelPageTransition();
        if (pageChanged) content = new StackPanel();
        else content.Children.Clear();
        renderedStep = step;
        steps.Children.Clear(); steps.ColumnDefinitions.Clear();
        for (int i = 0; i < Steps.Length; i++)
        {
            steps.ColumnDefinitions.Add(new ColumnDefinition());
            var label = Ui.Text(L.T(Steps[i]), 13, i == step, muted: i != step);
            var item = new Border { Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(8), Child = label };
            if (i == step) item.SetResourceReference(Border.BackgroundProperty, "Selected");
            Grid.SetColumn(item, i); steps.Children.Add(item);
        }
        back.IsEnabled = step > 0; next.Content = L.T(step == 3 ? "Open DesktopTools" : "Next");
        previewCard.Visibility = step < 2 ? Visibility.Visible : Visibility.Collapsed;
        string title = step switch { 0 => "Choose how it looks", 1 => "Explore your tools", 2 => "Your keyboard, your shortcuts", _ => "You're ready" };
        content.Children.Add(Ui.Text(L.T(title), 20, true));
        var description = Ui.Text(L.T(step switch
        {
            0 => "Try a theme and see it change right here.",
            1 => "All tools are ready to open. None starts automatically; choose shortcuts on the next page.",
            2 => "Default shortcuts are on. Extra shortcuts are off until you enable them. Click any key combination to change it.",
            _ => "Your choices are saved. Open a tool from Home, or use one of your enabled shortcuts."
        }), 13, muted: true);
        description.Margin = new Thickness(0, 8, 0, 16); content.Children.Add(description);
        if (step == 0)
        {
            content.Children.Add(Ui.Row(L.T("Interface language"), L.T("DesktopTools restarts automatically to apply the new language."),
                Ui.Choice(L.LanguageNames, L.LanguageNames[Array.IndexOf(L.Languages, s.Language)],
                    async name =>
                    {
                        if (!await controller.ChangeLanguageAsync(L.Languages[Array.IndexOf(L.LanguageNames, name)])) RenderStep();
                    }, translate: false)));
            var themes = new WrapPanel();
            foreach (string theme in new[] { "System", "Light", "Dark" })
            {
                var choice = Ui.Button(L.T(theme), () => { Change(x => x.Theme = theme); RenderStep(); });
                choice.MinWidth = 92; if (s.Theme == theme) choice.SetResourceReference(BackgroundProperty, "Selected"); themes.Children.Add(choice);
            }
            content.Children.Add(themes);
            content.Children.Add(Ui.Row(L.T("Transparency"), L.T("Let a little desktop color show through floating windows."), Ui.Toggle(s.Transparency, value => Change(x => x.Transparency = value))));
            content.Children.Add(Ui.Row(L.T("Animations"), L.T("Smooth transitions when you open tools and change settings."), Ui.Toggle(s.Animations, value => Change(x => x.Animations = value))));
            content.Children.Add(Ui.Row(L.T("Start at login"), L.T("Keep your tools and shortcuts ready after you sign in."), Ui.Toggle(s.StartAtLogin, value => Change(x => x.StartAtLogin = value))));
        }
        else if (step == 1)
        {
            foreach (var feature in FeatureAvailability.All)
            {
                var row = Ui.Row(L.T(FeatureTitle(feature.Id)), L.T(FeatureDescription(feature.Id)), Ui.Icon("Check", 16));
                row.MouseEnter += (_, _) => RenderPreview(feature.Id);
                content.Children.Add(row);
            }
        }
        else if (step == 2)
        {
            foreach (var entry in FeatureShortcutCatalog.All) content.Children.Add(ShortcutCatalogView.Row(controller, entry));
            var drawing = new StackPanel();
            foreach (var action in s.DrawingShortcuts.Keys)
            {
                var edit = Ui.Button(s.DrawingShortcuts[action], () => { controller.RecordDrawingShortcut(action); RenderStep(); });
                drawing.Children.Add(Ui.Row(L.T(action == "FinishText" ? "Finish text" : action), null, edit));
            }
            drawing.Children.Add(Ui.Row(L.T("Cancel / hide"), L.T("Always available as an emergency exit."), Ui.Shortcut("Esc")));
            drawing.Children.Add(Ui.Row(L.T("Snap shapes"), L.T("Hold while drawing a shape."), Ui.Shortcut("Shift")));
            content.Children.Add(new Expander { Header = L.T("While drawing"), Content = drawing, Margin = new Thickness(0, 14, 0, 0) });
        }
        else
        {
            content.Children.Add(Ui.Row(L.T("Appearance"), L.T("You can adjust this in Settings."), Ui.Text(L.T(s.Theme), 14, true)));
            content.Children.Add(Ui.Row(L.T("Tools available"), null, Ui.Text(FeatureAvailability.All.Count.ToString(), 16, true)));
            content.Children.Add(Ui.Row(L.T("Global shortcuts"), null, Ui.Text(FeatureShortcutCatalog.All.Count(e => FeatureShortcutCatalog.IsEnabled(s, e)).ToString(), 16, true)));
            content.Children.Add(Ui.Text(L.T("Presenting or recording?"), 16, true));
            var sharing = Ui.Text(L.T("Drawing and audience effects stay visible. Recorder controls, drawing controls, notes, notifications and the teleprompter start hidden. Change each choice in Settings → Privacy."), 13, muted: true);
            sharing.Margin = new Thickness(0, 8, 0, 0); content.Children.Add(sharing);
            var warning = Ui.Text(L.T("Hiding depends on the recording or sharing app. These windows might still be visible. Check what your viewers see."), 13, true);
            warning.Margin = new Thickness(0, 12, 0, 12); content.Children.Add(warning);
            content.Children.Add(Ui.Row(L.T("Hide main app"), null, Ui.Toggle(s.HideMainWindowFromCapture ?? false, hide => Change(x => x.HideMainWindowFromCapture = hide))));
            foreach (string feature in AppCapturePrivacy.FeatureGroups.Concat(AppCapturePrivacy.IndividualTools))
            {
                string key = feature;
                var choice = Ui.Toggle(AppCapturePrivacy.ShouldHideFeature(key, s), hide => Change(x => x.CaptureVisibilityOverrides[key] = hide)); choice.Tag = "setup-privacy-" + key;
                content.Children.Add(Ui.Row(L.T(key), L.T("Hide this tool from supported screen sharing and recordings."), choice));
            }
        }
        RenderPreview(); SmoothScroll.ScrollToOffset(scroll, 0);
        if (pageChanged) BeginPageTransition(outgoing, content, direction);
        else
        {
            SettlePage(content);
            pageHost.Children.Clear();
            pageHost.Children.Add(content);
        }
    }
    private void CancelPageTransition()
    {
        transitionVersion++;
        foreach (StackPanel page in pageHost.Children.OfType<StackPanel>().ToArray()) SettlePage(page);
        pageHost.Children.Clear();
        pageHost.Children.Add(content);
    }
    private void BeginPageTransition(StackPanel outgoing, StackPanel incoming, int direction)
    {
        int version = ++transitionVersion;
        pageHost.Children.Clear();
        pageHost.Children.Add(outgoing);
        pageHost.Children.Add(incoming);
        SettlePage(outgoing);
        SettlePage(incoming);
        // The old controls remain visible only as an animation layer.
        outgoing.IsHitTestVisible = false;
        outgoing.IsEnabled = false;
        if (!Motion.Enabled)
        {
            pageHost.Children.Remove(outgoing);
            return;
        }

        double distance = direction >= 0 ? 28 : -28;
        var outgoingOffset = (TranslateTransform)outgoing.RenderTransform;
        var incomingOffset = (TranslateTransform)incoming.RenderTransform;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(200);
        outgoing.Opacity = 0;
        outgoingOffset.X = -distance;
        incoming.Opacity = 1;
        incomingOffset.X = 0;
        outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        outgoingOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -distance, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            if (version != transitionVersion) return;
            SettlePage(incoming);
            pageHost.Children.Clear();
            pageHost.Children.Add(incoming);
        };
        incoming.BeginAnimation(OpacityProperty, fade);
        incomingOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(distance, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    private static void SettlePage(StackPanel page)
    {
        page.BeginAnimation(OpacityProperty, null);
        page.Opacity = 1;
        var offset = page.RenderTransform as TranslateTransform ?? new TranslateTransform();
        page.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        offset.X = 0;
    }
    private void RenderPreview(string? featureId = null)
    {
        preview.Children.Clear();
        var s = controller.Settings;
        preview.Children.Add(Ui.Text(L.T("Live preview"), 12, muted: true));
        if (s.Setup.Step == 1)
        {
            string id = featureId ?? "capture";
            var icon = Ui.Icon(id switch { "record" => "Monitor", "draw" => "Draw", "laser" => "Laser", "spotlight" => "Spotlight", "qr" => "QR", "notes" => "Notes", "prompter" => "Prompter", "audio" => "Audio", "images" => "Image", "video" => "Video", "color" => "Eyedropper", "files" => "Folder", "translate" => "Translate", _ => "Capture" }, 36);
            icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(0, 24, 0, 18); preview.Children.Add(icon);
            preview.Children.Add(Ui.Text(L.T(FeatureTitle(id)), 18, true));
            var description = Ui.Text(L.T(FeatureDescription(id)), 13, muted: true); description.Margin = new Thickness(0, 10, 0, 22); preview.Children.Add(description);
            preview.Children.Add(Ui.Text(L.T("Move over a tool to learn what it does."), 12, muted: true));
            preview.Children.Add(Ui.Text(L.T("Tools available") + " · " + FeatureAvailability.All.Count, 16, true));
        }
        else
        {
            var brand = Ui.AppIcon(44); brand.HorizontalAlignment = HorizontalAlignment.Left; brand.Margin = new Thickness(0, 20, 0, 16); preview.Children.Add(brand);
            preview.Children.Add(Ui.Text("DesktopTools", 19, true));
            var hint = Ui.Text(L.T("Ready when you need it."), 12, muted: true); hint.Margin = new Thickness(0, 6, 0, 20); preview.Children.Add(hint);
            foreach (var pair in new[] { ("Capture", "Capture region"), ("Draw", "Screen drawing"), ("Notes", "Floating notes") })
            {
                var row = Ui.IconLabel(pair.Item1, L.T(pair.Item2), 20); row.Margin = new Thickness(0, 0, 0, 14); preview.Children.Add(row);
            }
            var sample = Ui.Button(L.T("Try animation"), () => Motion.ModalEntrance(preview), true); sample.Margin = new Thickness(0, 10, 0, 0); preview.Children.Add(sample);
            previewCard.Opacity = s.Transparency ? .94 : 1;
        }
        Motion.Reveal(preview);
    }
    private static string FeatureTitle(string id) => id switch
    {
        "capture" => "Region capture", "record" => "Screen recorder", "draw" => "Screen drawing", "laser" => "Laser pointer", "spotlight" => "Cursor spotlight",
        "freeze" => "Freeze frame", "prompter" => "Teleprompter", "images" => "Image tools", "video" => "Video editor", "color" => "Screen eyedropper",
        "ocr" => "Scan screen text", "translate" => "Local translation", "qr" => "QR codes", "notes" => "Floating notes", "files" => "File shelf",
        "audio" => "Audio controls", "wheel" => "Quick actions wheel", "window" => "Pin active window", _ => id.StartsWith("aid-", StringComparison.Ordinal) ? id[4..] : id
    };
    private static string FeatureDescription(string id) => id switch
    {
        _ when id.StartsWith("aid-", StringComparison.Ordinal) => "Presentation aids",
        "capture" => "Select, edit, and share a screenshot.", "record" => "Record a monitor or region to MP4.",
        "draw" => "Annotate over any application.", "images" => "Resize, rotate, mirror and convert images.",
        "video" => "Trim, cut, crop, rotate and export MP4.", "color" => "Pick a desktop pixel and copy HEX or RGB.",
        "ocr" => "Recognize a selected screen area without saving a screenshot.", "translate" => "Russian and English, processed on this computer.",
        "qr" => "Create a code from text or a link.", "notes" => "Keep notes and checklists above your work.",
        "files" => "Collect files for dragging between apps.", "audio" => "Adjust volume and mute for individual apps.",
        "wheel" => "Hold the shortcut, point to an action, then release. Escape or the center cancels.",
        "prompter" => "Floating script with playback and adjustable speed.", "window" => "Focus any application window and use the shortcut to pin or unpin it.",
        "freeze" => "Annotate a still image; applications keep running underneath.", _ => "Guide attention with a laser, spotlight, or frozen screen."
    };
}
