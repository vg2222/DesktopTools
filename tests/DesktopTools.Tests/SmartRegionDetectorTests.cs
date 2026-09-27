using System.Windows;
using DesktopTools.Native;

internal static class SmartRegionDetectorTests
{
    private sealed record FakeNode(Rect Bounds, bool Candidate, IReadOnlyList<FakeNode> Children);

    internal static void Run()
    {
        Rect? Pick(Rect[] candidates, Point pointer, Rect capture, Rect window) =>
            SmartRegionDetector.ChooseRegion(candidates, pointer, capture, window);
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        var capture = new Rect(-200, 0, 500, 600);
        var window = new Rect(-180, 40, 450, 440);
        var pointer = new Point(-20, 160);
        var panel = new Rect(-140, 90, 300, 250);
        var image = new Rect(-40, 130, 100, 80);
        Check(Pick([panel, image], pointer, capture, window) == image,
            "The smallest useful region under the pointer should be suggested.");
        Check(Pick([panel, new Rect(-30, 145, 48, 40)], pointer, capture, window) == panel,
            "A small control must not hide a useful surrounding panel.");

        var crossing = new Rect(-250, 100, 300, 120);
        var clipped = new Rect(-180, 100, 230, 120);
        Check(Pick([crossing], new Point(-150, 150), capture, window) == clipped,
            "A region across a negative monitor origin must be clipped to the capture and window.");

        Check(Pick([new Rect(-25, 150, 10, 10), new Rect(80, 80, 100, 100)], pointer, capture, window) == null,
            "Tiny targets and regions away from the pointer should not be suggested.");
        Check(Pick([image], new Point(350, 160), capture, window) == null,
            "The pointer outside the selected capture area must not receive a suggestion.");

        var offPointer = new FakeNode(new Rect(220, 500, 72, 60), false, []);
        FakeNode nested = new(image, true, []);
        for (int level = 0; level < 8; level++) nested = new FakeNode(panel, false, [nested]);
        var viewport = new FakeNode(window, false, [.. Enumerable.Repeat(offPointer, 200), nested]);
        var root = new FakeNode(window, false, [.. Enumerable.Repeat(offPointer, 200), viewport]);
        Func<FakeNode, (Rect Bounds, bool Offscreen, bool Candidate)> inspect = node => (node.Bounds, false, node.Candidate);
        Func<FakeNode, IEnumerable<FakeNode>> children = node => node.Children;
        var selected = SmartRegionDetector.ChooseFromTree(root, pointer, capture, window, inspect, children);
        Check(selected == image, "A deep region after hundreds of off-pointer siblings must remain reachable.");

        var front = new SmartRegionWindow((nint)11, image);
        var behind = new SmartRegionWindow((nint)22, window);
        var hit = SmartRegionDetector.ChooseWindow([front, behind], pointer);
        Check(hit?.Handle == front.Handle, "The frontmost snapshot window under the pointer should win.");
        hit = SmartRegionDetector.ChooseWindow([front, behind], new Point(150, 160));
        Check(hit?.Handle == behind.Handle, "A snapshot must keep lower windows selectable outside the front window.");
    }
}
