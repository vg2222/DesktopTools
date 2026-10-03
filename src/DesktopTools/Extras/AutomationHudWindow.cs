using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using DesktopTools.Localization;
using DesktopTools.UI;
using DesktopTools.Native;
namespace DesktopTools.Extras;

/// <summary>Content-sized status surface; no fixed height can clip localized text.</summary>
internal sealed class AutomationHudWindow : Window
{
    private readonly TextBlock caption;
    private readonly TextBlock detail;
    internal AutomationHudWindow(string title,Action stop)
    {
        Title=L.T("Desktop Automation");Width=410;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false;ShowActivated=false;Topmost=true;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        UtilityWindowChrome.EnableBackdrop(this);
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var icon=new Border{Width=38,Height=38,CornerRadius=new CornerRadius(11),Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center,Child=Ui.Icon("Play",18)};
        icon.SetResourceReference(Border.BackgroundProperty,"Selected");row.Children.Add(icon);
        var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};caption=Ui.Text(title,13,true);caption.TextWrapping=TextWrapping.Wrap;copy.Children.Add(caption);
        detail=Ui.Text(L.T("Esc to stop"),11,muted:true);detail.Margin=new Thickness(0,4,0,0);copy.Children.Add(detail);Grid.SetColumn(copy,1);row.Children.Add(copy);
        var cancel=Ui.IconButton("Stop",L.T("Stop"),stop);cancel.Width=cancel.Height=38;cancel.Margin=new Thickness(12,0,0,0);cancel.BorderThickness=new Thickness(1);cancel.SetResourceReference(Control.BorderBrushProperty,"Stroke");Grid.SetColumn(cancel,2);row.Children.Add(cancel);
        var surface=Ui.Card(row,16);surface.Margin=new Thickness(0);Content=surface;
        Loaded+=(_,_)=>Place();
        SizeChanged+=(_,_)=>{if(IsLoaded)Place();};
        SourceInitialized+=(_,_)=>NativeWindowService.TryExcludeFromCapture(this,true,out _);
    }
    internal void Caption(string title)=>caption.Text=title;
    internal void Progress(int index,int count)=>detail.Text=L.F($"Action {index+1} of {count}")+" · Esc";
    private void Place()
    {
        var monitor=MonitorService.GetForWindow(this);
        var dpi=VisualTreeHelper.GetDpi(this);
        int width=(int)Math.Ceiling(ActualWidth*dpi.DpiScaleX),height=(int)Math.Ceiling(ActualHeight*dpi.DpiScaleY);
        int x=(int)Math.Max(monitor.WorkingArea.Left,monitor.WorkingArea.Right-width-20),y=(int)Math.Max(monitor.WorkingArea.Top,Math.Min(monitor.WorkingArea.Top+20,monitor.WorkingArea.Bottom-height));
        NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle,new IntPtr(-1),x,y,0,0,0x0001|0x0010);
    }
}
