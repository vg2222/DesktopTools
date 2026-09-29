using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;

internal static class TestDisplayPlacement
{
    // Keep interactive fixtures off the primary display when a second monitor is connected.
    internal static void OnSecondary(Window window)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(item => !item.Primary) ?? Forms.Screen.PrimaryScreen!;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = screen.WorkingArea.Left + 40;
        window.Top = screen.WorkingArea.Top + 40;
    }
    internal static void OnSecondary(Forms.Form window)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(item => !item.Primary) ?? Forms.Screen.PrimaryScreen!;
        window.StartPosition = Forms.FormStartPosition.Manual;
        window.Location = new System.Drawing.Point(screen.WorkingArea.Left + 40, screen.WorkingArea.Top + 40);
    }
}
