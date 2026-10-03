using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
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
    private readonly TextBlock status=Ui.Text("",13),stepCount=Ui.Text("",12,muted:true);
    private readonly TextBox name=new(){FontSize=20,FontWeight=FontWeights.SemiBold,MaxLength=120,MinHeight=40,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(0,5,0,5)};
    private readonly List<FrameworkElement> editable=[];
    private readonly Stack<List<AutomationScript>> undo=[];
    private readonly Button runButton,stopButton;
    private readonly FrameworkElement empty;
    private readonly Border notice=new(){Visibility=Visibility.Collapsed,CornerRadius=new CornerRadius(10),Padding=new Thickness(12,10,8,10),BorderThickness=new Thickness(1),Margin=new Thickness(0,10,0,0)};
    private readonly ContentControl noticeIcon=new(){Width=18,Height=18,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
    private bool closed,closingWindow,refreshing,dirty,editingSession,savePromptOpen;
    private int noticeVersion;
    internal AutomationWindow(AutomationService service)
    {
        this.service=service;drafts=service.Scripts.Select(s=>s.Copy()).ToList();
        Title=L.T("Desktop Automation");Tag="Desktop Automation";Width=1260;Height=800;MinWidth=1040;MinHeight=620;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;ResizeMode=ResizeMode.CanResize;
        UtilityWindowChrome.EnableBackdrop(this);
        var root=new DockPanel();
        var chrome=UtilityWindowChrome.Header(this,Title,Close,L.T("Close"),16,allowMinimize:true,allowMaximize:true);
        chrome.Margin=new Thickness(0,0,0,14);DockPanel.SetDock(chrome,Dock.Top);root.Children.Add(chrome);
        ConfigureNotice();DockPanel.SetDock(notice,Dock.Bottom);root.Children.Add(notice);
        var columns=new Grid();columns.ColumnDefinitions.Add(new(){Width=new GridLength(210)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(300)});
        StyleList(scripts);StyleList(steps);
        var library=new DockPanel{Margin=new Thickness(0,0,20,0)};
        var libTop=new DockPanel{Margin=new Thickness(0,0,0,12)};
        var create=EditIcon("Plus","New automation",New);DockPanel.SetDock(create,Dock.Right);libTop.Children.Add(create);libTop.Children.Add(Ui.Text(L.T("My workflows"),14,true));
        DockPanel.SetDock(libTop,Dock.Top);library.Children.Add(libTop);
        var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};
        footer.Children.Add(EditButton("Templates",Templates,icon:"Profiles"));
        var io=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};io.Children.Add(EditIcon("Folder","Import",Import));io.Children.Add(EditIcon("Save","Export",Export));footer.Children.Add(io);
        DockPanel.SetDock(footer,Dock.Bottom);library.Children.Add(footer);library.Children.Add(scripts);columns.Children.Add(library);
        var center=new DockPanel{Margin=new Thickness(0,0,20,0)};var top=new StackPanel();
        var title=new DockPanel();var more=EditIcon("More","More options",WorkflowMenu);DockPanel.SetDock(more,Dock.Right);title.Children.Add(more);title.Children.Add(name);top.Children.Add(title);
        name.TextChanged+=(_,_)=>{if(refreshing||current==null)return;RememberEdit();current.Name=name.Text;RefreshWorkflowNames();};name.LostKeyboardFocus+=(_,_)=>editingSession=false;
        var metadata=new DockPanel{Margin=new Thickness(0,5,0,12)};var settings=EditButton("Run settings",OpenRunSettings,icon:"Settings");DockPanel.SetDock(settings,Dock.Right);metadata.Children.Add(settings);metadata.Children.Add(stepCount);top.Children.Add(metadata);
        var bar=new Grid();bar.ColumnDefinitions.Add(new());bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(EditButton("Add action",OpenActions,icon:"Plus"));actions.Children.Add(EditIcon("Record","Record actions",()=>_=RecordAsync()));actions.Children.Add(EditIcon("Undo","Undo last edit",Undo));bar.Children.Add(actions);
        var execution=new StackPanel{Orientation=Orientation.Horizontal};execution.Children.Add(EditIcon("Save","Save",()=>Save()));
        runButton=EditButton("Run",()=>_=RunAsync(),true,"Play");execution.Children.Add(runButton);
        stopButton=Ui.IconButton("Stop",L.T("Stop"),()=>{service.Stop();StopRecording();pickCancellation?.Cancel();});execution.Children.Add(stopButton);
        Grid.SetColumn(execution,1);bar.Children.Add(execution);top.Children.Add(bar);top.Margin=new Thickness(0,0,0,16);DockPanel.SetDock(top,Dock.Top);center.Children.Add(top);
        var canvas=new Grid();canvas.Children.Add(steps);
        var hint=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=290};
        var symbol=Ui.Icon("Utilities",34);symbol.HorizontalAlignment=HorizontalAlignment.Center;symbol.Margin=new Thickness(0,0,0,16);hint.Children.Add(symbol);
        var heading=Ui.Text(L.T("Start with one action"),18,true);heading.TextAlignment=TextAlignment.Center;hint.Children.Add(heading);
        var help=Ui.Text(L.T("Add an action, record your inputs or choose a template."),12,muted:true);help.TextAlignment=TextAlignment.Center;help.Margin=new Thickness(0,8,0,16);hint.Children.Add(help);
        var add=EditButton("Add action",OpenActions,true,"Plus");add.HorizontalAlignment=HorizontalAlignment.Center;hint.Children.Add(add);empty=hint;canvas.Children.Add(hint);
        center.Children.Add(canvas);Grid.SetColumn(center,1);columns.Children.Add(center);
        var inspector=Ui.Card(Scroll(details),16);inspector.Margin=new Thickness(0);inspector.CornerRadius=new CornerRadius(12);Grid.SetColumn(inspector,2);columns.Children.Add(inspector);
        root.Children.Add(columns);
        var card=Ui.Card(root,18);card.Margin=new Thickness(0);Content=card;
        editable.AddRange([scripts,steps,name,details]);
        scripts.SelectionChanged+=(_,_)=>{if(refreshing)return;editingSession=false;current=(scripts.SelectedItem as ListBoxItem)?.Tag as AutomationScript;RefreshSteps();if(IsLoaded&&!service.IsActive){Motion.PageTransition(steps);Motion.Transition(details);}};
        steps.SelectionChanged+=(_,_)=>{if(refreshing)return;editingSession=false;RefreshEditor();if(details.IsVisible&&!service.IsActive)Motion.Transition(details);};
        service.Changed+=ServiceChanged;service.Progress+=StepProgress;service.StatusChanged+=Report;
        PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.S){e.Handled=true;if(!service.IsActive)Save();}else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.Z&&Keyboard.FocusedElement is not TextBox){e.Handled=true;Undo();}};
        Closing+=(_,e)=>{
            if(savePromptOpen){e.Cancel=true;return;}
            closingWindow=true;StopRecording();pickCancellation?.Cancel();
            if(dirty&&!closed){
                savePromptOpen=true;
                try{
                    var dialog=new AutomationSaveChangesWindow(()=>Save()?null:status.Text){Owner=this};dialog.ShowDialog();
                    if(dialog.Choice==AutomationSaveChoice.Cancel)e.Cancel=true;
                }finally{savePromptOpen=false;}
            }
            if(e.Cancel)Dispatcher.BeginInvoke(()=>{closingWindow=false;Show();RefreshSteps();Activate();});
        };
        Closed+=(_,_)=>{closed=true;pickCancellation?.Cancel();service.Stop();StopRecording();service.Changed-=ServiceChanged;service.Progress-=StepProgress;service.StatusChanged-=Report;};
        if(drafts.Count==0)drafts.Add(new(){Name=L.T("My automation")});current=drafts[0];RefreshScripts();ServiceChanged();
    }
    private Button EditButton(string label,Action action,bool primary=false,string? icon=null)
    {
        var b=Ui.Button(L.T(label),action,primary);if(icon!=null)b.Content=Ui.IconLabel(icon,L.T(label),16,primary,12);
        b.Height=36;editable.Add(b);return b;
    }
    private Button EditIcon(string icon,string label,Action action){var b=Ui.IconButton(icon,L.T(label),action);editable.Add(b);return b;}
    private static ScrollViewer Scroll(UIElement content)
    {
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        scroll.SetResourceReference(StyleProperty,"PageScroller");SmoothScroll.Enable(scroll);return scroll;
    }
    private static void StyleList(ListBox list)
    {
        list.Background=Brushes.Transparent;list.SetResourceReference(Control.ForegroundProperty,"Text");list.BorderThickness=new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        list.ItemContainerStyle=(Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>
<Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='FontFamily' Value='{DynamicResource BodyFont}'/><Setter Property='FontSize' Value='13'/>
<Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='10'/><Setter Property='Margin' Value='0,0,0,6'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Tile' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='9' Background='{DynamicResource Field}' BorderThickness='1' BorderBrush='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
<ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Tile' Property='Background' Value='{DynamicResource Selected}'/><Setter TargetName='Tile' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Tile' Property='BorderBrush' Value='{DynamicResource Stroke}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Tile' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter></Style>");
    }
    private void ServiceChanged(){if(closed)return;foreach(var b in editable)b.IsEnabled=!service.IsActive;stopButton.Visibility=service.IsActive?Visibility.Visible:Visibility.Collapsed;runButton.IsEnabled=!service.IsActive&&current?.Steps.Count>0;}
    private void StepProgress(int index){if(!closed&&index<steps.Items.Count){steps.SelectedIndex=index;steps.ScrollIntoView(steps.SelectedItem);ShowMessage(L.F($"Running action {index+1} of {steps.Items.Count}"),"Play",false);}}

    private void Remember(){if(undo.Count>=40)undo.Clear();undo.Push(drafts.Select(s=>s.Copy()).ToList());dirty=true;editingSession=false;}
    private void RememberEdit(){if(!editingSession){Remember();editingSession=true;}dirty=true;}
    private void Undo(){if(service.IsActive||undo.Count==0)return;var id=current?.Id;drafts=undo.Pop();current=drafts.FirstOrDefault(s=>s.Id==id)??drafts.FirstOrDefault();dirty=true;editingSession=false;RefreshScripts();}
    private void RefreshScripts(){refreshing=true;FillWorkflows();refreshing=false;RefreshSteps();}
    private void New(){if(drafts.Count>=100){Report(L.T("Invalid or oversized automation."));return;}Remember();current=new(){Name=L.T("My automation")};drafts.Add(current);RefreshScripts();}
    private void Duplicate(){if(current==null||drafts.Count>=100)return;Remember();var copy=current.Copy();copy.Id=Guid.NewGuid();copy.Name=copy.Name[..Math.Min(copy.Name.Length,118)]+" 2";copy.Armed=false;copy.Shortcut="";drafts.Add(copy);current=copy;RefreshScripts();}
    private void Delete(){if(current==null)return;if(MessageBox.Show(this,L.T("Delete this automation?"),Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Remember();drafts.Remove(current);current=drafts.FirstOrDefault();RefreshScripts();}
    private bool Save(){try{service.Save(drafts);dirty=false;Report(L.T("Automation saved."));return true;}catch(Exception e){ReportError(e.Message);return false;}}
    private async Task RunAsync(){if(current==null||!Save())return;await service.RunAsync(current.Copy());if(!closed){Show();Activate();}}
    private void EditStructure(Func<int> action){if(current==null)return;try{var positions=AutomationMotion.CaptureRows(steps);Remember();int index=action();RefreshSteps(index>=0&&index<current.Steps.Count?current.Steps[index]:null);AutomationMotion.MoveRows(steps,positions);if(details.IsVisible)Motion.Transition(details);}catch(Exception e){ReportError(e.Message);}}
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
            script.Id=Guid.NewGuid();script.Armed=false;script.Shortcut="";Remember();drafts.Add(script);current=script;RefreshScripts();Report(L.T("Workflow imported with automatic triggers disabled."));}catch(Exception e){ReportError(e.Message);}
    }
    private void Export()
    {
        if(current==null)return;try{AutomationProgram.Compile(current);var dialog=new SaveFileDialog{Filter="JSON (*.json)|*.json",FileName="automation.json"};if(dialog.ShowDialog(this)==true)System.IO.File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(current,new JsonSerializerOptions{WriteIndented=true}));}catch(Exception e){ReportError(e.Message);}
    }
}
