using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>Plain-text editor shared by the library and floating notes.</summary>
internal sealed class NoteEditorView : Grid, IDisposable
{
    internal TextBox TitleEditor { get; } = Editor("NoteTitle", 24, false);
    internal TextBox BodyEditor { get; } = Editor("NoteBody", 15, true);
    private readonly TextBlock count = Ui.Text("", 11, muted: true);
    private FloatingNote? note;
    private bool updating;

    internal NoteEditorView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TitleEditor.MaxLength = NotesStore.MaximumTitleLength; TitleEditor.FontWeight = FontWeights.SemiBold;
        BodyEditor.MaxLength = NotesStore.MaximumBodyLength;
        AutomationProperties.SetName(TitleEditor, L.T("Note title"));
        AutomationProperties.SetName(BodyEditor, L.T("Note text"));
        var heading = Placeholder(TitleEditor, L.T("Untitled note")); heading.Margin = new Thickness(0, 0, 0, 10); Children.Add(heading);
        var text = Placeholder(BodyEditor, L.T("Write a note…")); Grid.SetRow(text, 1); Children.Add(text);
        count.Margin = new Thickness(10, 10, 0, 0); Grid.SetRow(count, 2); Children.Add(count);
        TitleEditor.TextChanged += (_, _) => Write(true);
        BodyEditor.TextChanged += (_, _) => Write(false);
    }
    internal void SetNote(FloatingNote? value)
    {
        if (ReferenceEquals(note, value)) return;
        if (note != null) note.PropertyChanged -= Changed;
        note = value; updating = true;
        try
        {
            TitleEditor.Text = value?.Title ?? ""; BodyEditor.Text = value?.Body ?? "";
            ClearUndo(TitleEditor); ClearUndo(BodyEditor);
            TitleEditor.Select(0, 0); BodyEditor.Select(0, 0); BodyEditor.ScrollToHome();
        }
        finally { updating = false; }
        if (note != null) note.PropertyChanged += Changed;
        Count();
    }
    private void Write(bool title)
    {
        if (updating || note == null) return;
        updating = true;
        try { if (title) note.Title = TitleEditor.Text; else note.Body = BodyEditor.Text; }
        finally { updating = false; }
        Count();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (updating || note == null) return;
        updating = true;
        try { Synchronize(TitleEditor, note.Title); Synchronize(BodyEditor, note.Body); }
        finally { updating = false; }
        Count();
    }
    private static void Synchronize(TextBox editor, string value)
    {
        if (editor.Text == value) return;
        int start = editor.SelectionStart, length = editor.SelectionLength; double offset = editor.VerticalOffset;
        editor.Text = value;
        // An edit from another view must never become an undo into another note or stale version.
        ClearUndo(editor); start = Math.Min(start, value.Length);
        editor.Select(start, Math.Min(length, value.Length - start)); editor.ScrollToVerticalOffset(offset);
    }
    private static void ClearUndo(TextBox editor)
    {
        bool enabled = editor.IsUndoEnabled; editor.IsUndoEnabled = false; editor.IsUndoEnabled = enabled;
    }
    private void Count() => count.Text = L.F($"{BodyEditor.Text.Length:N0} characters");
    private static TextBox Editor(string name, double size, bool multiline)
    {
        var editor = new TextBox
        {
            Name = name, FontSize = size, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8), AcceptsReturn = multiline, AcceptsTab = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, UndoLimit = 100
        };
        // TextBoxView applies TextBox.Padding itself. Padding the host as well
        // shifts the caret/text away from the placeholder and reduces the hit area.
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FocusableProperty, false);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, new TemplateBindingExtension(TextBoxBase.VerticalScrollBarVisibilityProperty));
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, new TemplateBindingExtension(TextBoxBase.HorizontalScrollBarVisibilityProperty));
        editor.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = host };
        editor.SetResourceReference(Control.FontFamilyProperty, "BodyFont");
        editor.SetResourceReference(Control.ForegroundProperty, "Text"); editor.SetResourceReference(TextBoxBase.CaretBrushProperty, "Text");
        return editor;
    }
    private static Grid Placeholder(TextBox editor, string text)
    {
        var grid = new Grid(); grid.Children.Add(editor);
        var hint = Ui.Text(text, editor.FontSize, editor.FontWeight == FontWeights.SemiBold, muted: true);
        hint.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding(nameof(editor.FontSize)) { Source = editor });
        hint.SetBinding(TextBlock.FontFamilyProperty, new System.Windows.Data.Binding(nameof(editor.FontFamily)) { Source = editor });
        hint.SetBinding(TextBlock.FontWeightProperty, new System.Windows.Data.Binding(nameof(editor.FontWeight)) { Source = editor });
        // Match TextBoxView's native two-DIP leading inset as well as its padding.
        hint.Margin = new Thickness(editor.Padding.Left + 2, editor.Padding.Top, editor.Padding.Right, editor.Padding.Bottom);
        hint.IsHitTestVisible = false; hint.VerticalAlignment = VerticalAlignment.Top;
        void Update() => hint.Visibility = editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        editor.TextChanged += (_, _) => Update(); Update(); grid.Children.Add(hint); return grid;
    }
    public void Dispose() { if (note != null) note.PropertyChanged -= Changed; }
}
