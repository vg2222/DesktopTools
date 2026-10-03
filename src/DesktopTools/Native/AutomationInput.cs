using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using DesktopTools.Localization;
namespace DesktopTools.Native;
internal sealed class AutomationInput : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Input {public uint Type; public Union Data;}
    [StructLayout(LayoutKind.Explicit)] private struct Union {[FieldOffset(0)]public Mouse Mouse;[FieldOffset(0)]public Keyboard Key;}
    [StructLayout(LayoutKind.Sequential)] private struct Mouse {public int X,Y;public uint Data,Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard {public ushort Key,Scan;public uint Flags,Time;public nuint Extra;}
    [DllImport("user32.dll",SetLastError=true)]private static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")]internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern bool EnumWindows(WindowCallback callback,nint data);
    private delegate bool WindowCallback(nint hwnd,nint data);
    [DllImport("user32.dll")]internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")]internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")]internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")]internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll",EntryPoint="ShowWindowAsync")]internal static extern bool SetWindowState(nint hwnd,int state);
    [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(nint hwnd,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(nint hwnd,StringBuilder title,int count);
    [DllImport("user32.dll")]internal static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll")]private static extern bool GetCursorPos(out PointI point);
    [StructLayout(LayoutKind.Sequential)]private struct PointI{public int X,Y;}
    [DllImport("user32.dll")]internal static extern nint WindowFromPoint(PointI64 point);
    [StructLayout(LayoutKind.Sequential)]internal struct PointI64 { public int X,Y; }
    private readonly HashSet<ushort> keys=[];
    private readonly HashSet<int> buttons=[];
    internal static Point Pointer(){if(!GetCursorPos(out var p))throw new Win32Exception();return new(p.X,p.Y);}
    internal static bool External(nint hwnd)=>hwnd!=0 && IsWindow(hwnd) && GetWindowThreadProcessId(hwnd,out uint pid)!=0 && pid!=(uint)Environment.ProcessId;
    internal static string Title(nint hwnd){var b=new StringBuilder(1024);GetWindowText(hwnd,b,b.Capacity);return b.ToString();}
    internal static nint FindWindow(string title,bool includeMinimized=false)
    {
        if(string.IsNullOrWhiteSpace(title))return 0;
        nint result=0;EnumWindows((h,_)=>{if(External(h)&&IsWindowVisible(h)&&(includeMinimized||!IsIconic(h))&&Title(h).Contains(title,StringComparison.OrdinalIgnoreCase)){result=h;return false;}return true;},0);return result;
    }
    private static void Send(params Input[] inputs){if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>())!=inputs.Length)throw new Win32Exception(Marshal.GetLastWin32Error(),L.T("Windows could not send input. Check the target application's permissions."));}
    internal void Move(int x,int y)
    {
        var bounds=MonitorService.GetVirtualDesktop().Bounds;
        if(!bounds.Contains(new Point(x,y)))throw new ArgumentException(L.T("The action point is outside the connected displays."));
        Send(new Input{Data=new Union{Mouse=new Mouse{X=(int)Math.Round((x-bounds.X)*65535/Math.Max(1,bounds.Width-1)),Y=(int)Math.Round((y-bounds.Y)*65535/Math.Max(1,bounds.Height-1)),Flags=0x8000|0x4000|1}}});
    }
    internal void Button(int button,bool down)
    {
        uint flags=button switch{1=>down?2u:4u,2=>down?8u:16u,3=>down?32u:64u,_=>throw new ArgumentException()};
        if(down)buttons.Add(button);
        Send(new Input{Data=new Union{Mouse=new Mouse{Flags=flags}}});
        if(!down)buttons.Remove(button);
    }
    internal void Wheel(int value)=>Send(new Input{Data=new Union{Mouse=new Mouse{Data=unchecked((uint)value),Flags=0x800}}});
    internal void Key(ushort key,bool down)
    {
        if(down)keys.Add(key);
        uint extended=key is 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 45 or 46 or 91 or 92 or 163 or 165 ? 1u:0u;
        Send(new Input{Type=1,Data=new Union{Key=new Keyboard{Key=key,Flags=extended|(down?0u:2u)}}});
        if(!down)keys.Remove(key);
    }
    internal void Unicode(char c)=>Send(new Input{Type=1,Data=new Union{Key=new Keyboard{Scan=c,Flags=4}}},new Input{Type=1,Data=new Union{Key=new Keyboard{Scan=c,Flags=6}}});
    internal static ushort[] ParseKeys(string text)
    {
        var result=new List<ushort>();
        foreach(string raw in text.Split('+'))
        {
            string s=raw.Trim().ToUpperInvariant();
            ushort key=s switch{"CTRL" or "CONTROL"=>17,"ALT"=>18,"SHIFT"=>16,"WIN"=>91,"ENTER"=>13,"TAB"=>9,"SPACE"=>32,"BACKSPACE"=>8,"DELETE"=>46,"INSERT"=>45,"HOME"=>36,"END"=>35,"PAGEUP"=>33,"PAGEDOWN"=>34,"LEFT"=>37,"UP"=>38,"RIGHT"=>39,"DOWN"=>40,_=>0};
            if(key==0 && s.Length==1 && char.IsAsciiLetterOrDigit(s[0]))key=s[0];
            if(key==0 && s.StartsWith('F') && int.TryParse(s.AsSpan(1),out int f) && f is >=1 and <=24)key=(ushort)(111+f);
            if(key==0&&Enum.TryParse<System.Windows.Input.Key>(raw.Trim(),true,out var named)&&Enum.IsDefined(named)){int vk=System.Windows.Input.KeyInterop.VirtualKeyFromKey(named);if(vk is >0 and <255)key=(ushort)vk;}
            if(key==0 || key==27 || result.Contains(key))throw new ArgumentException(L.T("Invalid key combination."));
            result.Add(key);
        }
        if(result.Count>5)throw new ArgumentException(L.T("Invalid key combination."));
        return result.ToArray();
    }
    public void Dispose()
    {
        foreach(var key in keys.ToArray())try{Key(key,false);}catch{}
        foreach(var button in buttons.ToArray())try{Button(button,false);}catch{}
    }
}
