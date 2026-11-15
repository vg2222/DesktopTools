using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>
/// The licence and consent dialog shown before a model pack downloads (spec 2.3). Download stays disabled until the user ticks the
/// checkbox for this pack, revision and licence text. The licence is shown in its original language.
/// </summary>
internal sealed class ModelConsentWindow : Window
{
    internal bool Accepted { get; private set; }

    internal ModelConsentWindow(ModelPack pack, string licenseText)
    {
        Title = L.F($"Download {L.T(pack.Name)}");
        Width = 600; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var header = UtilityWindowChrome.Header(this, Title, Close, L.T("Cancel"), 20);
        var body = new StackPanel();
        void Add(UIElement element, double bottom = 12) { if (element is FrameworkElement framed) framed.Margin = new Thickness(0, 0, 0, bottom); body.Children.Add(element); }

        Add(Ui.Text(L.T(pack.Purpose)), 4);
        Add(Ui.Text(L.F($"Used by: {L.T(pack.Feature)}"), 13, muted: true), 14);

        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        string hosts = string.Join(", ", ModelCatalog.ContactedDomains(pack));
        foreach (var (label, value) in new[]
        {
            (L.T("Publisher"), pack.Publisher),
            (L.T("Original authors"), pack.Authors),
            (L.T("Source"), pack.Upstream),
            (L.T("Revision"), pack.Revision),
            (L.T("Licence"), ModelPacks.LicenseName(pack.LicenseId)),
            (L.T("Download size"), ModelPacks.FormatSize(pack.TotalBytes)),
            (L.T("Size on disk"), L.F($"about {ModelPacks.FormatSize(pack.TotalBytes)}")),
            (L.T("Training data"), L.T(pack.TrainingData)),
        })
        {
            int row = details.RowDefinitions.Count;
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Ui.Text(label, 13, muted: true); name.Margin = new Thickness(0, 0, 16, 4); name.VerticalAlignment = VerticalAlignment.Top;
            var content = Ui.Text(value, 13); content.Margin = new Thickness(0, 0, 0, 4);
            if (ReferenceEquals(value, pack.Upstream) && Uri.TryCreate(value, UriKind.Absolute, out var source) && source.Scheme == Uri.UriSchemeHttps)
            {
                // The catalog only accepts https sources, so the link opens the publisher's page in the default browser.
                content.Text = string.Empty;
                var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(value)) { NavigateUri = source, Cursor = Cursors.Hand, ToolTip = L.T("Open in browser") };
                link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Accent");
                link.RequestNavigate += (_, e) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    e.Handled = true;
                };
                AutomationProperties.SetName(link, L.F($"Source: {value}"));
                content.Inlines.Add(link);
            }
            Grid.SetRow(name, row); Grid.SetRow(content, row); Grid.SetColumn(content, 1);
            details.Children.Add(name); details.Children.Add(content);
        }
        Add(details, 14);

        Add(Ui.Text(L.T("Known limitations"), 14, bold: true), 4);
        foreach (string limitation in pack.Limitations) Add(Ui.Text("•  " + L.T(limitation), 13), 2);
        ((FrameworkElement)body.Children[^1]).Margin = new Thickness(0, 0, 0, 14);

        Add(Ui.Text(L.F($"This model is third-party software provided by its publisher under its own licence. DesktopTools downloads it at your request and runs it only on this PC. Your images, recordings and text are not sent anywhere. The download contacts {hosts}, which can see your IP address."), 13), 14);

        Add(Ui.Text(L.F($"Licence: {ModelPacks.LicenseName(pack.LicenseId)}"), 14, bold: true), 2);
        Add(Ui.Text(L.T("The licence is shown in its original language. That text is the one that applies."), 12, muted: true), 6);
        var license = new TextBox
        {
            Name = "LicenseText", Text = licenseText, IsReadOnly = true, IsReadOnlyCaretVisible = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top, HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
            Padding = new Thickness(8), BorderThickness = new Thickness(1)
        };
        license.SetResourceReference(Control.BackgroundProperty, "Field");
        license.SetResourceReference(Control.ForegroundProperty, "Text");
        license.SetResourceReference(Control.BorderBrushProperty, "Stroke");
        license.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en");
        AutomationProperties.SetName(license, L.T("Licence text"));
        Add(license, 14);

        // Visually set apart and above the checkbox (spec 2.3).
        var warningText = Ui.Text(L.T("This model is not part of DesktopTools. It was not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by its publisher. It is provided by its publisher as is, under the licence above. Results can be wrong; check them before you rely on them."), 13);
        warningText.Name = "NotOursWarning";
        var warningRow = new DockPanel();
        var warningIcon = Ui.Icon("Shield", 18); warningIcon.Margin = new Thickness(0, 1, 10, 0); warningIcon.VerticalAlignment = VerticalAlignment.Top;
        if (warningIcon is System.Windows.Shapes.Shape shape) shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Warning");
        warningRow.Children.Add(warningIcon); warningRow.Children.Add(warningText);
        var warning = new Border { Child = warningRow, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1, 1, 1, 1) };
        warning.SetResourceReference(Border.BorderBrushProperty, "Warning");
        warning.SetResourceReference(Border.BackgroundProperty, "Surface");
        AutomationProperties.SetName(warning, warningText.Text);
        Add(warning, 14);

        var download = Ui.Button(L.T("Download"), () => { }, true);
        // The app's checkboxes are drawn as switches without their content, so the sentence sits beside the switch and toggles it too.
        var accept = new CheckBox { Name = "AcceptLicense", IsChecked = false, Margin = new Thickness(0, 0, 12, 0) };
        accept.Checked += (_, _) => download.IsEnabled = true;
        accept.Unchecked += (_, _) => download.IsEnabled = false;
        AutomationProperties.SetName(accept, L.T("I have read and accept this licence"));
        var acceptLabel = Ui.Text(L.T("I have read and accept this licence")); acceptLabel.Name = "AcceptLicenseLabel"; acceptLabel.Cursor = Cursors.Hand;
        acceptLabel.MouseLeftButtonUp += (_, _) => accept.IsChecked = accept.IsChecked != true;
        var acceptRow = new DockPanel();
        DockPanel.SetDock(accept, Dock.Left); acceptRow.Children.Add(accept); acceptRow.Children.Add(acceptLabel);
        Add(acceptRow, 4);

        var actions = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = Ui.Button(L.T("Cancel"), Close);
        cancel.Name = "CancelDownload"; cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 6, 0);
        download.Name = "DownloadModel"; download.IsEnabled = false; download.Margin = new Thickness(6, 0, 0, 0);
        download.Click += (_, _) => { if (accept.IsChecked == true) { Accepted = true; Close(); } };
        Grid.SetColumn(download, 1); actions.Children.Add(cancel); actions.Children.Add(download);

        Content = UtilityWindowChrome.DialogCard(this, header, body, actions, 24);
        SourceInitialized += (_, _) => NativeWindowService.TryExcludeFromCapture(this, true, out _);
        Loaded += (_, _) => accept.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Motion.WindowEntrance(this);
    }

    /// <summary>Shows the dialog for a pack and records the acceptance. Returns true when the user accepted.</summary>
    internal static bool Ask(Window? owner, ModelPack pack, ModelConsentStore consents)
    {
        string text = ModelPacks.LicenseText(pack);
        var dialog = new ModelConsentWindow(pack, text);
        if (owner is { IsLoaded: true }) dialog.Owner = owner; else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        if (!dialog.Accepted) return false;
        consents.Accept(pack, text, DateTimeOffset.Now, typeof(ModelConsentWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown");
        return true;
    }
}
