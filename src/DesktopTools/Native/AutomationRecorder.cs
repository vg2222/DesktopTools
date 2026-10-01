using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopTools.Core;
namespace DesktopTools.Native;
/// <summary>Temporary explicit recording only. Callbacks never suppress events or write files.</summary>
internal sealed class AutomationRecorder : IDisposable
{
    private delegate nint Hook(int code,nint message,nint data);
    [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookEx(int kind,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? module);
    [StructLayout(LayoutKind.Sequential)]private struct KeyEvent {public uint Key,Scan,Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Sequential)]private struct MouseEvent {public int X,Y;public uint Data,Flags,Time;public nuint Extra;}
    private readonly Hook keyboardCallback,mouseCallback;
    private nint keyboard,mouse,lastWindow;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private long lastTime,lastMove;
    private int lastX,lastY;
    private readonly HashSet<uint> held=[];
    private readonly HashSet<int> buttons=[];
    internal bool HasHooks => keyboard != 0 && mouse != 0;
    internal List<AutomationStep> Steps {get;}=[];
    internal event Action? StopRequested;
    internal AutomationRecorder()
    {
        keyboardCallback=Keyboard;mouseCallback=Mouse;
        keyboard=SetWindowsHookEx(13,keyboardCallback,GetModuleHandle(null),0);
        mouse=SetWindowsHookEx(14,mouseCallback,GetModuleHandle(null),0);
        if(keyboard==0||mouse==0){Dispose();throw new System.ComponentModel.Win32Exception();}
    }
    private void Add(AutomationStep step)
    {
        if(Steps.Count>=490){StopRequested?.Invoke();return;}
        nint target=NativeWindowService.GetForegroundWindowHandle();
        if(!AutomationInput.External(target))return;
        if(lastWindow!=target){lastWindow=target;string title=AutomationInput.Title(target);if(title.Length>0)Steps.Add(new(){Kind=AutomationKind.FocusWindow,Text=title});}
        long elapsed=clock.ElapsedMilliseconds;
        if(elapsed-lastTime>=60)Steps.Add(new(){Kind=AutomationKind.Wait,Value=(int)Math.Min(600000,elapsed-lastTime)});
        if(step.Kind is AutomationKind.MouseDown or AutomationKind.MouseUp or AutomationKind.Move or AutomationKind.Scroll){lastX=step.X;lastY=step.Y;}
        lastTime=elapsed;Steps.Add(step);
    }
    private nint Keyboard(int code,nint message,nint data)
    {
        if(code>=0){var e=Marshal.PtrToStructure<KeyEvent>(data);int m=message.ToInt32();
            if((e.Flags&0x10)==0){
                if(e.Key==27 && m is 0x100 or 0x104)StopRequested?.Invoke();
                else if(e.Key!=27){
                    if(m is 0x100 or 0x104 && held.Add(e.Key))Add(new(){Kind=AutomationKind.KeyDown,Value=(int)e.Key});
                    else if(m is 0x101 or 0x105 && held.Remove(e.Key))Add(new(){Kind=AutomationKind.KeyUp,Value=(int)e.Key});
                }
            }
        }
        return CallNextHookEx(0,code,message,data);
    }
    private nint Mouse(int code,nint message,nint data)
    {
        if(code>=0){var e=Marshal.PtrToStructure<MouseEvent>(data);int m=message.ToInt32();
            if((e.Flags&1)==0){
                nint hit=AutomationInput.GetAncestor(AutomationInput.WindowFromPoint(new(){X=e.X,Y=e.Y}),2);
                if(!AutomationInput.External(hit))return CallNextHookEx(0,code,message,data);
                int button=m switch{0x201 or 0x202=>1,0x204 or 0x205=>2,0x207 or 0x208=>3,_=>0};
                if(button!=0){bool down=m is 0x201 or 0x204 or 0x207;if(down)buttons.Add(button);else buttons.Remove(button);Add(new(){Kind=down?AutomationKind.MouseDown:AutomationKind.MouseUp,Value=button,X=e.X,Y=e.Y});}
                else if(m==0x20A)Add(new(){Kind=AutomationKind.Scroll,Value=unchecked((short)(e.Data>>16)),X=e.X,Y=e.Y});
                else if(m==0x200 && buttons.Count>0 && clock.ElapsedMilliseconds-lastMove>35){lastMove=clock.ElapsedMilliseconds;Add(new(){Kind=AutomationKind.Move,X=e.X,Y=e.Y});}
            }
        }
        return CallNextHookEx(0,code,message,data);
    }
    public void Dispose()
    {
        if(keyboard!=0){UnhookWindowsHookEx(keyboard);keyboard=0;}
        if(mouse!=0){UnhookWindowsHookEx(mouse);mouse=0;}
        foreach(uint key in held)Steps.Add(new(){Kind=AutomationKind.KeyUp,Value=(int)key});
        foreach(int button in buttons)Steps.Add(new(){Kind=AutomationKind.MouseUp,Value=button,X=lastX,Y=lastY});
        held.Clear();buttons.Clear();GC.KeepAlive(keyboardCallback);GC.KeepAlive(mouseCallback);
    }
}
