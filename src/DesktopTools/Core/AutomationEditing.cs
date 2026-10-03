using System;
using System.Linq;
namespace DesktopTools.Core;

public static class AutomationEditing
{
    public static bool IsStart(AutomationKind kind)=>kind is AutomationKind.Repeat or AutomationKind.IfWindow or AutomationKind.IfPixel;
    public static bool IsEnd(AutomationKind kind)=>kind is AutomationKind.EndRepeat or AutomationKind.EndIf or AutomationKind.Else;
    public static (int Start,int Count) Range(AutomationScript script,int index)
    {
        var jumps=AutomationProgram.Compile(script,validateValues:false);
        if(index<0||index>=script.Steps.Count)throw new ArgumentOutOfRangeException(nameof(index));
        if(script.Steps[index].Kind==AutomationKind.Else)index=jumps.First(p=>p.Value==index&&IsStart(script.Steps[p.Key].Kind)).Key;
        else if(IsEnd(script.Steps[index].Kind))index=jumps[index];
        int end=index;
        if(IsStart(script.Steps[index].Kind)){end=jumps[index];if(script.Steps[end].Kind==AutomationKind.Else)end=jumps[end];}
        return(index,end-index+1);
    }
    public static int Insert(AutomationScript script,int selected,AutomationKind kind)
    {
        if(IsEnd(kind))throw new ArgumentException("Block boundaries are managed by the editor.");
        int index=Math.Clamp(selected+1,0,script.Steps.Count);
        var step=new AutomationStep{Kind=kind,Value=kind switch{AutomationKind.Repeat=>2,AutomationKind.Scroll=>-120,AutomationKind.WaitWindow or AutomationKind.WaitPixel=>10000,_=>500},Text=kind is AutomationKind.IfPixel or AutomationKind.WaitPixel?"#FFFFFF":""};
        int count=IsStart(kind)?kind==AutomationKind.Repeat?2:3:1;
        if(script.Steps.Count+count>500)throw new ArgumentException(DesktopTools.Localization.L.T("Invalid or oversized automation."));
        script.Steps.Insert(index,step);
        if(kind==AutomationKind.Repeat)script.Steps.Insert(index+1,new(){Kind=AutomationKind.EndRepeat});
        else if(IsStart(kind)){script.Steps.Insert(index+1,new(){Kind=AutomationKind.Else});script.Steps.Insert(index+2,new(){Kind=AutomationKind.EndIf});}
        try{AutomationProgram.Compile(script,validateValues:false);}catch{script.Steps.RemoveRange(index,count);throw;}
        return index;
    }
    public static int Remove(AutomationScript script,int index){var range=Range(script,index);script.Steps.RemoveRange(range.Start,range.Count);return Math.Min(range.Start,script.Steps.Count-1);}
    public static int Duplicate(AutomationScript script,int index){var range=Range(script,index);if(script.Steps.Count+range.Count>500)throw new ArgumentException(DesktopTools.Localization.L.T("Invalid or oversized automation."));script.Steps.InsertRange(range.Start+range.Count,script.Steps.Skip(range.Start).Take(range.Count).Select(s=>s.Copy()).ToArray());return range.Start+range.Count;}
    public static int Move(AutomationScript script,int index,int direction)
    {
        var range=Range(script,index);int target;
        if(direction<0){if(range.Start==0)return range.Start;int previous=range.Start-1;if(script.Steps[previous].Kind is AutomationKind.Else||IsStart(script.Steps[previous].Kind))return range.Start;target=Range(script,previous).Start;}
        else{int next=range.Start+range.Count;if(next>=script.Steps.Count||IsEnd(script.Steps[next].Kind))return range.Start;target=next+Range(script,next).Count-range.Count;}
        var moved=script.Steps.GetRange(range.Start,range.Count);script.Steps.RemoveRange(range.Start,range.Count);script.Steps.InsertRange(target,moved);return target;
    }
}
