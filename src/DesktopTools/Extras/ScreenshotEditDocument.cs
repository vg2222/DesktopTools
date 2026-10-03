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
    private State? _previewState;
    private BitmapSource? _preview;
    public long Revision { get; private set; }
    public IReadOnlyList<Annotation> Items => _state.Items;
    public Int32Rect Crop => _state.Crop;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public ScreenshotEditDocument(BitmapSource image)
    {
        _source = image; _state = new(Array.AsReadOnly(Array.Empty<Annotation>()), new(0, 0, image.PixelWidth, image.PixelHeight));
    }
    public void Add(Annotation item) => Commit(new(Array.AsReadOnly(Items.Append(item with { Points = Array.AsReadOnly(item.Points.ToArray()) }).ToArray()), Crop));
    public void AddRange(IReadOnlyList<Annotation> items)
    {
        if (items.Count == 0) return;
        Commit(new(Array.AsReadOnly(Items.Concat(items.Select(item => item with { Points = Array.AsReadOnly(item.Points.ToArray()) })).ToArray()), Crop));
    }
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
    public void Undo() { if (!CanUndo) return; _redo.Push(_state); _state = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Revision++; }
    public void Redo() { if (!CanRedo) return; _undo.Add(_state); _state = _redo.Pop(); Revision++; }
    private void Commit(State state) { _undo.Add(_state); if (_undo.Count > 200) _undo.RemoveAt(0); _redo.Clear(); _state = state; Revision++; }
    public BitmapSource Export()
    {
        // Flatten at source coordinates before cropping so blur samples and
        // pixelation cells remain identical to the reviewed preview.
        var preview = RenderPreview();
        if (Crop == new Int32Rect(0, 0, _source.PixelWidth, _source.PixelHeight)) return preview;
        var result = new CroppedBitmap(preview, Crop); result.Freeze(); return result;
    }
    public BitmapSource RenderPreview(Annotation? pending = null, IReadOnlyCollection<Guid>? hidden = null)
    {
        if (pending == null && (hidden == null || hidden.Count == 0))
        {
            if (!ReferenceEquals(_previewState, _state)) { _preview = Render(Items, new(0, 0, _source.PixelWidth, _source.PixelHeight)); _previewState = _state; }
            return _preview!;
        }
        var items = Items.Where(a => a.Id != pending?.Id && hidden?.Contains(a.Id) != true);
        return Render(pending == null ? items : items.Append(pending), new(0, 0, _source.PixelWidth, _source.PixelHeight));
    }
    private BitmapSource Render(IEnumerable<Annotation> annotations, Int32Rect crop)
    {
        var items = annotations.ToArray();
        var composed = AnnotationRenderer.Composite(_source, items.Where(a => a.Kind != AnnotationKind.Redaction), 1, 1, crop);
        var covers = items.Where(a => a.Kind == AnnotationKind.Redaction && a.Points.Count >= 2).Select(a =>
        {
            var rect = new Rect(a.Points[0], a.Points[^1]);
            int x = (int)Math.Floor(rect.Left) - crop.X, y = (int)Math.Floor(rect.Top) - crop.Y;
            return new RedactionRegion(new(x, y, (int)Math.Ceiling(rect.Right) - crop.X - x, (int)Math.Ceiling(rect.Bottom) - crop.Y - y), a.RedactionStyle);
        }).ToArray();
        return RedactionRenderer.Apply(composed, covers);
    }
}
