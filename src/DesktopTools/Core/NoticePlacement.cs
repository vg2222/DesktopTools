using System.Windows;

namespace DesktopTools.Core;

/// <summary>Work area and optional anchor are physical pixels; size is in DIP.</summary>
public static class NoticePlacement
{
    public static Rect Calculate(Rect work, Size size, double scaleX, double scaleY, Point? anchor = null)
    {
        if (work.IsEmpty || work.Width <= 0 || work.Height <= 0 ||
            !double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
            throw new ArgumentOutOfRangeException(nameof(work));
        double width = Math.Min(work.Width, Math.Ceiling(size.Width * scaleX));
        double height = Math.Min(work.Height, Math.Ceiling(size.Height * scaleY));
        var desired = anchor ?? new Point(work.Left + (work.Width - width) / 2, work.Top + 24 * scaleY);
        return new Rect(Math.Floor(Math.Clamp(desired.X, work.Left, work.Right - width)),
            Math.Floor(Math.Clamp(desired.Y, work.Top, work.Bottom - height)), width, height);
    }
}
