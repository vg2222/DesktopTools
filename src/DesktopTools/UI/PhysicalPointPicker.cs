using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Native;
namespace DesktopTools.UI;
internal static class PhysicalPointPicker
{
    internal static async Task<Point?> PickAsync(CancellationToken token)
    {
        var result=new TaskCompletionSource<Point?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows=new List<Window>();
        try{
            foreach(var monitor in MonitorService.GetAll()){
                var w=new Window{WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=new SolidColorBrush(Color.FromArgb(2,0,0,0)),Topmost=true,ShowInTaskbar=false,ResizeMode=ResizeMode.NoResize,Cursor=Cursors.Cross,Width=monitor.Bounds.Width/monitor.ScaleX,Height=monitor.Bounds.Height/monitor.ScaleY};
                w.SourceInitialized+=(_,_)=>{MonitorService.PlaceWindow(w,monitor);NativeWindowService.TryExcludeFromCapture(w,true,out _);};
                w.MouseLeftButtonDown+=(_,e)=>{result.TrySetResult(w.PointToScreen(e.GetPosition(w)));e.Handled=true;};
                w.KeyDown+=(_,e)=>{if(e.Key==Key.Escape)result.TrySetResult(null);};
                w.Closed+=(_,_)=>result.TrySetResult(null);windows.Add(w);w.Show();
            }
            using var registration=token.Register(()=>result.TrySetCanceled(token));
            return await result.Task;
        }finally{foreach(var w in windows)w.Close();}
    }
}
