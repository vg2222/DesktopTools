using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
namespace DesktopTools.Extras;

internal sealed class AutomationRunSettingsWindow : Window
{
    private readonly AutomationScript draft;
    internal AutomationRunSettingsWindow(AutomationScript source,Func<AutomationScript,string?> apply,Action<Window,Action<string>> chooseWindow)
    {
        draft=source.Copy();Title=L.T("Run settings");Width=620;Height=670;MinWidth=520;MinHeight=460;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.CanResize;ShowInTaskbar=false;
        UtilityWindowChrome.EnableBackdrop(this);
        var root=new DockPanel();var header=UtilityWindowChrome.Header(this,Title,Close,L.T("Close"),20);header.Margin=new Thickness(0,0,0,16);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var error=Ui.Text("",12);var errorBox=new Border{Child=error,CornerRadius=new CornerRadius(8),Padding=new Thickness(12),Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,12),Background=new SolidColorBrush(Color.FromArgb(28,230,81,92)),BorderBrush=new SolidColorBrush(Color.FromArgb(100,230,81,92)),BorderThickness=new Thickness(1)};
        AutomationProperties.SetLiveSetting(errorBox,AutomationLiveSetting.Assertive);
        var footer=new StackPanel{Margin=new Thickness(0,14,0,0)};footer.Children.Add(errorBox);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=Ui.Button(L.T("Cancel"),Close);cancel.IsCancel=true;buttons.Children.Add(cancel);
        var save=Ui.Button(L.T("Save"),()=>{
            try{
                AutomationProgram.Compile(draft);
                if(draft.Shortcut.Length>0&&!HotkeyGesture.TryParse(draft.Shortcut,out _,out var message))throw new ArgumentException(message);
                string? failure=apply(draft);if(failure!=null)throw new InvalidOperationException(failure);Close();
            }catch(Exception e){error.Text=e.Message;errorBox.Visibility=Visibility.Visible;Motion.Transition(errorBox);}
        },true);save.IsDefault=true;save.Content=Ui.IconLabel("Save",L.T("Save"),16,true,12);buttons.Children.Add(save);footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var fields=new StackPanel();
        void Heading(string icon,string label){var text=Ui.IconLabel(icon,L.T(label),18,textSize:15);text.Margin=new Thickness(0,12,0,8);fields.Children.Add(text);}
        TextBox Field(Panel panel,string label,string value,Action<string> change,string? hint=null){
            var text=Ui.Text(L.T(label),12,muted:true);text.Margin=new Thickness(0,10,0,5);panel.Children.Add(text);
            var input=new TextBox{Text=value,MinHeight=40,Padding=new Thickness(12,7,12,7)};AutomationProperties.SetName(input,L.T(label));input.TextChanged+=(_,_)=>change(input.Text);
            if(hint!=null)AutomationTextInput.Hint(input,hint);panel.Children.Add(input);
            return input;
        }
        void Picker(Panel panel,TextBox input,string icon,string label,Action click){
            UIElement content=input;
            panel.Children.Remove(content);var row=new DockPanel();var button=Ui.IconButton(icon,L.T(label),click);button.Margin=new Thickness(6,0,0,0);DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);row.Children.Add(content);panel.Children.Add(row);
        }
        Heading("Window","Target window");
        var target=Field(fields,"Window title contains",draft.TargetWindow,v=>draft.TargetWindow=v,L.T("Leave blank to use the current window."));target.MaxLength=1024;Ui.Tip(target,L.T("Leave blank to use the current window."));
        Picker(fields,target,"Window","Choose window",()=>chooseWindow(this,v=>target.Text=v));
        var delay=Field(fields,"Delay between actions (ms)",draft.StepDelayMs.ToString(),v=>draft.StepDelayMs=int.TryParse(v,out int n)?n:int.MinValue);
        Ui.Tip(delay,L.T("Use a value from 0 to 10000."));
        var divider=new Border{Height=1,Margin=new Thickness(0,18,0,8)};divider.SetResourceReference(Border.BackgroundProperty,"Stroke");fields.Children.Add(divider);
        Heading("Timer","Automatic triggers");
        fields.Children.Add(Ui.Row(L.T("Enable automatic triggers"),null,Ui.Toggle(draft.Armed,v=>draft.Armed=v)));
        var shortcut=Field(fields,"Run shortcut",draft.Shortcut,v=>draft.Shortcut=v);shortcut.MaxLength=100;
        Picker(fields,shortcut,"Shortcuts","Record shortcut",()=>new ShortcutRecorderWindow(shortcut.Text,v=>{shortcut.Text=v;return true;}){Owner=this}.ShowDialog());
        var schedule=new Grid();schedule.ColumnDefinitions.Add(new());schedule.ColumnDefinitions.Add(new());var interval=new StackPanel{Margin=new Thickness(0,0,14,0)};var daily=new StackPanel();Grid.SetColumn(daily,1);schedule.Children.Add(interval);schedule.Children.Add(daily);fields.Children.Add(schedule);
        Field(interval,"Every N minutes (0 = off)",draft.IntervalMinutes.ToString(),v=>draft.IntervalMinutes=int.TryParse(v,out int n)?n:int.MinValue);
        var clock=Field(daily,"Daily at",draft.DailyTime,v=>draft.DailyTime=v,"HH:mm");clock.MaxLength=5;Ui.Tip(clock,L.T("HH:mm; leave blank to disable."));
        var window=Field(fields,"When window appears",draft.WindowTrigger,v=>draft.WindowTrigger=v);window.MaxLength=512;Picker(fields,window,"Window","Choose window",()=>chooseWindow(this,v=>window.Text=v));
        fields.Children.Add(Ui.Row(L.T("Run when DesktopTools starts"),null,Ui.Toggle(draft.RunOnStartup,v=>draft.RunOnStartup=v)));
        var hint=Ui.Text(L.T("Triggers run while DesktopTools is open."),11,muted:true);hint.Margin=new Thickness(0,10,0,8);Ui.Tip(hint,L.T("Save to apply triggers. Missed daily runs are not replayed after restarting."));fields.Children.Add(hint);
        var scroll=new ScrollViewer{Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};scroll.SetResourceReference(StyleProperty,"PageScroller");SmoothScroll.Enable(scroll);root.Children.Add(scroll);
        var card=Ui.Card(root,20);card.Margin=new Thickness(0);Content=card;Loaded+=(_,_)=>Motion.ModalEntrance(card);
    }
}
