using DesktopTools.Localization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.UI;
namespace DesktopTools.Extras;
internal sealed class QuickWheelWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Button[] buttons = new Button[8];
    private readonly System.Windows.Shapes.Path[] wedges = new System.Windows.Shapes.Path[8];
    private readonly Func<string, bool> available;
    private readonly string[] actions;
    private readonly bool[] enabled;
    private readonly Action<string?> complete;
    private int selected = -1;
    private bool finished;
    private readonly TextBlock hint = Ui.Text(L.T("Choose an action"), 16, true);
    internal bool Polling => timer.IsEnabled;
    internal int Selected => selected;
    public QuickWheelWindow(string[] actions, Func<string,bool> available, Action<string?> complete, uint? heldKey = null)
    {
        this.actions = actions.ToArray(); this.complete = complete; this.available = available; enabled = actions.Select(available).ToArray();
        Title = L.T("Quick actions"); Width = Height = 440; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var canvas = new Canvas { Width = 440, Height = 440, Background = Brushes.Transparent };
        var surface = new Border { Background = Ui.Brush("GlassSurface"), BorderBrush = Ui.Brush("Stroke"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(220), Width = 436, Height = 436 };
        Canvas.SetLeft(surface,2); Canvas.SetTop(surface,2); canvas.Children.Add(surface);
        var center = new StackPanel { Width = 100, IsHitTestVisible = false };
        var closeIcon = Ui.Icon("Close", 18); closeIcon.HorizontalAlignment = HorizontalAlignment.Center; center.Children.Add(closeIcon);
        hint.FontSize = 12; hint.Text = L.T("Close"); hint.TextAlignment = TextAlignment.Center; hint.Margin = new Thickness(0, 8, 0, 0); center.Children.Add(hint);
        Canvas.SetLeft(center,170); Canvas.SetTop(center,196); canvas.Children.Add(center);
        for (int i=0;i<8;i++)
        {
            int index=i; double a = (i*45-90)*Math.PI/180;
            var wedge = new System.Windows.Shapes.Path { Data = Wedge(i), StrokeThickness = 1, IsHitTestVisible = false };
            wedge.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "GlassSurface"); wedge.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Stroke"); wedge.Opacity = enabled[i] ? 1 : .35;
            canvas.Children.Insert(1, wedge); wedges[i] = wedge;
            var button = Ui.Button(L.T(actions[i]), () => Finish(index)); button.Width=104; button.Height=74; button.Padding=new Thickness(3); button.Margin=new Thickness(0); button.BorderThickness = new Thickness(0); button.Background = Brushes.Transparent;
            // The sector itself is the hover/keyboard selection. Do not draw a second
            // rectangular button highlight on top of the radial selection.
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = presenter };
            button.Focusable = false; button.Opacity = enabled[i] ? 1 : .42;
            var label = new StackPanel(); var icon = Ui.Icon(ActionIcon(actions[i]), 23); icon.HorizontalAlignment = HorizontalAlignment.Center; label.Children.Add(icon);
            var text = Ui.Text(L.T(actions[i]),12,true); text.TextAlignment = TextAlignment.Center; text.Margin = new Thickness(0, 7, 0, 0); label.Children.Add(text); button.Content = label;
            button.IsEnabled=enabled[i]; button.MouseEnter += (_,_) => Select(index);
            Canvas.SetLeft(button,220+146*Math.Cos(a)-52); Canvas.SetTop(button,220+146*Math.Sin(a)-37); canvas.Children.Add(button); buttons[i]=button;
        }
        Content=canvas;
        MouseMove += (_,e) => { var p=e.GetPosition(canvas); Select(QuickWheelActions.Sector(p.X-220,p.Y-220)); };
        PreviewMouseLeftButtonDown += (_, e) => { var p = e.GetPosition(canvas); e.Handled = true; Finish(QuickWheelActions.Sector(p.X - 220, p.Y - 220)); };
        MouseRightButtonDown += (_,e) => { e.Handled=true; Finish(-1); };
        PreviewKeyDown += (_,e) =>
        {
            if (e.Key==Key.Escape) { e.Handled=true; Finish(-1); }
            else if(e.Key==Key.Enter) { e.Handled=true; Finish(selected); }
            else if(e.Key is Key.Right or Key.Down or Key.Left or Key.Up or Key.Tab)
            {
                e.Handled=true; int direction=e.Key is Key.Left or Key.Up ? -1 : 1;
                for(int n=1;n<=8;n++) { int index=((selected<0?(direction>0?-1:0):selected)+direction*n+16)%8; if(enabled[index]) { Select(index); break; } }
            }
        };
        Loaded += (_,_) =>
        {
            var monitor=MonitorService.GetCurrent(); NativeMethods.GetCursorPos(out var cursor);
            int width=(int)Math.Ceiling(440*monitor.ScaleX), height=(int)Math.Ceiling(440*monitor.ScaleY);
            int x=(int)Math.Clamp(cursor.X-width/2d,monitor.WorkingArea.Left,Math.Max(monitor.WorkingArea.Left,monitor.WorkingArea.Right-width));
            int y=(int)Math.Clamp(cursor.Y-height/2d,monitor.WorkingArea.Top,Math.Max(monitor.WorkingArea.Top,monitor.WorkingArea.Bottom-height));
            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle,new nint(-1),x,y,width,height,0x10);
            if(heldKey.HasValue) timer.Start();
        };
        timer.Tick += (_,_) => { if(heldKey.HasValue && (GetAsyncKeyState((int)heldKey.Value)&0x8000)==0) Finish(selected); };
        Deactivated += (_,_) => { if(IsVisible) Finish(-1); };
        Closed += (_,_) => { timer.Stop(); if(!finished) { finished=true; complete(null); } };
        // Selection immediately dispatches capture/drawing; remove the wheel first.
        Motion.WindowEntrance(this, animateClose: false);
    }
    internal void Select(int index)
    {
        if(index>=0 && index<8 && (!enabled[index] || !available(actions[index]))) index=-1;
        if(index < -1 || index >= 8) index = -1;
        if(selected==index)return; selected=index;
        for(int i=0;i<8;i++) wedges[i].SetResourceReference(System.Windows.Shapes.Shape.FillProperty, i==index ? "Selected" : "GlassSurface");
        hint.Text=index<0?L.T("Close"):L.T(actions[index]); Motion.Reveal(hint);
    }
    internal void Finish(int index)
    {
        if(finished)return; finished=true; timer.Stop(); Close(); complete(index>=0 && index<8 && enabled[index] && available(actions[index])?actions[index]:null);
    }
    private static Geometry Wedge(int index)
    {
        double start = (index * 45 - 112.5) * Math.PI / 180, end = start + Math.PI / 4;
        Point At(double radius, double angle) => new(220 + radius * Math.Cos(angle), 220 + radius * Math.Sin(angle));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(62, start), true, true); context.LineTo(At(215, start), true, false);
            context.ArcTo(At(215, end), new Size(215, 215), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(At(62, end), true, false); context.ArcTo(At(62, start), new Size(62, 62), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        geometry.Freeze(); return geometry;
    }
    private static string ActionIcon(string action) => action switch
    {
        "Text tools" => "Translate", "Scan screen text" => "ScanText", "File shelf" => "Folder", "Screen recorder" => "Record",
        "Video editor" => "Video", "QR codes" => "QR", "Image tools" => "Image", "Teleprompter" => "Prompter", _ => action
    };
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
