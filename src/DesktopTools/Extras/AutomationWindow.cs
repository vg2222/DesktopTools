using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using Microsoft.Win32;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.Localization;
using DesktopTools.UI;
namespace DesktopTools.Extras;

internal sealed partial class AutomationWindow : Window
{
    private readonly AutomationService service;
    private List<AutomationScript> drafts;
    private AutomationScript? current;
    private readonly ListBox scripts=new(),steps=new();
    private readonly StackPanel details=new();
    private readonly TextBlock status=Ui.Text("",12,muted:true),stepCount=Ui.Text("",12,muted:true);
    private readonly TextBox name=new(){FontSize=20,FontWeight=FontWeights.SemiBold,MaxLength=120,MinHeight=38};
    private readonly List<FrameworkElement> editable=[];
    private readonly Stack<List<AutomationScript>> undo=[];
    private readonly Button runButton,stopButton;
    private readonly FrameworkElement empty;
    private bool closed,closingWindow,refreshing,dirty,settingsMode,editingSession;
    internal AutomationWindow(AutomationService service)
    {
        this.service=service;drafts=service.Scripts.Select(s=>s.Copy()).ToList();
        Title=L.T("Desktop Automation");Tag="Desktop Automation";Width=1280;Height=830;MinWidth=1040;MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;ResizeMode=ResizeMode.CanResizeWithGrip;UtilityWindowChrome.EnableBackdrop(this);
        var root=new DockPanel();
        var chrome=UtilityWindowChrome.Header(this,Title,Close,L.T("Close"),13,allowMinimize:true,allowMaximize:true);
        DockPanel.SetDock(chrome,Dock.Top);root.Children.Add(chrome);
        var heading=new DockPanel{Margin=new Thickness(0,12,0,22)};
        var commands=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        commands.Children.Add(EditButton("Save",()=>Save()));
        runButton=EditButton("Run automation",()=>_=RunAsync(),true);commands.Children.Add(runButton);
        stopButton=Ui.Button(L.T("Stop"),()=>{service.Stop();StopRecording();pickCancellation?.Cancel();});commands.Children.Add(stopButton);
        DockPanel.SetDock(commands,Dock.Right);heading.Children.Add(commands);
        var intro=new StackPanel();intro.Children.Add(Ui.Text(L.T("Desktop Automation"),27,true));
        var subtitle=Ui.Text(L.T("Turn everyday tasks into reusable workflows."),13,muted:true);subtitle.Margin=new Thickness(0,6,12,0);intro.Children.Add(subtitle);heading.Children.Add(intro);
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var footer=new DockPanel{Margin=new Thickness(0,14,0,0)};var esc=Ui.Text(L.T("Esc stops execution or recording."),12,muted:true);
        DockPanel.SetDock(esc,Dock.Right);footer.Children.Add(esc);status.Margin=new Thickness(0,0,16,0);footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var columns=new Grid();columns.ColumnDefinitions.Add(new(){Width=new GridLength(205)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(300)});
        StyleList(scripts);StyleList(steps);
        var library=new DockPanel{Margin=new Thickness(0,0,14,0)};
        var libTop=new StackPanel();libTop.Children.Add(Ui.Text(L.T("My workflows"),14,true));
        var newRow=new WrapPanel{Margin=new Thickness(0,12,0,10)};newRow.Children.Add(EditButton("New",New));newRow.Children.Add(EditButton("Templates",Templates));libTop.Children.Add(newRow);DockPanel.SetDock(libTop,Dock.Top);library.Children.Add(libTop);
        var io=new WrapPanel{Margin=new Thickness(0,10,0,0)};io.Children.Add(EditButton("Import",Import));io.Children.Add(EditButton("Export",Export));DockPanel.SetDock(io,Dock.Bottom);library.Children.Add(io);library.Children.Add(scripts);columns.Children.Add(library);
        var center=new DockPanel{Margin=new Thickness(0,0,14,0)};var top=new StackPanel();
        name.TextChanged+=(_,_)=>{if(refreshing||current==null)return;RememberEdit();current.Name=name.Text;scripts.Items.Refresh();};name.LostKeyboardFocus+=(_,_)=>editingSession=false;top.Children.Add(name);
        var meta=new DockPanel{Margin=new Thickness(0,10,0,10)};var settings=EditButton("Run settings",()=>{settingsMode=true;RefreshEditor();});DockPanel.SetDock(settings,Dock.Right);meta.Children.Add(settings);stepCount.VerticalAlignment=VerticalAlignment.Center;meta.Children.Add(stepCount);top.Children.Add(meta);
        var bar=new WrapPanel();bar.Children.Add(EditButton("Add action",OpenActions,true));bar.Children.Add(EditButton("Record actions",()=>_=RecordAsync()));bar.Children.Add(EditButton("Undo last edit",Undo));top.Children.Add(bar);top.Margin=new Thickness(0,0,12,12);DockPanel.SetDock(top,Dock.Top);center.Children.Add(top);
        var canvas=new Grid();canvas.Children.Add(steps);
        var hint=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=310};
        hint.Children.Add(Ui.Text(L.T("Start with one action"),20,true));
        var help=Ui.Text(L.T("Add an action, record your inputs or choose a template."),13,muted:true);help.Margin=new Thickness(0,10,0,16);hint.Children.Add(help);hint.Children.Add(EditButton("Add action",OpenActions,true));empty=hint;canvas.Children.Add(hint);center.Children.Add(canvas);Grid.SetColumn(center,1);columns.Children.Add(center);
        var editor=Ui.Card(new ScrollViewer{Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},16);editor.Margin=new Thickness(0);Grid.SetColumn(editor,2);columns.Children.Add(editor);root.Children.Add(columns);
        var card=Ui.Card(root,20);card.Margin=new Thickness(0);card.SetResourceReference(Border.BackgroundProperty,"Surface");Content=card;
        editable.AddRange([scripts,steps,name,details]);
        scripts.SelectionChanged+=(_,_)=>{if(refreshing)return;editingSession=false;current=scripts.SelectedItem as AutomationScript;RefreshSteps();};
        steps.SelectionChanged+=(_,_)=>{if(refreshing)return;editingSession=false;settingsMode=false;RefreshEditor();};
        service.Changed+=ServiceChanged;service.Progress+=StepProgress;service.StatusChanged+=Report;
        PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.S){e.Handled=true;if(!service.IsActive)Save();}else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Z&&Keyboard.FocusedElement is not TextBox){e.Handled=true;Undo();}};
        Closing+=(_,e)=>{
            closingWindow=true;StopRecording();pickCancellation?.Cancel();
            if(dirty&&!closed){var answer=MessageBox.Show(this,L.T("Save automation before closing?"),Title,MessageBoxButton.YesNoCancel,MessageBoxImage.Question);if(answer==MessageBoxResult.Cancel||answer==MessageBoxResult.Yes&&!Save())e.Cancel=true;}
            if(e.Cancel)Dispatcher.BeginInvoke(()=>{closingWindow=false;Show();RefreshSteps();Activate();});
        };
        Closed+=(_,_)=>{closed=true;pickCancellation?.Cancel();service.Stop();StopRecording();service.Changed-=ServiceChanged;service.Progress-=StepProgress;service.StatusChanged-=Report;};
        if(drafts.Count==0)drafts.Add(new(){Name=L.T("My automation")});current=drafts[0];RefreshScripts();ServiceChanged();
    }
    private Button EditButton(string label,Action action,bool primary=false){var b=Ui.Button(L.T(label),action,primary);editable.Add(b);return b;}
    private static void StyleList(ListBox list)
    {
        list.SetResourceReference(Control.BackgroundProperty,"Surface");list.SetResourceReference(Control.ForegroundProperty,"Text");list.BorderThickness=new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        list.ItemContainerStyle=(Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>
<Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='FontFamily' Value='{DynamicResource BodyFont}'/><Setter Property='FontSize' Value='13'/>
<Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='12'/><Setter Property='Margin' Value='0,0,0,6'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Tile' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='8' Background='{DynamicResource Card}' BorderThickness='1' BorderBrush='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
<ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Tile' Property='Background' Value='{DynamicResource Selected}'/><Setter TargetName='Tile' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Tile' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter></Style>");
    }
    private void Report(string message){if(!closed)status.Text=message;}
    private void ServiceChanged(){if(closed)return;foreach(var b in editable)b.IsEnabled=!service.IsActive;stopButton.IsEnabled=service.IsActive;runButton.IsEnabled=!service.IsActive&&current?.Steps.Count>0;}
    private void StepProgress(int index){if(!closed&&index<steps.Items.Count){steps.SelectedIndex=index;steps.ScrollIntoView(steps.SelectedItem);status.Text=L.F($"Running action {index+1} of {steps.Items.Count}");}}
    private void Remember(){if(undo.Count>=40)undo.Clear();undo.Push(drafts.Select(s=>s.Copy()).ToList());dirty=true;editingSession=false;}
    private void RememberEdit(){if(!editingSession){Remember();editingSession=true;}dirty=true;}
    private void Undo(){if(service.IsActive||undo.Count==0)return;var id=current?.Id;drafts=undo.Pop();current=drafts.FirstOrDefault(s=>s.Id==id)??drafts.FirstOrDefault();dirty=true;editingSession=false;RefreshScripts();}
    private void RefreshScripts(){refreshing=true;scripts.ItemsSource=null;scripts.DisplayMemberPath="Name";scripts.ItemsSource=drafts;scripts.SelectedItem=current;refreshing=false;RefreshSteps();}
    private void New(){if(drafts.Count>=100){Report(L.T("Invalid or oversized automation."));return;}Remember();current=new(){Name=L.T("My automation")};drafts.Add(current);RefreshScripts();}
    private void Duplicate(){if(current==null||drafts.Count>=100)return;Remember();var copy=current.Copy();copy.Id=Guid.NewGuid();copy.Name=copy.Name[..Math.Min(copy.Name.Length,118)]+" 2";copy.Armed=false;copy.Shortcut="";drafts.Add(copy);current=copy;RefreshScripts();}
    private void Delete(){if(current==null)return;if(MessageBox.Show(this,L.T("Delete this automation?"),Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Remember();drafts.Remove(current);current=drafts.FirstOrDefault();RefreshScripts();}
    private bool Save(){try{service.Save(drafts);dirty=false;Report(L.T("Automation saved."));return true;}catch(Exception e){Report(e.Message);return false;}}
    private async Task RunAsync(){if(current==null||!Save())return;await service.RunAsync(current.Copy());if(!closed){Show();Activate();}}
    private void EditStructure(Func<int> action){if(current==null)return;try{Remember();int index=action();RefreshSteps(index>=0&&index<current.Steps.Count?current.Steps[index]:null);}catch(Exception e){Report(e.Message);}}
    private void RefreshSteps(AutomationStep? selected=null)
    {
        refreshing=true;name.Text=current?.Name??"";steps.Items.Clear();int depth=0;
        if(current!=null)foreach(var s in current.Steps){
            if(AutomationEditing.IsEnd(s.Kind))depth=Math.Max(0,depth-1);
            var content=new StackPanel();content.Children.Add(Ui.Text($"{steps.Items.Count+1:00}   {AutomationCatalog.Label(s.Kind)}",13,true));
            string summary=AutomationCatalog.Summary(s);if(summary.Length>0){var sub=Ui.Text(summary,12,muted:true);sub.Margin=new Thickness(27,5,0,0);sub.MaxHeight=36;content.Children.Add(sub);}
            var row=new ListBoxItem{Tag=s,Content=content,Padding=new Thickness(12+depth*16,12,10,12)};steps.Items.Add(row);if(s==selected)steps.SelectedItem=row;
            if(AutomationEditing.IsStart(s.Kind)||s.Kind==AutomationKind.Else)depth++;
        }
        if(steps.SelectedIndex<0&&steps.Items.Count>0)steps.SelectedIndex=0;refreshing=false;
        empty.Visibility=steps.Items.Count==0?Visibility.Visible:Visibility.Collapsed;stepCount.Text=L.F($"{steps.Items.Count} actions");
        RefreshEditor();ServiceChanged();
    }
    private void OpenActions()
    {
        if(current==null){New();if(current==null)return;}
        var dialog=new Window{Title=L.T("Add action"),Width=570,Height=670,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.CanResize,MinWidth=450,MinHeight=450};
        UtilityWindowChrome.EnableBackdrop(dialog);var root=new DockPanel();var header=new StackPanel();header.Children.Add(UtilityWindowChrome.Header(dialog,dialog.Title,dialog.Close,L.T("Close"),22));
        var search=new TextBox{MinHeight=38,Margin=new Thickness(0,16,0,12),ToolTip=L.T("Search actions")};header.Children.Add(Ui.Text(L.T("Search actions"),12,muted:true));header.Children.Add(search);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var entries=new StackPanel();root.Children.Add(new ScrollViewer{Content=entries,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        void Fill(){entries.Children.Clear();foreach(var group in AutomationCatalog.Entries.Where(e=>(L.T(e.Label)+" "+L.T(e.Help)+" "+L.T(e.Group)).Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).GroupBy(e=>e.Group)){
            var title=Ui.Text(L.T(group.Key),12,true,muted:true);title.Margin=new Thickness(0,12,0,8);entries.Children.Add(title);
            foreach(var entry in group){var content=new StackPanel();content.Children.Add(Ui.Text(L.T(entry.Label),14,true));var help=Ui.Text(L.T(entry.Help),12,muted:true);help.Margin=new Thickness(0,5,0,0);content.Children.Add(help);
                var button=Ui.Button("",()=>{EditStructure(()=>AutomationEditing.Insert(current!,steps.SelectedIndex,entry.Kind));dialog.Close();});button.Content=content;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,0,0,6);entries.Children.Add(button);}}}
        search.TextChanged+=(_,_)=>Fill();Fill();dialog.Content=Ui.Card(root,20);dialog.Loaded+=(_,_)=>search.Focus();dialog.ShowDialog();
    }
    private void Templates()
    {
        var menu=new ContextMenu();
        void Add(string label,params AutomationStep[] actions){var item=new MenuItem{Header=L.T(label)};item.Click+=(_,_)=>{if(drafts.Count>=100)return;Remember();current=new(){Name=L.T(label),Steps=actions.Select(a=>a.Copy()).ToList()};drafts.Add(current);RefreshScripts();};menu.Items.Add(item);}
        Add("Text snippet",new AutomationStep(){Kind=AutomationKind.Text,Text=L.T("Your text here")});
        Add("Repeat a shortcut",new(){Kind=AutomationKind.Repeat,Value=3},new(){Kind=AutomationKind.Keys,Text="Tab"},new(){Kind=AutomationKind.Wait,Value=500},new(){Kind=AutomationKind.EndRepeat});
        Add("Open a website",new AutomationStep(){Kind=AutomationKind.Launch,Text="https://example.com"});
        menu.IsOpen=true;
    }
    private void Import()
    {
        var dialog=new OpenFileDialog{Filter="JSON (*.json)|*.json"};if(dialog.ShowDialog(this)!=true)return;
        try{if(drafts.Count>=100||new System.IO.FileInfo(dialog.FileName).Length>2_000_000)throw new ArgumentException(L.T("Invalid or oversized automation."));
            var script=JsonSerializer.Deserialize<AutomationScript>(System.IO.File.ReadAllText(dialog.FileName))??throw new ArgumentException(L.T("Invalid or oversized automation."));AutomationProgram.Compile(script);
            script.Id=Guid.NewGuid();script.Armed=false;script.Shortcut="";Remember();drafts.Add(script);current=script;RefreshScripts();Report(L.T("Workflow imported with automatic triggers disabled."));}catch(Exception e){Report(e.Message);}
    }
    private void Export()
    {
        if(current==null)return;try{AutomationProgram.Compile(current);var dialog=new SaveFileDialog{Filter="JSON (*.json)|*.json",FileName="automation.json"};if(dialog.ShowDialog(this)==true)System.IO.File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(current,new JsonSerializerOptions{WriteIndented=true}));}catch(Exception e){Report(e.Message);}
    }
}
