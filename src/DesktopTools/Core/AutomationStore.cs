using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DesktopTools.Localization;
namespace DesktopTools.Core;
public sealed class AutomationStore(string directory)
{
    private string PathName=>Path.Combine(directory,"automation.json");
    private bool writable;
    public string? RecoveryMessage { get; private set; }
    private sealed class Document { public int Version {get;set;}=2; public List<AutomationScript> Scripts {get;set;}=[]; }
    public List<AutomationScript> Load()
    {
        writable=false; RecoveryMessage=null;
        if(!File.Exists(PathName)){writable=true;return [];}
        try {
            if(new FileInfo(PathName).Length>16000000) throw new JsonException();
            string text=File.ReadAllText(PathName);
            using var parsed=JsonDocument.Parse(text);
            if(parsed.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            if(parsed.RootElement.TryGetProperty("Version",out var version) && version.TryGetInt32(out int number) && number>2)
            {RecoveryMessage=L.T("Automation uses a newer format. Saving is disabled.");return [];}
            if(!parsed.RootElement.TryGetProperty("Version",out _) || !parsed.RootElement.TryGetProperty("Scripts",out _)) throw new JsonException();
            var doc=JsonSerializer.Deserialize<Document>(text)??throw new JsonException();
            if(doc.Version is not (1 or 2))throw new JsonException();
            try{Validate(doc.Scripts);}catch(ArgumentException e){throw new JsonException(e.Message);}
            writable=true; return doc.Scripts;
        }catch(JsonException){
            try{File.Move(PathName,Path.Combine(directory,$"automation.damaged-{Guid.NewGuid():N}.json"));writable=true;RecoveryMessage=L.T("Damaged automation was preserved in a backup.");}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){RecoveryMessage=L.T("Automation could not be read. Saving is disabled.");}
            return [];
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException){RecoveryMessage=L.T("Automation could not be read. Saving is disabled.");return [];}
    }
    public void Save(IReadOnlyCollection<AutomationScript> scripts)
    {
        if(!writable)throw new InvalidOperationException(L.T("Automation saving is unavailable."));
        Validate(scripts); Directory.CreateDirectory(directory);
        string temp=Path.Combine(directory,$"automation-{Guid.NewGuid():N}.tmp");
        try {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,new Document{Scripts=scripts.ToList()});stream.Flush(true);}
            if(File.Exists(PathName))File.Replace(temp,PathName,null);else File.Move(temp,PathName);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    private static void Validate(IReadOnlyCollection<AutomationScript> scripts)
    {
        if(scripts==null || scripts.Count>100)throw new ArgumentException(L.T("Invalid or oversized automation."));
        var ids=new HashSet<Guid>();
        foreach(var s in scripts){if(s==null || s.Id==Guid.Empty || !ids.Add(s.Id))throw new ArgumentException(L.T("Invalid or oversized automation."));AutomationProgram.Compile(s);}
    }
}
