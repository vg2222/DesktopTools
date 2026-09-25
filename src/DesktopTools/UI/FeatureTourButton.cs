using System.Windows;
using System.Windows.Controls;
using DesktopTools.Core;
using DesktopTools.Localization;

namespace DesktopTools.UI;

/// <summary>Explicit, repeatable guidance. Opening help never runs a feature action.</summary>
internal static class FeatureTourButton
{
    internal static Button Create(Window owner, string feature, Func<IReadOnlyList<GuidedTour.Step>> steps,
        AppSettings? settings = null, Func<Action<AppSettings>, bool>? save = null, bool? autoStart = null)
    {
        var controller = (Application.Current as App)?.Controller;
        settings ??= controller?.Settings; save ??= controller == null ? null : controller.UpdateSettings;
        GuidedTour? tour = null;
        bool opening = false;
        void Start(bool repeatSetup)
        {
            if (opening) return;
            opening = true;
            try
            {
            tour?.Dispose();
            if (settings != null && save != null && HasSetup(feature) &&
                (repeatSetup || !settings.FeatureSetup.TryGetValue(feature, out var setup) || setup.ShouldResume))
            {
                var dialog = new FeatureSetupWindow(feature, settings, save) { Owner = owner }; dialog.ShowDialog(); if (!dialog.ContinueToGuide) return;
                owner.RaiseEvent(new RoutedEventArgs(SetupAppliedEvent));
            }
            var available = steps().Where(s => s.Target() is { IsVisible: true }).ToArray();
            if (available.Length == 0) return;
            int first = settings?.FeatureTours.TryGetValue(feature, out var progress) == true && progress.Status == SetupStatus.InProgress ? progress.Step : 0;
            tour = new GuidedTour(owner, available, first, (status, step) => save == null || save(s => s.FeatureTours[feature] = new OnboardingProgress { Status = status, Step = step }));
            owner.SetValue(CurrentTourProperty, tour);
            }
            finally { opening = false; }
        }
        var button = Ui.IconButton("Help", L.T("Show guide"), () => Start(false));
        owner.SetValue(StartGuideProperty, (Action<bool>)Start);
        button.Tag = "feature-guide-" + feature;
        if ((autoStart ?? controller != null) && settings != null && (HasSetup(feature) || feature == "image-editor"))
            owner.Loaded += (_, _) => owner.Dispatcher.BeginInvoke(() =>
            {
                bool setupPending = HasSetup(feature) && (!settings.FeatureSetup.TryGetValue(feature, out var setup) || setup.ShouldResume);
                bool guidePending = !settings.FeatureTours.TryGetValue(feature, out var progress) || progress.ShouldResume;
                if (owner.IsVisible && (setupPending || guidePending)) Start(false);
            });
        owner.Closed += (_, _) => { tour?.Dispose(); tour = null; owner.ClearValue(CurrentTourProperty); owner.ClearValue(StartGuideProperty); };
        return button;
    }
    private static bool HasSetup(string feature) => feature is "recorder" or "teleprompter" or "text-tools" or "video-editor";
    internal static void Start(Window owner, bool repeatSetup) => (owner.GetValue(StartGuideProperty) as Action<bool>)?.Invoke(repeatSetup);
    private static readonly DependencyProperty StartGuideProperty = DependencyProperty.RegisterAttached("StartGuide", typeof(Action<bool>), typeof(FeatureTourButton));
    internal static readonly DependencyProperty CurrentTourProperty = DependencyProperty.RegisterAttached("CurrentTour", typeof(GuidedTour), typeof(FeatureTourButton));
    internal static readonly RoutedEvent SetupAppliedEvent = EventManager.RegisterRoutedEvent("SetupApplied", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(FeatureTourButton));
}
