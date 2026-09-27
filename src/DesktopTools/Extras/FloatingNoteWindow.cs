using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed class FloatingNoteWindow : Window
{
    private readonly TextBlock status = Ui.Text("", 11, muted: true);
    private readonly Button retry;
    private readonly TextBlock attachmentStatus = Ui.Text("", 11, muted: true);
    private readonly DispatcherTimer attachmentTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly FloatingNote note;
    private RecordingWindowInfo? attachedWindow;
    private NativeMethods.Rect? lastSourceBounds, lastNoteBounds;
    private int offsetX, offsetY;
    private bool removing;

    internal FloatingNoteWindow(FloatingNote note, bool topmost, Action openLibrary, Func<bool> flush)
    {
        this.note = note;
        Tag = "Floating notes"; Width = 410; Height = 380; MinWidth = 320; MinHeight = 260;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = topmost; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var editor = new NoteEditorView(); editor.TitleEditor.FontSize = 22; editor.SetNote(note);
        var layout = new DockPanel();
        var header = UtilityWindowChrome.Header(this, L.T("Note"), Close, L.T("Close"), 14);
        var options = UtilityWindowChrome.CaptionButton("More", L.T("Note options"), () => { });
        DockPanel.SetDock(options, Dock.Right); header.Children.Insert(1, options);
        var library = UtilityWindowChrome.CaptionButton("Notes", L.T("All notes"), openLibrary);
        DockPanel.SetDock(library, Dock.Right); header.Children.Insert(2, library);
        var menu = new ContextMenu();
        var pin = new MenuItem { Header = L.T("Always on top"), IsCheckable = true, IsChecked = Topmost };
        pin.Click += (_, _) => Topmost = pin.IsChecked; menu.Items.Add(pin);
        var sizes = new MenuItem { Header = L.T("Text size") };
        foreach (double size in new[] { 12d, 15d, 18d, 22d })
        {
            var choice = new MenuItem { Header = L.F($"{size:0} px"), IsCheckable = true, IsChecked = size == editor.BodyEditor.FontSize };
            choice.Click += (_, _) => { editor.BodyEditor.FontSize = size; foreach (MenuItem item in sizes.Items) item.IsChecked = item == choice; };
            sizes.Items.Add(choice);
        }
        menu.Items.Add(sizes);
        var attach = new MenuItem { Header = L.T("Attach to window") };
        attach.SubmenuOpened += (_, _) =>
        {
            attach.Items.Clear();
            foreach (var candidate in RecordingWindows.GetAll().Take(40))
            {
                var window = candidate;
                var choice = new MenuItem { Header = window.Title, IsCheckable = true,
                    IsChecked = attachedWindow?.Handle == window.Handle };
                choice.Click += (_, _) => Attach(window);
                attach.Items.Add(choice);
            }
            if (attach.Items.Count == 0) attach.Items.Add(new MenuItem { Header = L.T("No open windows"), IsEnabled = false });
        };
        menu.Items.Add(new Separator()); menu.Items.Add(attach);
        var detach = new MenuItem { Header = L.T("Detach from window") };
        detach.Click += (_, _) => Detach(); menu.Items.Add(detach);
        menu.Opened += (_, _) => detach.IsEnabled = note.AttachedWindow != null;
        var wrap = new MenuItem { Header = L.T("Wrap text"), IsCheckable = true, IsChecked = true };
        wrap.Click += (_, _) =>
        {
            editor.BodyEditor.TextWrapping = wrap.IsChecked ? TextWrapping.Wrap : TextWrapping.NoWrap;
            editor.BodyEditor.HorizontalScrollBarVisibility = wrap.IsChecked ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        };
        menu.Items.Add(wrap); options.ContextMenu = menu;
        options.Click += (_, _) => { menu.PlacementTarget = options; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(10, 12, 10, 0) };
        retry = Ui.Button(L.T("Retry saving"), () => { flush(); }); retry.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(retry, Dock.Right); footer.Children.Add(retry);
        var footerText = new StackPanel(); footerText.Children.Add(attachmentStatus); footerText.Children.Add(status); footer.Children.Add(footerText);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(editor);
        var card = Ui.Card(layout, 16); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        void UpdateTitle(object? sender, PropertyChangedEventArgs e) => Title = string.IsNullOrWhiteSpace(note.Title) ? L.T("Note · DesktopTools") : note.Title + " · DesktopTools";
        UpdateTitle(null, new(nameof(FloatingNote.Title))); note.PropertyChanged += UpdateTitle;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { flush(); e.Handled = true; } };
        Closing += (_, e) => { if (!removing && !flush()) e.Cancel = true; };
        Closed += (_, _) => { menu.IsOpen = false; editor.Dispose(); note.PropertyChanged -= UpdateTitle; };
        attachmentTimer.Tick += (_, _) => UpdateAttachment();
        note.PropertyChanged += AttachmentChanged;
        Loaded += (_, _) => { editor.BodyEditor.Focus(); UpdateAttachment(); attachmentTimer.Start(); };
        Closed += (_, _) => { attachmentTimer.Stop(); note.PropertyChanged -= AttachmentChanged; };
        Motion.WindowEntrance(this);
    }
    private static string? ProcessName(uint id)
    {
        try { using var process = Process.GetProcessById(checked((int)id)); return process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or OverflowException) { return null; }
    }
    private void Attach(RecordingWindowInfo window)
    {
        string? process = ProcessName(window.ProcessId);
        if (process == null) return;
        note.AttachedProcess = process; note.AttachedWindow = window.Title;
        attachedWindow = window; lastSourceBounds = lastNoteBounds = null;
        UpdateAttachment(placeBesideWindow: true);
    }
    private void Detach()
    {
        note.AttachedWindow = null; note.AttachedProcess = null;
        attachedWindow = null; lastSourceBounds = lastNoteBounds = null;
        attachmentStatus.Text = "";
        if (!IsVisible) Show();
    }
    private void AttachmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(FloatingNote.AttachedProcess) or nameof(FloatingNote.AttachedWindow))) return;
        if (note.AttachedProcess == null || note.AttachedWindow == null) { attachedWindow = null; attachmentStatus.Text = ""; if (!IsVisible) Show(); }
        else if (attachedWindow?.Title != note.AttachedWindow || ProcessName(attachedWindow.ProcessId) != note.AttachedProcess)
            { attachedWindow = null; lastSourceBounds = lastNoteBounds = null; }
    }
    private void UpdateAttachment(bool placeBesideWindow = false)
    {
        if (note.AttachedProcess == null || note.AttachedWindow == null) return;
        if (attachedWindow == null || !RecordingWindows.IsSameWindow(attachedWindow))
        {
            var matches = RecordingWindows.GetAll().Where(w => w.Title == note.AttachedWindow && ProcessName(w.ProcessId) == note.AttachedProcess).Take(2).ToArray();
            attachedWindow = matches.Length == 1 ? matches[0] : null;
            lastSourceBounds = lastNoteBounds = null;
            placeBesideWindow = true;
        }
        if (attachedWindow == null || !RecordingWindows.Available(attachedWindow) ||
            !NativeMethods.GetWindowRect(attachedWindow.Handle, out var sourceBounds))
        {
            attachmentStatus.Text = L.T("Attached window is unavailable");
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible) { ShowActivated = false; Show(); }
        attachmentStatus.Text = L.T("Attached to") + " " + note.AttachedWindow;
        nint ownHandle = new WindowInteropHelper(this).Handle;
        if (ownHandle == 0 || !NativeMethods.GetWindowRect(ownHandle, out var noteBounds)) return;
        if (placeBesideWindow || lastSourceBounds == null)
        {
            var monitor = MonitorService.GetAll().FirstOrDefault(m => m.Bounds.Contains(new Point(sourceBounds.Left, sourceBounds.Top))) ?? MonitorService.GetForWindow(this);
            int width = noteBounds.Right - noteBounds.Left;
            int desiredX = sourceBounds.Right + 12;
            if (desiredX + width > monitor.WorkingArea.Right) desiredX = sourceBounds.Left - width - 12;
            desiredX = Math.Clamp(desiredX, (int)monitor.WorkingArea.Left, Math.Max((int)monitor.WorkingArea.Left, (int)monitor.WorkingArea.Right - width));
            offsetX = desiredX - sourceBounds.Left; offsetY = Math.Max((int)monitor.WorkingArea.Top, sourceBounds.Top) - sourceBounds.Top;
        }
        else if (lastSourceBounds.Value.Left == sourceBounds.Left && lastSourceBounds.Value.Top == sourceBounds.Top && lastNoteBounds is { } oldNote &&
                 (oldNote.Left != noteBounds.Left || oldNote.Top != noteBounds.Top))
        {
            // The user dragged the note; preserve their chosen offset on later window moves.
            offsetX = noteBounds.Left - sourceBounds.Left; offsetY = noteBounds.Top - sourceBounds.Top;
        }
        int x = sourceBounds.Left + offsetX, y = sourceBounds.Top + offsetY;
        if (x != noteBounds.Left || y != noteBounds.Top)
            NativeMethods.SetWindowPos(ownHandle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
        lastSourceBounds = sourceBounds;
        lastNoteBounds = new NativeMethods.Rect { Left = x, Top = y, Right = x + noteBounds.Right - noteBounds.Left, Bottom = y + noteBounds.Bottom - noteBounds.Top };
    }
    internal void SetSaved(bool saved, bool failed = false)
    {
        status.Text = L.T(saved ? "Saved on this device" : failed ? "Could not save your changes" : "Saving…");
        retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void CloseForRemoval() { removing = true; Close(); }
}
