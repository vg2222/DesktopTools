using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.UI;

internal static class PageTransitionChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static async Task RunAsync()
    {
        var surface = new Border();
        Motion.PageTransition(surface, true);
        Check(surface.HasAnimatedProperties
            && surface.RenderTransform is TranslateTransform slide && slide.HasAnimatedProperties,
            "Category page did not animate opacity and position.");
        Motion.PageTransition(surface, true);
        Check(surface.HasAnimatedProperties
            && surface.RenderTransform is TranslateTransform restarted && restarted.HasAnimatedProperties,
            "Rapid category switching lost its transition.");
        Motion.PageTransition(surface, false);
        Check(!surface.HasAnimatedProperties
            && surface.RenderTransform is TranslateTransform reset && !reset.HasAnimatedProperties
            && reset.X == 0 && reset.Y == 0 && surface.Opacity == 1,
            "Reduced motion did not leave the category fully visible and unshifted.");

        using var controller = new AppController(true);
        controller.UpdateSettings(s => s.Animations = true);
        controller.OpenMain();
        var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        var page = (Panel)typeof(MainWindow).GetField("page", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        main.Navigate("Capture tools");
        await Task.Delay(250);
        main.Navigate("Presentation tools");
        await Task.Delay(30);
        Check(page.Opacity < .95 && page.RenderTransform is TranslateTransform repeated && repeated.X > 2,
            $"A later category change started without visible motion: opacity={page.Opacity:0.###}, x={(page.RenderTransform as TranslateTransform)?.X:0.###}, loaded={page.IsLoaded}, enabled={Motion.Enabled}, clocks={page.HasAnimatedProperties}.");
        foreach (var category in new[] { "Capture tools", "Presentation tools", "Media tools", "Text tools", "Desktop utilities" })
            main.Navigate(category);
        Check(page.Children.Count > 0, "Rapid category switching left the destination blank.");
        main.Close();
    }
}
