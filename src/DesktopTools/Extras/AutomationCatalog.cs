using DesktopTools.Core;
using DesktopTools.Localization;
using System.Windows.Input;
namespace DesktopTools.Extras;

internal static class AutomationCatalog
{
    internal sealed record Entry(AutomationKind Kind,string Label,string Group,string Help);
    internal static readonly Entry[] Entries=[
        new(AutomationKind.Click,"Click","Mouse and keyboard","Click a point selected on your screen."),
        new(AutomationKind.DoubleClick,"Double click","Mouse and keyboard","Click twice at the selected point."),
        new(AutomationKind.RightClick,"Right click","Mouse and keyboard","Open the context menu at a point."),
        new(AutomationKind.Move,"Move pointer","Mouse and keyboard","Move to a screen point without clicking."),
        new(AutomationKind.Scroll,"Scroll","Mouse and keyboard","Scroll up or down at the selected point."),
        new(AutomationKind.Drag,"Drag","Mouse and keyboard","Drag from the start point to the end point."),
        new(AutomationKind.Keys,"Press keys","Mouse and keyboard","Send a key or a shortcut to the target window."),
        new(AutomationKind.Text,"Type text","Mouse and keyboard","Type your text, including multiple lines."),
        new(AutomationKind.Launch,"Open app, file or URL","Apps and windows","Open a local file, application or website."),
        new(AutomationKind.FocusWindow,"Focus window","Apps and windows","Bring a matching window to the foreground."),
        new(AutomationKind.MaximizeWindow,"Maximize window","Apps and windows","Maximize a matching application window."),
        new(AutomationKind.MinimizeWindow,"Minimize window","Apps and windows","Minimize a matching application window."),
        new(AutomationKind.RestoreWindow,"Restore window","Apps and windows","Restore a minimized or maximized window."),
        new(AutomationKind.Wait,"Wait","Flow control","Pause for a fixed number of milliseconds."),
        new(AutomationKind.WaitWindow,"Wait for window","Flow control","Wait until a matching window becomes available."),
        new(AutomationKind.WaitPixel,"Wait for color","Flow control","Wait for a screen point to match a color."),
        new(AutomationKind.Repeat,"Repeat","Flow control","Repeat the actions inside this block."),
        new(AutomationKind.IfWindow,"If window exists","Flow control","Choose a branch based on an open window."),
        new(AutomationKind.IfPixel,"If pixel color","Flow control","Choose a branch based on a screen color."),
        new(AutomationKind.ClipboardText,"Set clipboard text","Clipboard","Replace clipboard contents with your text."),
        new(AutomationKind.PasteClipboard,"Paste clipboard","Clipboard","Paste the current clipboard into the target window.")
    ];
    internal static string Label(AutomationKind kind)=>L.T(Entries.FirstOrDefault(e=>e.Kind==kind)?.Label??kind switch{
        AutomationKind.EndRepeat=>"End repeat",AutomationKind.Else=>"Otherwise",AutomationKind.EndIf=>"End condition",
        AutomationKind.KeyDown=>"Key down",AutomationKind.KeyUp=>"Key up",AutomationKind.MouseDown=>"Mouse down",AutomationKind.MouseUp=>"Mouse up",_=>kind.ToString()});
    internal static string Help(AutomationKind kind)=>L.T(Entries.FirstOrDefault(e=>e.Kind==kind)?.Help??"Recorded input or control block boundary.");
    internal static string Summary(AutomationStep s)=>s.Kind switch{
        AutomationKind.Click or AutomationKind.RightClick or AutomationKind.DoubleClick or AutomationKind.Move or AutomationKind.MouseDown or AutomationKind.MouseUp=>$"({s.X}, {s.Y})",
        AutomationKind.Drag=>$"({s.X}, {s.Y}) → ({s.EndX}, {s.EndY})",
        AutomationKind.Scroll=>$"({s.X}, {s.Y}) · {s.Value}",
        AutomationKind.IfPixel or AutomationKind.WaitPixel=>$"({s.X}, {s.Y}) · {s.Text}",
        AutomationKind.Wait=>$"{s.Value} ms",AutomationKind.Repeat=>$"× {s.Value}",
        AutomationKind.KeyDown or AutomationKind.KeyUp=>KeyInterop.KeyFromVirtualKey(s.Value).ToString(),
        _=>s.Text.Replace("\r"," ").Replace("\n"," ") is var text&&text.Length>65?text[..65]+"…":text
    };
}
