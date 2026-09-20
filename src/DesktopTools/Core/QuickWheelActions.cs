namespace DesktopTools.Core;
public static class QuickWheelActions
{
    public static string[] Available => ["Text tools", "Scan screen text", "Draw", "Capture", "Laser", "Spotlight", "Freeze", "File shelf", "Notes", "Audio", "Screen recorder", "Video editor", "QR codes", "Image tools", "Eyedropper", "Teleprompter"];
    public static string[] Defaults => ["Draw", "Capture", "Laser", "File shelf", "Notes", "QR codes", "Eyedropper", "Teleprompter"];
    public static int Sector(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x*x+y*y < 60*60 || x*x+y*y > 215*215) return -1;
        double angle = (Math.Atan2(y,x) * 180 / Math.PI + 90 + 360) % 360;
        return (int)Math.Floor((angle+22.5)/45)%8;
    }
}
