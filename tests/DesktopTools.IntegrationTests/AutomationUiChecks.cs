using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using System.Windows.Threading;
using System.Windows.Interop;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class AutomationUiChecks
{
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window,int attribute,out int value,int size);
    internal static async Task RunAsync()
    {
        string language=L.Language;var failures=new System.Collections.Generic.List<string>();
        using var controller=new AppController(true);
        using var service=new AutomationService(Path.Combine(Environment.CurrentDirectory,"automation-ui-"+Guid.NewGuid().ToString("N")),()=>false,_=>{});
        service.Save([new(){Name="Workflow fixture",Steps=[new(){Kind=AutomationKind.Wait,Value=2000},new(){Kind=AutomationKind.Repeat,Value=2},new(){Kind=AutomationKind.Wait,Value=1000},new(){Kind=AutomationKind.EndRepeat}]}]);
        controller.UpdateSettings(s=>{s.Theme="Dark";s.UseCustomBackground=true;s.BackgroundColor="#101827";s.Transparency=true;s.Animations=false;});
        try{
            if(Application.Current.Resources["Shell"] is SolidColorBrush shell&&shell.Color.A==255)failures.Add("Custom background blocks desktop transparency");
            L.Use("ru");
            var window=new AutomationWindow(service);window.Show();await Task.Delay(100);window.UpdateLayout();Save(window,"automation-ui-polished.png");
            if(window.Content is Border surface&&surface.Background is SolidColorBrush solid&&solid.Color.A==255)failures.Add("Automation root blocks acrylic");
            var steps=(ListBox)typeof(AutomationWindow).GetField("steps",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var first=(ListBoxItem)steps.Items[0];
            var move=Descendants(first).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T("Move down"));move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
            var current=(AutomationScript)typeof(AutomationWindow).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(current.Steps[0].Kind!=AutomationKind.Repeat||current.Steps[^1].Kind!=AutomationKind.Wait)failures.Add("Inline row move failed");
            typeof(AutomationWindow).GetMethod("Undo",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            typeof(AutomationWindow).GetMethod("ReportError",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[L.T("The target window is unavailable.")]);window.UpdateLayout();Save(window,"automation-message-error.png");
            var notice=(Border)typeof(AutomationWindow).GetField("notice",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(notice.Visibility!=Visibility.Visible||AutomationProperties.GetLiveSetting(notice)!=AutomationLiveSetting.Assertive)failures.Add("Error is not visible or accessible");
            typeof(AutomationWindow).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Save(window,"automation-message-success.png");
            if(AutomationProperties.GetLiveSetting(notice)!=AutomationLiveSetting.Polite)failures.Add("Informational message interrupts screen-reader speech");
            foreach(string mode in new[]{"cancel","invalid","save"}){
                var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};Exception? error=null;
                timer.Tick+=(_,_)=>{timer.Stop();var dialog=Application.Current.Windows.OfType<AutomationRunSettingsWindow>().Single();try{
                    var target=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==L.T("Window title contains"));target.Text="Test target";
                    if(mode=="cancel"){dialog.Close();return;}
                    var daily=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==L.T("Daily at"));daily.Text=mode=="invalid"?"25:80":"18:30";
                    Descendants(dialog).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T("Save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(mode=="invalid"){if(!dialog.IsVisible||!Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text==L.T("Enter a time in HH:mm format.")))throw new Exception("Invalid schedule closed dialog or hid error");Save(dialog,"automation-runsettings-error.png");dialog.Close();}
                }catch(Exception e){error=e;dialog.Close();}};
                timer.Start();typeof(AutomationWindow).GetMethod("OpenRunSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);timer.Stop();if(error!=null)throw error;
                if(mode!="save"&&service.Scripts[0].TargetWindow.Length!=0)failures.Add("Cancel or failed validation saved settings");
            }
            if(service.Scripts[0].DailyTime!="18:30"||service.Scripts[0].TargetWindow!="Test target")failures.Add("Run settings save lost fields");
            var libraryTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};Exception? libraryError=null;
            libraryTimer.Tick+=(_,_)=>{libraryTimer.Stop();var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Owner==window);try{
                dialog.Width=dialog.MinWidth;dialog.Height=dialog.MinHeight;dialog.UpdateLayout();
                var search=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==L.T("Search actions"));search.Text="no-such-action-unique";dialog.UpdateLayout();
                if(!Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text==L.T("No matching actions")))throw new Exception("Action search lost its empty state");
                Save(dialog,"automation-library-small-empty.png");search.Clear();dialog.UpdateLayout();Save(dialog,"automation-library-small.png");
                if(dialog.Content is not Border surface||surface.Margin!=new Thickness(0))throw new Exception("Action library has a footer gap");
            }catch(Exception e){libraryError=e;}finally{dialog.Close();}};
            libraryTimer.Start();typeof(AutomationWindow).GetMethod("OpenActions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);libraryTimer.Stop();if(libraryError!=null)throw libraryError;
            window.Close();
            foreach(string locale in new[]{"en","ru","de","fr","es"}){
                L.Use(locale);var hud=AutomationService.CreateHud(L.T("Automation running"),()=>{});hud.Show();await Task.Delay(80);hud.UpdateLayout();Save(hud,"automation-hud-"+locale+".png");
                if(hud.Content is not Border hudSurface||hudSurface.Margin!=new Thickness(0))failures.Add("HUD root margin leaves bottom strip");
                var stop=Descendants(hud).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T("Stop"));
                if(stop.TranslatePoint(new Point(0,stop.ActualHeight),hud).Y>hud.ActualHeight-8)failures.Add("HUD stop control is clipped");
                hud.Close();
                var settings=new AutomationRunSettingsWindow(new AutomationScript(),_=>null,(_,_)=>{});settings.Width=settings.MinWidth;settings.Height=settings.MinHeight;settings.Show();await Task.Delay(60);settings.UpdateLayout();
                try{
                    foreach(var label in new[]{"Cancel","Save"}){
                        var button=Descendants(settings).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T(label));var position=button.TranslatePoint(new Point(),settings);
                        if(position.X<0||position.Y<0||position.X+button.ActualWidth>settings.ActualWidth||position.Y+button.ActualHeight>settings.ActualHeight-8)failures.Add("Run settings footer is clipped: "+locale+" "+label);
                        if(label=="Save"&&!button.IsDefault||label=="Cancel"&&!button.IsCancel)failures.Add("Run settings keyboard default is missing");
                    }
                    var scroll=Descendants(settings).OfType<ScrollViewer>().First();if(scroll.ScrollableHeight<=0)failures.Add("Small settings cannot scroll");scroll.ScrollToEnd();settings.UpdateLayout();Save(settings,"automation-settings-small-"+locale+".png");
                }finally{settings.Close();}
            }
            await BackdropPixels(controller,service);
            controller.UpdateSettings(s=>s.Transparency=false);
            if(Application.Current.Resources["Shell"] is not SolidColorBrush opaque||opaque.Color.A!=255)failures.Add("Transparency off must be opaque");
            if(failures.Count>0)throw new Exception(string.Join("; ",failures));
        }finally{L.Use(language);}
    }
    private static async Task BackdropPixels(AppController controller,AutomationService service)
    {
        var monitor=MonitorService.GetCurrent();
        var background=new Window{Title="Owned backdrop fixture",WindowStyle=WindowStyle.None,Left=monitor.WorkingArea.Left/monitor.ScaleX,Top=monitor.WorkingArea.Top/monitor.ScaleY,Width=monitor.WorkingArea.Width/monitor.ScaleX,Height=monitor.WorkingArea.Height/monitor.ScaleY,ShowInTaskbar=false,Background=new SolidColorBrush(Color.FromRgb(30,130,220))};
        background.Show();
        var window=new AutomationWindow(service){Width=1040,Height=650,Topmost=true};window.Show();window.Activate();await Task.Delay(250);
        try{
            nint handle=new WindowInteropHelper(window).Handle;NativeWindowService.TryExcludeFromCapture(window,false,out _);NativeWindowService.TryExcludeFromCapture(background,false,out _);
            var p=window.PointToScreen(new Point(40,window.ActualHeight-110));
            byte[] Pixel(){NativeWindowService.SynchronizeDesktop();var source=CaptureService.Capture(new MonitorInfo("Owned pixel",new Rect(p.X,p.Y,1,1),Rect.Empty,1,1));var bytes=new byte[4];new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0).CopyPixels(bytes,4,0);return bytes;}
            byte[] first=Pixel();var firstOrigin=window.PointToScreen(new Point());var firstDpi=VisualTreeHelper.GetDpi(window);SaveBitmap(CaptureService.Capture(new MonitorInfo("Owned window",new Rect(firstOrigin.X,firstOrigin.Y,Math.Floor(window.ActualWidth*firstDpi.DpiScaleX),Math.Floor(window.ActualHeight*firstDpi.DpiScaleY)),Rect.Empty,1,1)),"automation-native-acrylic-first.png");background.Background=new SolidColorBrush(Color.FromRgb(220,60,120));await Task.Delay(400);byte[] second=Pixel();
            DwmGetWindowAttribute(handle,38,out int backdrop,sizeof(int));
            Console.WriteLine($"Backdrop fixture: type={backdrop}, foreground={NativeWindowService.GetForegroundWindowHandle()}, window={handle}, first={string.Join(",",first)}, second={string.Join(",",second)}, brush={((Border)window.Content).Background}");
            if(backdrop==3 && NativeWindowService.GetForegroundWindowHandle()==handle){
                if(first.Take(3).Zip(second.Take(3),(a,b)=>Math.Abs(a-b)).Sum()<5)throw new Exception("Native backdrop is enabled but an opaque layer still hides desktop color");
                Console.WriteLine("PASS native acrylic changes with the owned background color");
            }else Console.WriteLine("NOT TESTED: active acrylic pixels (system unavailable or Windows refused foreground ownership); translucent resources and opaque fallback verified");
            var origin=window.PointToScreen(new Point());var dpi=VisualTreeHelper.GetDpi(window);SaveBitmap(CaptureService.Capture(new MonitorInfo("Owned window",new Rect(origin.X,origin.Y,Math.Floor(window.ActualWidth*dpi.DpiScaleX),Math.Floor(window.ActualHeight*dpi.DpiScaleY)),Rect.Empty,1,1)),"automation-native-acrylic.png");
            controller.UpdateSettings(s=>s.Transparency=false);NativeWindowService.ApplyBackdrop(window,true,false);
            if(window.Background is not SolidColorBrush solid||solid.Color.A!=255)throw new Exception("Native opaque fallback missing");
        }finally{window.Close();background.Close();}
    }
    internal static async Task PreviewAsync()
    {
        using var controller=new AppController(true);controller.UpdateSettings(s=>{s.Theme="Dark";s.UseCustomBackground=false;s.Transparency=true;s.Animations=false;});L.Use("ru");
        using var service=new AutomationService(Path.Combine(Environment.CurrentDirectory,"automation-preview-"+Guid.NewGuid().ToString("N")),()=>true,_=>{});
        service.Save([new(){Name="Мой сценарий",Steps=[new(){Kind=AutomationKind.Wait,Value=3000},new(){Kind=AutomationKind.Repeat,Value=2},new(){Kind=AutomationKind.Wait,Value=2000},new(){Kind=AutomationKind.EndRepeat}]}]);
        var completion=new TaskCompletionSource();var window=new AutomationWindow(service);window.Closed+=(_,_)=>completion.TrySetResult();window.Show();await completion.Task.WaitAsync(TimeSpan.FromMinutes(8));
    }
    internal static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    internal static void Save(Window window,string path)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);SaveBitmap(bitmap,path);
    }
    private static void SaveBitmap(BitmapSource bitmap,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);}
}
