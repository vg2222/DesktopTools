using System.Windows;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.UI;

namespace DesktopTools.Extras;

public sealed class ScreenshotEditDocument
{
    private sealed record State(IReadOnlyList<Annotation> Items, Int32Rect Crop);
    private readonly BitmapSource _source;
    private State _state;
    private readonly List<State> _undo = [];
    private readonly Stack<State> _redo = new();
    public IReadOnlyList<Annotation> Items => _state.Items;
    public Int32Rect Crop => _state.Crop;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public ScreenshotEditDocument(BitmapSource image)
    {
        _source = image; _state = new(Array.AsReadOnly(Array.Empty<Annotation>()), new(0, 0, image.PixelWidth, image.PixelHeight));
    }
    public void Add(Annotation item) => Commit(new(Array.AsReadOnly(Items.Append(item with { Points = Array.AsReadOnly(item.Points.ToArray()) }).ToArray()), Crop));
    public void Remove(IReadOnlyCollection<Guid> ids)
    {
        if (Items.Any(a => ids.Contains(a.Id))) Commit(new(Array.AsReadOnly(Items.Where(a => !ids.Contains(a.Id)).ToArray()), Crop));
    }
    public void Replace(Annotation item)
    {
        if (!Items.Any(a => a.Id == item.Id)) return;
        var frozen = item with { Points = Array.AsReadOnly(item.Points.ToArray()) };
        Commit(new(Array.AsReadOnly(Items.Select(a => a.Id == item.Id ? frozen : a).ToArray()), Crop));
    }
    public void Clear() { if (Items.Count > 0) Commit(new(Array.AsReadOnly(Array.Empty<Annotation>()), Crop)); }
    public void SetCrop(Int32Rect crop)
    {
        if (crop.Width < 1 || crop.Height < 1 || crop.X < 0 || crop.Y < 0 || (long)crop.X + crop.Width > _source.PixelWidth || (long)crop.Y + crop.Height > _source.PixelHeight) throw new ArgumentOutOfRangeException(nameof(crop));
        if (crop != Crop) Commit(new(Items, crop));
    }
    public void Undo() { if (!CanUndo) return; _redo.Push(_state); _state = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); }
    public void Redo() { if (!CanRedo) return; _undo.Add(_state); _state = _redo.Pop(); }
    private void Commit(State state) { _undo.Add(_state); if (_undo.Count > 200) _undo.RemoveAt(0); _redo.Clear(); _state = state; }
    public BitmapSource Export()
    {
        var composed = AnnotationRenderer.Composite(_source, Items.Where(a => a.Kind != AnnotationKind.Redaction), 1, 1, Crop);
        var covers = Items.Where(a => a.Kind == AnnotationKind.Redaction && a.Points.Count >= 2).Select(a =>
        {
            var rect = new Rect(a.Points[0], a.Points[^1]);
            int x = (int)Math.Floor(rect.Left) - Crop.X, y = (int)Math.Floor(rect.Top) - Crop.Y;
            return new Int32Rect(x, y, (int)Math.Ceiling(rect.Right) - Crop.X - x, (int)Math.Ceiling(rect.Bottom) - Crop.Y - y);
        }).ToArray();
        return RedactionRenderer.Apply(composed, covers);
    }
}
