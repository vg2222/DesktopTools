using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools.Localization;
namespace DesktopTools.Core;
public enum AutomationKind { Click, DoubleClick, RightClick, Scroll, Drag, Keys, Text, Wait, Launch, FocusWindow, Repeat, EndRepeat, IfWindow, IfPixel, Else, EndIf, KeyDown, KeyUp, MouseDown, MouseUp, Move }
public sealed class AutomationStep
{
    public AutomationKind Kind { get; set; } = AutomationKind.Wait;
    public string Text { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int EndX { get; set; }
    public int EndY { get; set; }
    public int Value { get; set; } = 500;
    public AutomationStep Copy() => (AutomationStep)MemberwiseClone();
}
public sealed class AutomationScript
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "My automation";
    public List<AutomationStep> Steps { get; set; } = [];
    public string Shortcut { get; set; } = "";
    public bool Armed { get; set; }
    public int IntervalMinutes { get; set; }
    public string WindowTrigger { get; set; } = "";
    public AutomationScript Copy() => new() {Id=Id, Name=Name, Steps=Steps.Select(s=>s.Copy()).ToList(),Shortcut=Shortcut,Armed=Armed,IntervalMinutes=IntervalMinutes,WindowTrigger=WindowTrigger};
}
public static class AutomationProgram
{
    public static IReadOnlyDictionary<int,int> Compile(AutomationScript script)
    {
        if(script.Name == null || script.Name.Length>120 || script.Steps==null || script.Steps.Count>500 || script.Shortcut==null || script.WindowTrigger==null || script.WindowTrigger.Length>512 || script.IntervalMinutes is <0 or >10080)
            throw new ArgumentException(L.T("Invalid or oversized automation."));
        var jumps=new Dictionary<int,int>(); var blocks=new Stack<(int Index, AutomationKind Kind, int Else)>();
        for(int i=0;i<script.Steps.Count;i++)
        {
            var step=script.Steps[i];
            if(step==null || !Enum.IsDefined(step.Kind) || step.Text==null || step.Text.Length>20000 || Math.Abs((long)step.X)>100000 || Math.Abs((long)step.Y)>100000 || Math.Abs((long)step.EndX)>100000 || Math.Abs((long)step.EndY)>100000)
                throw new ArgumentException(L.T("Invalid or oversized automation."));
            if(step.Kind==AutomationKind.Repeat && step.Value is <1 or >1000 || step.Kind==AutomationKind.Wait && step.Value is <1 or >600000 ||
                step.Kind==AutomationKind.Scroll && step.Value is <-12000 or >12000 ||
                step.Kind is AutomationKind.KeyDown or AutomationKind.KeyUp && step.Value is <1 or >254 ||
                step.Kind is AutomationKind.MouseDown or AutomationKind.MouseUp && step.Value is <1 or >3)
                throw new ArgumentException(L.T("Invalid action value."));
            if(step.Kind is AutomationKind.IfWindow or AutomationKind.FocusWindow or AutomationKind.Launch or AutomationKind.Keys && string.IsNullOrWhiteSpace(step.Text))
                throw new ArgumentException(L.T("This action needs text."));
            if(step.Kind==AutomationKind.IfPixel && !System.Text.RegularExpressions.Regex.IsMatch(step.Text, "^#[0-9A-Fa-f]{6}$"))
                throw new ArgumentException(L.T("Use a pixel color such as #FF8800."));
            if(step.Kind is AutomationKind.Repeat or AutomationKind.IfWindow or AutomationKind.IfPixel) blocks.Push((i,step.Kind,-1));
            else if(step.Kind==AutomationKind.Else)
            {
                if(blocks.Count==0 || blocks.Peek().Kind==AutomationKind.Repeat || blocks.Peek().Else>=0) throw Structure();
                var block=blocks.Pop(); blocks.Push((block.Index,block.Kind,i)); jumps[block.Index]=i;
            }
            else if(step.Kind is AutomationKind.EndRepeat or AutomationKind.EndIf)
            {
                if(blocks.Count==0) throw Structure();
                var block=blocks.Pop();
                if((step.Kind==AutomationKind.EndRepeat)!=(block.Kind==AutomationKind.Repeat)) throw Structure();
                jumps[i]=block.Index;
                if(block.Else>=0) jumps[block.Else]=i; else jumps[block.Index]=i;
            }
            if(blocks.Count>16) throw Structure();
        }
        if(blocks.Count!=0) throw Structure();
        return jumps;
    }
    private static ArgumentException Structure()=>new(L.T("Control blocks are incomplete or too deeply nested."));
    public static async Task RunAsync(AutomationScript source, Func<AutomationStep,CancellationToken,Task<bool>> condition,
        Func<AutomationStep,CancellationToken,Task> action, CancellationToken cancellation, int budget=10000, Action<int>? progress=null)
    {
        var script=source.Copy(); var jumps=Compile(script);
        var loops=new Stack<(int Start,int Remaining)>(); int executed=0;
        for(int i=0;i<script.Steps.Count;i++)
        {
            cancellation.ThrowIfCancellationRequested();
            if(++executed>budget) throw new InvalidOperationException(L.T("Automation reached its action limit."));
            var step=script.Steps[i]; progress?.Invoke(i);
            switch(step.Kind)
            {
                case AutomationKind.Repeat: loops.Push((i,step.Value)); break;
                case AutomationKind.EndRepeat:
                    var loop=loops.Pop(); if(loop.Remaining>1){loops.Push((loop.Start,loop.Remaining-1));i=loop.Start;} break;
                case AutomationKind.IfWindow: case AutomationKind.IfPixel:
                    if(!await condition(step,cancellation)) i=jumps[i]; break;
                case AutomationKind.Else: i=jumps[i]; break;
                case AutomationKind.EndIf: break;
                default: await action(step,cancellation); break;
            }
        }
    }
}
