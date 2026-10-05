using DesktopTools.Core;
using System.Windows;
internal static class PerformanceRegressionTests
{
 internal static void Run(Action<string,Action> test,Action<bool,string> check)
 {
  test("Pipeline measurement scopes restore and isolate their observers",()=>{
   var first=new List<PipelineMetrics.Sample>();var second=new List<PipelineMetrics.Sample>();
   using(PipelineMetrics.Capture(first.Add)) {
    using(PipelineMetrics.Measure("outer")) { }
    Task.Run(()=>{using(PipelineMetrics.Capture(second.Add)){using(PipelineMetrics.Measure("child")){}}}).GetAwaiter().GetResult();
    using(PipelineMetrics.Measure("restored")) { }
   }
   using(PipelineMetrics.Measure("disabled")) { }
   check(first.Select(s=>s.Stage).SequenceEqual(new[]{"outer","restored"}) && second.Single().Stage=="child","Observer leaked across nested/task scopes");
  });
  test("Repeated phone detection retains coverage within an allocation budget",()=>{
   var words=Enumerable.Range(0,1200).Select(i=>new OcrWordBox($"+1 202-555-{i:0000}",new Rect(10,10+i*22,160,12),i)).ToArray();
   var layout=new OcrLayout(900,30000,words);_=SensitiveDataDetector.Detect(layout,["phone"]);
   long before=GC.GetAllocatedBytesForCurrentThread();var found=SensitiveDataDetector.Detect(layout,["phone"]);long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
   check(found.Count==1200 && found.All(f=>f.Bounds.X==7 && f.Bounds.Width==166),"Phone geometry or number of findings changed");
   check(bytes<8_000_000,"Phone detection allocated more than8MB: "+bytes);
  });
 }
}