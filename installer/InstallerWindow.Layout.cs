using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DesktopTools.Localization;

namespace DesktopTools.Installer;

internal sealed partial class InstallerWindow
{
    private Border? runtimeCard;
    private TextBlock? runtimeTitle;
    private TextBlock? runtimeDescription;
    private Button? runtimeDownload;
    private Button? runtimeRecheck;
    private TextBlock? updateDescription;
    private bool checkingLatest;
    private Func<bool> runtimeCheck = RecordingRuntime.IsAvailable;

    private void BuildLayout()
    {
        Width = 860; Height = 680;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 16);
        MaxWidth = Math.Max(600, SystemParameters.WorkArea.Width - 16);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        var surface = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), BorderBrush = StrokeBrush, Background = BackgroundBrush };
        surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), 16, 16);
        Content = surface;
        var columns = new Grid(); surface.Child = columns;
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(212) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var rail = new Border { Background = RailBrush, Padding = new Thickness(26, 34, 24, 28) };
        var brand = new Grid(); rail.Child = brand; columns.Children.Add(rail);
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        brand.RowDefinitions.Add(new RowDefinition());
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var identity = new StackPanel();
        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/DesktopTools.Installer;component/Assets/Icons/AppIcon.png")), Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Left };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        identity.Children.Add(logo);
        identity.Children.Add(Text("DesktopTools", 20, FontWeights.SemiBold, TextBrush, new Thickness(0, 16, 0, 4)));
        identity.Children.Add(Text("Your desktop, equipped.", 12, FontWeights.Normal, MutedBrush));
        brand.Children.Add(identity);
        var features = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        features.Children.Add(RailFeature("screenshot", "Capture", "Screenshots & recording"));
        features.Children.Add(RailFeature("pen", "Create", "Images, text & annotations"));
        features.Children.Add(RailFeature("presenter", "Present", "Tools for your audience"));
        Grid.SetRow(features, 1); brand.Children.Add(features);
        var version = new StackPanel();
        version.Children.Add(Text("Windows 11 · x64", 11, FontWeights.Medium, MutedBrush));
        version.Children.Add(Text(L.T("Setup ") + displayedSetupVersion, 11, FontWeights.Normal, MutedBrush, new Thickness(0, 5, 0, 0)));
        Grid.SetRow(version, 2); brand.Children.Add(version);

        var main = new Grid { Margin = new Thickness(30, 14, 28, 22) };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        main.RowDefinitions.Add(new RowDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(main, 1); columns.Children.Add(main);
        var caption = new Grid();
        caption.Children.Add(Text(uninstall ? "Uninstall" : setupMode == Program.SetupMode.Install ? "Welcome to setup" : "Manage DesktopTools", 12, FontWeights.Medium, MutedBrush));
        var captionActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        languageChoice = CreateLanguageChoice();
        captionActions.Children.Add(languageChoice);
        var close = Button("", false, Close, 34); close.Content = FluentIcon("dismiss", TextBrush, 16);
        close.Height = 34; close.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetName(close, L.T("Close")); close.ToolTip = L.T("Close");
        captionActions.Children.Add(close); caption.Children.Add(captionActions); main.Children.Add(caption);

        var content = new StackPanel { Margin = new Thickness(0, 16, 8, 12) };
        headline = Text(uninstall ? "Remove DesktopTools" : setupMode switch
        {
            Program.SetupMode.Update => "Update DesktopTools",
            Program.SetupMode.Maintenance => "Manage your installation",
            Program.SetupMode.OlderSetup => "Your app is newer than this setup",
            _ => "Install DesktopTools"
        }, 28, FontWeights.SemiBold, TextBrush);
        content.Children.Add(headline);
        string description = uninstall ? "Choose what to keep before removing the app." : setupMode switch
        {
            Program.SetupMode.Update => L.F($"Update {displayedInstalledVersion} to {displayedSetupVersion}. Your saved notes and settings stay with you."),
            Program.SetupMode.Maintenance => L.F($"Version {displayedInstalledVersion} is installed. Choose what you'd like to do."),
            Program.SetupMode.OlderSetup => L.F($"Installed: {displayedInstalledVersion} · This setup: {displayedSetupVersion}. Use a newer setup to update or repair."),
            _ => "Install for your Windows account. Choose a location and we'll take care of the rest."
        };
        content.Children.Add(Text(description, 13, FontWeights.Normal, MutedBrush, new Thickness(0, 8, 0, 20)));

        if (uninstall || setupMode is Program.SetupMode.Install or Program.SetupMode.Update)
        {
            var location = new Grid { Margin = new Thickness(16) };
            location.ColumnDefinitions.Add(new ColumnDefinition()); location.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var copy = new StackPanel();
            copy.Children.Add(Text(uninstall ? "Application location" : "Install location", 12, FontWeights.SemiBold, TextBrush));
            var path = Text(selectedInstallDir, 12, FontWeights.Normal, MutedBrush, new Thickness(0, 6, 0, 0));
            path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis; path.ToolTip = selectedInstallDir;
            copy.Children.Add(path); location.Children.Add(copy);
            if (!uninstall && setupMode == Program.SetupMode.Install)
            {
                browse = Button("Change…", false, () => ChooseFolder(path), 88);
                browse.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(browse, 1); location.Children.Add(browse);
            }
            content.Children.Add(new Border { Child = location, Background = CardBrush, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 16) });
        }
        if (uninstall)
        {
            keepData = new RadioButton { Content = L.T("Keep notes and settings"), IsChecked = true, Foreground = TextBrush, FontSize = 14, Margin = new Thickness(0, 4, 0, 8) };
            deleteData = new RadioButton { Content = L.T("Delete all DesktopTools data"), Foreground = TextBrush, FontSize = 14, Margin = new Thickness(0, 12, 0, 8) };
            content.Children.Add(keepData);
            content.Children.Add(Text("Recommended if you might reinstall later.", 12, FontWeights.Normal, MutedBrush, new Thickness(22, 0, 0, 0)));
            content.Children.Add(deleteData);
            content.Children.Add(Text("Removes app-managed notes, settings and cache. Screenshots and videos saved elsewhere stay untouched.", 12, FontWeights.Normal, MutedBrush, new Thickness(22, 0, 0, 18)));
        }
        else
        {
            githubUpdate = ActionRow("arrow_clockwise", "Update", "Check GitHub for the latest version.", async () =>
            {
                if (setupMode == Program.SetupMode.Update && latestRelease is null) await ExecuteAsync(forceLocal: true);
                else if (latestRelease is not null) await DownloadUpdateAsync();
                else await CheckLatestAsync();
            }, out updateDescription);
            if (setupMode != Program.SetupMode.Install)
            {
                repair = ActionRow("desktop_toolbox", "Repair", "Reinstall app files. Keep your saved notes and settings.", async () => await ExecuteAsync(forceLocal: true), out var repairDescription);
                repair.IsEnabled = setupMode != Program.SetupMode.OlderSetup;
                if (setupMode == Program.SetupMode.OlderSetup) repairDescription.Text = L.T("Requires a setup matching or newer than your installed version.");
                if (setupMode == Program.SetupMode.Update) { repairDescription.Text = L.T("Reinstall using this newer setup. Your saved data is kept."); updateDescription.Text = L.F($"Install version {displayedSetupVersion}. Keep your saved data."); }
                remove = ActionRow("delete", "Uninstall", "Remove the app. Choose whether to keep your data next.", StartBundledUninstall, out _);
                var choices = new StackPanel(); choices.Children.Add(githubUpdate); choices.Children.Add(repair); choices.Children.Add(remove);
                advancedOptions = new Border { Child = choices, Visibility = setupMode == Program.SetupMode.Update ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(0, 0, 0, 10) };
                content.Children.Add(advancedOptions);
            }
            else { githubUpdate.Visibility = Visibility.Collapsed; content.Children.Add(githubUpdate); }
            runtimeCard = CreateRuntimeCard(); content.Children.Add(runtimeCard);
            RefreshRuntime();
        }

        status = Text(uninstall ? "Ready to remove" : "Checking GitHub for updates…", 12, FontWeights.SemiBold, MutedBrush, new Thickness(0, 8, 0, 0));
        statusHint = Text(uninstall ? "You'll confirm before any app data is deleted." : "You can continue while this check runs.", 11, FontWeights.Normal, MutedBrush, new Thickness(0, 5, 0, 10));
        content.Children.Add(status); content.Children.Add(statusHint);
        progressTrack = new Border { Height = 4, Background = StrokeBrush, CornerRadius = new CornerRadius(2), Visibility = Visibility.Collapsed };
        progressFill = new Border { Width = 0, Background = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
        progressTrack.Child = new Grid { Children = { progressFill } }; progressTrack.SizeChanged += (_, _) => UpdateProgress(); content.Children.Add(progressTrack);
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ScrollBar">
              <Setter Property="Width" Value="10"/><Setter Property="Background" Value="Transparent"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent" Margin="2,0">
                  <Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Orientation="Vertical" IsDirectionReversed="True">
                    <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                    <Track.Thumb><Thumb Background="#515B6B" MinHeight="26"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="3"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                    <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
                  </Track>
                </Grid>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """));
        Grid.SetRow(scroll, 1); main.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        launch = new CheckBox { Content = L.T("Open DesktopTools after setup"), IsChecked = true, Foreground = MutedBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Visibility = !uninstall && setupMode is Program.SetupMode.Install or Program.SetupMode.Update ? Visibility.Visible : Visibility.Collapsed };
        if (setupMode == Program.SetupMode.Update && !uninstall)
        {
            launch.Margin = new Thickness(0, 14, 0, 0); content.Children.Add(launch);
            moreOptions = CreateMoreOptionsButton(); moreOptions.Background = Brushes.Transparent;
            moreOptions.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(moreOptions);
        }
        else footer.Children.Add(launch);
        cancel = Button(!uninstall && setupMode is Program.SetupMode.Maintenance or Program.SetupMode.OlderSetup ? "Close" : "Cancel", false, Close, 82);
        cancel.Margin = new Thickness(8, 0, 10, 0); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        primary = Button(uninstall ? "Uninstall" : setupMode == Program.SetupMode.Update ? "Update DesktopTools" : "Install DesktopTools", true, async () => await ExecuteAsync(forceLocal: true), 164);
        primary.Visibility = !uninstall && setupMode is Program.SetupMode.Maintenance or Program.SetupMode.OlderSetup ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(primary, 2); footer.Children.Add(primary); Grid.SetRow(footer, 2); main.Children.Add(footer);
    }

    private Button CreateLanguageChoice()
    {
        var button = Button("", false, () =>
        {
            if (!busy && !finished && languageMenu is not null) languageMenu.IsOpen = !languageMenu.IsOpen;
        }, 158);
        button.Height = 34; button.Margin = new Thickness(0, 0, 8, 0);
        button.Tag = "installer-language";
        button.ToolTip = L.T("Interface language");
        AutomationProperties.SetName(button, L.T("Interface language") + ": " + L.LanguageNames[Array.IndexOf(L.Languages, L.Language)]);
        var label = new Grid { Margin = new Thickness(10, 0, 8, 0) };
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.ColumnDefinitions.Add(new ColumnDefinition());
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.Children.Add(FluentIcon("translate", MutedBrush, 17));
        var name = Text(L.LanguageNames[Array.IndexOf(L.Languages, L.Language)], 12, FontWeights.SemiBold, TextBrush, new Thickness(9, 0, 0, 0));
        Grid.SetColumn(name, 1); label.Children.Add(name);
        var chevron = FluentIcon("chevron_right", MutedBrush, 14);
        chevron.RenderTransformOrigin = new Point(.5, .5); chevron.RenderTransform = new RotateTransform(90);
        Grid.SetColumn(chevron, 2); label.Children.Add(chevron);
        button.Content = label;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var options = new StackPanel { Margin = new Thickness(5) };
        for (int index = 0; index < L.Languages.Length; index++)
        {
            string language = L.Languages[index];
            string nativeName = L.LanguageNames[index];
            var option = new Button
            {
                Content = LanguageOptionContent(nativeName, language == L.Language),
                Tag = "installer-language-" + language,
                Height = 38,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 0, 10, 0),
                Foreground = TextBrush,
                Background = language == L.Language ? Brush("#2D415F") : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.Name = "OptionSurface";
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(System.Windows.Controls.Button.Background))
                { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, AccentHoverBrush, "OptionSurface"));
            template.Triggers.Add(focus);
            option.Template = template;
            option.MouseEnter += (_, _) => option.Background = Brush("#303845");
            option.MouseLeave += (_, _) => option.Background = language == L.Language ? Brush("#2D415F") : Brushes.Transparent;
            AutomationProperties.SetName(option, nativeName);
            option.Click += (_, _) =>
            {
                languageMenu!.IsOpen = false;
                if (busy || finished || language == L.Language) return;
                L.Use(language);
                Title = L.T(uninstall ? "Remove DesktopTools" : "DesktopTools Setup");
                BuildLayout();
                if (!uninstall && !checkingLatest) _ = CheckLatestAsync();
            };
            options.Children.Add(option);
        }
        var menu = new Border
        {
            Child = options, Width = 206, Background = CardBrush, BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11)
        };
        languageMenu = new Popup
        {
            PlacementTarget = button, Placement = PlacementMode.Bottom, HorizontalOffset = -48,
            VerticalOffset = 6, AllowsTransparency = true, StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade, Child = menu
        };
        menu.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { languageMenu.IsOpen = false; e.Handled = true; } };
        button.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Down) return;
            languageMenu.IsOpen = true;
            options.Children.OfType<Button>().FirstOrDefault()?.Focus();
            e.Handled = true;
        };
        return button;
    }

    private static Grid LanguageOptionContent(string nativeName, bool selected)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Text(nativeName, 12, FontWeights.Medium, TextBrush));
        if (selected)
        {
            var check = FluentIcon("checkmark", AccentHoverBrush, 16);
            Grid.SetColumn(check, 1); row.Children.Add(check);
        }
        return row;
    }

    private Border CreateRuntimeCard()
    {
        var stack = new StackPanel();
        runtimeTitle = Text("One extra step for screen recording", 13, FontWeights.SemiBold, TextBrush);
        runtimeDescription = Text("", 12, FontWeights.Normal, MutedBrush, new Thickness(0, 6, 0, 10));
        stack.Children.Add(runtimeTitle); stack.Children.Add(runtimeDescription);
        var actions = new WrapPanel();
        runtimeDownload = Button("Open Microsoft download", false, () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(RecordingRuntime.DownloadPage) { UseShellExecute = true });
                runtimeDescription.Text = L.T("Microsoft's page is open. Choose the x64 download, run it, then return here and select Check again. Microsoft's installer presents its own license terms.");
            }
            catch (Exception ex) { runtimeDescription.Text = L.T("Couldn't open the browser. Visit ") + RecordingRuntime.DownloadPage + ". " + ex.Message; }
        }, 200);
        runtimeRecheck = Button("Check again", false, RefreshRuntime, 150); runtimeRecheck.Margin = new Thickness(8, 0, 0, 0);
        runtimeDownload.Height = runtimeRecheck.Height = 34;
        actions.Children.Add(runtimeDownload); actions.Children.Add(runtimeRecheck); stack.Children.Add(actions);
        return new Border { Child = stack, Padding = new Thickness(16, 13, 16, 13), Background = Brush("#222B3A"), BorderBrush = Brush("#3E5778"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 8) };
    }

    private void RefreshRuntime()
    {
        if (runtimeCard is null || runtimeTitle is null || runtimeDescription is null) return;
        bool ready = runtimeCheck();
        runtimeTitle.Text = L.T(ready ? "Screen recording runtime is installed" : "One extra step for screen recording");
        runtimeDescription.Text = L.T(ready ? "Microsoft Visual C++ x64 is ready on this PC." : "Install Microsoft Visual C++ x64 to record your screen. Other tools work without it. Setup can continue; recording stays unavailable until it's installed.");
        runtimeCard.Background = ready ? CardBrush : Brush("#222B3A");
        runtimeCard.BorderBrush = ready ? StrokeBrush : Brush("#3E5778");
        runtimeDownload!.Visibility = runtimeRecheck!.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        if (ready && finished) statusHint.Text = L.T("DesktopTools is ready to use. Restart the app if it was already open.");
    }

    private static FrameworkElement RailFeature(string icon, string title, string detail)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(FluentIcon(icon, MutedBrush, 18));
        row.Children.Add(Text(title, 13, FontWeights.SemiBold, TextBrush, new Thickness(10, 0, 0, 0)));
        panel.Children.Add(row); panel.Children.Add(Text(detail, 11, FontWeights.Normal, MutedBrush, new Thickness(28, 6, 0, 0)));
        return panel;
    }

    private static Button ActionRow(string icon, string title, string description, Action action, out TextBlock descriptionBlock)
    {
        var row = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.Children.Add(FluentIcon(icon, Brush("#B9CFF5"), 22));
        var copy = new StackPanel(); copy.Children.Add(Text(title, 14, FontWeights.SemiBold, TextBrush));
        descriptionBlock = Text(description, 12, FontWeights.Normal, MutedBrush, new Thickness(0, 4, 0, 0)); copy.Children.Add(descriptionBlock);
        Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var arrow = FluentIcon("arrow_up_right", MutedBrush, 16); Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
        var button = Button(title, false, action, double.NaN); button.Height = double.NaN; button.MinHeight = 70;
        button.Margin = new Thickness(0, 0, 0, 7); button.Content = row;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, L.T(title)); AutomationProperties.SetHelpText(button, L.T(description));
        return button;
    }

    private static FrameworkElement FluentIcon(string name, Brush fill, double size)
    {
        // Existing Microsoft Fluent System Icons assets, covered by THIRD-PARTY-NOTICES.
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/DesktopTools.Installer;component/Assets/Icons/{name}_24_regular.svg"))!;
        using var stream = resource.Stream;
        var svg = XDocument.Load(stream);
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var path in svg.Descendants().Where(node => node.Name.LocalName == "path"))
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(path.Attribute("d")!.Value), Fill = fill });
        return new Viewbox { Child = canvas, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    }
}
