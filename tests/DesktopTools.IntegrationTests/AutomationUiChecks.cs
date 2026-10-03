using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class AutomationUiChecks
{
    internal static async Task RunAsync()
    {
        string language=L.Language;var failures=new System.Collections.Generic.List<string>();
        using var controller=new AppController(true);
        using var service=new AutomationService(Path.Combine(Environment.CurrentDirectory,"automation-ui-"+Guid.NewGuid().ToString("N")),()=>false,_=>{});
        service.Save([new(){Name="Workflow fixture",Steps=[new(){Kind=AutomationKind.Wait,Value=2000},new(){Kind=AutomationKind.Repeat,Value=2},new(){Kind=AutomationKind.Wait,Value=1000},new(){Kind=AutomationKind.EndRepeat}]}]);
        controller.UpdateSettings(s=>{s.Theme="Dark";s.UseCustomBackground=true;s.BackgroundColor="#101827";s.Transparency=true;s.Animations=false;});
        try{
            var expectedCustom=Color.FromRgb(16,24,39);
            if(Application.Current.Resources["Shell"] is not SolidColorBrush shell||shell.Color!=expectedCustom)failures.Add("Custom shell color differs from the previous appearance");
            L.Use("ru");
            var window=new AutomationWindow(service);window.Show();await Task.Delay(100);window.UpdateLayout();Save(window,"automation-ui-polished.png");
            if(window.Content is not Border surface||surface.Background is not SolidColorBrush solid||solid.Color!=expectedCustom)failures.Add("Automation background changes the selected color");
            var steps=(ListBox)typeof(AutomationWindow).GetField("steps",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            var first=(ListBoxItem)steps.Items[0];
            var move=Descendants(first).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T("Move down"));move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
            var current=(AutomationScript)typeof(AutomationWindow).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(current.Steps[0].Kind!=AutomationKind.Repeat||current.Steps[^1].Kind!=AutomationKind.Wait)failures.Add("Inline row move failed");
            typeof(AutomationWindow).GetMethod("Undo",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            typeof(AutomationWindow).GetMethod("ReportError",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[L.T("The target window is unavailable.")]);window.UpdateLayout();Save(window,"automation-message-error.png");
            var notice=(Border)typeof(AutomationWindow).GetField("notice",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(notice.Visibility!=Visibility.Visible||AutomationProperties.GetLiveSetting(notice)!=AutomationLiveSetting.Assertive)failures.Add("Error is not visible or accessible");
            typeof(AutomationWindow).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Save(window,"automation-message-success.png");
            if(AutomationProperties.GetLiveSetting(notice)!=AutomationLiveSetting.Polite)failures.Add("Informational message interrupts screen-reader speech");
            foreach(string mode in new[]{"cancel","invalid","save"}){
                var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};Exception? error=null;
                timer.Tick+=(_,_)=>{timer.Stop();var dialog=Application.Current.Windows.OfType<AutomationRunSettingsWindow>().Single();try{
                    var target=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==L.T("Window title contains"));target.Text="Test target";
                    if(mode=="cancel"){dialog.Close();return;}
                    var daily=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==L.T("Daily at"));daily.Text=mode=="invalid"?"25:80":"18:30";
                    Descendants(dialog).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T("Save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(mode=="invalid"){if(!dialog.IsVisible||!Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text==L.T("Enter a time in HH:mm format.")))throw new Exception("Invalid schedule closed dialog or hid error");Save(dialog,"automation-runsettings-error.png");dialog.Close();}
                }catch(Exception e){error=e;dialog.Close();}};
                timer.Start();typeof(AutomationWindow).GetMethod("OpenRunSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);timer.Stop();if(error!=null)throw error;
                if(mode!="save"&&service.Scripts[0].TargetWindow.Length!=0)failures.Add("Cancel or failed validation saved settings");
            }
            if(service.Scripts[0].DailyTime!="18:30"||service.Scripts[0].TargetWindow!="Test target")failures.Add("Run settings save lost fields");
            var libraryTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(160)};Exception? libraryError=null;
            libraryTimer.Tick+=(_,_)=>{libraryTimer.Stop();var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Owner==window);try{
                dialog.Width=dialog.MinWidth;dialog.Height=dialog.MinHeight;dialog.UpdateLayout();
                var search=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==L.T("Search actions"));CheckInputAlignment(search,L.T("Search actions"),failures);search.Text="no-such-action-unique";dialog.UpdateLayout();
                if(!Descendants(dialog).OfType<TextBlock>().Any(t=>t.Text==L.T("No matching actions")))throw new Exception("Action search lost its empty state");
                Save(dialog,"automation-library-small-empty.png");search.Clear();dialog.UpdateLayout();Save(dialog,"automation-library-small.png");
                if(dialog.Content is not Border surface||surface.Margin!=new Thickness(0))throw new Exception("Action library has a footer gap");
            }catch(Exception e){libraryError=e;}finally{dialog.Close();}};
            libraryTimer.Start();typeof(AutomationWindow).GetMethod("OpenActions",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);libraryTimer.Stop();if(libraryError!=null)throw libraryError;
            window.Close();
            foreach(string locale in new[]{"en","ru","de","fr","es"}){
                L.Use(locale);var hud=AutomationService.CreateHud(L.T("Automation running"),()=>{});hud.Show();await Task.Delay(80);hud.UpdateLayout();Save(hud,"automation-hud-"+locale+".png");
                if(hud.Content is not Border hudSurface||hudSurface.Margin!=new Thickness(0))failures.Add("HUD root margin leaves bottom strip");
                var stop=Descendants(hud).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T("Stop"));
                if(stop.TranslatePoint(new Point(0,stop.ActualHeight),hud).Y>hud.ActualHeight-8)failures.Add("HUD stop control is clipped");
                hud.Close();
                var settings=new AutomationRunSettingsWindow(new AutomationScript(),_=>null,(_,_)=>{});settings.Width=settings.MinWidth;settings.Height=settings.MinHeight;settings.Show();await Task.Delay(60);settings.UpdateLayout();
                try{
                    CheckInputAlignment(Descendants(settings).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)==L.T("Daily at")),"HH:mm",failures);
                    foreach(var label in new[]{"Cancel","Save"}){
                        var button=Descendants(settings).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==L.T(label));var position=button.TranslatePoint(new Point(),settings);
                        if(position.X<0||position.Y<0||position.X+button.ActualWidth>settings.ActualWidth||position.Y+button.ActualHeight>settings.ActualHeight-8)failures.Add("Run settings footer is clipped: "+locale+" "+label);
                        if(label=="Save"&&!button.IsDefault||label=="Cancel"&&!button.IsCancel)failures.Add("Run settings keyboard default is missing");
                    }
                    var scroll=Descendants(settings).OfType<ScrollViewer>().First();if(scroll.ScrollableHeight<=0)failures.Add("Small settings cannot scroll");scroll.ScrollToEnd();settings.UpdateLayout();Save(settings,"automation-settings-small-"+locale+".png");
                }finally{settings.Close();}
            }
            await BackgroundColors(controller,service);
            if(failures.Count>0)throw new Exception(string.Join("; ",failures));
        }finally{L.Use(language);}
    }
    private static async Task BackgroundColors(AppController controller,AutomationService service)
    {
        foreach(string theme in new[]{"Light","Dark"})foreach(bool custom in new[]{false,true})foreach(bool transparent in new[]{false,true}){
            controller.UpdateSettings(s=>{s.Theme=theme;s.UseCustomBackground=custom;s.BackgroundColor="#101827";s.Transparency=transparent;s.Animations=false;});
            Color expected=(Color)ColorConverter.ConvertFromString(custom?"#101827":theme=="Dark"?"#060606":"#F3F5FA");
            Color expectedShell=custom?expected:(Color)ColorConverter.ConvertFromString(theme=="Dark"?"#D0121316":"#B3F3F5FA");
            if(Application.Current.Resources["Shell"] is not SolidColorBrush shell||shell.Color!=expectedShell)throw new Exception("Original shell palette changed");
            var window=new AutomationWindow(service);window.Show();await Task.Delay(40);window.UpdateLayout();
            try{
                if(window.Content is not Border root||root.Background is not SolidColorBrush fill||fill.Color!=expected||root.Margin!=new Thickness(0))throw new Exception("Original tool background color or no-gap layout changed");
                if(Descendants(window).OfType<Border>().Any(b=>b.Background is LinearGradientBrush))throw new Exception("Automation inspector still uses the rejected background gradient");
                var inspector=Descendants(window).OfType<Border>().Single(b=>b.Child is ScrollViewer&&b.Padding==new Thickness(16));
                if(!ReferenceEquals(inspector.Background,Application.Current.Resources["Card"]))throw new Exception("Original inspector card palette changed");
                Save(window,$"automation-background-{theme}-{custom}-{transparent}.png");
                NativeWindowService.ApplyBackdrop(window,theme=="Dark",false);
                if(window.Background is not SolidColorBrush fallback||fallback.Color!=expected)throw new Exception("Opaque fallback differs from the selected color");
            }finally{window.Close();}
        }
    }
    private static void CheckInputAlignment(TextBox input,string hint,System.Collections.Generic.List<string> failures)
    {
        string original=input.Text;input.Clear();var window=Window.GetWindow(input);window.UpdateLayout();
        var placeholder=Descendants(window).OfType<TextBlock>().Single(t=>t.Text==hint);var caret=input.GetRectFromCharacterIndex(0);var origin=placeholder.TranslatePoint(new Point(),input);
        Console.WriteLine($"Field spacing {AutomationProperties.GetName(input)}: font={input.FontSize}/{placeholder.FontSize}, caret={caret}, hint={origin}, height={input.ActualHeight}");
        if(Math.Abs(input.FontSize-placeholder.FontSize)>.01||input.FontFamily.Source!=placeholder.FontFamily.Source||Math.Abs(caret.X-origin.X)>1.5||Math.Abs(caret.Y-origin.Y)>2)failures.Add("Placeholder differs from text/caret alignment: "+AutomationProperties.GetName(input));
        input.Text="18:30";window.UpdateLayout();var typed=input.GetRectFromCharacterIndex(0);
        if(Math.Abs(typed.X-caret.X)>.5||Math.Abs(typed.Y-caret.Y)>.5)failures.Add("Typing moves text inside the field: "+AutomationProperties.GetName(input));
        input.Text=original;
    }
    internal static async Task PreviewAsync()
    {
        using var controller=new AppController(true);controller.UpdateSettings(s=>{s.Theme="Dark";s.UseCustomBackground=false;s.Transparency=true;s.Animations=false;});L.Use("ru");
        using var service=new AutomationService(Path.Combine(Environment.CurrentDirectory,"automation-preview-"+Guid.NewGuid().ToString("N")),()=>true,_=>{});
        service.Save([new(){Name="Мой сценарий",Steps=[new(){Kind=AutomationKind.Wait,Value=3000},new(){Kind=AutomationKind.Repeat,Value=2},new(){Kind=AutomationKind.Wait,Value=2000},new(){Kind=AutomationKind.EndRepeat}]}]);
        var completion=new TaskCompletionSource();var window=new AutomationWindow(service);window.Closed+=(_,_)=>completion.TrySetResult();window.Show();await completion.Task.WaitAsync(TimeSpan.FromMinutes(8));
    }
    internal static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    internal static void Save(Window window,string path)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);SaveBitmap(bitmap,path);
    }
    private static void SaveBitmap(BitmapSource bitmap,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);}
}
