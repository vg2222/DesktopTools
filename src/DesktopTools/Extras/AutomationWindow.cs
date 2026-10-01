using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.Localization;
using DesktopTools.UI;
namespace DesktopTools.Extras;
internal sealed class AutomationWindow : Window
{
    private readonly AutomationService service;
    private List<AutomationScript> drafts;
    private AutomationScript? current;
    private readonly ListBox scripts=new(){MinWidth=200};
    private readonly ListBox steps=new(){MinHeight=180};
    private readonly StackPanel details=new();
    private readonly StackPanel properties=new();
    private readonly TextBlock status=Ui.Text("",12,muted:true);
    private readonly List<Button> busyButtons=[];
    private CancellationTokenSource? pickCancellation;
    private AutomationRecorder? recorder;
    private EscapeKeyService? recordEscape;
    private Window? recordHud;
    private bool closed,refreshing,dirty;
    private static readonly Dictionary<AutomationKind,string> Labels=new(){
        [AutomationKind.Click]="Click",[AutomationKind.DoubleClick]="Double click",[AutomationKind.RightClick]="Right click",[AutomationKind.Scroll]="Scroll",
        [AutomationKind.Drag]="Drag",[AutomationKind.Keys]="Press keys",[AutomationKind.Text]="Type text",[AutomationKind.Wait]="Wait",
        [AutomationKind.Launch]="Open app, file or URL",[AutomationKind.FocusWindow]="Focus window",[AutomationKind.Repeat]="Repeat",
        [AutomationKind.EndRepeat]="End repeat",[AutomationKind.IfWindow]="If window exists",[AutomationKind.IfPixel]="If pixel color",
        [AutomationKind.Else]="Otherwise",[AutomationKind.EndIf]="End condition",[AutomationKind.KeyDown]="Key down",[AutomationKind.KeyUp]="Key up",
        [AutomationKind.MouseDown]="Mouse down",[AutomationKind.MouseUp]="Mouse up",[AutomationKind.Move]="Move pointer"};
    internal AutomationWindow(AutomationService service)
    {
        this.service=service;drafts=service.Scripts.Select(s=>s.Copy()).ToList();
        Title=L.T("Desktop Automation");Tag="Desktop Automation";Width=1200;Height=820;MinWidth=860;MinHeight=600;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;ResizeMode=ResizeMode.CanResizeWithGrip;UtilityWindowChrome.EnableBackdrop(this);
        var root=new DockPanel();var header=UtilityWindowChrome.Header(this,Title,Close,L.T("Close"),19,allowMinimize:true,allowMaximize:true);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var intro=Ui.Text(L.T("Build local actions, repeat blocks and conditions. Esc stops execution or recording."),13,muted:true);intro.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);
        var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,14)};
        void Button(string label,Action action,bool primary=false){var b=Ui.Button(L.T(label),action,primary);toolbar.Children.Add(b);if(label!="Stop")busyButtons.Add(b);}
        Button("New automation",New);Button("Duplicate",Duplicate);Button("Delete automation",Delete);Button("Save automation",()=>Save(),true);
        Button("Run automation",()=>{if(Save()&&current!=null)_=service.RunAsync(current);});Button("Record actions",()=>_=RecordAsync());Button("Stop",()=>{service.Stop();StopRecording();pickCancellation?.Cancel();});
        DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        var columns=new Grid();columns.ColumnDefinitions.Add(new(){Width=new GridLength(220)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(300)});
        StyleList(scripts);StyleList(steps);
        scripts.Margin=new Thickness(0,0,12,0);columns.Children.Add(scripts);
        var center=new DockPanel{Margin=new Thickness(0,0,12,0)};var propScroll=new ScrollViewer{Content=properties,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};DockPanel.SetDock(propScroll,Dock.Top);center.Children.Add(propScroll);
        var actionBar=new WrapPanel{Margin=new Thickness(0,12,0,12)};
        var choice=Ui.Choice(Labels.Values.Take(16).ToArray(),"Click",_=>{});choice.MinWidth=160;actionBar.Children.Add(choice);
        var add=Ui.Button(L.T("Add action"),()=>{if(current==null)return;var kind=Labels.Single(p=>p.Value==(string)choice.SelectedItem).Key;var step=new AutomationStep{Kind=kind,Value=kind==AutomationKind.Repeat?2:kind==AutomationKind.Scroll?-360:500};
            current.Steps.Add(step);if(kind==AutomationKind.Repeat)current.Steps.Add(new(){Kind=AutomationKind.EndRepeat});
            if(kind is AutomationKind.IfWindow or AutomationKind.IfPixel){current.Steps.Add(new(){Kind=AutomationKind.Else});current.Steps.Add(new(){Kind=AutomationKind.EndIf});}
            dirty=true;RefreshSteps(step);});actionBar.Children.Add(add);busyButtons.Add(add);
        DockPanel.SetDock(actionBar,Dock.Top);center.Children.Add(actionBar);center.Children.Add(steps);Grid.SetColumn(center,1);columns.Children.Add(center);
        var editor=new ScrollViewer{Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(editor,2);columns.Children.Add(editor);root.Children.Add(columns);
        Content=Ui.Card(root,20);
        scripts.SelectionChanged+=(_,_)=>{if(refreshing)return;current=scripts.SelectedItem as AutomationScript;RefreshProperties();RefreshSteps();};
        steps.SelectionChanged+=(_,_)=>RefreshEditor();
        service.Changed+=ServiceChanged;service.Progress+=StepProgress;
        Closing+=(_,e)=>{if(dirty&&!closed){var answer=MessageBox.Show(this,L.T("Save automation before closing?"),Title,MessageBoxButton.YesNoCancel,MessageBoxImage.Question);if(answer==MessageBoxResult.Cancel){e.Cancel=true;return;}if(answer==MessageBoxResult.Yes&&!Save()){e.Cancel=true;return;}}};
        Closed+=(_,_)=>{closed=true;pickCancellation?.Cancel();service.Stop();StopRecording();service.Changed-=ServiceChanged;service.Progress-=StepProgress;};
        if(drafts.Count==0)New();else{current=drafts[0];RefreshScripts();}ServiceChanged();
    }
    private static void StyleList(ListBox list)
    {
        list.SetResourceReference(Control.BackgroundProperty,"Surface");list.SetResourceReference(Control.ForegroundProperty,"Text");list.BorderThickness=new Thickness(0);
        list.ItemContainerStyle=(Style)System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'>
<Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='FontFamily' Value='{DynamicResource BodyFont}'/><Setter Property='FontSize' Value='13'/>
<Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='10,12'/><Setter Property='Margin' Value='0,0,0,4'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'><Border x:Name='Tile' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='8' Background='{DynamicResource Card}' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
<ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Tile' Property='Background' Value='{DynamicResource Selected}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Tile' Property='BorderBrush' Value='{DynamicResource Accent}'/><Setter TargetName='Tile' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate></Setter.Value></Setter></Style>");
    }

    private void ServiceChanged(){if(closed)return;foreach(var b in busyButtons)b.IsEnabled=!service.IsActive;scripts.IsEnabled=steps.IsEnabled=properties.IsEnabled=details.IsEnabled=!service.IsActive;}
    private void StepProgress(int index){if(!closed && index<steps.Items.Count){steps.SelectedIndex=index;steps.ScrollIntoView(steps.SelectedItem);}}
    private void RefreshScripts(){refreshing=true;scripts.ItemsSource=null;scripts.DisplayMemberPath="Name";scripts.ItemsSource=drafts;scripts.SelectedItem=current;refreshing=false;RefreshProperties();RefreshSteps();}
    private void New(){if(drafts.Count>=100){status.Text=L.T("Invalid or oversized automation.");return;}current=new(){Name=L.T("My automation")};drafts.Add(current);dirty=true;RefreshScripts();}
    private void Duplicate(){if(current==null || drafts.Count>=100)return;var copy=current.Copy();copy.Id=Guid.NewGuid();copy.Name+=" 2";copy.Armed=false;copy.Shortcut="";drafts.Add(copy);current=copy;dirty=true;RefreshScripts();}
    private void Delete(){if(current==null)return;if(MessageBox.Show(this,L.T("Delete this automation?"),Title,MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;drafts.Remove(current);current=drafts.FirstOrDefault();dirty=true;RefreshScripts();}
    private bool Save(){try{service.Save(drafts);dirty=false;status.Text=L.T("Automation saved.");return true;}catch(Exception e){status.Text=e.Message;return false;}}
    private static void Field(Panel panel,string label,string text,Action<string> change)
    {
        var title=Ui.Text(L.T(label),12,muted:true);title.Margin=new Thickness(0,8,0,4);panel.Children.Add(title);
        var input=new TextBox{Text=text,MinHeight=36,MaxLength=20000};input.TextChanged+=(_,_)=>change(input.Text);panel.Children.Add(input);
    }
    private void RefreshProperties()
    {
        properties.Children.Clear();if(current==null)return;var s=current;
        Field(properties,"Automation name",s.Name,v=>{s.Name=v;dirty=true;});
        var triggers=new Grid();triggers.ColumnDefinitions.Add(new());triggers.ColumnDefinitions.Add(new());triggers.ColumnDefinitions.Add(new());
        var hotkey=new StackPanel();Field(hotkey,"Run shortcut",s.Shortcut,v=>{s.Shortcut=v;dirty=true;});hotkey.Margin=new Thickness(0,0,8,0);triggers.Children.Add(hotkey);
        var interval=new StackPanel();Field(interval,"Every N minutes (0 = off)",s.IntervalMinutes.ToString(),v=>{if(int.TryParse(v,out int n)){s.IntervalMinutes=n;dirty=true;}});Grid.SetColumn(interval,1);interval.Margin=new Thickness(0,0,8,0);triggers.Children.Add(interval);
        var window=new StackPanel();Field(window,"When window appears",s.WindowTrigger,v=>{s.WindowTrigger=v;dirty=true;});Grid.SetColumn(window,2);triggers.Children.Add(window);properties.Children.Add(triggers);
        var armed=new CheckBox{Content=L.T("Enable automatic triggers"),IsChecked=s.Armed,Margin=new Thickness(0,10,0,0)};armed.Checked+=(_,_)=>{s.Armed=true;dirty=true;};armed.Unchecked+=(_,_)=>{s.Armed=false;dirty=true;};properties.Children.Add(Ui.Row(L.T("Enable automatic triggers"),"",armed));
        var note=Ui.Text(L.T("Schedules run while DesktopTools is open. Window triggers use part of a title."),11,muted:true);note.Margin=new Thickness(0,6,0,0);properties.Children.Add(note);
    }
    private void RefreshSteps(AutomationStep? selected=null)
    {
        steps.Items.Clear();if(current==null){RefreshEditor();return;}int depth=0;
        foreach(var s in current.Steps){if(s.Kind is AutomationKind.EndIf or AutomationKind.EndRepeat or AutomationKind.Else)depth=Math.Max(0,depth-1);
            var row=new ListBoxItem{Tag=s,Content=$"{steps.Items.Count+1:00}  {L.T(Labels[s.Kind])}  {Summary(s)}",Padding=new Thickness(12+depth*16,12,8,12)};
            steps.Items.Add(row);if(s==selected)steps.SelectedItem=row;
            if(s.Kind is AutomationKind.Repeat or AutomationKind.IfWindow or AutomationKind.IfPixel or AutomationKind.Else)depth++;
        }
        if(steps.SelectedIndex<0&&steps.Items.Count>0)steps.SelectedIndex=0;RefreshEditor();
    }
    private static string Summary(AutomationStep s)=>s.Kind switch{AutomationKind.Click or AutomationKind.RightClick or AutomationKind.DoubleClick or AutomationKind.Drag or AutomationKind.IfPixel=>$"({s.X}, {s.Y})",AutomationKind.Wait=>$"{s.Value} ms",AutomationKind.Repeat=>$"× {s.Value}",_=>s.Text.Length>28?s.Text[..28]+"…":s.Text};
    private void RefreshEditor()
    {
        details.Children.Clear();if(steps.SelectedItem is not ListBoxItem row || row.Tag is not AutomationStep s)return;
        details.Children.Add(Ui.Text(L.T(Labels[s.Kind]),18,true));
        var move=new WrapPanel{Margin=new Thickness(0,12,0,6)};
        move.Children.Add(Ui.Button("↑",()=>Move(s,-1)));move.Children.Add(Ui.Button("↓",()=>Move(s,1)));move.Children.Add(Ui.Button(L.T("Remove action"),()=>{current!.Steps.Remove(s);dirty=true;RefreshSteps();}));details.Children.Add(move);
        bool point=s.Kind is AutomationKind.Click or AutomationKind.DoubleClick or AutomationKind.RightClick or AutomationKind.Scroll or AutomationKind.Drag or AutomationKind.IfPixel or AutomationKind.MouseDown or AutomationKind.MouseUp or AutomationKind.Move;
        if(point){
            Field(details,"X (screen pixels)",s.X.ToString(),v=>{if(int.TryParse(v,out int n)){s.X=n;dirty=true;}});
            Field(details,"Y (screen pixels)",s.Y.ToString(),v=>{if(int.TryParse(v,out int n)){s.Y=n;dirty=true;}});
            details.Children.Add(Ui.Button(L.T("Pick screen point"),()=>_=PickAsync(s,false)));
            if(s.Kind==AutomationKind.Drag){Field(details,"End X",s.EndX.ToString(),v=>{if(int.TryParse(v,out int n)){s.EndX=n;dirty=true;}});Field(details,"End Y",s.EndY.ToString(),v=>{if(int.TryParse(v,out int n)){s.EndY=n;dirty=true;}});details.Children.Add(Ui.Button(L.T("Pick drag end"),()=>_=PickAsync(s,true)));}
        }
        if(s.Kind is AutomationKind.Wait or AutomationKind.Repeat or AutomationKind.Scroll or AutomationKind.KeyDown or AutomationKind.KeyUp or AutomationKind.MouseDown or AutomationKind.MouseUp)
            Field(details,s.Kind==AutomationKind.Wait?"Milliseconds":s.Kind==AutomationKind.Repeat?"Repeat count":s.Kind==AutomationKind.Scroll?"Wheel delta (negative = down)":"Input code",s.Value.ToString(),v=>{if(int.TryParse(v,out int n)){s.Value=n;dirty=true;}});
        if(s.Kind is AutomationKind.Keys or AutomationKind.Text or AutomationKind.Launch or AutomationKind.FocusWindow or AutomationKind.IfWindow or AutomationKind.IfPixel){
            Field(details,s.Kind==AutomationKind.Keys?"Keys (Ctrl+Shift+A)":s.Kind==AutomationKind.IfPixel?"Pixel color (#RRGGBB)":s.Kind is AutomationKind.FocusWindow or AutomationKind.IfWindow?"Window title contains":s.Kind==AutomationKind.Launch?"App, file or URL":"Text to type",s.Text,v=>{s.Text=v;dirty=true;});
            if(s.Kind==AutomationKind.IfPixel)details.Children.Add(Ui.Button(L.T("Sample pixel color"),()=>{try{var pixel=CaptureService.Capture(new MonitorInfo("Pixel",new Rect(s.X,s.Y,1,1),Rect.Empty,1,1));byte[] bytes=new byte[4];new System.Windows.Media.Imaging.FormatConvertedBitmap(pixel,PixelFormats.Bgra32,null,0).CopyPixels(bytes,4,0);s.Text=$"#{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}";dirty=true;RefreshEditor();}catch(Exception e){status.Text=e.Message;}}));
        }
    }
    private void Move(AutomationStep s,int delta){int from=current!.Steps.IndexOf(s),to=from+delta;if(to<0||to>=current.Steps.Count)return;current.Steps.RemoveAt(from);current.Steps.Insert(to,s);dirty=true;RefreshSteps(s);}
    private async Task PickAsync(AutomationStep s,bool end)
    {
        pickCancellation=new();
        try{using var hidden=new HiddenWindowsScope();var p=await PhysicalPointPicker.PickAsync(pickCancellation.Token);if(p!=null){if(end){s.EndX=(int)p.Value.X;s.EndY=(int)p.Value.Y;}else{s.X=(int)p.Value.X;s.Y=(int)p.Value.Y;}dirty=true;}}
        catch(OperationCanceledException){}catch(Exception e){status.Text=e.Message;}
        finally{pickCancellation.Dispose();pickCancellation=null;if(!closed){RefreshSteps(s);Activate();}}
    }
    private async Task RecordAsync()
    {
        if(current==null||service.IsActive)return;
        service.SetRecording(true);
        nint foreground=NativeWindowService.GetForegroundWindowHandle();
        if(AutomationInput.External(foreground))service.Target=foreground;
        var pending=new CancellationTokenSource();pickCancellation=pending;
        try{
            status.Text=L.T("Recording starts in 3 seconds. Esc stops recording.");
            await Task.Delay(3000,pending.Token);if(closed)return;
            recordEscape=new();recordEscape.Pressed+=StopRecording;
            if(!recordEscape.SetEnabled(true))throw new InvalidOperationException(L.T("Escape is unavailable. Automation was not started."));
            Hide();if(AutomationInput.External(service.Target))NativeWindowService.RestoreForeground(service.Target);
            recordHud=AutomationService.CreateHud(L.T("Recording actions — Esc to stop"),StopRecording);recordHud.Show();
            recorder=new AutomationRecorder();recorder.StopRequested+=()=>Dispatcher.BeginInvoke(StopRecording);
        }catch(OperationCanceledException){}catch(Exception e){status.Text=e.Message;StopRecording();}
        finally{pending.Dispose();if(ReferenceEquals(pickCancellation,pending))pickCancellation=null;}
    }
    private void StopRecording()
    {
        pickCancellation?.Cancel();
        if(recorder!=null){recorder.Dispose();if(current!=null){current.Steps.AddRange(recorder.Steps.Take(Math.Max(0,500-current.Steps.Count)));dirty=true;}recorder=null;}
        recordHud?.Close();recordHud=null;recordEscape?.Dispose();recordEscape=null;
        service.SetRecording(false);
        if(!closed){Show();RefreshSteps();status.Text=L.T("Recorded actions added. Review them before running.");}
    }
}
