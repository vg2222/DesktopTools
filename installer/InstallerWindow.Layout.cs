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

    private Border[]? stepMarkers;
    private Border[]? stepConnectors;

    private void BuildLayout()
    {
        if (!shellConfigured)
        {
            Width = 880; Height = 640;
            MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 16);
            MaxWidth = Math.Max(600, SystemParameters.WorkArea.Width - 16);
            MinWidth = Math.Min(740, MaxWidth); MinHeight = Math.Min(480, MaxHeight);
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            shellConfigured = true;
        }
        bool contrast = SystemParameters.HighContrast;
        var surface = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), BorderBrush = Brush("#2EFFFFFF"), Background = contrast ? Brushes.Black : BackgroundBrush };
        surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), 20, 20);
        Content = surface;
        var shell = new Grid(); surface.Child = shell;
        var columns = new Grid(); shell.Children.Add(columns);
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        // Rail: the product, the setup version and, for install-type tasks, where the task stands.
        var railSurface = new Grid();
        if (!contrast)
        {
            railSurface.Children.Add(new Border { Background = RailBrush });
            railSurface.Children.Add(new Border
            {
                Background = new RadialGradientBrush(Color("#596A45E0"), Color("#006A45E0")) { Center = new Point(.3, .18), GradientOrigin = new Point(.3, .18), RadiusX = .85, RadiusY = .42 },
                IsHitTestVisible = false
            });
        }
        var rail = new Border { Child = railSurface, Background = contrast ? Brushes.Black : Brushes.Transparent, BorderBrush = Brush("#1FFFFFFF"), BorderThickness = new Thickness(0, 0, 1, 0) };
        animatedRail = rail;
        columns.Children.Add(rail);
        var brand = new Grid { Margin = new Thickness(26, 34, 24, 24) }; railSurface.Children.Add(brand);
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        brand.RowDefinitions.Add(new RowDefinition());
        brand.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var identity = new StackPanel();
        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/DesktopTools.Installer;component/Assets/Icons/AppIcon.png")), Width = 76, Height = 76, HorizontalAlignment = HorizontalAlignment.Left };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        if (!contrast) logo.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color("#6A45E0"), BlurRadius = 34, ShadowDepth = 0, Opacity = .55 };
        identity.Children.Add(logo);
        identity.Children.Add(Text("DesktopTools", 22, FontWeights.SemiBold, TextBrush, new Thickness(0, 20, 0, 0)));
        var setupPill = new Border
        {
            Child = Text(L.T("Setup ") + displayedSetupVersion, 11, FontWeights.SemiBold, Brush("#CFC4FF")),
            Background = Brush("#2B6A45E0"), BorderBrush = Brush("#476A45E0"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left
        };
        identity.Children.Add(setupPill);
        brand.Children.Add(identity);

        var stage = new StackPanel { Margin = new Thickness(0, 40, 0, 0) };
        string[] steps = uninstall ? ["Remove DesktopTools", "Removal complete"] : setupMode switch
        {
            Program.SetupMode.Install => ["Install location", "Install DesktopTools", "You're all set"],
            Program.SetupMode.Update => ["Update DesktopTools", "You're all set"],
            _ => []
        };
        stepMarkers = new Border[steps.Length];
        stepConnectors = new Border[Math.Max(0, steps.Length - 1)];
        for (int index = 0; index < steps.Length; index++)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var marker = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Left };
            stepMarkers[index] = marker; row.Children.Add(marker);
            var label = Text(steps[index], 13, FontWeights.SemiBold, TextBrush); label.Tag = "installer-step-label";
            Grid.SetColumn(label, 1); row.Children.Add(label);
            if (index < steps.Length - 1)
            {
                var connector = new Border { Width = 1.5, Height = 22, Margin = new Thickness(10, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Left };
                stepConnectors[index] = connector;
                Grid.SetRow(connector, 1); row.Children.Add(connector);
            }
            stage.Children.Add(row);
        }
        var stageSummary = new Border { Child = stage, Tag = "installer-stage-summary" };
        Grid.SetRow(stageSummary, 1); brand.Children.Add(stageSummary);

        var railActions = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 18) };
        Grid.SetRow(railActions, 2); brand.Children.Add(railActions);
        var platform = Text("Windows 11 · x64", 11, FontWeights.Normal, FaintBrush);
        Grid.SetRow(platform, 3); brand.Children.Add(platform);

        // Main column: language and close, the task, a fixed status line, then the decision buttons.
        var main = new Grid { Margin = new Thickness(38, 18, 26, 24) };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        main.RowDefinitions.Add(new RowDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(main, 1); columns.Children.Add(main);
        var caption = new Grid(); animatedCaption = caption;
        var captionActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        languageChoice = CreateLanguageChoice();
        captionActions.Children.Add(languageChoice);
        var close = Button("", false, Close, 34); close.Content = FluentIcon("dismiss", MutedBrush, 15);
        close.Height = 34; close.HorizontalAlignment = HorizontalAlignment.Right; close.HorizontalContentAlignment = HorizontalAlignment.Center;
        close.Background = Brushes.Transparent;
        AutomationProperties.SetName(close, L.T("Close")); close.ToolTip = L.T("Close");
        captionActions.Children.Add(close); caption.Children.Add(captionActions); main.Children.Add(caption);

        var content = new StackPanel { Margin = new Thickness(0, 14, 12, 4) };
        headline = Text(uninstall ? "Remove DesktopTools" : setupMode switch
        {
            Program.SetupMode.Update => "Update DesktopTools",
            Program.SetupMode.Maintenance => "Manage your installation",
            Program.SetupMode.OlderSetup => "Your app is newer than this setup",
            _ => "Install DesktopTools"
        }, 32, FontWeights.SemiBold, TextBrush);
        headline.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        content.Children.Add(headline);
        string description = uninstall ? "Choose what to keep before removing the app." : setupMode switch
        {
            Program.SetupMode.Update => L.F($"Update {displayedInstalledVersion} to {displayedSetupVersion}. Your saved notes and settings stay with you."),
            Program.SetupMode.Maintenance => L.F($"Version {displayedInstalledVersion} is installed. Choose what you'd like to do."),
            Program.SetupMode.OlderSetup => L.F($"Installed: {displayedInstalledVersion} · This setup: {displayedSetupVersion}. Use a newer setup to update or repair."),
            _ => "Install for your Windows account. Choose a location and we'll take care of the rest."
        };
        var lead = Text(description, 14, FontWeights.Normal, MutedBrush, new Thickness(0, 10, 0, 28)); lead.MaxWidth = 520; lead.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(lead);

        if (uninstall || setupMode is Program.SetupMode.Install or Program.SetupMode.Update)
        {
            var location = new Grid { Margin = new Thickness(18, 16, 16, 16) };
            location.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            location.ColumnDefinitions.Add(new ColumnDefinition()); location.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            location.Children.Add(IconTile("folder", 40));
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(Text(uninstall ? "Application location" : "Install location", 13, FontWeights.SemiBold, TextBrush));
            var path = Text(selectedInstallDir, 12, FontWeights.Normal, MutedBrush, new Thickness(0, 4, 0, 0));
            path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis; path.ToolTip = selectedInstallDir;
            copy.Children.Add(path); Grid.SetColumn(copy, 1); location.Children.Add(copy);
            if (!uninstall && setupMode == Program.SetupMode.Install)
            {
                browse = Button("Change…", false, () => ChooseFolder(path), 96);
                browse.Height = 34; browse.Margin = new Thickness(12, 0, 0, 0); browse.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(browse, 2); location.Children.Add(browse);
            }
            content.Children.Add(Card(location, new Thickness(0, 0, 0, 12)));
        }
        if (uninstall)
        {
            keepData = new RadioButton { Content = L.T("Keep notes and settings"), IsChecked = true, Foreground = TextBrush, FontSize = 14, Margin = new Thickness(0, 4, 0, 6) };
            deleteData = new RadioButton { Content = L.T("Delete all DesktopTools data"), Foreground = TextBrush, FontSize = 14, Margin = new Thickness(0, 14, 0, 6) };
            var choices = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
            choices.Children.Add(keepData);
            choices.Children.Add(Text("Recommended if you might reinstall later.", 12, FontWeights.Normal, MutedBrush, new Thickness(22, 0, 0, 0)));
            choices.Children.Add(deleteData);
            choices.Children.Add(Text("Removes app-managed notes, settings and cache. Screenshots and videos saved elsewhere stay untouched.", 12, FontWeights.Normal, MutedBrush, new Thickness(22, 0, 0, 0)));
            content.Children.Add(Card(choices, new Thickness(0, 0, 0, 12)));
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
                advancedOptions = new Border { Child = choices, Visibility = setupMode == Program.SetupMode.Update ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(0, 0, 0, 6) };
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
                    downgradeOptions = new Border { Child = downgrade, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 6) };
                    content.Children.Add(downgradeOptions);
                }
            }
            else { githubUpdate.Visibility = Visibility.Collapsed; content.Children.Add(githubUpdate); }
            runtimeCard = CreateRuntimeCard(); content.Children.Add(runtimeCard);
            RefreshRuntime();
        }

        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ScrollBar">
              <Setter Property="Width" Value="8"/><Setter Property="Background" Value="Transparent"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent" Margin="2,2">
                  <Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Orientation="Vertical" IsDirectionReversed="True">
                    <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                    <Track.Thumb><Thumb Background="#2E2A3D" MinHeight="26"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="3"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                    <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
                  </Track>
                </Grid>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """));
        animatedContent = scroll;
        Grid.SetRow(scroll, 1); main.Children.Add(scroll);

        // Status line: always visible, with the progress bar under it while setup works.
        var state = new Grid { Margin = new Thickness(0, 14, 12, 0) };
        state.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        state.ColumnDefinitions.Add(new ColumnDefinition());
        state.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = AccentSoftBrush, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 6, 0, 0) });
        var stateText = new StackPanel(); Grid.SetColumn(stateText, 1); state.Children.Add(stateText);
        status = Text(uninstall ? "Ready to remove" : "Checking GitHub for updates…", 12, FontWeights.SemiBold, TextBrush);
        statusHint = Text(uninstall ? "You'll confirm before any app data is deleted." : "You can continue while this check runs.", 12, FontWeights.Normal, MutedBrush, new Thickness(0, 3, 0, 0));
        stateText.Children.Add(status); stateText.Children.Add(statusHint);
        progressTrack = new Border { Height = 6, Background = Brush("#221F2E"), CornerRadius = new CornerRadius(3), Visibility = Visibility.Collapsed, ClipToBounds = true };
        progressFill = new Border { Width = 0, Background = AccentGradientBrush, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };
        progressTrack.Margin = new Thickness(0, 12, 0, 0);
        progressTrack.Child = new Grid { Children = { progressFill } }; progressTrack.SizeChanged += (_, _) => UpdateProgress(); stateText.Children.Add(progressTrack);
        Grid.SetRow(state, 2); main.Children.Add(state);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) }; animatedFooter = footer;
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        launch = new CheckBox { Content = L.T("Open DesktopTools after setup"), IsChecked = true, Foreground = MutedBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Visibility = !uninstall && setupMode is Program.SetupMode.Install or Program.SetupMode.Update ? Visibility.Visible : Visibility.Collapsed };
        if (setupMode == Program.SetupMode.Update && !uninstall)
        {
            footer.Children.Add(launch);
            moreOptions = CreateMoreOptionsButton(); railActions.Children.Add(moreOptions);
        }
        else if (setupMode == Program.SetupMode.OlderSetup && !uninstall)
        {
            moreOptions = CreateAdvancedOptionsLink();
            railActions.Children.Add(moreOptions);
        }
        else footer.Children.Add(launch);
        cancel = Button(!uninstall && setupMode is Program.SetupMode.Maintenance or Program.SetupMode.OlderSetup ? "Close" : "Cancel", false, Close, 96);
        cancel.Margin = new Thickness(8, 0, 10, 0); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        primary = Button(uninstall ? "Uninstall" : setupMode == Program.SetupMode.Update ? "Update DesktopTools" : "Install DesktopTools", true, async () => await ExecuteAsync(forceLocal: true), 176);
        primary.Visibility = !uninstall && setupMode is Program.SetupMode.Maintenance or Program.SetupMode.OlderSetup ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(primary, 2); footer.Children.Add(primary); Grid.SetRow(footer, 3); main.Children.Add(footer);
        var resizeGrip = CreateResizeGrip(); shell.Children.Add(resizeGrip);
        UpdateSteps();
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(AnimateLayout), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateSteps()
    {
        if (stepMarkers is null || stepMarkers.Length == 0) return;
        int current = finished ? stepMarkers.Length : busy && stepMarkers.Length == 3 ? 1 : 0;
        for (int index = 0; index < stepMarkers.Length; index++)
        {
            var marker = stepMarkers[index];
            bool done = index < current, active = index == current;
            marker.Background = done ? AccentGradientBrush : active ? Brush("#2B6A45E0") : Brushes.Transparent;
            marker.BorderBrush = done ? Brushes.Transparent : active ? AccentSoftBrush : Brush("#3A3650");
            marker.Child = done ? Center(FluentIcon("checkmark", Brushes.White, 12))
                : active ? new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = AccentSoftBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } : null;
            if (marker.Parent is Grid row)
                foreach (var label in row.Children.OfType<TextBlock>()) label.Foreground = done || active ? TextBrush : FaintBrush;
            if (index < stepConnectors!.Length) stepConnectors[index].Background = done ? AccentBrush : Brush("#2A2638");
        }
        static FrameworkElement Center(FrameworkElement element) { element.HorizontalAlignment = HorizontalAlignment.Center; return element; }
    }

    private static Border Card(UIElement child, Thickness margin) => new()
    {
        Child = child, Background = CardBrush, BorderBrush = StrokeBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Margin = margin
    };

    private static Border IconTile(string icon, double size) => new()
    {
        Child = FluentIcon(icon, Brush("#E2DAFF"), size * .5), Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        Background = new LinearGradientBrush(Color("#4D7C5AF2"), Color("#265B36D6"), 45), BorderBrush = Brush("#386A45E0"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(size * .3), Padding = new Thickness(size * .25)
    };

    private Button CreateLanguageChoice()
    {
        var button = Button("", false, () =>
        {
            if (!busy && !finished && languageMenu is not null) languageMenu.IsOpen = !languageMenu.IsOpen;
        }, 168);
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
                Background = language == L.Language ? Brush("#34295A") : Brushes.Transparent,
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
            option.MouseEnter += (_, _) => option.Background = Brush("#2A2638");
            option.MouseLeave += (_, _) => option.Background = language == L.Language ? Brush("#34295A") : Brushes.Transparent;
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
            Child = new ScrollViewer { Content = options, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Width = 220, Background = CardBrush, BorderBrush = StrokeBrush,
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
        string country = language switch { "ru" => "ru", "de" => "de", "fr" => "fr", "es" => "es", "uk" => "ua", "zh" => "cn", "pt" => "pt", "ja" => "jp", "pl" => "pl", "tr" => "tr", "it" => "it", _ => "gb" };
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
            CornerRadius = new CornerRadius(2.5), BorderBrush = Brush("#5E5A70"), BorderThickness = new Thickness(.5),
            VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true
        };
    }

    private Border? runtimeIcon;
    private FrameworkElement? runtimeInfoIcon;
    private FrameworkElement? runtimeReadyIcon;

    private Border CreateRuntimeCard()
    {
        var grid = new Grid { Margin = new Thickness(18, 15, 16, 15) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        runtimeInfoIcon = FluentIcon("info", Brush("#E2DAFF"), 16); runtimeReadyIcon = FluentIcon("checkmark", Brush("#86EFAC"), 16);
        runtimeIcon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Grid { Children = { runtimeInfoIcon, runtimeReadyIcon } } };
        grid.Children.Add(runtimeIcon);
        var stack = new StackPanel(); Grid.SetColumn(stack, 1); grid.Children.Add(stack);
        runtimeTitle = Text("One extra step for screen recording", 13, FontWeights.SemiBold, TextBrush);
        runtimeDescription = Text("", 12, FontWeights.Normal, MutedBrush, new Thickness(0, 4, 0, 0));
        stack.Children.Add(runtimeTitle); stack.Children.Add(runtimeDescription);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        runtimeDownload = Button("Open Microsoft download", false, () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(RecordingRuntime.DownloadPage) { UseShellExecute = true });
                runtimeDescription.Text = L.T("Microsoft's page is open. Choose the x64 download, run it, then return here and select Check again. Microsoft's installer presents its own license terms.");
            }
            catch (Exception ex) { runtimeDescription.Text = L.T("Couldn't open the browser. Visit ") + RecordingRuntime.DownloadPage + ". " + ex.Message; }
        }, double.NaN);
        runtimeRecheck = Button("Check again", false, RefreshRuntime, double.NaN); runtimeRecheck.Margin = new Thickness(8, 0, 0, 0);
        runtimeDownload.Height = runtimeRecheck.Height = 32;
        runtimeDownload.Padding = runtimeRecheck.Padding = new Thickness(14, 0, 14, 0);
        runtimeDownload.MinWidth = 150; runtimeRecheck.MinWidth = 110;
        actions.Children.Add(runtimeDownload); actions.Children.Add(runtimeRecheck); stack.Children.Add(actions);
        return Card(grid, new Thickness(0, 6, 0, 4));
    }

    private void RefreshRuntime()
    {
        if (runtimeCard is null || runtimeTitle is null || runtimeDescription is null) return;
        bool ready = runtimeCheck();
        runtimeTitle.Text = L.T(ready ? "Screen recording runtime is installed" : "One extra step for screen recording");
        runtimeDescription.Text = L.T(ready ? "Microsoft Visual C++ x64 is ready on this PC." : "Install Microsoft Visual C++ x64 for screen recording, offline translation and background removal. Setup can continue; these actions stay unavailable until it is installed.");
        runtimeCard.Background = ready ? Brush("#0DFFFFFF") : CardBrush;
        runtimeCard.BorderBrush = ready ? Brush("#14FFFFFF") : Brush("#4D6A45E0");
        if (runtimeIcon is not null)
        {
            runtimeInfoIcon!.Opacity = ready ? 0 : 1; runtimeReadyIcon!.Opacity = ready ? 1 : 0;
            runtimeIcon.Background = Brush(ready ? "#1F22C55E" : "#336A45E0");
        }
        runtimeDownload!.Visibility = runtimeRecheck!.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        if (ready && finished) statusHint.Text = L.T("DesktopTools is ready to use. Restart the app if it was already open.");
    }

    private Thumb CreateResizeGrip()
    {
        var grip = new Thumb
        {
            Tag = "installer-resize", Width = 20, Height = 20,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 3), Cursor = Cursors.SizeNWSE,
            Focusable = true, ToolTip = L.T("Resize")
        };
        AutomationProperties.SetName(grip, L.T("Resize"));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var lines = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        lines.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 4,15 L 15,4 M 10,15 L 15,10"));
        lines.SetValue(System.Windows.Shapes.Path.StrokeProperty, MutedBrush);
        lines.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.4);
        border.AppendChild(lines);
        grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = border };
        grip.DragDelta += (_, e) => ResizeBy(e.HorizontalChange, e.VerticalChange);
        grip.KeyDown += (_, e) =>
        {
            (double dx, double dy) = e.Key switch
            {
                Key.Right => (24, 0), Key.Left => (-24, 0),
                Key.Down => (0, 24), Key.Up => (0, -24), _ => (0, 0)
            };
            if (dx == 0 && dy == 0) return;
            ResizeBy(dx, dy); e.Handled = true;
        };
        Panel.SetZIndex(grip, 2);
        return grip;
    }

    private void ResizeBy(double dx, double dy)
    {
        Width = Math.Clamp(Width + dx, MinWidth, MaxWidth);
        Height = Math.Clamp(Height + dy, MinHeight, MaxHeight);
    }

    private static Button ActionRow(string icon, string title, string description, Action action, out TextBlock descriptionBlock)
    {
        var row = new Grid { Margin = new Thickness(16, 14, 14, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.Children.Add(IconTile(icon, 36));
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; copy.Children.Add(Text(title, 14, FontWeights.SemiBold, TextBrush));
        descriptionBlock = Text(description, 12, FontWeights.Normal, MutedBrush, new Thickness(0, 3, 0, 0)); copy.Children.Add(descriptionBlock);
        Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var arrow = FluentIcon("chevron_right", FaintBrush, 14); arrow.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
        var button = Button(title, false, action, double.NaN); button.Height = double.NaN; button.MinHeight = 72;
        button.Margin = new Thickness(0, 0, 0, 8); button.Content = row;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.BorderBrush = StrokeBrush;
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
