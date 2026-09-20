using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;

internal static class SetupTransitionChecks
{
    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void Click(Window surface, string tag) =>
        Walk(surface).OfType<Button>().Single(button => button.Tag as string == tag)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string PageTitle(StackPanel page) =>
        page.Children.OfType<TextBlock>().First().Text;

    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        Check(controller.UpdateSettings(settings =>
        {
            settings.Animations = true;
            settings.Setup.Status = SetupStatus.InProgress;
            settings.Setup.Step = 0;
        }), "Could not initialize setup transition preferences");

        controller.OpenSetup(restart: true);
        await Task.Delay(60);
        var setup = Application.Current.Windows.OfType<SetupWindow>().Single();
        var surface = setup.Owner;
        if (!surface.IsVisible) surface.Show();
        await Task.Delay(60);
        surface.UpdateLayout();
        var pageHost = Field<Grid>(setup, "pageHost");

        Click(surface, "setup-next");
        var forwardPage = Field<StackPanel>(setup, "content");
        Check(controller.Settings.Setup.Step == 1, "Forward navigation did not persist its page");
        if (Motion.Enabled)
        {
            await Task.Delay(30);
            Check(pageHost.Children.Count == 2, $"Forward transition did not retain outgoing and incoming pages (children={pageHost.Children.Count}, loaded={forwardPage.IsLoaded}, motion={Motion.Enabled})");
            Check(forwardPage.RenderTransform is TranslateTransform && ((TranslateTransform)forwardPage.RenderTransform).X > 0,
                $"Forward page did not enter from the right (offset={((TranslateTransform)forwardPage.RenderTransform).X}, pageClock={forwardPage.HasAnimatedProperties}, transformClock={((TranslateTransform)forwardPage.RenderTransform).HasAnimatedProperties})");
            Check(forwardPage.HasAnimatedProperties && ((TranslateTransform)forwardPage.RenderTransform).HasAnimatedProperties,
                "Forward page did not use slide and fade animation clocks");
        }
        await Task.Delay(260);
        surface.UpdateLayout();
        Check(pageHost.Children.Count == 1 && PageTitle(forwardPage).Length > 0,
            "Forward transition did not settle on one page");

        Click(surface, "setup-back");
        var backwardPage = Field<StackPanel>(setup, "content");
        Check(controller.Settings.Setup.Step == 0, "Back navigation did not persist its page");
        if (Motion.Enabled)
        {
            await Task.Delay(30);
            Check(backwardPage.RenderTransform is TranslateTransform backwardOffset && backwardOffset.X < 0,
                "Back page did not enter from the left");
        }
        await Task.Delay(260);

        string theme = controller.Settings.Theme == "Dark" ? "Light" : "Dark";
        var samePage = Field<StackPanel>(setup, "content");
        Walk(pageHost).OfType<Button>().Single(button => button.Content as string == DesktopTools.Localization.L.T(theme))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(controller.Settings.Theme == theme, "Same-page appearance change did not persist");
        Check(ReferenceEquals(samePage, Field<StackPanel>(setup, "content")) && pageHost.Children.Count == 1,
            "Same-page appearance change created a page transition");
        Check(!samePage.HasAnimatedProperties &&
              samePage.RenderTransform is TranslateTransform samePageOffset && !samePageOffset.HasAnimatedProperties,
            "Same-page appearance rebuild retained page animation clocks");

        Click(surface, "setup-next");
        Click(surface, "setup-next");
        Click(surface, "setup-back");
        Click(surface, "setup-next");
        var latestPage = Field<StackPanel>(setup, "content");
        await Task.Delay(260);
        surface.UpdateLayout();
        Check(controller.Settings.Setup.Step == 2, "Rapid navigation lost the latest requested page");
        Check(pageHost.Children.Count == 1 && ReferenceEquals(pageHost.Children[0], latestPage),
            "A stale transition replaced or retained the latest page");
        Check(PageTitle(latestPage) == DesktopTools.Localization.L.T("Your keyboard, your shortcuts"),
            "Rapid navigation rendered stale setup content");

        Check(controller.UpdateSettings(settings => settings.Animations = false),
            "Could not enable reduced-motion test preference");
        Click(surface, "setup-back");
        var reducedPage = Field<StackPanel>(setup, "content");
        Check(controller.Settings.Setup.Step == 1, "Reduced-motion navigation did not persist its page");
        Check(pageHost.Children.Count == 1 && ReferenceEquals(pageHost.Children[0], reducedPage),
            "Reduced motion retained an outgoing setup page");
        Check(!reducedPage.HasAnimatedProperties &&
              reducedPage.RenderTransform is TranslateTransform reducedOffset &&
              !reducedOffset.HasAnimatedProperties && Math.Abs(reducedOffset.X) < .001 &&
              Math.Abs(reducedPage.Opacity - 1) < .001,
            "Reduced motion retained page animation state");

        setup.Close();
        controller.OpenSetup();
        await Task.Delay(60);
        var resumed = Application.Current.Windows.OfType<SetupWindow>().Single();
        Check(controller.Settings.Setup.Step == 1, "Setup did not resume the persisted transition destination");
        Check(controller.Settings.Theme == theme, "Setup transition lost a persisted appearance choice");
        resumed.Close();
    }
}
