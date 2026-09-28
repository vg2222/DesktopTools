using System.ComponentModel;
using System.Diagnostics;
using DesktopTools.Native;

namespace DesktopTools.Extras;

internal static class NoteAttachmentService
{
    internal static bool TryAttach(FloatingNote note, RecordingWindowInfo window, out string error)
    {
        error = "";
        if (window.Handle == 0 || window.ProcessId == 0 || string.IsNullOrWhiteSpace(window.Title) || !RecordingWindows.Available(window))
        {
            error = "The selected window is no longer available.";
            return false;
        }
        string processName;
        try
        {
            using var process = Process.GetProcessById(checked((int)window.ProcessId));
            processName = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
        {
            error = "DesktopTools cannot identify this window. Try another window.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(processName))
        {
            error = "DesktopTools cannot identify this window. Try another window.";
            return false;
        }
        note.AttachedProcess = processName;
        note.AttachedWindow = window.Title;
        return true;
    }
}
