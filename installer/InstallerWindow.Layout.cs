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
        if (!shellConfigured)
        {
            Width = 880; Height = 700;
            MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 16);
            MaxWidth = Math.Max(600, SystemParameters.WorkArea.Width - 16);
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            shellConfigured = true;
        }
        var surface = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), BorderBrush = Brush("#7593A7C5"), Background = SystemParameters.HighContrast ? Brushes.Black : BackgroundBrush };
        surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), 18, 18);
        Content = surface;
        var columns = new Grid(); surface.Child = columns;
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var rail = new Border { Background = SystemParameters.HighContrast ? Brushes.Black : RailBrush, BorderBrush = Brush("#557E91A9"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(19, 23, 19, 25) };
        animatedRail = rail;
        var brand = new Grid(); rail.Child = brand; columns.Children.Add(rail);
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        brand.RowDefinitions.Add(new RowDefinition());
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var identity = new StackPanel();
        var brandLine = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/DesktopTools.Installer;component/Assets/Icons/AppIcon.png")), Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Left };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        brandLine.Children.Add(logo);
        brandLine.Children.Add(Text("DesktopTools", 17, FontWeights.SemiBold, TextBrush, new Thickness(11, 0, 0, 0)));
        identity.Children.Add(brandLine);
        identity.Children.Add(Text("SETUP", 10, FontWeights.SemiBold, Brush("#90ADD9"), new Thickness(5, 32, 0, 11)));
        identity.Children.Add(RailPage(uninstall ? "delete" : setupMode switch
        {
            Program.SetupMode.Update => "arrow_clockwise",
            Program.SetupMode.Maintenance => "desktop_toolbox",
            Program.SetupMode.OlderSetup => "info",
            _ => "desktop"
        }, uninstall ? "Uninstall" : setupMode switch
        {
            Program.SetupMode.Update => "Update DesktopTools",
            Program.SetupMode.Maintenance => "Manage DesktopTools",
            Program.SetupMode.OlderSetup => "Manage DesktopTools",
            _ => "Install DesktopTools"
        }));
        brand.Children.Add(identity);
        var features = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 0, 12) };
        features.Children.Add(Text("DESKTOPTOOLS", 10, FontWeights.SemiBold, Brush("#90ADD9"), new Thickness(0, 0, 0, 16)));
        features.Children.Add(RailFeature("screenshot", "Capture", "Screenshots & recording"));
        features.Children.Add(RailFeature("pen", "Create", "Images, text & annotations"));
        features.Children.Add(RailFeature("presenter", "Present", "Tools for your audience"));
        Grid.SetRow(features, 1); brand.Children.Add(features);
        var version = new StackPanel();
        version.Children.Add(new Border { Height = 1, Background = Brush("#4F8190A5"), Margin = new Thickness(0, 0, 0, 15) });
        version.Children.Add(Text("Windows 11 · x64", 11, FontWeights.Medium, MutedBrush));
        version.Children.Add(Text(L.T("Setup ") + displayedSetupVersion, 11, FontWeights.Normal, MutedBrush, new Thickness(0, 5, 0, 0)));
        Grid.SetRow(version, 2); brand.Children.Add(version);

        var main = new Grid { Margin = new Thickness(30, 18, 28, 22) };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        main.RowDefinitions.Add(new RowDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(main, 1); columns.Children.Add(main);
        var caption = new Grid(); animatedCaption = caption;
        caption.Children.Add(Text(uninstall ? "Uninstall" : setupMode == Program.SetupMode.Install ? "Welcome to setup" : "Manage DesktopTools", 12, FontWeights.SemiBold, MutedBrush));
        var captionActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        languageChoice = CreateLanguageChoice();
        captionActions.Children.Add(languageChoice);
        var close = Button("", false, Close, 34); close.Content = FluentIcon("dismiss", TextBrush, 16);
        close.Height = 34; close.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetName(close, L.T("Close")); close.ToolTip = L.T("Close");
        captionActions.Children.Add(close); caption.Children.Add(captionActions); main.Children.Add(caption);

        var content = new StackPanel { Margin = new Thickness(0, 16, 8, 2) };
        headline = Text(uninstall ? "Remove DesktopTools" : setupMode switch
        {
            Program.SetupMode.Update => "Update DesktopTools",
            Program.SetupMode.Maintenance => "Manage your installation",
            Program.SetupMode.OlderSetup => "Your app is newer than this setup",
            _ => "Install DesktopTools"
        }, 30, FontWeights.SemiBold, TextBrush);
        content.Children.Add(headline);
        string description = uninstall ? "Choose what to keep before removing the app." : setupMode switch
        {
            Program.SetupMode.Update => L.F($"Update {displayedInstalledVersion} to {displayedSetupVersion}. Your saved notes and settings stay with you."),
            Program.SetupMode.Maintenance => L.F($"Version {displayedInstalledVersion} is installed. Choose what you'd like to do."),
            Program.SetupMode.OlderSetup => L.F($"Installed: {displayedInstalledVersion} · This setup: {displayedSetupVersion}. Use a newer setup to update or repair."),
            _ => "Install for your Windows account. Choose a location and we'll take care of the rest."
        };
        content.Children.Add(Text(description, 13, FontWeights.Normal, MutedBrush, new Thickness(0, 9, 0, 23)));

        if (uninstall || setupMode is Program.SetupMode.Install or Program.SetupMode.Update)
        {
            var location = new Grid { Margin = new Thickness(16) };
            location.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            location.ColumnDefinitions.Add(new ColumnDefinition()); location.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            location.Children.Add(new Border { Child = FluentIcon("folder", Brush("#C8DCFF"), 18), Width = 30, Height = 30,
                Background = Brush("#665477AC"), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
            var copy = new StackPanel();
            copy.Children.Add(Text(uninstall ? "Application location" : "Install location", 12, FontWeights.SemiBold, TextBrush));
            var path = Text(selectedInstallDir, 12, FontWeights.Normal, MutedBrush, new Thickness(0, 6, 0, 0));
            path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis; path.ToolTip = selectedInstallDir;
            copy.Children.Add(path); Grid.SetColumn(copy, 1); location.Children.Add(copy);
            if (!uninstall && setupMode == Program.SetupMode.Install)
            {
                browse = Button("Change…", false, () => ChooseFolder(path), 88);
                browse.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(browse, 2); location.Children.Add(browse);
            }
            content.Children.Add(new Border { Child = location, Background = CardBrush, BorderBrush = StrokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 16) });
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
                if (setupMode == Program.SetupMode.OlderSetup)
                {
                    downgrade = ActionRow("info", "Install older version", "Install this setup version. Keep notes and settings; newer settings may not work in the older app.", async () =>
                    {
                        if (MessageBox.Show(this,
                            L.F($"Install version {displayedSetupVersion} over version {displayedInstalledVersion}? Your notes and settings will be kept, but some newer settings may not work in the older app."),
                            L.T("Install older version"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                            await ExecuteAsync(forceLocal: true, allowDowngrade: true);
                    }, out _);
                    downgradeOptions = new Border { Child = downgrade, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 10) };
                    content.Children.Add(downgradeOptions);
                }
            }
            else { githubUpdate.Visibility = Visibility.Collapsed; content.Children.Add(githubUpdate); }
            runtimeCard = CreateRuntimeCard(); content.Children.Add(runtimeCard);
            RefreshRuntime();
        }

        var state = new StackPanel();
        status = Text(uninstall ? "Ready to remove" : "Checking GitHub for updates…", 12, FontWeights.SemiBold, TextBrush);
        statusHint = Text(uninstall ? "You'll confirm before any app data is deleted." : "You can continue while this check runs.", 11, FontWeights.Normal, MutedBrush, new Thickness(0, 5, 0, 0));
        state.Children.Add(status); state.Children.Add(statusHint);
        progressTrack = new Border { Height = 6, Background = StrokeBrush, CornerRadius = new CornerRadius(3), Visibility = Visibility.Collapsed };
        progressFill = new Border { Width = 0, Background = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };
        progressTrack.Margin = new Thickness(0, 13, 0, 0);
        progressTrack.Child = new Grid { Children = { progressFill } }; progressTrack.SizeChanged += (_, _) => UpdateProgress(); state.Children.Add(progressTrack);
        content.Children.Add(new Border { Child = state, Background = Brush("#792F3C4F"), BorderBrush = Brush("#597C93AE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 1, 0, 0) });
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
        animatedContent = scroll;
        Grid.SetRow(scroll, 1); main.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(0, 15, 0, 0) }; animatedFooter = footer;
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        launch = new CheckBox { Content = L.T("Open DesktopTools after setup"), IsChecked = true, Foreground = MutedBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Visibility = !uninstall && setupMode is Program.SetupMode.Install or Program.SetupMode.Update ? Visibility.Visible : Visibility.Collapsed };
        if (setupMode == Program.SetupMode.Update && !uninstall)
        {
            launch.Margin = new Thickness(0, 14, 0, 0); content.Children.Add(launch);
            moreOptions = CreateMoreOptionsButton(); moreOptions.Background = Brushes.Transparent;
            moreOptions.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(moreOptions);
        }
        else if (setupMode == Program.SetupMode.OlderSetup && !uninstall)
        {
            moreOptions = CreateAdvancedOptionsLink();
            footer.Children.Add(moreOptions);
        }
        else footer.Children.Add(launch);
        cancel = Button(!uninstall && setupMode is Program.SetupMode.Maintenance or Program.SetupMode.OlderSetup ? "Close" : "Cancel", false, Close, 82);
        cancel.Margin = new Thickness(8, 0, 10, 0); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        primary = Button(uninstall ? "Uninstall" : setupMode == Program.SetupMode.Update ? "Update DesktopTools" : "Install DesktopTools", true, async () => await ExecuteAsync(forceLocal: true), 164);
        primary.Visibility = !uninstall && setupMode is Program.SetupMode.Maintenance or Program.SetupMode.OlderSetup ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(primary, 2); footer.Children.Add(primary); Grid.SetRow(footer, 2); main.Children.Add(footer);
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(AnimateLayout), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private Button CreateLanguageChoice()
    {
        var button = Button("", false, () =>
        {
            if (!busy && !finished && languageMenu is not null) languageMenu.IsOpen = !languageMenu.IsOpen;
        }, 176);
        button.Height = 34; button.Margin = new Thickness(0, 0, 8, 0);
        button.Tag = "installer-language";
        button.ToolTip = L.T("Interface language");
        AutomationProperties.SetName(button, L.T("Interface language") + ": " + L.LanguageNames[Array.IndexOf(L.Languages, L.Language)]);
        var label = new Grid { Margin = new Thickness(16, 0, 12, 0) };
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.ColumnDefinitions.Add(new ColumnDefinition());
        label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        label.Children.Add(LanguageFlag(L.Language));
        var name = Text(L.LanguageNames[Array.IndexOf(L.Languages, L.Language)], 12, FontWeights.SemiBold, TextBrush, new Thickness(11, 0, 0, 0));
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
                Content = LanguageOptionContent(language, nativeName, language == L.Language),
                Tag = "installer-language-" + language,
                Height = 38,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 0, 12, 0),
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
                bool? launchAfterSetup = launch.IsChecked;
                bool deleteManagedData = deleteData?.IsChecked == true;
                bool advancedVisible = advancedOptions?.Visibility == Visibility.Visible;
                bool downgradeVisible = downgradeOptions?.Visibility == Visibility.Visible;
                L.Use(language);
                Title = L.T(uninstall ? "Remove DesktopTools" : "DesktopTools Setup");
                BuildLayout();
                launch.IsChecked = launchAfterSetup;
                if (deleteManagedData && deleteData is not null) deleteData.IsChecked = true;
                if (advancedVisible && advancedOptions is not null) advancedOptions.Visibility = Visibility.Visible;
                if (downgradeVisible && downgradeOptions is not null) downgradeOptions.Visibility = Visibility.Visible;
                if (!uninstall && !checkingLatest) _ = CheckLatestAsync();
            };
            options.Children.Add(option);
        }
        var menu = new Border
        {
            Child = options, Width = 220, Background = CardBrush, BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11)
        };
        languageMenu = new Popup
        {
            PlacementTarget = button, Placement = PlacementMode.Bottom, HorizontalOffset = -36,
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

    private static Grid LanguageOptionContent(string language, string nativeName, bool selected)
    {
        var row = new Grid { Margin = new Thickness(8, 0, 4, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(LanguageFlag(language));
        var name = Text(nativeName, 12, FontWeights.Medium, TextBrush, new Thickness(12, 0, 0, 0));
        Grid.SetColumn(name, 1); row.Children.Add(name);
        if (selected)
        {
            var check = FluentIcon("checkmark", AccentHoverBrush, 16);
            Grid.SetColumn(check, 1); row.Children.Add(check);
        }
        return row;
    }

    private static Border LanguageFlag(string language)
    {
        string country = language switch { "ru" => "ru", "de" => "de", "fr" => "fr", "es" => "es", _ => "gb" };
        const double width = 22;
        const double height = 16.5;
        var flag = new Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,/DesktopTools.Installer;component/Assets/Flags/{country}.png")),
            Width = width, Height = height, Stretch = Stretch.Fill,
            Clip = new RectangleGeometry(new Rect(0, 0, width, height), 2.5, 2.5)
        };
        RenderOptions.SetBitmapScalingMode(flag, BitmapScalingMode.HighQuality);
        return new Border
        {
            Tag = "installer-language-flag", Child = flag, Width = width, Height = height,
            CornerRadius = new CornerRadius(2.5), BorderBrush = Brush("#59616B"), BorderThickness = new Thickness(.5),
            VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true
        };
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
        return new Border { Child = stack, Padding = new Thickness(16, 14, 16, 14), Background = Brush("#D6253448"), BorderBrush = Brush("#806A8AB8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 10) };
    }

    private void RefreshRuntime()
    {
        if (runtimeCard is null || runtimeTitle is null || runtimeDescription is null) return;
        bool ready = runtimeCheck();
        runtimeTitle.Text = L.T(ready ? "Screen recording runtime is installed" : "One extra step for screen recording");
        runtimeDescription.Text = L.T(ready ? "Microsoft Visual C++ x64 is ready on this PC." : "Install Microsoft Visual C++ x64 for screen recording, offline translation and background removal. Setup can continue; these actions stay unavailable until it is installed.");
        runtimeCard.Background = ready ? CardBrush : Brush("#D6253448");
        runtimeCard.BorderBrush = ready ? StrokeBrush : Brush("#3E5778");
        runtimeDownload!.Visibility = runtimeRecheck!.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        if (ready && finished) statusHint.Text = L.T("DesktopTools is ready to use. Restart the app if it was already open.");
    }

    private static FrameworkElement RailFeature(string icon, string title, string detail)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 21) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(FluentIcon(icon, Brush("#A5BDE2"), 17));
        row.Children.Add(Text(title, 12, FontWeights.SemiBold, TextBrush, new Thickness(10, 0, 0, 0)));
        panel.Children.Add(row); panel.Children.Add(Text(detail, 11, FontWeights.Normal, MutedBrush, new Thickness(27, 5, 0, 0)));
        return panel;
    }

    private static FrameworkElement RailPage(string icon, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(FluentIcon(icon, Brush("#D6E6FF"), 18));
        row.Children.Add(Text(title, 12, FontWeights.SemiBold, TextBrush, new Thickness(11, 0, 0, 0)));
        return new Border
        {
            Child = row, Background = Brush("#B62C4777"), BorderBrush = Brush("#9975A2EF"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 12, 10, 12)
        };
    }

    private static Button ActionRow(string icon, string title, string description, Action action, out TextBlock descriptionBlock)
    {
        var row = new Grid { Margin = new Thickness(15, 12, 15, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.Children.Add(new Border { Child = FluentIcon(icon, Brush("#D1E1FF"), 19), Width = 31, Height = 31,
            Background = Brush("#665477AC"), CornerRadius = new CornerRadius(8), Padding = new Thickness(6) });
        var copy = new StackPanel(); copy.Children.Add(Text(title, 14, FontWeights.SemiBold, TextBrush));
        descriptionBlock = Text(description, 12, FontWeights.Normal, MutedBrush, new Thickness(0, 4, 0, 0)); copy.Children.Add(descriptionBlock);
        Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var arrow = FluentIcon("arrow_up_right", MutedBrush, 16); Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
        var button = Button(title, false, action, double.NaN); button.Height = double.NaN; button.MinHeight = 70;
        button.Margin = new Thickness(0, 0, 0, 7); button.Content = row;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, L.T(title)); AutomationProperties.SetHelpText(button, L.T(description));
        button.RenderTransform = new TranslateTransform();
        button.MouseEnter += (_, _) => AnimateActionRow(button, -2);
        button.MouseLeave += (_, _) => AnimateActionRow(button, 0);
        return button;
    }

    private static void AnimateActionRow(Button button, double target)
    {
        if (button.RenderTransform is not TranslateTransform transform) return;
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) { transform.Y = target; return; }
        transform.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(target, TimeSpan.FromMilliseconds(130))
        { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    private static FrameworkElement FluentIcon(string name, Brush fill, double size)
    {
        // Existing Microsoft Fluent System Icons assets, covered by THIRD-PARTY-NOTICES.
        using var stream = typeof(InstallerWindow).Assembly.GetManifestResourceStream($"DesktopTools.Installer.Icons.{name}_24_regular.svg")
            ?? throw new InvalidOperationException($"Setup icon is missing: {name}");
        var svg = XDocument.Load(stream);
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var path in svg.Descendants().Where(node => node.Name.LocalName == "path"))
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(path.Attribute("d")!.Value), Fill = fill });
        return new Viewbox { Child = canvas, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    }
}
