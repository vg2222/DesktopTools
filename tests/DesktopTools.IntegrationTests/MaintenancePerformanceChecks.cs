using DesktopTools.Native;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class MaintenancePerformanceChecks
{
    internal static async Task RunAsync()
    {
        var stages = new Dictionary<string, (int Count, double Milliseconds)>();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 2560, 1440));
            for (int row = 0; row < 18; row++)
                drawing.DrawText(new FormattedText($"Password: Example#Secret{row:0000} Email: sample{row}@example.test",
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"),
                    row % 2 == 0 ? 10 : 8, Brushes.WhiteSmoke, 1), new Point(1180, 100 + row * 35));
        }
        var image = new RenderTargetBitmap(2560, 1440, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        var rotationWatch = Stopwatch.StartNew();
        var oldRotation = ImageTransforms.Rotate(ImageTransforms.Rotate(ImageTransforms.Rotate(image)));
        double oldRotationMs = rotationWatch.Elapsed.TotalMilliseconds;
        rotationWatch.Restart(); var newRotation = ImageTransforms.Rotate(image, -90);
        double newRotationMs = rotationWatch.Elapsed.TotalMilliseconds;
        var oldPixels = OcrEnhancer.ToBgra(oldRotation); var newPixels = OcrEnhancer.ToBgra(newRotation);
        if (!oldPixels.SequenceEqual(newPixels)) throw new Exception("Large image left-rotation pixels changed");
        Console.WriteLine($"MEASURE left-rotation 2560x1440: three rotations={oldRotationMs:F2}ms, one rotation={newRotationMs:F2}ms, copied pixel payloads={image.PixelWidth*image.PixelHeight*4L*3}/{image.PixelWidth*image.PixelHeight*4L}");
        string old = DesktopTools.Localization.L.Language;
        try
        {
            DesktopTools.Localization.L.Use("ru");
            var english = LocalOcr.Languages.FirstOrDefault(l => l.Tag.StartsWith("en"));
            if (english == null) throw new InvalidOperationException("Benchmark requires the installed English Windows OCR recognizer");
            using (PipelineMetrics.Capture(sample =>
            {
                lock (stages) { var previous = stages.GetValueOrDefault(sample.Stage); stages[sample.Stage] = (previous.Count + 1, previous.Milliseconds + sample.Elapsed.TotalMilliseconds); }
            }))
            {
                var watch = Stopwatch.StartNew(); long allocated = GC.GetTotalAllocatedBytes();
                var findings = await Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(image, english.Tag,
                    AutoRedactOptions.AvailableCategories, CancellationToken.None));
                Console.WriteLine($"MEASURE privacy 2560x1440: {watch.Elapsed.TotalMilliseconds:F2}ms, allocations={GC.GetTotalAllocatedBytes() - allocated}, findings={findings.Count}");
                var signature = findings.Select(f => "GEOMETRY " + string.Join(",", f.Categories) + "=" + f.Bounds).ToArray();
                foreach (string entry in signature) Console.WriteLine(entry);
                // Optional same-machine baseline; this deliberately includes 8px
                // glyphs that were already missed by the original Windows OCR.
                // Compare the complete result rather than pretending a count
                // proves that the baseline fixture was read perfectly.
                if (Environment.GetEnvironmentVariable("DESKTOPTOOLS_PRIVACY_BASELINE") is { Length: > 0 } baseline)
                {
                    var expected = System.IO.File.ReadAllLines(baseline).Where(l => l.StartsWith("GEOMETRY ")).ToArray();
                    if (expected.Length == 0 || !signature.SequenceEqual(expected)) throw new Exception("Privacy geometry/categories differ from the same-machine baseline");
                    Console.WriteLine("PASS complete privacy geometry matches the original assembly baseline");
                }                var document = new ScreenshotEditDocument(image);
                using (PipelineMetrics.Measure("privacy.cover-creation"))
                    document.AddRange(findings.Select(f => new Annotation { Kind = AnnotationKind.Redaction,
                        Points = [new Point(f.Bounds.X, f.Bounds.Y), new Point(f.Bounds.X + f.Bounds.Width, f.Bounds.Y + f.Bounds.Height)],
                        Color = Colors.Black, RedactionStyle = RedactionStyle.Solid }).ToArray());
            }
            foreach (var (stage, value) in stages.OrderBy(p => p.Key))
                Console.WriteLine($"MEASURE stage {stage}: calls={value.Count}, total={value.Milliseconds:F2}ms (nested timings are not additive)");
        }
        finally { DesktopTools.Localization.L.Use(old); }
    }
    internal static async Task TransportEquivalenceAsync()
    {
        async Task<SoftwareBitmap> PngReference(BitmapSource source,int scale)
        {
            var scaled=new TransformedBitmap(source,new ScaleTransform(scale,scale));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(scaled));
            using var memory=new MemoryStream();encoder.Save(memory);
            using var stream=new InMemoryRandomAccessStream();
            using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(memory.ToArray());await writer.StoreAsync();await writer.FlushAsync();}
            stream.Seek(0);var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore);
        }
        byte[] Bytes(SoftwareBitmap bitmap)
        {
            var buffer=new Windows.Storage.Streams.Buffer((uint)(bitmap.PixelWidth*bitmap.PixelHeight*4));bitmap.CopyToBuffer(buffer);
            using var reader=DataReader.FromBuffer(buffer);var bytes=new byte[buffer.Length];reader.ReadBytes(bytes);return bytes;
        }
        int cases=0;
        var transport=typeof(LocalOcr).GetMethod("ScaledBitmapAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        foreach(var format in new[]{PixelFormats.Bgra32,PixelFormats.Pbgra32,PixelFormats.Bgr24,PixelFormats.Gray8})
        foreach(int scale in new[]{1,2,3,4})
        foreach(bool opaque in new[]{true,false})
        {
            int width=37,height=23,stride=(width*format.BitsPerPixel+7)/8;var pixels=new byte[stride*height];var random=new Random(821);
            random.NextBytes(pixels);
            if(format.BitsPerPixel==32)for(int i=0;i<pixels.Length;i+=4){byte alpha=opaque?(byte)255:(byte)random.Next(1,256);pixels[i+3]=alpha;if(format==PixelFormats.Pbgra32)for(int c=0;c<3;c++)pixels[i+c]=(byte)(pixels[i+c]*alpha/255);}
            var source=BitmapSource.Create(width,height,144,96,format,null,pixels,stride);source.Freeze();
            using var expected=await PngReference(source,scale);
            using var actual=await (Task<SoftwareBitmap>)transport.Invoke(null,new object[]{source,scale})!;
            var a=Bytes(actual);var e=Bytes(expected);
            if(!a.SequenceEqual(e))throw new Exception($"Legacy PNG/direct input differ: format={format},scale={scale},opaque={opaque},first={Enumerable.Range(0,a.Length).First(i=>a[i]!=e[i])}");
            cases++;
        }
        using(var scope=PipelineMetrics.Capture(_=>{}))
        {
            var bigPixels=new byte[1024*512*4];var big=BitmapSource.Create(1024,512,96,96,PixelFormats.Bgra32,null,bigPixels,4096);
            using(var native=SoftwareBitmapFactory.Create(big)) { }
            var tinyPixels=new byte[4*4*4];var tiny=BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,tinyPixels,16);
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<20;i++){using var native=SoftwareBitmapFactory.Create(tiny);}
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Console.WriteLine($"MEASURE native tiny bitmap transfers: {allocated} bytes for20 tiny bitmaps");
            if(allocated>1_000_000)throw new Exception("Small native transfers exceeded the allocation budget: "+allocated);
        }
        var red=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);
        var blue=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{255,0,0,255},4);
        using var first=SoftwareBitmapFactory.Create(red); using var second=SoftwareBitmapFactory.Create(blue);
        if(!Bytes(first).SequenceEqual(new byte[]{0,0,255,255}) || !Bytes(second).SequenceEqual(new byte[]{255,0,0,255}))throw new Exception("Native bitmap copies share source storage");
        Console.WriteLine($"PASS legacy PNG/direct bitmap bytes are identical in {cases} alpha/format/scale/DPI cases");
    }
    internal static Task PixelSamplingAsync()
    {
        var pixels = new byte[4000 * 2000 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 8; pixels[i+1] = 18; pixels[i+2] = 28; pixels[i+3] = 255; }
        var image = BitmapSource.Create(4000,2000,96,144,PixelFormats.Bgra32,null,pixels,16000); image.Freeze();
        _ = OcrEnhancer.IsMostlyDark(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{1,2,3,255},4));
        long before = GC.GetAllocatedBytesForCurrentThread();
        if (!OcrEnhancer.IsMostlyDark(image)) throw new Exception("Dark opaque image classification changed");
        long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
        Console.WriteLine($"MEASURE 8MP polarity sampling allocation: {allocated} bytes");
        if (allocated > 256_000) throw new Exception("Polarity sampling retained a full-image pixel copy: "+allocated);
        foreach(var format in new[]{PixelFormats.Bgra32,PixelFormats.Pbgra32})
        foreach(int width in new[]{1,97,250})
        {
            int height=101; var data=new byte[width*height*4];var random=new Random(91);
            for(int i=0;i<data.Length;i+=4){byte alpha=(byte)random.Next(1,256);data[i+3]=alpha;for(int c=0;c<3;c++)data[i+c]=(byte)random.Next(format==PixelFormats.Pbgra32?alpha+1:256);}
            var sample=BitmapSource.Create(width,height,144,96,format,null,data,width*4);sample.Freeze();
            var original=OcrEnhancer.ToBgra(sample);long sum=0;int count=0;
            for(int y=0;y<height;y+=Math.Max(1,height/96))for(int x=0;x<width;x+=Math.Max(1,width/96)){int index=(y*width+x)*4;sum+=(original[index]*29+original[index+1]*150+original[index+2]*77)>>8;count++;}
            if(OcrEnhancer.IsMostlyDark(sample)!=(sum/count<115))throw new Exception("Polarity result changed for alpha/format/DPI sampling");
            var after=new byte[data.Length];sample.CopyPixels(after,width*4,0);if(!after.SequenceEqual(data))throw new Exception("Polarity sampling changed original pixels");
        }
        return Task.CompletedTask;
    }
    internal static async Task RecordingMeasurementAsync()
    {
        using var target = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
            ClientSize = new System.Drawing.Size(640,360), StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
            Text = "DesktopTools maintenance recording helper", TopMost = true, BackColor = System.Drawing.Color.RoyalBlue };
        target.Show(); target.Refresh(); await Task.Delay(200);
        using var process = Process.GetCurrentProcess();
        await using var recorder = new ScreenRecordingService();
        string path = Path.GetFullPath("maintenance-recording-" + Guid.NewGuid().ToString("N") + ".mp4");
        var completion = recorder.StartSource(new ScreenRecorderLib.WindowRecordingSource(target.Handle) { IsCursorCaptureEnabled = false, IsBorderRequired = true },
            path, microphone:false, systemAudio:false, framesPerSecond:30, hardwareAcceleration:false);
        var watch = Stopwatch.StartNew();process.Refresh(); var cpu=process.TotalProcessorTime;long peak=process.PrivateMemorySize64;
        try
        {
            for(int tick=0;tick<60;tick++)
            {
                await Task.Delay(500); target.BackColor = tick%2==0?System.Drawing.Color.RoyalBlue:System.Drawing.Color.OrangeRed; target.Refresh();
                process.Refresh(); peak=Math.Max(peak,process.PrivateMemorySize64);
            }
            process.Refresh();double cpuPercent=(process.TotalProcessorTime-cpu).TotalMilliseconds/watch.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100;
            recorder.Stop(); await completion.WaitAsync(TimeSpan.FromSeconds(25));
            var video=await VideoEditorService.ProbeAsync(path);
            if(video.Width!=640 || video.Height!=360 || video.HasAudio || video.Duration<25)throw new Exception("Owned helper recording was not finalized correctly");
            Console.WriteLine($"MEASURE owned-window recording: wall={watch.Elapsed.TotalSeconds:F1}s, encoded={video.Duration:F1}s, CPU={cpuPercent:F3}% normalized, peak-private={peak/1048576d:F1}MiB; audio off, software encoding, target30FPS");
        }
        finally { recorder.Stop(); }
    }
    internal static async Task StartupIdleAsync()
    {
        using var process = Process.GetCurrentProcess();
        var watch = Stopwatch.StartNew();
        using var controller = new AppController(true);
        Console.WriteLine($"MEASURE smoke controller construction: {watch.Elapsed.TotalMilliseconds:F2}ms; native startup/hotkey initialization excluded");
        await Task.Delay(500);
        process.Refresh(); var cpu = process.TotalProcessorTime; watch.Restart();
        await Task.Delay(TimeSpan.FromSeconds(30)); process.Refresh();
        Console.WriteLine($"MEASURE smoke idle30s: {(process.TotalProcessorTime-cpu).TotalMilliseconds/watch.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100:F3}% normalized CPU, private={process.PrivateMemorySize64/1048576d:F1}MiB, handles={process.HandleCount}");
    }
}