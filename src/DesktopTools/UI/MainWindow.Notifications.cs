using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private void NotificationSettings()
    {
        var settings = controller.Settings;
        var previews = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        previews.ColumnDefinitions.Add(new ColumnDefinition());
        previews.ColumnDefinitions.Add(new ColumnDefinition());
        var screenshot = new ContentControl { Margin = new Thickness(0, 0, 8, 0) };
        var message = new ContentControl { Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(message, 1); previews.Children.Add(screenshot); previews.Children.Add(message);
        var example = new DrawingVisual();
        using (var drawing = example.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(199, 227, 239)), null, new Rect(0, 0, 160, 100));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 217, 136)), null, new Point(120, 24), 12, 12);
            drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(86, 155, 164)), null, Geometry.Parse("M0,100 L55,30 105,100 Z M55,100 L120,48 160,100 Z"));
        }
        var bitmap = new RenderTargetBitmap(160, 100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(example); bitmap.Freeze();
        void RefreshPreviews()
        {
            screenshot.Content = NotificationSurface.Create(L.T("Screenshot ready"), NotificationKind.Info,
                controller.Settings.ScreenshotNotificationStyle, () => { }, controller.LastCapture ?? bitmap);
            message.Content = NotificationSurface.Create(L.T("Notification preview"), NotificationKind.Info,
                controller.Settings.MessageNotificationStyle, () => { });
        }
        RefreshPreviews(); page.Children.Add(previews);
        var duration = new Slider { Minimum = 3, Maximum = 30, TickFrequency = 1, IsSnapToTickEnabled = true, Value = settings.NotificationSeconds, Width = 150 };
        var value = Ui.Text(settings.NotificationSeconds.ToString("0") + " s", 13);
        System.Windows.Automation.AutomationProperties.SetName(duration, L.T("Display duration"));
        duration.ValueChanged += (_, _) => { value.Text = duration.Value.ToString("0") + " s"; Change(s => s.NotificationSeconds = duration.Value); };
        var durationRow = new StackPanel { Orientation = Orientation.Horizontal };
        value.Margin = new Thickness(10, 0, 0, 0); durationRow.Children.Add(duration); durationRow.Children.Add(value);
        Group(L.T("Notifications"),
            Ui.Row(L.T("Screenshot notifications"), L.T("Preview shows the actual screenshot."), Ui.Choice(new[] { "Preview", "Card", "Capsule" }, settings.ScreenshotNotificationStyle,
                v => { Change(s => s.ScreenshotNotificationStyle = v); RefreshPreviews(); })),
            Ui.Row(L.T("Text notifications"), L.T("Style for messages without media."), Ui.Choice(new[] { "Capsule", "Card" }, settings.MessageNotificationStyle,
                v => { Change(s => s.MessageNotificationStyle = v); RefreshPreviews(); })),
            Ui.Row(L.T("Display duration"), L.T("Starts after mouse or keyboard activity. Pauses while you interact with a notification."), durationRow),
            Ui.Row(L.T("Animations"), L.T("Also respects the Windows animation preference."), Ui.Toggle(settings.Animations, v => Change(s => s.Animations = v))),
            Ui.Row(L.T("Show preview"), null, Ui.Button(L.T("Show preview"), () => controller.Report(L.T("Notification preview"), NotificationKind.Info))),
            Ui.Row(L.T("Restore defaults"), null, Ui.Button(L.T("Restore defaults"), () =>
            {
                Change(s => { s.ScreenshotNotificationStyle = "Preview"; s.MessageNotificationStyle = "Capsule"; s.NotificationSeconds = 6; });
                Navigate("Settings");
            })));
    }
}
