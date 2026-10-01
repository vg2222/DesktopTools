using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.Localization;
using DesktopTools;
internal static class AutomationChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint hwnd,out RECT rect);
    internal static async Task RunAsync()
    {
        string folder=Path.Combine(Environment.CurrentDirectory,"automation-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string originalLanguage=L.Language;
        try {
            using var service=new AutomationService(folder,()=>true,m=>File.AppendAllText(Path.Combine(folder,"status.txt"),m+"\n"));
            service.Save([new(){Name="Browser work",Steps=[new(){Kind=AutomationKind.FocusWindow,Text="Browser"},new(){Kind=AutomationKind.Repeat,Value=3},new(){Kind=AutomationKind.Keys,Text="Ctrl+L"},new(){Kind=AutomationKind.Text,Text="https://example.com"},new(){Kind=AutomationKind.Keys,Text="Enter"},new(){Kind=AutomationKind.Wait,Value=500},new(){Kind=AutomationKind.EndRepeat}]}]);
            using var themeController=new AppController(true);
            foreach(string theme in new[]{"Light","Dark"}) foreach(string language in new[]{"en","ru","de","fr","es"}){
                themeController.UpdateSettings(s=>{s.Theme=theme;s.BackgroundColor="#060606";s.UseCustomBackground=theme=="Dark";});
                L.Use(language);var w=new AutomationWindow(service);w.Show();await Task.Delay(180);w.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);Save(bitmap,"automation-"+theme+"-"+language+".png");
                // Capture physical pixels of the actual app-owned window, without private desktop content.
                nint hwnd=new WindowInteropHelper(w).Handle;GetWindowRect(hwnd,out var r);
                NativeWindowService.SynchronizeDesktop();
                if(language=="ru" && theme=="Dark")Save(CaptureService.Capture(new MonitorInfo("Fixture",new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top),Rect.Empty,1,1)),"automation-actual-window.png");
                w.Close();
            }
            L.Use("en");
            var countdownWindow=new AutomationWindow(service);countdownWindow.Show();
            var recordMethod=typeof(AutomationWindow).GetMethod("RecordAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var first=(Task)recordMethod.Invoke(countdownWindow,null)!;var second=(Task)recordMethod.Invoke(countdownWindow,null)!;
            if(!service.IsActive || !second.IsCompleted)throw new Exception("Multiple recording countdowns accepted");
            typeof(AutomationWindow).GetMethod("StopRecording",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(countdownWindow,null);
            await first.WaitAsync(TimeSpan.FromSeconds(1));countdownWindow.Close();
            if(!AutomationService.RequiresStableTarget(AutomationKind.Click)||!AutomationService.RequiresStableTarget(AutomationKind.Drag)||AutomationService.RequiresStableTarget(AutomationKind.Wait))
                throw new Exception("Pointer target stability policy is incomplete");
            var launchOwner=new Window{Title="Automation test launcher",Width=320,Height=120,Content=new TextBlock{Text="Synthetic input target"}};launchOwner.Show();launchOwner.Activate();await Task.Delay(100);
            string executable=Environment.ProcessPath!;var start=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Environment.CurrentDirectory,CreateNoWindow=true};
            if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            string statePath=Path.Combine(folder,"input.json");start.ArgumentList.Add("--input-target");start.ArgumentList.Add(statePath);
            using var child=Process.Start(start)!;
            AllowSetForegroundWindow((uint)child.Id);
            nint original=NativeWindowService.GetForegroundWindowHandle();var pointer=AutomationInput.Pointer();
            try{
                var state=await State(statePath);nint target=(nint)state.Form;
                if(!AutomationInput.External(target)||AutomationInput.GetWindowThreadProcessId(target,out var pid)==0||pid!=child.Id)throw new Exception("Target ownership failed");
                launchOwner.Hide();
                bool focused=false;for(int attempt=0;attempt<15;attempt++){NativeWindowService.RestoreForeground(target);if(NativeWindowService.GetForegroundWindowHandle()==target){focused=true;break;}await Task.Delay(100);}
                if(!focused)Console.WriteLine("NOT TESTED: cross-process input; Windows refused foreground ownership. Foreground HWND="+NativeWindowService.GetForegroundWindowHandle()+", target="+target+", session="+Process.GetCurrentProcess().SessionId);
                if(focused){
                await Task.Delay(150);GetWindowRect((nint)state.TextBox,out var r);int x=(r.Left+r.Right)/2,y=(r.Top+r.Bottom)/2;
                var hit=AutomationInput.GetAncestor(AutomationInput.WindowFromPoint(new(){X=x,Y=y}),2);if(hit!=target)throw new Exception("Text point is occluded; input not sent");
                service.Target=target;
                await service.RunAsync(new(){Name="Synthetic Unicode",Steps=[new(){Kind=AutomationKind.Click,X=x,Y=y},new(){Kind=AutomationKind.Repeat,Value=2},new(){Kind=AutomationKind.Text,Text="Hello Привет "},new(){Kind=AutomationKind.EndRepeat}]});
                await Task.Delay(120);state=await State(statePath);
                if(state.Text!="Hello Привет Hello Привет ")throw new Exception("Unicode/repeat playback failed: "+File.ReadAllText(Path.Combine(folder,"status.txt")));
                }
                var cancelScript=new AutomationScript{Steps=[new(){Kind=AutomationKind.Wait,Value=30000}]};service.Target=target;
                var running=service.RunAsync(cancelScript);await Task.Delay(180);service.Stop();await running.WaitAsync(TimeSpan.FromSeconds(3));if(service.IsActive)throw new Exception("Cancel left active service");
                // Recorder callbacks reject injected events and unregister on every disposal.
                for(int i=0;i<10;i++){using var recorder=new AutomationRecorder();if(!recorder.HasHooks)throw new Exception("Recording hooks unavailable");recorder.Dispose();if(recorder.HasHooks)throw new Exception("Recording hooks leaked");}
            }finally{
                launchOwner.Close();
                if(!child.HasExited){child.CloseMainWindow();if(!child.WaitForExit(2000))child.Kill();}
                using var restore=new AutomationInput();restore.Move((int)pointer.X,(int)pointer.Y);NativeWindowService.RestoreForeground(original);
            }
        }finally{L.Use(originalLanguage);}
    }
    private static async Task<NativeInputChecks.TargetState> State(string path)
    {
        for(int i=0;i<100;i++){try{if(File.Exists(path))return JsonSerializer.Deserialize<NativeInputChecks.TargetState>(File.ReadAllText(path))!;}catch(IOException){}catch(JsonException){}await Task.Delay(50);}
        throw new Exception("Native target did not respond");
    }
    private static void Save(BitmapSource bitmap,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
}
