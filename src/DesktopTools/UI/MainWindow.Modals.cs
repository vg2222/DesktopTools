using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private Grid? modalRoot;
    private readonly List<FrameworkElement> modalLayers = [];
    internal bool HasModal => modalLayers.Count != 0;

    internal void ShowEmbeddedDialog(Window dialog, bool wait = false)
    {
        if (modalRoot == null)
        {
            var shell = (UIElement)Content;
            Content = null;
            modalRoot = new Grid(); modalRoot.Children.Add(shell); Content = modalRoot;
            modalRoot.SizeChanged += (_, _) => UtilityWindowChrome.ClipContent(this);
        }
        var previous = modalLayers.LastOrDefault() ?? (FrameworkElement)modalRoot.Children[0];
        var previousFocus = Keyboard.FocusedElement;
        var oldEffect = previous.Effect;
        previous.IsEnabled = false;
        if (!SystemParameters.HighContrast) previous.Effect = new BlurEffect { Radius = 5, RenderingBias = RenderingBias.Performance };
        var layer = new Grid { Background = new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)), Tag = "app-modal" };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(layer, true);
        var content = (FrameworkElement)dialog.Content;
        dialog.Content = null;
        content.MaxWidth = dialog.Width;
        content.Margin = new Thickness(28);
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.VerticalAlignment = VerticalAlignment.Center;
        void SizeDialog(object? sender, SizeChangedEventArgs? args)
        {
            content.MaxHeight = Math.Max(200, ActualHeight - 76);
            if (dialog.SizeToContent == SizeToContent.Manual) content.Height = Math.Min(dialog.Height, content.MaxHeight);
        }
        SizeDialog(null, null); SizeChanged += SizeDialog;
        layer.Children.Add(content); modalRoot.Children.Add(layer); modalLayers.Add(layer);
        controller.RefreshModalInput();
        KeyboardNavigation.SetTabNavigation(layer, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(layer, KeyboardNavigationMode.Cycle);
        dialog.Owner = this;
        DesktopTools.Presentation.WindowDismissal.Attach(dialog, () => Motion.Enabled, layer);
        var frame = new DispatcherFrame();
        void OnOwnerVisibility(object sender, DependencyPropertyChangedEventArgs args) { if (!IsVisible) dialog.Close(); }
        IsVisibleChanged += OnOwnerVisibility;
        dialog.Closed += (_, _) =>
        {
            SizeChanged -= SizeDialog; IsVisibleChanged -= OnOwnerVisibility;
            modalRoot.Children.Remove(layer); modalLayers.Remove(layer);
            controller.RefreshModalInput();
            previous.Effect = oldEffect; previous.IsEnabled = true;
            if (previousFocus is UIElement { IsVisible: true, IsEnabled: true } focus) Keyboard.Focus(focus);
            frame.Continue = false;
        };
        layer.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; dialog.Close(); } };
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            Motion.ModalEntrance(content);
        }));
        if (wait) Dispatcher.PushFrame(frame);
    }
}
