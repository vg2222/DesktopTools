using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace DesktopTools.Core;

public enum AnnotationKind { Pen, Highlighter, Arrow, Line, Rectangle, Ellipse, Text, Redaction, Number }

public sealed record Annotation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AnnotationKind Kind { get; init; }
    public IReadOnlyList<Point> Points { get; init; } = [];
    public Color Color { get; init; } = System.Windows.Media.Color.FromRgb(40, 112, 255);
    public double Thickness { get; init; } = 3;
    public double ArrowHeadSize { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public string Text { get; init; } = "";
    public double FontSize { get; init; } = 24;
    public double TextWidth { get; init; }
    public string FontFamily { get; init; } = AnnotationTypography.DefaultFamily;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Filled { get; init; }
    public int Number { get; init; } = 1;
}

/// <summary>UI-thread document. History stores immutable annotation references, never bitmaps.</summary>
public sealed class AnnotationDocument
{
    private IReadOnlyList<Annotation> _items = Array.AsReadOnly(Array.Empty<Annotation>());
    private readonly List<IReadOnlyList<Annotation>> _undo = [];
    private readonly Stack<IReadOnlyList<Annotation>> _redo = new();
    public IReadOnlyList<Annotation> Items => _items;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public void Add(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (_items.Any(a => a.Id == annotation.Id)) throw new ArgumentException("Annotation ID already exists.", nameof(annotation));
        Commit(_items.Append(annotation with { Points = Array.AsReadOnly(annotation.Points.ToArray()) }).ToArray());
    }

    public void Remove(Guid id)
    {
        if (_items.Any(a => a.Id == id)) Commit(_items.Where(a => a.Id != id).ToArray());
    }

    public void Replace(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (!_items.Any(a => a.Id == annotation.Id)) return;
        var frozen = annotation with { Points = Array.AsReadOnly(annotation.Points.ToArray()) };
        Commit(_items.Select(a => a.Id == annotation.Id ? frozen : a).ToArray());
    }

    public void Clear() { if (_items.Count > 0) Commit([]); }
    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(_items); _items = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Changed?.Invoke();
    }
    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Add(_items); _items = _redo.Pop(); Changed?.Invoke();
    }
    public void Reset()
    {
        _items = Array.AsReadOnly(Array.Empty<Annotation>()); _undo.Clear(); _redo.Clear(); Changed?.Invoke();
    }
    private void Commit(Annotation[] items)
    {
        _undo.Add(_items);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear(); _items = Array.AsReadOnly(items); Changed?.Invoke();
    }
}
