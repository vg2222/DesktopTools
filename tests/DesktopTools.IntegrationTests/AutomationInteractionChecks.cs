using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.UI;

internal static class AutomationInteractionChecks
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    internal static async Task RunAsync()
    {
        string language=L.Language;
        using var controller=new AppController(true);controller.UpdateSettings(s=>{s.Theme="Dark";s.UseCustomBackground=false;s.Animations=true;});L.Use("en");
        using var service=new AutomationService(Path.Combine(Environment.CurrentDirectory,"automation-interaction-"+Guid.NewGuid().ToString("N")),()=>false,_=>{});
        service.Save([new(){Name="Motion fixture",Steps=[new(){Kind=AutomationKind.Wait,Value=20},new(){Kind=AutomationKind.Wait,Value=30},new(){Kind=AutomationKind.Wait,Value=40}]}]);
        try{
            bool activeMotion=Motion.Enabled;var window=new AutomationWindow(service);window.Show();await Task.Delay(300);window.UpdateLayout();
            var steps=Field<ListBox>(window,"steps");var details=Field<StackPanel>(window,"details");
            steps.SelectedIndex=1;
            if(Motion.Enabled&&!details.HasAnimatedProperties)throw new Exception("Selecting an action has no transition");
            AutomationUiChecks.Save(window,"automation-motion-selection-early.png");await Task.Delay(240);
            var selected=(ListBoxItem)steps.SelectedItem;ButtonIn(selected,"Move down").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(Motion.Enabled&&!steps.Items.OfType<ListBoxItem>().Any(row=>row.RenderTransform is TranslateTransform t&&t.HasAnimatedProperties))throw new Exception("Moving rows has no transition");
            AutomationUiChecks.Save(window,"automation-motion-move-early.png");await Task.Delay(25);
            ButtonIn((ListBoxItem)steps.SelectedItem,"Move up").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);
            if(steps.Items.OfType<ListBoxItem>().Any(row=>row.RenderTransform is TranslateTransform t&&Math.Abs(t.Y)>.1))throw new Exception("Row motion did not settle to its layout position");
            Invoke(window,"ReportError",L.T("The target window is unavailable."));Invoke(window,"DismissNotice");await Task.Delay(25);Invoke(window,"ReportError",L.T("Invalid action value."));await Task.Delay(150);
            if(Field<Border>(window,"notice").Visibility!=Visibility.Visible)throw new Exception("An old dismissal hid the newer message");
            Invoke(window,"Save");
            controller.UpdateSettings(s=>s.Animations=false);steps.SelectedIndex=0;
            if(details.HasAnimatedProperties||details.Opacity!=1)throw new Exception("Disabled animations still run on selection");
            ButtonIn((ListBoxItem)steps.SelectedItem,"Move down").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(steps.Items.OfType<ListBoxItem>().Any(row=>row.RenderTransform is TranslateTransform t&&t.HasAnimatedProperties))throw new Exception("Disabled row animations still run");
            Invoke(window,"Save");window.Close();
            Console.WriteLine(activeMotion?"PASS enabled selection/row transitions and disabled motion":"NOT TESTED: active animations (Windows preference disabled); disabled motion verified");
            foreach(string choice in new[]{"cancel","escape","header","repeat","discard","save","retry"})await CloseChoice(service,choice);
            foreach(string locale in L.Languages){
                L.Use(locale);int calls=0;
                var dialog=new AutomationSaveChangesWindow(()=>{calls++;return calls==1?L.T("Invalid action value."):null;});dialog.Show();await Task.Delay(80);dialog.UpdateLayout();
                AutomationUiChecks.Save(dialog,"automation-save-"+locale+".png");
                ButtonNamed(dialog,"SaveAutomationChanges").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));dialog.UpdateLayout();
                if(!dialog.IsVisible||dialog.Choice!=AutomationSaveChoice.Cancel||!AutomationUiChecks.Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text==L.T("Invalid action value.")))throw new Exception("Failed save hid the prompt or error");
                dialog.MaxWidth=340;dialog.MaxHeight=300;dialog.UpdateLayout();
                foreach(var button in AutomationUiChecks.Descendants(dialog).OfType<Button>()){
                    Point p=button.TranslatePoint(new Point(),dialog);if(p.X<0||p.Y<0||p.X+button.ActualWidth>dialog.ActualWidth+1||p.Y+button.ActualHeight>dialog.ActualHeight+1)throw new Exception("Save dialog button clipped: "+locale);
                }
                AutomationUiChecks.Save(dialog,"automation-save-error-small-"+locale+".png");
                ButtonNamed(dialog,"SaveAutomationChanges").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(dialog.IsVisible||dialog.Choice!=AutomationSaveChoice.Saved||calls!=2)throw new Exception("Save retry failed");
            }
            controller.UpdateSettings(s=>s.Animations=true);L.Use("en");var animated=new AutomationSaveChangesWindow(()=>null);animated.Show();
            if(Motion.Enabled&&(((Border)animated.Content).RenderTransform is not ScaleTransform scale||!scale.HasAnimatedProperties))throw new Exception("Custom save dialog has no entrance animation");
            await Task.Delay(300);animated.Close();
        }finally{L.Use(language);}
    }
    private static async Task CloseChoice(AutomationService service,string choice)
    {
        var before=service.Scripts[0].Steps.Select(s=>s.Value).ToArray();var window=new AutomationWindow(service);window.Show();await Task.Delay(80);
        var steps=Field<ListBox>(window,"steps");steps.SelectedIndex=0;
        var input=AutomationUiChecks.Descendants(window).OfType<TextBox>().Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)==L.T("Milliseconds"));int changed=before[0]+7;input.Text=choice=="retry"?"-1":changed.ToString();
        bool retried=false;Exception? error=null;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(120)};
        timer.Tick+=(_,_)=>{
            var dialog=Application.Current.Windows.OfType<AutomationSaveChangesWindow>().SingleOrDefault();if(dialog==null)return;
            try{
                if(choice is "cancel" or "escape" or "header" or "repeat"){
                    timer.Stop();
                    if(choice=="repeat"){
                        try{window.Close();}catch(InvalidOperationException){}
                        if(Application.Current.Windows.OfType<AutomationSaveChangesWindow>().Count()!=1)throw new Exception("Repeated close opened another save prompt");
                    }
                    if(choice=="escape")dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(dialog),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                    else if(choice=="header")AutomationUiChecks.Descendants(dialog).OfType<Button>().Single(b=>b.Name!="CancelAutomationSave"&&System.Windows.Automation.AutomationProperties.GetName(b)==L.T("Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else ButtonNamed(dialog,"CancelAutomationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if(choice=="discard"){timer.Stop();ButtonNamed(dialog,"DiscardAutomationSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                else{
                    if(choice=="retry"&&!retried){retried=true;ButtonNamed(dialog,"SaveAutomationChanges").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!dialog.IsVisible)throw new Exception("Invalid action closed the save prompt");input.Text=changed.ToString();return;}
                    timer.Stop();ButtonNamed(dialog,"SaveAutomationChanges").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }catch(Exception e){error=e;timer.Stop();dialog.Close();}
        };
        timer.Start();window.Close();timer.Stop();await Task.Delay(40);if(error!=null)throw error;
        if(choice is "cancel" or "escape" or "header" or "repeat"){
            if(!window.IsVisible||!Field<bool>(window,"dirty"))throw new Exception("Cancel closed or discarded the draft");
            if(!service.Scripts[0].Steps.Select(s=>s.Value).SequenceEqual(before))throw new Exception("Cancel wrote the store");
            input.Text=before[0].ToString();Invoke(window,"Save");window.Close();
        }else{
            if(!Field<bool>(window,"closed"))throw new Exception("Save/discard did not close owner");
            if(choice=="discard"&&!service.Scripts[0].Steps.Select(s=>s.Value).SequenceEqual(before))throw new Exception("Discard wrote the store");
            if(choice is "save" or "retry"&&service.Scripts[0].Steps[0].Value!=changed)throw new Exception("Save did not persist the draft");
        }
        Console.WriteLine("PASS custom save close choice: "+choice);
    }
    private static T Field<T>(object target,string name)=>(T)typeof(AutomationWindow).GetField(name,Flags)!.GetValue(target)!;
    private static void Invoke(object target,string method,params object[] args)=>typeof(AutomationWindow).GetMethod(method,Flags)!.Invoke(target,args);
    private static Button ButtonIn(DependencyObject root,string label)=>AutomationUiChecks.Descendants(root).OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)==L.T(label));
    private static Button ButtonNamed(DependencyObject root,string name)=>AutomationUiChecks.Descendants(root).OfType<Button>().Single(b=>b.Name==name);
}