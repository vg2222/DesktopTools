using System.Windows;
using System.Windows.Media;
using DesktopTools.Core;
using DesktopTools.UI;
using DesktopTools.Native;
using DesktopTools.Localization;
namespace DesktopTools.Extras;

internal sealed partial class AutomationWindow
{
    private CancellationTokenSource? pickCancellation;
    private AutomationRecorder? recorder;
    private EscapeKeyService? recordEscape;
    private Window? recordHud;
    private HiddenWindowsScope? recordHidden;
    private AutomationScript? recordingScript;
    private bool isRecording;
    private async Task PickAsync(AutomationStep s,bool end)
    {
        if(service.IsActive)return;var pending=new CancellationTokenSource();pickCancellation=pending;service.SetRecording(true);
        try{
            using var hidden=new HiddenWindowsScope();var point=await PhysicalPointPicker.PickAsync(pending.Token);
            if(point!=null){Remember();if(end){s.EndX=(int)point.Value.X;s.EndY=(int)point.Value.Y;}else{s.X=(int)point.Value.X;s.Y=(int)point.Value.Y;}
                if(!end&&s.Kind is AutomationKind.IfPixel or AutomationKind.WaitPixel){await Task.Delay(150,pending.Token);var pixel=CaptureService.Capture(new MonitorInfo("Pixel",new Rect(s.X,s.Y,1,1),Rect.Empty,1,1));byte[] bytes=new byte[4];new System.Windows.Media.Imaging.FormatConvertedBitmap(pixel,PixelFormats.Bgra32,null,0).CopyPixels(bytes,4,0);s.Text=$"#{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}";}
            }
        }catch(OperationCanceledException){}catch(Exception e){ReportError(e.Message);}
        finally{pending.Dispose();if(ReferenceEquals(pickCancellation,pending))pickCancellation=null;service.SetRecording(false);if(!closed){RefreshSteps(s);Activate();}}
    }
    private async Task RecordAsync()
    {
        if(current==null||service.IsActive)return;
        isRecording=true;recordingScript=current;service.SetRecording(true);
        var pending=new CancellationTokenSource();pickCancellation=pending;
        try{
            recordEscape=new();recordEscape.Pressed+=StopRecording;
            if(!recordEscape.SetEnabled(true))throw new InvalidOperationException(L.T("Escape is unavailable. Automation was not started."));
            nint target=current.TargetWindow.Length>0?AutomationInput.FindWindow(current.TargetWindow):service.Target;
            if(current.TargetWindow.Length>0&&!AutomationInput.External(target))throw new InvalidOperationException(L.T("The target window is unavailable."));
            Hide();recordHidden=new HiddenWindowsScope();if(AutomationInput.External(target))NativeWindowService.RestoreForeground(target);
            recordHud=AutomationService.CreateHud(L.F($"Recording starts in {3} seconds"),StopRecording);recordHud.Show();
            for(int remaining=3;remaining>0;remaining--){if(recordHud is AutomationHudWindow countdown)countdown.Caption(L.F($"Recording starts in {remaining} seconds"));await Task.Delay(1000,pending.Token);}if(closed||!isRecording)return;
            recordHud.Close();recordHud=AutomationService.CreateHud(L.T("Recording actions"),StopRecording);recordHud.Show();
            recorder=new AutomationRecorder();recorder.StopRequested+=()=>Dispatcher.BeginInvoke(StopRecording);
        }catch(OperationCanceledException){}catch(Exception e){ReportError(e.Message);FinishRecording(false);}
        finally{pending.Dispose();if(ReferenceEquals(pickCancellation,pending))pickCancellation=null;}
    }
    private void StopRecording()=>FinishRecording(true);
    private void FinishRecording(bool report)
    {
        if(!isRecording)return;isRecording=false;pickCancellation?.Cancel();
        if(recorder!=null){
            recorder.Dispose();
            if(recordingScript!=null&&recorder.Steps.Count>0){
                if(recordingScript.Steps.Count+recorder.Steps.Count>500)ReportError(L.T("Recording is too large. Create a new workflow before recording."));
                else{Remember();recordingScript.Steps.AddRange(recorder.Steps);if(report)Report(L.T("Recorded actions added. Review them before running."));}
            }else if(report)Report(L.T("Automation stopped."));
            recorder=null;
        }
        recordHud?.Close();recordHud=null;recordEscape?.Dispose();recordEscape=null;recordHidden?.Dispose();recordHidden=null;recordingScript=null;
        service.SetRecording(false);if(!closed&&!closingWindow){Show();RefreshSteps();Activate();}
    }
}
