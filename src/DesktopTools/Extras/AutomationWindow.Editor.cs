using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.Localization;
using DesktopTools.UI;
namespace DesktopTools.Extras;

internal sealed partial class AutomationWindow
{
    private TextBox Field(Panel panel,string label,string text,Action<string> change,bool multiline=false)
    {
        var title=Ui.Text(L.T(label),12,muted:true);title.Margin=new Thickness(0,14,0,6);panel.Children.Add(title);
        var input=new TextBox{Text=text,MinHeight=multiline?110:36,MaxHeight=multiline?200:36,MaxLength=20000,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};
        input.TextChanged+=(_,_)=>{RememberEdit();change(input.Text);UpdateSelectedSummary();};input.LostKeyboardFocus+=(_,_)=>editingSession=false;panel.Children.Add(input);return input;
    }
    private void Number(Panel panel,string label,int value,Action<int> change)
    {
        var (minimum,maximum)=label switch{"Repeat count"=>(1,1000),"Milliseconds" or "Timeout (milliseconds)"=>(1,600000),"Every N minutes (0 = off)"=>(0,10080),"Delay between actions (ms)"=>(0,10000),"Wheel delta (negative = down)"=>(-12000,12000),_=>(-100000,100000)};
        bool Valid(string text,out int number)=>int.TryParse(text,out number)&&number>=minimum&&number<=maximum;
        var input=Field(panel,label,value.ToString(),v=>change(Valid(v,out int n)?n:int.MinValue));
        input.TextChanged+=(_,_)=>{if(Valid(input.Text,out _)){input.ClearValue(Control.BorderBrushProperty);input.ToolTip=null;}else{input.BorderBrush=System.Windows.Media.Brushes.IndianRed;input.ToolTip=L.T("Invalid action value.");}};
    }
    private void Note(string text){var note=Ui.Text(L.T(text),12,muted:true);note.Margin=new Thickness(0,8,0,4);details.Children.Add(note);}
    private void UpdateSelectedSummary()
    {
        if(steps.SelectedItem is not ListBoxItem row||row.Tag is not AutomationStep s||row.Content is not StackPanel content)return;
        if(content.Children.Count>1)content.Children.RemoveAt(1);
        string summary=AutomationCatalog.Summary(s);if(summary.Length>0){var sub=Ui.Text(summary,12,muted:true);sub.Margin=new Thickness(27,5,0,0);sub.MaxHeight=36;content.Children.Add(sub);}
    }
    private void RefreshEditor()
    {
        details.Children.Clear();if(current==null)return;
        if(settingsMode||steps.SelectedItem is not ListBoxItem){RunSettings();return;}
        if(((ListBoxItem)steps.SelectedItem).Tag is not AutomationStep s)return;
        details.Children.Add(Ui.Text(AutomationCatalog.Label(s.Kind),20,true));Note(AutomationCatalog.Help(s.Kind));
        var buttons=new WrapPanel{Margin=new Thickness(0,14,0,0)};
        buttons.Children.Add(Ui.Button("↑",()=>EditStructure(()=>AutomationEditing.Move(current,current.Steps.IndexOf(s),-1))));
        buttons.Children.Add(Ui.Button("↓",()=>EditStructure(()=>AutomationEditing.Move(current,current.Steps.IndexOf(s),1))));
        buttons.Children.Add(Ui.Button(L.T("Duplicate"),()=>EditStructure(()=>AutomationEditing.Duplicate(current,current.Steps.IndexOf(s)))));
        buttons.Children.Add(Ui.Button(L.T("Remove action"),()=>EditStructure(()=>AutomationEditing.Remove(current,current.Steps.IndexOf(s)))));
        details.Children.Add(buttons);
        bool point=s.Kind is AutomationKind.Click or AutomationKind.DoubleClick or AutomationKind.RightClick or AutomationKind.Scroll or AutomationKind.Drag or AutomationKind.IfPixel or AutomationKind.WaitPixel or AutomationKind.MouseDown or AutomationKind.MouseUp or AutomationKind.Move;
        if(point){
            var coords=new Grid();coords.ColumnDefinitions.Add(new());coords.ColumnDefinitions.Add(new());var x=new StackPanel{Margin=new Thickness(0,0,8,0)};var y=new StackPanel();Grid.SetColumn(y,1);coords.Children.Add(x);coords.Children.Add(y);
            Number(x,"X (screen pixels)",s.X,v=>s.X=v);Number(y,"Y (screen pixels)",s.Y,v=>s.Y=v);details.Children.Add(coords);
            details.Children.Add(Ui.Button(L.T(s.Kind is AutomationKind.IfPixel or AutomationKind.WaitPixel?"Pick point and color":"Pick screen point"),()=>_=PickAsync(s,false)));
            if(s.Kind==AutomationKind.Drag){Number(details,"End X",s.EndX,v=>s.EndX=v);Number(details,"End Y",s.EndY,v=>s.EndY=v);details.Children.Add(Ui.Button(L.T("Pick drag end"),()=>_=PickAsync(s,true)));}
        }
        if(s.Kind is AutomationKind.Wait or AutomationKind.Repeat or AutomationKind.Scroll or AutomationKind.WaitWindow or AutomationKind.WaitPixel)
            Number(details,s.Kind==AutomationKind.Repeat?"Repeat count":s.Kind==AutomationKind.Scroll?"Wheel delta (negative = down)":s.Kind==AutomationKind.Wait?"Milliseconds":"Timeout (milliseconds)",s.Value,v=>s.Value=v);
        if(s.Kind is AutomationKind.KeyDown or AutomationKind.KeyUp){
            var choices=Enum.GetValues<Key>().Distinct().Where(k=>KeyInterop.VirtualKeyFromKey(k) is >0 and <255).ToList();var combo=new ComboBox{ItemsSource=choices,SelectedItem=KeyInterop.KeyFromVirtualKey(s.Value),Margin=new Thickness(0,12,0,0)};
            combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is Key key){RememberEdit();s.Value=KeyInterop.VirtualKeyFromKey(key);UpdateSelectedSummary();}};details.Children.Add(combo);
        }
        if(s.Kind is AutomationKind.MouseDown or AutomationKind.MouseUp){var combo=new ComboBox{ItemsSource=new[]{L.T("Left"),L.T("Right"),L.T("Middle")},SelectedIndex=s.Value-1,Margin=new Thickness(0,12,0,0)};combo.SelectionChanged+=(_,_)=>{RememberEdit();s.Value=combo.SelectedIndex+1;};details.Children.Add(combo);}
        if(s.Kind is AutomationKind.Keys or AutomationKind.Text or AutomationKind.ClipboardText or AutomationKind.Launch or AutomationKind.FocusWindow or AutomationKind.IfWindow or AutomationKind.WaitWindow or AutomationKind.MaximizeWindow or AutomationKind.MinimizeWindow or AutomationKind.RestoreWindow or AutomationKind.IfPixel or AutomationKind.WaitPixel){
            bool window=s.Kind is AutomationKind.FocusWindow or AutomationKind.IfWindow or AutomationKind.WaitWindow or AutomationKind.MaximizeWindow or AutomationKind.MinimizeWindow or AutomationKind.RestoreWindow;
            var input=Field(details,s.Kind==AutomationKind.Keys?"Keys (Ctrl+Shift+A)":s.Kind is AutomationKind.IfPixel or AutomationKind.WaitPixel?"Pixel color (#RRGGBB)":window?"Window title contains":s.Kind==AutomationKind.Launch?"App, file or URL":"Text to type",s.Text,v=>s.Text=v,s.Kind is AutomationKind.Text or AutomationKind.ClipboardText);
            if(window)details.Children.Add(Ui.Button(L.T("Choose window"),()=>ChooseWindow(v=>input.Text=v)));
            if(s.Kind==AutomationKind.Keys)details.Children.Add(Ui.Button(L.T("Record shortcut"),()=>RecordShortcut(input,true)));
            if(s.Kind==AutomationKind.Launch){details.Children.Add(Ui.Button(L.T("Browse"),()=>{var dialog=new OpenFileDialog();if(dialog.ShowDialog(this)==true)input.Text=dialog.FileName;}));Note("Add Focus window after opening an app before sending input.");}
        }
        if(AutomationEditing.IsStart(s.Kind)||AutomationEditing.IsEnd(s.Kind))Note("Moving, duplicating or removing a boundary applies to the whole block.");
        if(s.Kind==AutomationKind.Repeat||s.Kind is AutomationKind.IfPixel or AutomationKind.IfWindow)Note("Select this row and add an action to place it inside the block.");
        if(!AutomationEditing.IsEnd(s.Kind))details.Children.Add(Ui.Button(L.T("Test selected action"),()=>_=TestAction(s)));
        foreach(var button in details.Children.OfType<Button>())button.Margin=new Thickness(0,8,0,0);
    }
    private void RunSettings()
    {
        if(current==null)return;var s=current;
        details.Children.Add(Ui.Text(L.T("Run settings"),20,true));
        Note("Choose where actions run and when this workflow starts.");
        var target=Field(details,"Target window (blank = current)",s.TargetWindow,v=>s.TargetWindow=v);
        details.Children.Add(Ui.Button(L.T("Choose window"),()=>ChooseWindow(v=>target.Text=v)));
        Number(details,"Delay between actions (ms)",s.StepDelayMs,v=>s.StepDelayMs=v);
        var shortcut=Field(details,"Run shortcut",s.Shortcut,v=>s.Shortcut=v);details.Children.Add(Ui.Button(L.T("Record shortcut"),()=>RecordShortcut(shortcut,false)));
        Number(details,"Every N minutes (0 = off)",s.IntervalMinutes,v=>s.IntervalMinutes=v);
        Field(details,"Daily at (HH:mm, blank = off)",s.DailyTime,v=>s.DailyTime=v);
        var trigger=Field(details,"When window appears",s.WindowTrigger,v=>s.WindowTrigger=v);details.Children.Add(Ui.Button(L.T("Choose window"),()=>ChooseWindow(v=>trigger.Text=v)));
        void Check(string label,bool value,Action<bool> change){var checkbox=Ui.Toggle(value,v=>{RememberEdit();change(v);});details.Children.Add(Ui.Row(L.T(label),null,checkbox));}
        Check("Run when DesktopTools starts",s.RunOnStartup,v=>s.RunOnStartup=v);
        Check("Enable automatic triggers",s.Armed,v=>s.Armed=v);
        Note("Schedules run while DesktopTools is open. Window triggers use part of a title.");
        Note("Save to apply triggers. Missed daily runs are not replayed after restarting.");
        var buttons=new WrapPanel{Margin=new Thickness(0,20,0,0)};buttons.Children.Add(Ui.Button(L.T("Duplicate"),Duplicate));buttons.Children.Add(Ui.Button(L.T("Delete automation"),Delete));details.Children.Add(buttons);
        foreach(var button in details.Children.OfType<Button>())button.Margin=new Thickness(0,8,0,0);
    }
    private void RecordShortcut(TextBox input,bool action)
    {
        var dialog=new ShortcutRecorderWindow(input.Text,v=>{input.Text=v;return true;},allowUnmodified:action,validate:action?v=>{try{AutomationInput.ParseKeys(v);return null;}catch(Exception e){return e.Message;}}:null){Owner=this};dialog.ShowDialog();
    }
    private void ChooseWindow(Action<string> choose)
    {
        var dialog=new Window{Title=L.T("Choose window"),Width=620,Height=510,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};UtilityWindowChrome.EnableBackdrop(dialog);
        var root=new DockPanel();var top=new StackPanel();top.Children.Add(UtilityWindowChrome.Header(dialog,dialog.Title,dialog.Close,L.T("Close"),22));var hint=Ui.Text(L.T("Choose an open window. You can shorten its title afterwards."),13,muted:true);hint.Margin=new Thickness(0,12,0,12);top.Children.Add(hint);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var list=new ListBox{DisplayMemberPath="Title"};StyleList(list);
        var bottom=new WrapPanel{Margin=new Thickness(0,12,0,0)};var select=Ui.Button(L.T("Choose window"),()=>{if(list.SelectedItem is RecordingWindowInfo info){choose(info.Title);dialog.Close();}},true);select.IsEnabled=false;bottom.Children.Add(select);bottom.Children.Add(Ui.Button(L.T("Refresh"),()=>list.ItemsSource=RecordingWindows.GetAll()));DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        list.SelectionChanged+=(_,_)=>select.IsEnabled=list.SelectedItem!=null;list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is RecordingWindowInfo info){choose(info.Title);dialog.Close();}};root.Children.Add(list);list.ItemsSource=RecordingWindows.GetAll();dialog.Content=Ui.Card(root,20);dialog.ShowDialog();
    }
    private async Task TestAction(AutomationStep s)
    {
        if(current==null)return;try{var (start,count)=AutomationEditing.Range(current,current.Steps.IndexOf(s));var sample=current.Copy();sample.Steps=sample.Steps.Skip(start).Take(count).ToList();await service.RunAsync(sample);if(!closed){Show();Activate();}}catch(Exception e){Report(e.Message);}
    }
}
