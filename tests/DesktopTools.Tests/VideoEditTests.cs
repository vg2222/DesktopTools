using DesktopTools.Core;

internal static class VideoEditTests
{
    public static void Run()
    {
        var source = new VideoInfo("test.mp4", 10, 1920, 1080, true);
        void Check(bool value) { if (!value) throw new Exception("Video edit assertion failed"); }
        void Reject(VideoEdit edit) { try { edit.Validate(source); throw new Exception("Invalid edit accepted"); } catch (ArgumentException) { } }
        var edit = new VideoEdit(1, 9, true, 3, 6);
        Check(edit.Segments(source).SequenceEqual(new[] { new VideoSegment(1, 3), new VideoSegment(6, 9) }));
        Check(edit.OutputDuration(source) == 5);
        Check(new VideoEdit(0, 10, true, 0, 2).Segments(source).SequenceEqual(new[] { new VideoSegment(2, 10) }));
        Check(new VideoEdit(0, 10, true, 8, 10).Segments(source).SequenceEqual(new[] { new VideoSegment(0, 8) }));
        Check(new VideoEdit(0, 10, CropWidth: 641, CropHeight: 479, Rotation: 90).OutputSize(source) == (480, 642));
        Check(new VideoEdit(0, 10, OutputPercent: 50).OutputSize(source) == (960, 540));
        Check(new VideoEdit(0, 10, CropWidth: 640, CropHeight: 480, Rotation: 90, OutputPercent: 50).OutputSize(source) == (240, 320));
        Reject(new(0, 10, OutputPercent: 0)); Reject(new(0, 10, OutputPercent: 101));
        Reject(new(double.NaN, 10)); Reject(new(0, double.PositiveInfinity)); Reject(new(-1, 10)); Reject(new(0, 11)); Reject(new(1, 1));
        Reject(new(0, 10, true, 0, 10)); Reject(new(0, 10, true, 4, 3)); Reject(new(1, 9, true, 0, 2)); Reject(new(0, 10, true, .01, 5));
        Reject(new(0, 10, CropX: 1)); Reject(new(0, 10, CropWidth: -1)); Reject(new(0, 10, CropX: int.MaxValue, CropWidth: int.MaxValue)); Reject(new(0, 10, Rotation: 45));
    }
}
