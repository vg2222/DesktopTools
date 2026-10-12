using System.IO;
using DesktopTools.Core;
internal static class AutomationTests
{
    internal static void Run(Action<string,Action> test, Action<bool,string> check)
    {
        test("Automation polling preserves deferred startup without polling manual workflows", () => {
            var now = new DateTime(2026, 10, 2, 9, 0, 0);
            var script = new AutomationScript { Armed = true, Shortcut = "Ctrl+Alt+F24" };
            var state = new AutomationTriggerState(script, now, false, true);
            check(!state.NeedsPolling(script), "Hotkey-only workflow requests polling");
            script.IntervalMinutes = 1;
            check(state.NeedsPolling(script), "Interval workflow lost polling");
            script.Armed = false;
            check(!state.NeedsPolling(script), "Disarmed schedule requests polling");
            script.Armed = true; script.IntervalMinutes = 0; script.DailyTime = "09:01";
            check(state.NeedsPolling(script), "Daily workflow lost polling");
            script.DailyTime = ""; script.WindowTrigger = "Window";
            check(state.NeedsPolling(script), "Window trigger lost polling");
            script.WindowTrigger = ""; script.RunOnStartup = true;
            state = new AutomationTriggerState(script, now, false, true);
            check(state.NeedsPolling(script), "Startup workflow lost polling");
            check(!state.Take(script, now, false, false) && state.NeedsPolling(script), "Busy startup stopped polling before dispatch");
            check(state.Take(script, now, false, true) && !state.NeedsPolling(script), "Consumed startup-only workflow kept polling");
            check(!state.Take(script, now.AddMinutes(1), false, true), "Consumed startup workflow ran twice");
            state = new AutomationTriggerState(script, now, false, false);
            check(!state.NeedsPolling(script), "Editing a startup-only workflow replayed startup");
        });
        test("Automation v1 workflows upgrade without losing content",()=>{
            string path=Path.Combine(Path.GetTempPath(),"DesktopTools-automation-"+Guid.NewGuid());Directory.CreateDirectory(path);
            try{
                var id=Guid.NewGuid();File.WriteAllText(Path.Combine(path,"automation.json"),System.Text.Json.JsonSerializer.Serialize(new {Version=1,Scripts=new[]{new {Id=id,Name="Existing",Steps=new[]{new {Kind=6,Text="Keep me"}}}}}));
                var store=new AutomationStore(path);var scripts=store.Load();check(scripts.Count==1&&scripts[0].Id==id&&scripts[0].StepDelayMs==120,"v1 settings lost");
                scripts[0].DailyTime="17:30";scripts[0].Steps.Add(new(){Kind=AutomationKind.WaitPixel,Text="#FFFFFF",Value=1000});store.Save(scripts);
                var reloaded=new AutomationStore(path).Load();check(reloaded[0].Steps[0].Text=="Keep me"&&reloaded[0].DailyTime=="17:30","v2 roundtrip lost data");
            }finally{Directory.Delete(path,true);}
        });
        test("Automation edit operations keep nested branches intact",()=>{
            var script=new AutomationScript{Steps=[new(){Kind=AutomationKind.IfWindow,Text="A"},new(){Kind=AutomationKind.Repeat,Value=2},new(){Kind=AutomationKind.Text,Text="yes"},new(){Kind=AutomationKind.EndRepeat},new(){Kind=AutomationKind.Else},new(){Kind=AutomationKind.Wait,Value=100},new(){Kind=AutomationKind.EndIf}]};
            check(AutomationEditing.Move(script,1,1)==1,"Loop crossed its parent branch");check(AutomationEditing.Move(script,5,-1)==5,"Else child crossed branch");
            int duplicate=AutomationEditing.Duplicate(script,4);check(duplicate==7&&script.Steps.Count==14,"Else duplication lost whole branch");
            AutomationEditing.Remove(script,6);check(script.Steps.Count==7,"End condition deletion incomplete");AutomationProgram.Compile(script);
            script=new();for(int i=0;i<16;i++)AutomationEditing.Insert(script,i-1,AutomationKind.Repeat);
            int previous=script.Steps.Count;bool rejected=false;try{AutomationEditing.Insert(script,15,AutomationKind.Repeat);}catch(ArgumentException){rejected=true;}
            check(rejected&&script.Steps.Count==previous,"Overdeep insertion left invalid structure");AutomationProgram.Compile(script);
        });
        test("Automation window and interval triggers defer without duplicate runs",()=>{
            var now=new DateTime(2026,10,2,12,0,0);var script=new AutomationScript{Armed=true,WindowTrigger="Window",IntervalMinutes=1};
            var state=new AutomationTriggerState(script,now,true,false);
            check(!state.Take(script,now,true,true),"Existing startup window triggered");
            state.Take(script,now,false,false);check(!state.Take(script,now,true,false),"Busy window fired");
            check(state.Take(script,now,true,true),"Window trigger was dropped");check(!state.Take(script,now,true,true),"Same window repeated");
            check(!state.Take(script,now.AddMinutes(1),true,false),"Busy interval fired");check(state.Take(script,now.AddMinutes(2),true,true),"Due interval lost");
            check(!state.Take(script,now.AddMinutes(2),true,true),"Deferred interval repeated");
        });

        test("Automation editor inserts inside loops and moves complete blocks",()=>{
            var script=new AutomationScript();
            int loop=AutomationEditing.Insert(script,-1,AutomationKind.Repeat);
            AutomationEditing.Insert(script,loop,AutomationKind.Text);
            check(script.Steps[1].Kind==AutomationKind.Text&&script.Steps[2].Kind==AutomationKind.EndRepeat,"Action escaped loop");
            AutomationEditing.Insert(script,2,AutomationKind.Wait);
            check(AutomationEditing.Move(script,0,1)==1&&script.Steps[0].Kind==AutomationKind.Wait,"Loop did not move as one block");
            AutomationEditing.Remove(script,3);
            check(script.Steps.Count==1,"Removing end marker did not remove complete block");AutomationProgram.Compile(script);
        });
        test("Automation schedules fire once and defer while busy",()=>{
            var now=new DateTime(2026,10,2,9,0,0);var script=new AutomationScript{Armed=true,RunOnStartup=true,DailyTime="09:01",IntervalMinutes=0};
            var state=new AutomationTriggerState(script,now,false,true);
            check(!state.Take(script,now,false,false),"Busy startup fired");check(state.Take(script,now,false,true),"Startup lost");
            check(!state.Take(script,now,false,true),"Startup repeated");check(state.Take(script,now.AddMinutes(1),false,true),"Daily time missed");
            check(!state.Take(script,now.AddMinutes(2),false,true),"Daily time repeated");
            check(state.Take(script,now.AddDays(1).AddMinutes(1),false,true),"Next day missed");
        });
        test("Automation new settings and actions preserve schema versions",()=>{
            var script=new AutomationScript{DailyTime="18:05",RunOnStartup=true,TargetWindow="Notes",StepDelayMs=200,Steps=[new(){Kind=AutomationKind.WaitWindow,Text="Notes",Value=2000},new(){Kind=AutomationKind.ClipboardText,Text="Hello"}]};
            var copy=script.Copy();AutomationProgram.Compile(copy);check(copy.TargetWindow=="Notes"&&copy.DailyTime=="18:05"&&copy.RunOnStartup&&copy.StepDelayMs==200,"Copy lost fields");
            copy.DailyTime="25:80";bool rejected=false;try{AutomationProgram.Compile(copy);}catch(ArgumentException){rejected=true;}check(rejected,"Invalid time accepted");
            check((int)AutomationKind.Move==20,"Existing persisted action IDs changed");
        });
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
