using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class MenuInteractionChecks
{
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; });
        var combo = new ComboBox { ItemsSource = new[] { "First device", "Second device" }, SelectedIndex = 0 };
        var button = new Button { Content = "Options", Margin = new Thickness(10) };
        var content = new StackPanel { Margin = new Thickness(30) }; content.Children.Add(combo); content.Children.Add(button);
        var window = new Window { Title = "DesktopTools menu helper", Tag = "Audio controls", Content = content,
            Width = 760, Height = 650, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        var pin = new MenuItem { Header = "Keep on top", IsCheckable = true, IsChecked = true, InputGestureText = "Ctrl+Alt+P" };
        var parent = new MenuItem { Header = "Export copy" };
        var nested = new MenuItem { Header = "Image formats" };
        int clicks = 0;
        var png = new MenuItem { Header = "PNG", InputGestureText = "Enter" }; png.Click += (_, _) => clicks++;
        nested.Items.Add(png); parent.Items.Add(nested);
        menu.Items.Add(pin); menu.Items.Add(new Separator()); menu.Items.Add(parent);
        for (int i = 0; i < 18; i++) menu.Items.Add(new MenuItem { Header = "Saved option " + (i + 1) });
        try
        {
            window.Show(); await Settle();
            var tip = new ToolTip { Content = "Plain tool tooltip", PlacementTarget = button };
            try
            {
                tip.IsOpen = true; await Settle(); CheckAffinity(tip);
                Check(ReferenceEquals(Ui.PopupOwner(tip), window), "Plain tooltip lost its owning tool");
            }
            finally { tip.IsOpen = false; }
            combo.IsDropDownOpen = true; await Settle();
            var dropdown = (Popup)combo.Template.FindName("PART_Popup", combo);
            CheckAffinity((Visual)dropdown.Child);
            combo.IsDropDownOpen = false;
            menu.IsOpen = true; await Settle();
            CheckAffinity(menu);
            Check(menu.ActualHeight <= 412, "Long menu escaped its scrolling surface");
            Check(((FrameworkElement)pin.Template.FindName("Checkmark", pin)).IsVisible, "Checked pin has no checkmark");
            Invoke(pin); await Settle();
            Check(!pin.IsChecked && !menu.IsOpen, "Checkable action did not toggle and close");
            menu.IsOpen = true; await Settle();
            parent.Focus(); Press(parent, Key.Right); await Settle();
            Check(parent.IsSubmenuOpen, "Right arrow did not open submenu");
            nested.Focus(); Press(nested, Key.Right); await Settle();
            Check(nested.IsSubmenuOpen, "Right arrow did not open nested submenu");
            var popup = (Popup)nested.Template.FindName("PART_Popup", nested);
            CheckAffinity((Visual)popup.Child);
            Check(ReferenceEquals(Ui.PopupOwner(nested), window), "Nested popup lost owning tool");
            var visible = new DesktopTools.Core.AppSettings { HideControlsFromCapture = true };
            visible.VisibleCaptureFeatures = ["Audio controls"];
            Check(!AppCapturePrivacy.ShouldHide(Ui.PopupOwner(nested)!, visible), "Popup owner lost visible-feature exception");
            Render(menu, "menu-interaction-root.png"); Render((FrameworkElement)popup.Child, "menu-interaction-nested.png");
            png.Focus(); Press(png, Key.Left); await Settle();
            Check(!nested.IsSubmenuOpen && parent.IsSubmenuOpen, "Left arrow did not close one submenu level");
            nested.IsSubmenuOpen = true; await Settle(); Invoke(png); await Settle();
            Check(clicks == 1 && !menu.IsOpen && !parent.IsSubmenuOpen && !nested.IsSubmenuOpen, "Nested action did not close the menu chain exactly once");
            menu.IsOpen = true; await Settle(); pin.Focus(); Press(pin, Key.Escape); await Settle();
            Check(!menu.IsOpen, "Escape did not dismiss menu");
            CheckDialogPrivacy(window);
        }
        finally { menu.IsOpen = false; combo.IsDropDownOpen = false; window.Close(); }
    }
    private static void CheckDialogPrivacy(Window owner)
    {
        var settings = new DesktopTools.Core.AppSettings { HideControlsFromCapture = true, VisibleCaptureFeatures = ["Audio controls"] };
        var previous = NativeWindowService.CapturePrivacyResolver;
        NativeWindowService.CapturePrivacyResolver = window => AppCapturePrivacy.ShouldHide(window, settings);
        var confirm = new ConfirmationDialog("Confirm test", "No changes will be made.", "Confirm") { Owner = owner };
        var save = new SaveBeforeDisableDialog(() => Task.FromResult(false));
        var setup = new FeatureSetupWindow("video-editor", settings, _ => false) { Owner = owner };
        var separate = new VideoEditorWindow(_ => { }) { Owner = owner };
        try
        {
            confirm.Show(); save.Owner = confirm; save.Show(); setup.Show();
            foreach (var dialog in new Window[] { confirm, save, setup })
            {
                Check(AppCapturePrivacy.FeatureGroup(dialog) == "Audio controls" && !AppCapturePrivacy.ShouldHide(dialog, settings), "Shared dialog lost its owning tool exception");
                AppCapturePrivacy.Apply(dialog, settings);
                Check(GetWindowDisplayAffinity(new WindowInteropHelper(dialog).Handle, out uint affinity) && affinity == 0, "Visible tool dialog stayed excluded");
            }
            Check(AppCapturePrivacy.ShouldHide(separate, settings), "Separate video tool inherited another tool's exception");
            settings.VisibleCaptureFeatures = [];
            AppCapturePrivacy.Apply(confirm, settings);
            Check(AppCapturePrivacy.ShouldHide(save, settings), "Nested dialog ignored restored capture privacy");
            Check(GetWindowDisplayAffinity(new WindowInteropHelper(confirm).Handle, out uint hidden) && hidden == 0x11, "Dialog did not restore native exclusion");
            settings.HideControlsFromCapture = false;
            Check(!AppCapturePrivacy.ShouldHide(setup, settings), "Shared dialog ignored disabled master privacy");
            setup.Tag = "Text tools";
            Check(AppCapturePrivacy.FeatureGroup(setup) == "Text tools", "Explicit dialog group was overridden");
        }
        finally
        {
            separate.Close(); setup.Close(); save.Close(); confirm.Close(); NativeWindowService.CapturePrivacyResolver = previous;
        }
    }
    private static void Invoke(MenuItem item) => ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Press(UIElement element, Key key) => element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(element)!, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });
    private static async Task Settle() { await Task.Delay(70); await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void CheckAffinity(Visual visual)
    {
        var source = PresentationSource.FromVisual(visual) as HwndSource;
        uint affinity = 0; bool read = source != null && GetWindowDisplayAffinity(source.Handle, out affinity);
        Check(read && affinity == 0x11, $"Popup HWND did not inherit capture exclusion: HWND={source?.Handle}, read={read}, affinity={affinity}, loaded={(visual as FrameworkElement)?.IsLoaded}");
    }
    private static void Render(FrameworkElement element, string path)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowDisplayAffinity(nint handle, out uint affinity);
}
