using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.Localization;
using DesktopTools.UI;
namespace DesktopTools.Extras;
internal sealed class AutomationService : IDisposable
{
    private readonly AutomationStore store;
    private readonly HotkeyService hotkeys=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly Dictionary<Guid,DateTimeOffset> due=[];
    private readonly Dictionary<Guid,bool> present=[];
    private readonly Func<bool> available;
    private readonly Action<string> report;
    private CancellationTokenSource? cancellation;
    private bool disposed;
    internal bool IsActive {get;private set;}
    internal List<AutomationScript> Scripts {get;private set;}
    internal event Action? Changed;
    internal event Action<int>? Progress;
    internal nint Target {get;set;}
    internal AutomationService(string path,Func<bool> available,Action<string> report)
    {
        this.available=available;this.report=report;store=new(path);Scripts=store.Load();
        hotkeys.Pressed+=id=>
        {
            if(!Guid.TryParse(id,out var key) || Scripts.FirstOrDefault(s=>s.Id==key) is not {} script)return;
            nint foreground=NativeWindowService.GetForegroundWindowHandle();
            Target=AutomationInput.External(foreground)?foreground:0;
            _=RunAsync(script);
        };
        timer.Tick+=(_,_)=>Tick();ApplyTriggers();timer.Start();
        if(store.RecoveryMessage!=null)report(store.RecoveryMessage);
    }
    internal void Save(List<AutomationScript> scripts)
    {
        foreach(var s in scripts){AutomationProgram.Compile(s);if(s.Shortcut.Length>0&&!HotkeyGesture.TryParse(s.Shortcut,out _,out var error))throw new ArgumentException(error);foreach(var step in s.Steps)if(step.Kind==AutomationKind.Keys)AutomationInput.ParseKeys(step.Text);}
        var bindings=scripts.Where(s=>s.Armed&&s.Shortcut.Length>0).ToDictionary(s=>s.Id.ToString(),s=>s.Shortcut);
        if(!hotkeys.TryValidate(bindings,out var conflict))throw new InvalidOperationException(conflict);
        store.Save(scripts);Scripts=scripts.Select(s=>s.Copy()).ToList();ApplyTriggers();Changed?.Invoke();
    }
    private void ApplyTriggers()
    {
        var failures=hotkeys.RegisterAvailable(Scripts.Where(s=>s.Armed&&s.Shortcut.Length>0).ToDictionary(s=>s.Id.ToString(),s=>s.Shortcut));
        if(failures.Count>0)report(L.T("An automation shortcut is unavailable.")+" "+string.Join("; ",failures.Values));
        due.Clear();present.Clear();
        foreach(var s in Scripts){due[s.Id]=DateTimeOffset.UtcNow.AddMinutes(Math.Max(1,s.IntervalMinutes));present[s.Id]=AutomationInput.FindWindow(s.WindowTrigger)!=0;}
    }
    private void Tick()
    {
        foreach(var s in Scripts.Where(s=>s.Armed)){
            nint window=AutomationInput.FindWindow(s.WindowTrigger);bool exists=window!=0;bool appeared=exists&&!present.GetValueOrDefault(s.Id);present[s.Id]=exists;
            bool scheduled=s.IntervalMinutes>0 && DateTimeOffset.UtcNow>=due[s.Id];
            if(scheduled)due[s.Id]=DateTimeOffset.UtcNow.AddMinutes(s.IntervalMinutes);
            if((scheduled||appeared)&&!IsActive&&available()){Target=window!=0?window:NativeWindowService.GetForegroundWindowHandle();_=RunAsync(s);break;}
        }
    }
    internal void SetRecording(bool value) { if(cancellation!=null)return; IsActive=value; hotkeys.DispatchSuspended=value; Changed?.Invoke(); }
    internal void Stop()=>cancellation?.Cancel();
    internal async Task RunAsync(AutomationScript source)
    {
        if(disposed||IsActive||!available())return;
        Window? hud=null;EscapeKeyService? escape=null;HiddenWindowsScope? hidden=null;
        try {
            AutomationProgram.Compile(source);
            foreach(var s in source.Steps)if(s.Kind==AutomationKind.Keys)AutomationInput.ParseKeys(s.Text);
            escape=new();escape.Pressed+=Stop;if(!escape.SetEnabled(true))throw new InvalidOperationException(L.T("Escape is unavailable. Automation was not started."));
            cancellation=new();cancellation.CancelAfter(TimeSpan.FromMinutes(10));
            IsActive=true;Changed?.Invoke();hotkeys.DispatchSuspended=true;
            nint target=AutomationInput.External(Target)?Target:NativeWindowService.GetForegroundWindowHandle();
            hidden=new HiddenWindowsScope();
            if(AutomationInput.External(target))NativeWindowService.RestoreForeground(target);
            hud=CreateHud(L.T("Automation running — Esc to stop"),Stop);hud.Show();
            await Task.Delay(700,cancellation.Token);
            using var input=new AutomationInput();
            nint expected=AutomationInput.External(target)?target:0;
            void GuardPoint(int x,int y)
            {
                nint hit=AutomationInput.GetAncestor(AutomationInput.WindowFromPoint(new(){X=x,Y=y}),2);
                if(hit!=expected && AutomationInput.GetAncestor(hit,3)!=expected)
                    throw new InvalidOperationException(L.T("The target window changed. Automation stopped."));
            }
            async Task Action(AutomationStep s,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if(RequiresStableTarget(s.Kind))
                    if(!AutomationInput.External(expected)||NativeWindowService.GetForegroundWindowHandle()!=expected)throw new InvalidOperationException(L.T("The target window changed. Automation stopped."));
                if(s.Kind is AutomationKind.Click or AutomationKind.DoubleClick or AutomationKind.RightClick or AutomationKind.Scroll or AutomationKind.Drag or AutomationKind.MouseDown or AutomationKind.MouseUp or AutomationKind.Move)
                    GuardPoint(s.X,s.Y);
                switch(s.Kind){
                    case AutomationKind.Wait:await Task.Delay(s.Value,token);return;
                    case AutomationKind.Launch:Process.Start(new ProcessStartInfo(s.Text){UseShellExecute=true});await Task.Delay(500,token);expected=NativeWindowService.GetForegroundWindowHandle();break;
                    case AutomationKind.FocusWindow:
                        expected=AutomationInput.FindWindow(s.Text);if(!NativeWindowService.RestoreForeground(expected))throw new InvalidOperationException(L.T("The target window is unavailable."));await Task.Delay(200,token);break;
                    case AutomationKind.Keys:var keys=AutomationInput.ParseKeys(s.Text);try{foreach(var k in keys)input.Key(k,true);}finally{foreach(var k in keys.Reverse())input.Key(k,false);}break;
                    case AutomationKind.KeyDown: input.Key((ushort)s.Value,true);break;
                    case AutomationKind.KeyUp: input.Key((ushort)s.Value,false);break;
                    case AutomationKind.Text:foreach(char c in s.Text){token.ThrowIfCancellationRequested();if(NativeWindowService.GetForegroundWindowHandle()!=expected)throw new InvalidOperationException(L.T("The target window changed. Automation stopped."));input.Unicode(c);await Task.Delay(2,token);}break;
                    case AutomationKind.Scroll:input.Move(s.X,s.Y);input.Wheel(s.Value);break;
                    case AutomationKind.Move:input.Move(s.X,s.Y);break;
                    case AutomationKind.MouseDown:input.Move(s.X,s.Y);input.Button(s.Value,true);expected=NativeWindowService.GetForegroundWindowHandle();break;
                    case AutomationKind.MouseUp:input.Move(s.X,s.Y);input.Button(s.Value,false);break;
                    case AutomationKind.Drag:
                        input.Move(s.X,s.Y);input.Button(1,true);try{for(int i=1;i<=20;i++){token.ThrowIfCancellationRequested();if(NativeWindowService.GetForegroundWindowHandle()!=expected)throw new InvalidOperationException(L.T("The target window changed. Automation stopped."));int x=s.X+(s.EndX-s.X)*i/20,y=s.Y+(s.EndY-s.Y)*i/20;GuardPoint(x,y);input.Move(x,y);await Task.Delay(15,token);}}finally{input.Button(1,false);}break;
                    case AutomationKind.Click:case AutomationKind.DoubleClick:case AutomationKind.RightClick:
                        input.Move(s.X,s.Y);int b=s.Kind==AutomationKind.RightClick?2:1;input.Button(b,true);input.Button(b,false);
                        if(s.Kind==AutomationKind.DoubleClick){await Task.Delay(70,token);GuardPoint(s.X,s.Y);input.Button(b,true);input.Button(b,false);}break;
                }
                await Task.Delay(35,token);
            }
            Task<bool> Condition(AutomationStep s,CancellationToken token)
            {
                if(s.Kind==AutomationKind.IfWindow)return Task.FromResult(AutomationInput.FindWindow(s.Text)!=0);
                var pixel=CaptureService.Capture(new MonitorInfo("AutomationPixel",new Rect(s.X,s.Y,1,1),Rect.Empty,1,1));var bytes=new byte[4];new FormatConvertedBitmap(pixel,System.Windows.Media.PixelFormats.Bgra32,null,0).CopyPixels(bytes,4,0);
                return Task.FromResult(string.Equals($"#{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}",s.Text,StringComparison.OrdinalIgnoreCase));
            }
            await AutomationProgram.RunAsync(source,Condition,Action,cancellation.Token,progress:i=>Progress?.Invoke(i));
            report(L.T("Automation completed."));
        }catch(OperationCanceledException){if(!disposed)report(L.T("Automation stopped."));}
        catch(Exception e){if(!disposed)report(L.T("Automation failed: ")+e.Message);}
        finally{
            hud?.Close();hidden?.Dispose();escape?.Dispose();cancellation?.Dispose();cancellation=null;IsActive=false;hotkeys.DispatchSuspended=false;Changed?.Invoke();
        }
    }
    internal static bool RequiresStableTarget(AutomationKind kind)=>kind is AutomationKind.Keys or AutomationKind.Text or AutomationKind.KeyDown or AutomationKind.KeyUp
        or AutomationKind.Click or AutomationKind.DoubleClick or AutomationKind.RightClick or AutomationKind.Scroll or AutomationKind.Drag
        or AutomationKind.MouseDown or AutomationKind.MouseUp or AutomationKind.Move;
    internal static Window CreateHud(string title,Action stop)
    {
        var w=new Window{Title=L.T("Desktop Automation"),Width=420,Height=100,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        var p=new StackPanel();p.Children.Add(Ui.Text(title,13,true));var b=Ui.Button(L.T("Stop"),stop);b.Margin=new Thickness(0,10,0,0);p.Children.Add(b);w.Content=Ui.Card(p,14);UtilityWindowChrome.EnableBackdrop(w);
        w.Loaded+=(_,_)=>{var monitor=MonitorService.GetCurrent();NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(w).Handle,new IntPtr(-1),(int)(monitor.WorkingArea.Right-w.ActualWidth*monitor.ScaleX-24),(int)(monitor.WorkingArea.Top+24),0,0,0x0001|0x0010);};
        w.SourceInitialized+=(_,_)=>{NativeWindowService.TryExcludeFromCapture(w,true,out _);};return w;
    }
    public void Dispose(){disposed=true;Stop();timer.Stop();hotkeys.Dispose();}
}
