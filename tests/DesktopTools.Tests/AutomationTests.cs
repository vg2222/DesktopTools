using System.IO;
using DesktopTools.Core;
internal static class AutomationTests
{
    internal static void Run(Action<string,Action> test, Action<bool,string> check)
    {
        test("Automation rejects unmatched blocks before input", () => {
            var script = new AutomationScript { Steps = [new() { Kind = AutomationKind.EndRepeat }] };
            bool rejected = false; try { AutomationProgram.Compile(script); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Unmatched end accepted");
        });
        test("Automation nested repeat and false branch execute deterministically", () => {
            var script = new AutomationScript { Steps = [
                new() { Kind=AutomationKind.Repeat, Value=3 },
                new() { Kind=AutomationKind.IfWindow, Text="missing" },
                new() { Kind=AutomationKind.Text, Text="wrong" },
                new() { Kind=AutomationKind.Else },
                new() { Kind=AutomationKind.Text, Text="right" },
                new() { Kind=AutomationKind.EndIf },
                new() { Kind=AutomationKind.EndRepeat }] };
            var output = new List<string>();
            AutomationProgram.RunAsync(script, (_,_) => Task.FromResult(false), (s,_) => {output.Add(s.Text); return Task.CompletedTask;}, CancellationToken.None).GetAwaiter().GetResult();
            check(output.SequenceEqual(new[]{"right","right","right"}), "Branch/repeat routing wrong");
        });
        test("Automation cancellation and action budget terminate loops", () => {
            var script = new AutomationScript { Steps=[new(){Kind=AutomationKind.Repeat,Value=1000},new(){Kind=AutomationKind.Repeat,Value=1000},new(){Kind=AutomationKind.Wait,Value=1},new(){Kind=AutomationKind.EndRepeat},new(){Kind=AutomationKind.EndRepeat}] };
            int calls=0; bool bounded=false;
            try { AutomationProgram.RunAsync(script,(_,_)=>Task.FromResult(true),(s,_)=>{calls++;return Task.CompletedTask;},CancellationToken.None,10).GetAwaiter().GetResult(); } catch(InvalidOperationException){bounded=true;}
            check(bounded && calls<=10,"Budget failed");
            using var cancel=new CancellationTokenSource(); cancel.Cancel(); bool canceled=false;
            try { AutomationProgram.RunAsync(script,(_,_)=>Task.FromResult(true),(s,_)=>Task.CompletedTask,cancel.Token).GetAwaiter().GetResult(); }catch(OperationCanceledException){canceled=true;}
            check(canceled,"Cancellation failed");
        });
        test("Automation future and damaged stores are never overwritten", () => {
            string path=Path.Combine(Path.GetTempPath(),"DesktopTools-automation-"+Guid.NewGuid()); Directory.CreateDirectory(path);
            try {
                string file=Path.Combine(path,"automation.json"); File.WriteAllText(file,"{\"Version\":999,\"Scripts\":[]}");
                var store=new AutomationStore(path); store.Load(); bool blocked=false; try{store.Save([]);}catch(InvalidOperationException){blocked=true;}
                check(blocked && File.ReadAllText(file).Contains("999"),"Future data lost");
                File.WriteAllText(file,"{broken"); store=new AutomationStore(path); store.Load();
                check(Directory.GetFiles(path).Any(f=>File.ReadAllText(f)=="{broken"),"Damaged data lost");
                store.Save([new(){Name="Local",Steps=[new(){Kind=AutomationKind.Text,Text="Привет"}]}]);
                check(new AutomationStore(path).Load().Single().Steps.Single().Text=="Привет","Unicode roundtrip failed");
                check(!Directory.GetFiles(path,"*.tmp").Any(),"Temporary file leaked");
            }finally{Directory.Delete(path,true);}
        });
    }
}
