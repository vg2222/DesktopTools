using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal static class RecordingAudioMeter
{
    internal static ProgressBar Create(double height, Thickness margin)
    {
        var bar = NotificationSurface.CreateProgressBar(0, null);
        bar.Name = "";
        bar.Height = height;
        bar.Margin = margin;
        AutomationProperties.SetHelpText(bar, "");
        return bar;
    }
}
