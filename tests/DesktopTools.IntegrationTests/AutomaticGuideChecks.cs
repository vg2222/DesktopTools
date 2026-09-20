using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Collections.Generic;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;

internal static class AutomaticGuideChecks
{
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true); controller.ApplyTheme(); DesktopTools.Localization.L.Use("en");
        var settings = new AppSettings(); int setups = 0;
        bool Save(Action<AppSettings> change) { change(settings); return true; }
        Window Create()
        {
            var first = new Button { Content = "Open file", Height = 40 };
            var second = new Button { Content = "Export after opening", Height = 40, IsEnabled = false };
            var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second);
            var owner = new Window { Width = 500, Height = 350, Content = panel };
            panel.Children.Add(FeatureTourButton.Create(owner, "recorder", () => new GuidedTour.Step[] { new(() => first, "Open", "Open"), new(() => second, "Export", "Export") }, settings, Save, autoStart: true));
            return owner;
        }
        var closer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        closer.Tick += (_, _) =>
        {
            var setup = Application.Current.Windows.OfType<FeatureSetupWindow>().FirstOrDefault(); if (setup == null) return;
            setups++;
            Walk(setup).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Continue to guide").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        closer.Start(); var window = Create(); window.Show(); await Task.Delay(100); closer.Stop();
        if (setups != 1 || window.GetValue(FeatureTourButton.CurrentTourProperty) is not GuidedTour { IsOpen: true } || settings.FeatureSetup["recorder"].Status != SetupStatus.Completed) throw new Exception("First entry did not transition from setup into tour");
        window.Close();
        window = Create(); window.Show(); await Task.Delay(80);
        var tour = window.GetValue(FeatureTourButton.CurrentTourProperty) as GuidedTour;
        if (tour?.IsOpen != true || settings.FeatureTours["recorder"].Step != 0) throw new Exception("Pending tour did not start after completed setup");
        tour.Next(); if (settings.FeatureTours["recorder"].Step != 1 || !tour.IsOpen) throw new Exception("Disabled export target was omitted");
        window.Close();
        window = Create(); window.Show(); await Task.Delay(80); tour = window.GetValue(FeatureTourButton.CurrentTourProperty) as GuidedTour;
        if (tour?.IsOpen != true || settings.FeatureTours["recorder"].Step != 1) throw new Exception("Interrupted tour did not resume its step");
        tour.Next(); window.Close();
        foreach (var status in new[] { SetupStatus.Completed, SetupStatus.Skipped })
        {
            settings.FeatureTours["recorder"] = new OnboardingProgress { Status = status }; window = Create(); window.Show(); await Task.Delay(60);
            if (window.GetValue(FeatureTourButton.CurrentTourProperty) != null) throw new Exception("Completed or skipped tour opened automatically"); window.Close();
        }
        if (settings.FeatureTours.Count != 1 || settings.FeatureSetup.Count != 1) throw new Exception("Guide modified unrelated feature progress");
    }
}
