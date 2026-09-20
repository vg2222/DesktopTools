using DesktopTools.Localization;

namespace DesktopTools.Core;

public sealed record VideoInfo(string Path, double Duration, int Width, int Height, bool HasAudio);
public sealed record VideoSegment(double Start, double End);
public sealed record VideoEdit(double Start, double End, bool RemoveSection = false, double CutStart = 0, double CutEnd = 0,
    int CropX = 0, int CropY = 0, int CropWidth = 0, int CropHeight = 0, int Rotation = 0, bool Mute = false, int OutputPercent = 100)
{
    public IReadOnlyList<VideoSegment> Segments(VideoInfo source)
    {
        Validate(source);
        if (!RemoveSection) return [new(Start, End)];
        var segments = new List<VideoSegment>();
        if (CutStart > Start) segments.Add(new(Start, CutStart));
        if (CutEnd < End) segments.Add(new(CutEnd, End));
        return segments;
    }
    public (int Width, int Height) OutputSize(VideoInfo source)
    {
        int width = CropWidth == 0 ? source.Width : CropWidth, height = CropHeight == 0 ? source.Height : CropHeight;
        width = Math.Max(2, (int)Math.Round(width * OutputPercent / 100d));
        height = Math.Max(2, (int)Math.Round(height * OutputPercent / 100d));
        // H.264 4:2:0 requires even dimensions; normalize output by at most one pixel.
        width += width % 2; height += height % 2;
        return Rotation is 90 or 270 ? (height, width) : (width, height);
    }
    public double OutputDuration(VideoInfo source) => Segments(source).Sum(s => s.End - s.Start);
    public void Validate(VideoInfo source)
    {
        if (OutputPercent < 10 || OutputPercent > 100) throw new ArgumentException(L.T("Choose an output size between 10% and 100%."));
        if (!double.IsFinite(source.Duration) || source.Duration <= 0 || source.Duration > 21600 || source.Width < 2 || source.Height < 2 || source.Width > 8192 || source.Height > 8192)
            throw new ArgumentException(L.T("Use a video up to six hours long and 8192 pixels per side."));
        if (!double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End > source.Duration + .001 || End - Start < .05)
            throw new ArgumentException(L.T("Choose a start and end within the video, at least 0.05 seconds apart."));
        if (RemoveSection && (!double.IsFinite(CutStart) || !double.IsFinite(CutEnd) || CutStart < Start || CutEnd > End || CutEnd <= CutStart || End - Start - (CutEnd - CutStart) < .05))
            throw new ArgumentException(L.T("The removed section must be inside the kept range and leave some video."));
        if (RemoveSection && ((CutStart > Start && CutStart - Start < .05) || (CutEnd < End && End - CutEnd < .05)))
            throw new ArgumentException(L.T("Each remaining section must be at least 0.05 seconds long."));
        int width = CropWidth == 0 ? source.Width : CropWidth, height = CropHeight == 0 ? source.Height : CropHeight;
        if (CropX < 0 || CropY < 0 || width < 2 || height < 2 || (long)CropX + width > source.Width || (long)CropY + height > source.Height)
            throw new ArgumentException(L.T("The crop must stay inside the original video frame."));
        if (Rotation is not (0 or 90 or 180 or 270)) throw new ArgumentException(L.T("Choose a rotation of 0, 90, 180 or 270 degrees."));
    }
}
