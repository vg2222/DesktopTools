using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace DesktopTools.Native;

/// <summary>A desktop window and its visible frame in physical screen pixels.</summary>
internal readonly record struct SmartRegionWindow(nint Handle, Rect Bounds);

/// <summary>
/// Suggests visible rectangular UI regions without reading their text or image content.
/// FindElement uses third-party UI Automation providers and must be called off the UI thread.
/// </summary>
internal static class SmartRegionDetector
{
    private const int MaxDepth = 12;
    private const int MaxScanned = 768;
    private const int MaxContaining = 64;
    private const int MaxSiblings = 512;
    private const double MinimumWidth = 64;
    private const double MinimumHeight = 48;

    private delegate bool WindowCallback(nint handle, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint handle, StringBuilder name, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect bounds);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(nint handle, int attribute, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(nint handle, int attribute, out NativeRect value, int size);

    internal static SmartRegionWindow? FindWindow(Point physicalPointer, Rect captureBounds)
    {
        if (!Valid(captureBounds) || !captureBounds.Contains(physicalPointer)) return null;
        return ChooseWindow(EnumerateVisibleWindows(captureBounds, physicalPointer), physicalPointer);
    }

    /// <summary>Capture external window geometry in z-order for a frozen selection frame.</summary>
    internal static IReadOnlyList<SmartRegionWindow> SnapshotWindows(Rect captureBounds) =>
        EnumerateVisibleWindows(captureBounds, null);

    /// <summary>Hit-test a window snapshot without querying changing desktop geometry.</summary>
    internal static SmartRegionWindow? ChooseWindow(IReadOnlyList<SmartRegionWindow> windows, Point physicalPointer)
    {
        foreach (var window in windows)
            if (window.Handle != 0 && window.Bounds.Contains(physicalPointer)) return window;
        return null;
    }

    private static IReadOnlyList<SmartRegionWindow> EnumerateVisibleWindows(Rect captureBounds, Point? stopAt)
    {
        if (!Valid(captureBounds)) return [];
        var windows = new List<SmartRegionWindow>();
        EnumWindows((handle, _) =>
        {
            if (!TryReadWindow(handle, captureBounds, out var window)) return true;
            if (stopAt is Point point && !window.Bounds.Contains(point)) return true;
            windows.Add(window);
            return stopAt == null; // A live hit needs only the first window in z-order.
        }, 0);
        return windows;
    }

    private static bool TryReadWindow(nint handle, Rect captureBounds, out SmartRegionWindow window)
    {
        window = default;
        if (!IsWindowVisible(handle) || IsIconic(handle)) return false;
        GetWindowThreadProcessId(handle, out uint processId);
        if (processId == Environment.ProcessId) return false;
        if (DwmGetWindowAttributeInt(handle, 14, out int cloaked, sizeof(int)) >= 0 && cloaked != 0) return false;

        // Desktop host windows are behind every app and would suggest the
        // entire display when the pointer is over an empty desktop area.
        var name = new StringBuilder(64);
        GetClassName(handle, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW") return false;

        if (!TryGetFrame(handle, out var frame)) return false;
        Rect visible = Rect.Intersect(frame, captureBounds);
        if (!Useful(visible)) return false;
        window = new SmartRegionWindow(handle, visible);
        return true;
    }

    internal static Rect? FindElement(nint handle, Point physicalPointer, Rect captureBounds, Rect windowBounds)
    {
        if (handle == 0 || !windowBounds.Contains(physicalPointer)) return null;
        try
        {
            var root = AutomationElement.FromHandle(handle);
            if (root == null) return null;
            var walker = TreeWalker.ControlViewWalker;
            IEnumerable<AutomationElement> Children(AutomationElement element)
            {
                AutomationElement? child = walker.GetFirstChild(element);
                while (child != null)
                {
                    yield return child;
                    child = walker.GetNextSibling(child);
                }
            }
            return ChooseFromTree(root, physicalPointer, captureBounds, windowBounds,
                element =>
                {
                    var current = element.Current;
                    return (current.BoundingRectangle, current.IsOffscreen, IsRegionControl(current.ControlType));
                }, Children);
        }
        catch (Exception)
        {
            // Stale, elevated, or unsupported providers must not break capture.
            // The caller keeps the visible-window suggestion as a fallback.
            return null;
        }
    }

    private static bool IsRegionControl(ControlType type) =>
        type == ControlType.Pane || type == ControlType.Group || type == ControlType.Image ||
        type == ControlType.Custom || type == ControlType.Document || type == ControlType.Window;

    /// <summary>Inspect more siblings without letting off-pointer nodes consume the hit-path budget.</summary>
    internal static Rect? ChooseFromTree<T>(T root, Point pointer, Rect captureBounds, Rect windowBounds,
        Func<T, (Rect Bounds, bool Offscreen, bool Candidate)> inspect, Func<T, IEnumerable<T>> children) where T : class
    {
        if (!captureBounds.Contains(pointer) || !windowBounds.Contains(pointer)) return null;
        var candidates = new List<Rect>();
        int scanned = 0, containing = 0;
        void Visit(T element, int depth)
        {
            if (scanned >= MaxScanned || containing >= MaxContaining) return;
            scanned++;
            var current = inspect(element);
            if (current.Offscreen || !Valid(current.Bounds) || !current.Bounds.Contains(pointer)) return;
            containing++;
            if (depth > 0 && current.Candidate) candidates.Add(current.Bounds);
            if (depth >= MaxDepth) return;
            int siblings = 0;
            foreach (var child in children(element))
            {
                if (siblings++ >= MaxSiblings || scanned >= MaxScanned || containing >= MaxContaining) break;
                Visit(child, depth + 1);
            }
        }
        Visit(root, 0);
        return ChooseRegion(candidates, pointer, captureBounds, windowBounds);
    }

    /// <summary>Pure physical-pixel filtering shared by UIA and deterministic tests.</summary>
    internal static Rect? ChooseRegion(IEnumerable<Rect> candidates, Point pointer, Rect captureBounds, Rect windowBounds)
    {
        if (!Valid(captureBounds) || !Valid(windowBounds) || !captureBounds.Contains(pointer)) return null;
        Rect visibleWindow = Rect.Intersect(captureBounds, windowBounds);
        if (!Useful(visibleWindow) || !visibleWindow.Contains(pointer)) return null;
        Rect? best = null;
        double bestArea = double.PositiveInfinity;
        foreach (Rect candidate in candidates)
        {
            if (!Valid(candidate) || !candidate.Contains(pointer)) continue;
            Rect clipped = Rect.Intersect(candidate, visibleWindow);
            if (!Useful(clipped) || clipped == visibleWindow) continue;
            double area = clipped.Width * clipped.Height;
            if (area >= bestArea) continue;
            best = clipped;
            bestArea = area;
        }
        return best;
    }

    private static bool TryGetFrame(nint handle, out Rect frame)
    {
        frame = Rect.Empty;
        if (DwmGetWindowAttributeRect(handle, 9, out NativeRect native, Marshal.SizeOf<NativeRect>()) < 0 ||
            native.Right <= native.Left || native.Bottom <= native.Top)
        {
            if (!GetWindowRect(handle, out native)) return false;
        }
        if (native.Right <= native.Left || native.Bottom <= native.Top) return false;
        frame = new Rect(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
        return true;
    }

    private static bool Valid(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 &&
        double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
        double.IsFinite(rect.Width) && double.IsFinite(rect.Height);
    private static bool Useful(Rect rect) => Valid(rect) && rect.Width >= MinimumWidth && rect.Height >= MinimumHeight;
}
