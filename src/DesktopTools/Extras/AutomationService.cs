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
    private readonly Dictionary<Guid,(string Key,AutomationTriggerState State)> triggers=[];
    private readonly Func<bool> available;
    private readonly Action<string> report;
    private CancellationTokenSource? cancellation;
    private bool disposed;
    internal bool IsActive {get;private set;}
    internal List<AutomationScript> Scripts {get;private set;}
    internal event Action? Changed;
    internal event Action<int>? Progress;
    internal event Action<string>? StatusChanged;
    private void Report(string message){report(message);StatusChanged?.Invoke(message);}
    private nint targetHandle;private uint targetProcess;
    internal nint Target {get=>AutomationInput.External(targetHandle)&&AutomationInput.GetWindowThreadProcessId(targetHandle,out uint pid)!=0&&pid==targetProcess?targetHandle:0;set{targetHandle=value;AutomationInput.GetWindowThreadProcessId(value,out targetProcess);}}
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
        timer.Tick+=(_,_)=>Tick();ApplyTriggers(true);
        if(store.RecoveryMessage!=null)report(store.RecoveryMessage);
    }
    internal void Save(List<AutomationScript> scripts)
    {
        foreach(var s in scripts){AutomationProgram.Compile(s);if(s.Shortcut.Length>0&&!HotkeyGesture.TryParse(s.Shortcut,out _,out var error))throw new ArgumentException(error);foreach(var step in s.Steps)if(step.Kind==AutomationKind.Keys)AutomationInput.ParseKeys(step.Text);}
        var bindings=scripts.Where(s=>s.Armed&&s.Shortcut.Length>0).ToDictionary(s=>s.Id.ToString(),s=>s.Shortcut);
        if(!hotkeys.TryValidate(bindings,out var conflict))throw new InvalidOperationException(conflict);
        store.Save(scripts);Scripts=scripts.Select(s=>s.Copy()).ToList();ApplyTriggers();Changed?.Invoke();
    }
    private void ApplyTriggers(bool startup=false)
    {
        var failures=hotkeys.RegisterAvailable(Scripts.Where(s=>s.Armed&&s.Shortcut.Length>0).ToDictionary(s=>s.Id.ToString(),s=>s.Shortcut));
        if(failures.Count>0)report(L.T("An automation shortcut is unavailable.")+" "+string.Join("; ",failures.Values));
        foreach(var id in triggers.Keys.Where(id=>!Scripts.Any(s=>s.Id==id)).ToArray())triggers.Remove(id);
        foreach(var s in Scripts){string key=$"{s.Armed}|{s.IntervalMinutes}|{s.DailyTime}|{s.WindowTrigger}|{s.RunOnStartup}";if(!triggers.TryGetValue(s.Id,out var old)||old.Key!=key)triggers[s.Id]=(key,new(s,DateTime.Now,AutomationInput.FindWindow(s.WindowTrigger)!=0,startup));}
        UpdatePolling();
    }
    private void UpdatePolling()
    {
        if(!disposed && Scripts.Any(s=>triggers.TryGetValue(s.Id,out var trigger)&&trigger.State.NeedsPolling(s)))timer.Start();
        else timer.Stop();
    }
    private void Tick()
    {
        foreach(var s in Scripts.Where(s=>s.Armed)){
            nint window=AutomationInput.FindWindow(s.WindowTrigger);
            if(triggers[s.Id].State.Take(s,DateTime.Now,window!=0,!IsActive&&available())){Target=window!=0?window:0;_=RunAsync(s);}
        }
        UpdatePolling();
    }
    internal void SetRecording(bool value) { if(cancellation!=null)return; IsActive=value; hotkeys.DispatchSuspended=value; Changed?.Invoke(); }
    internal void Stop()=>cancellation?.Cancel();
    internal async Task RunAsync(AutomationScript source)
    {
        if(disposed||IsActive||!available())return;
        source=source.Copy();nint requestedTarget=Target;Target=0;
        Window? hud=null;EscapeKeyService? escape=null;HiddenWindowsScope? hidden=null;
        try {
            AutomationProgram.Compile(source);
            foreach(var s in source.Steps)if(s.Kind==AutomationKind.Keys)AutomationInput.ParseKeys(s.Text);
            escape=new();escape.Pressed+=Stop;if(!escape.SetEnabled(true))throw new InvalidOperationException(L.T("Escape is unavailable. Automation was not started."));
            cancellation=new();cancellation.CancelAfter(TimeSpan.FromMinutes(10));
            IsActive=true;Changed?.Invoke();hotkeys.DispatchSuspended=true;
            nint target=source.TargetWindow.Length>0?AutomationInput.FindWindow(source.TargetWindow,true):requestedTarget;
            if(source.TargetWindow.Length>0&&!AutomationInput.External(target))throw new InvalidOperationException(L.T("The target window is unavailable."));
            hidden=new HiddenWindowsScope();
            if(!AutomationInput.External(target))target=NativeWindowService.GetForegroundWindowHandle();
            if(AutomationInput.External(target)){if(AutomationInput.IsIconic(target))AutomationInput.SetWindowState(target,9);NativeWindowService.RestoreForeground(target);}
            hud=CreateHud(L.T("Automation running"),Stop);hud.Show();
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
                    case AutomationKind.WaitWindow:case AutomationKind.WaitPixel:
                        var wait=Stopwatch.StartNew();while(!await Condition(s,token)){if(wait.ElapsedMilliseconds>=s.Value)throw new TimeoutException(L.T("The waiting action timed out."));await Task.Delay(100,token);}break;
                    case AutomationKind.ClipboardText:System.Windows.Clipboard.SetText(s.Text);break;
                    case AutomationKind.PasteClipboard:input.Key(17,true);try{input.Key(86,true);input.Key(86,false);}finally{input.Key(17,false);}break;
                    case AutomationKind.MaximizeWindow:case AutomationKind.MinimizeWindow:case AutomationKind.RestoreWindow:
                        nint managed=AutomationInput.FindWindow(s.Text,true);if(!AutomationInput.External(managed))throw new InvalidOperationException(L.T("The target window is unavailable."));
                        AutomationInput.SetWindowState(managed,s.Kind==AutomationKind.MaximizeWindow?3:s.Kind==AutomationKind.MinimizeWindow?6:1);
                        var stateWait=Stopwatch.StartNew();
                        while(s.Kind==AutomationKind.MinimizeWindow?!AutomationInput.IsIconic(managed):s.Kind==AutomationKind.MaximizeWindow?(!AutomationInput.IsZoomed(managed)||AutomationInput.IsIconic(managed)):(AutomationInput.IsZoomed(managed)||AutomationInput.IsIconic(managed))){
                            if(!AutomationInput.External(managed)||stateWait.ElapsedMilliseconds>2000)throw new InvalidOperationException(L.T("The target window is unavailable."));
                            await Task.Delay(50,token);
                        }
                        if(s.Kind!=AutomationKind.MinimizeWindow){NativeWindowService.RestoreForeground(managed);expected=managed;}break;
                    case AutomationKind.Launch:using(var launched=Process.Start(new ProcessStartInfo(s.Text){UseShellExecute=true})){}expected=0;await Task.Delay(500,token);break;
                    case AutomationKind.FocusWindow:
                        expected=AutomationInput.FindWindow(s.Text,true);if(!AutomationInput.External(expected))throw new InvalidOperationException(L.T("The target window is unavailable."));if(AutomationInput.IsIconic(expected))AutomationInput.SetWindowState(expected,9);if(!NativeWindowService.RestoreForeground(expected)&&NativeWindowService.GetForegroundWindowHandle()!=expected)throw new InvalidOperationException(L.T("The target window is unavailable."));await Task.Delay(200,token);break;
                    case AutomationKind.Keys:var keys=AutomationInput.ParseKeys(s.Text);try{foreach(var k in keys)input.Key(k,true);}finally{foreach(var k in keys.Reverse())input.Key(k,false);}break;
                    case AutomationKind.KeyDown: input.Key((ushort)s.Value,true);break;
                    case AutomationKind.KeyUp: input.Key((ushort)s.Value,false);break;
                    case AutomationKind.Text:foreach(char c in s.Text){token.ThrowIfCancellationRequested();if(NativeWindowService.GetForegroundWindowHandle()!=expected)throw new InvalidOperationException(L.T("The target window changed. Automation stopped."));input.Unicode(c);await Task.Delay(2,token);}break;
                    case AutomationKind.Scroll:input.Move(s.X,s.Y);input.Wheel(s.Value);break;
                    case AutomationKind.Move:input.Move(s.X,s.Y);break;
                    case AutomationKind.MouseDown:input.Move(s.X,s.Y);input.Button(s.Value,true);break;
                    case AutomationKind.MouseUp:input.Move(s.X,s.Y);input.Button(s.Value,false);break;
                    case AutomationKind.Drag:
                        input.Move(s.X,s.Y);input.Button(1,true);try{for(int i=1;i<=20;i++){token.ThrowIfCancellationRequested();if(NativeWindowService.GetForegroundWindowHandle()!=expected)throw new InvalidOperationException(L.T("The target window changed. Automation stopped."));int x=s.X+(s.EndX-s.X)*i/20,y=s.Y+(s.EndY-s.Y)*i/20;GuardPoint(x,y);input.Move(x,y);await Task.Delay(15,token);}}finally{input.Button(1,false);}break;
                    case AutomationKind.Click:case AutomationKind.DoubleClick:case AutomationKind.RightClick:
                        input.Move(s.X,s.Y);int b=s.Kind==AutomationKind.RightClick?2:1;input.Button(b,true);input.Button(b,false);
                        if(s.Kind==AutomationKind.DoubleClick){await Task.Delay(70,token);GuardPoint(s.X,s.Y);input.Button(b,true);input.Button(b,false);}break;
                }
                await Task.Delay(source.StepDelayMs,token);
            }
            Task<bool> Condition(AutomationStep s,CancellationToken token)
            {
                token.ThrowIfCancellationRequested();if(s.Kind is AutomationKind.IfWindow or AutomationKind.WaitWindow)return Task.FromResult(AutomationInput.FindWindow(s.Text)!=0);
                var pixel=CaptureService.Capture(new MonitorInfo("AutomationPixel",new Rect(s.X,s.Y,1,1),Rect.Empty,1,1));var bytes=new byte[4];new FormatConvertedBitmap(pixel,System.Windows.Media.PixelFormats.Bgra32,null,0).CopyPixels(bytes,4,0);
                return Task.FromResult(string.Equals($"#{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}",s.Text,StringComparison.OrdinalIgnoreCase));
            }
            int lastStep=-1;
            try{await AutomationProgram.RunAsync(source,Condition,Action,cancellation.Token,progress:i=>{lastStep=i;if(hud is AutomationHudWindow progressHud)progressHud.Progress(i,source.Steps.Count);Progress?.Invoke(i);});}
            catch(Exception e)when(e is not OperationCanceledException){throw new InvalidOperationException(L.F($"Action {lastStep+1}: {e.Message}"),e);}
            Report(L.T("Automation completed."));
        }catch(OperationCanceledException){if(!disposed)Report(L.T("Automation stopped."));}
        catch(Exception e){if(!disposed)Report(L.T("Automation failed: ")+e.Message);}
        finally{
            hud?.Close();hidden?.Dispose();escape?.Dispose();cancellation?.Dispose();cancellation=null;IsActive=false;hotkeys.DispatchSuspended=false;Changed?.Invoke();
        }
    }
    internal static bool RequiresStableTarget(AutomationKind kind)=>kind is AutomationKind.Keys or AutomationKind.Text or AutomationKind.PasteClipboard or AutomationKind.KeyDown or AutomationKind.KeyUp
        or AutomationKind.Click or AutomationKind.DoubleClick or AutomationKind.RightClick or AutomationKind.Scroll or AutomationKind.Drag
        or AutomationKind.MouseDown or AutomationKind.MouseUp or AutomationKind.Move;
    internal static Window CreateHud(string title,Action stop)=>new AutomationHudWindow(title,stop);
    public void Dispose(){disposed=true;Stop();timer.Stop();hotkeys.Dispose();}
}
