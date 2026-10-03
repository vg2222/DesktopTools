using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using Microsoft.Win32;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.Localization;
using DesktopTools.UI;
namespace DesktopTools.Extras;

internal sealed partial class AutomationWindow
{
    private static Button ActionButton(string icon,string label,Action click){var b=Ui.Button(L.T(label),click);b.Content=Ui.IconLabel(icon,L.T(label),16,textSize:12);return b;}
    private static void AttachPicker(Panel panel,TextBox input,string icon,string label,Action click){panel.Children.Remove(input);var row=new DockPanel();var button=Ui.IconButton(icon,L.T(label),click);button.Margin=new Thickness(6,0,0,0);DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);row.Children.Add(input);panel.Children.Add(row);}
    private TextBox Field(Panel panel,string label,string text,Action<string> change,bool multiline=false)
    {
        var title=Ui.Text(L.T(label),12,muted:true);title.Margin=new Thickness(0,14,0,6);panel.Children.Add(title);
        var input=new TextBox{Text=text,MinHeight=multiline?110:36,MaxHeight=multiline?200:36,MaxLength=20000,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};
        input.TextChanged+=(_,_)=>{RememberEdit();change(input.Text);UpdateSelectedSummary();};input.LostKeyboardFocus+=(_,_)=>editingSession=false;AutomationProperties.SetName(input,L.T(label));panel.Children.Add(input);return input;
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
        if(steps.SelectedItem is not ListBoxItem row||row.Tag is not AutomationStep s||row.Content is not Grid content)return;
        var copy=content.Children.OfType<StackPanel>().FirstOrDefault(p=>Grid.GetColumn(p)==2);
        var summary=copy?.Children.OfType<TextBlock>().FirstOrDefault(t=>Equals(t.Tag,"summary"));if(summary==null)return;
        summary.Text=AutomationCatalog.Summary(s);summary.Visibility=summary.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
    }
    private void RefreshEditor()
    {
        details.Children.Clear();if(current==null)return;
        if(steps.SelectedItem is not ListBoxItem){details.Children.Add(Ui.IconLabel("Select",L.T("Select an action"),18,textSize:16));return;}
        if(((ListBoxItem)steps.SelectedItem).Tag is not AutomationStep s)return;
        var heading=new DockPanel();var help=Ui.IconButton("Help",L.T("Action help"),()=>ShowMessage(AutomationCatalog.Help(s.Kind),"Help",false));Ui.Tip(help,AutomationCatalog.Help(s.Kind));help.Margin=new Thickness(8,0,0,0);DockPanel.SetDock(help,Dock.Right);heading.Children.Add(help);var titleRow=new Grid();titleRow.ColumnDefinitions.Add(new(){Width=new GridLength(30)});titleRow.ColumnDefinitions.Add(new());titleRow.Children.Add(Ui.Icon(AutomationCatalog.Icon(s.Kind),20));var actionTitle=Ui.Text(AutomationCatalog.Label(s.Kind),17,true);Grid.SetColumn(actionTitle,1);titleRow.Children.Add(actionTitle);heading.Children.Add(titleRow);details.Children.Add(heading);
        bool point=s.Kind is AutomationKind.Click or AutomationKind.DoubleClick or AutomationKind.RightClick or AutomationKind.Scroll or AutomationKind.Drag or AutomationKind.IfPixel or AutomationKind.WaitPixel or AutomationKind.MouseDown or AutomationKind.MouseUp or AutomationKind.Move;
        if(point){
            var coords=new Grid();coords.ColumnDefinitions.Add(new());coords.ColumnDefinitions.Add(new());var x=new StackPanel{Margin=new Thickness(0,0,8,0)};var y=new StackPanel();Grid.SetColumn(y,1);coords.Children.Add(x);coords.Children.Add(y);
            Number(x,"X (screen pixels)",s.X,v=>s.X=v);Number(y,"Y (screen pixels)",s.Y,v=>s.Y=v);details.Children.Add(coords);
            details.Children.Add(ActionButton("Select",s.Kind is AutomationKind.IfPixel or AutomationKind.WaitPixel?"Pick point and color":"Pick screen point",()=>_=PickAsync(s,false)));
            if(s.Kind==AutomationKind.Drag){Number(details,"End X",s.EndX,v=>s.EndX=v);Number(details,"End Y",s.EndY,v=>s.EndY=v);details.Children.Add(ActionButton("Select","Pick drag end",()=>_=PickAsync(s,true)));}
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
            if(window)AttachPicker(details,input,"Window","Choose window",()=>ChooseWindow(v=>input.Text=v));
            if(s.Kind==AutomationKind.Keys)AttachPicker(details,input,"Shortcuts","Record shortcut",()=>RecordShortcut(input,true));
            if(s.Kind==AutomationKind.Launch){AttachPicker(details,input,"Folder","Browse",()=>{var dialog=new OpenFileDialog();if(dialog.ShowDialog(this)==true)input.Text=dialog.FileName;});Note("Add Focus window after opening an app before sending input.");}
        }
        if(AutomationEditing.IsStart(s.Kind)||AutomationEditing.IsEnd(s.Kind))Ui.Tip(heading,L.T("Moving, duplicating or removing a boundary applies to the whole block."));
        if(s.Kind==AutomationKind.Repeat||s.Kind is AutomationKind.IfPixel or AutomationKind.IfWindow)Note("Select this row and add an action to place it inside the block.");
        if(!AutomationEditing.IsEnd(s.Kind))details.Children.Add(ActionButton("Play","Test action",()=>_=TestAction(s)));
        foreach(var button in details.Children.OfType<Button>())button.Margin=new Thickness(0,8,0,0);
    }
    private void RecordShortcut(TextBox input,bool action)
    {
        var dialog=new ShortcutRecorderWindow(input.Text,v=>{input.Text=v;return true;},allowUnmodified:action,validate:action?v=>{try{AutomationInput.ParseKeys(v);return null;}catch(Exception e){return e.Message;}}:null){Owner=this};dialog.ShowDialog();
    }
    private void ChooseWindow(Action<string> choose)=>ChooseWindowFor(this,choose);
    private void ChooseWindowFor(Window owner,Action<string> choose)
    {
        var dialog=new Window{Title=L.T("Choose window"),Width=620,Height=510,Owner=owner,WindowStartupLocation=WindowStartupLocation.CenterOwner};UtilityWindowChrome.EnableBackdrop(dialog);
        var root=new DockPanel();var top=new StackPanel();top.Children.Add(UtilityWindowChrome.Header(dialog,dialog.Title,dialog.Close,L.T("Close"),22));var hint=Ui.Text(L.T("Choose an open window. You can shorten its title afterwards."),13,muted:true);hint.Margin=new Thickness(0,12,0,12);top.Children.Add(hint);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var list=new ListBox{DisplayMemberPath="Title"};StyleList(list);
        var bottom=new WrapPanel{Margin=new Thickness(0,12,0,0)};var select=Ui.Button(L.T("Choose window"),()=>{if(list.SelectedItem is RecordingWindowInfo info){choose(info.Title);dialog.Close();}},true);select.IsEnabled=false;bottom.Children.Add(select);bottom.Children.Add(Ui.Button(L.T("Refresh"),()=>list.ItemsSource=RecordingWindows.GetAll()));DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        list.SelectionChanged+=(_,_)=>select.IsEnabled=list.SelectedItem!=null;list.MouseDoubleClick+=(_,_)=>{if(list.SelectedItem is RecordingWindowInfo info){choose(info.Title);dialog.Close();}};root.Children.Add(list);list.ItemsSource=RecordingWindows.GetAll();var surface=Ui.Card(root,20);surface.Margin=new Thickness(0);dialog.Content=surface;dialog.ShowDialog();
    }
    private async Task TestAction(AutomationStep s)
    {
        if(current==null)return;try{var (start,count)=AutomationEditing.Range(current,current.Steps.IndexOf(s));var sample=current.Copy();sample.Steps=sample.Steps.Skip(start).Take(count).ToList();await service.RunAsync(sample);if(!closed){Show();Activate();}}catch(Exception e){ReportError(e.Message);}
    }
}
