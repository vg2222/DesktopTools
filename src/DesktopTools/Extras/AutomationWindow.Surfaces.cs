using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;
namespace DesktopTools.Extras;

internal sealed partial class AutomationWindow
{
    private void FillWorkflows()
    {
        scripts.Items.Clear();
        foreach(var script in drafts){
            var row=new ListBoxItem{Tag=script,Content=Ui.IconLabel("Utilities",script.Name,16,textSize:12),Padding=new Thickness(10,12,10,12)};
            Ui.Tip(row,script.Name);scripts.Items.Add(row);if(script==current)scripts.SelectedItem=row;
        }
    }
    private void RefreshWorkflowNames()
    {
        foreach(ListBoxItem row in scripts.Items)if(row.Tag is AutomationScript script){row.Content=Ui.IconLabel("Utilities",script.Name,16,textSize:12);Ui.Tip(row,script.Name);}
    }
    private void RefreshSteps(AutomationStep? selected=null)
    {
        refreshing=true;name.Text=current?.Name??"";steps.Items.Clear();int depth=0;
        if(current!=null)foreach(var s in current.Steps){
            if(AutomationEditing.IsEnd(s.Kind))depth=Math.Max(0,depth-1);
            int index=steps.Items.Count;
            var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(26)});content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var number=Ui.Text((index+1).ToString("00"),11,muted:true);content.Children.Add(number);
            var icon=Ui.Icon(AutomationCatalog.Icon(s.Kind),18);icon.Margin=new Thickness(0,0,12,0);Grid.SetColumn(icon,1);content.Children.Add(icon);
            var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var title=Ui.Text(AutomationCatalog.Label(s.Kind),13,!AutomationEditing.IsEnd(s.Kind));title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;copy.Children.Add(title);
            var summary=Ui.Text(AutomationCatalog.Summary(s),11,muted:true);summary.Tag="summary";summary.TextWrapping=TextWrapping.NoWrap;summary.TextTrimming=TextTrimming.CharacterEllipsis;summary.Margin=new Thickness(0,4,0,0);summary.Visibility=summary.Text.Length==0?Visibility.Collapsed:Visibility.Visible;copy.Children.Add(summary);
            Grid.SetColumn(copy,2);content.Children.Add(copy);
            var tools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            Button Tool(string symbol,string label,Action action,double angle=0){var b=Ui.IconButton(symbol,L.T(label),action);b.Width=b.Height=b.MinHeight=26;b.Padding=new Thickness(6);b.Margin=new Thickness(0);b.Content=Ui.Icon(symbol,14);if(angle!=0)((FrameworkElement)b.Content).RenderTransform=new RotateTransform(angle,7,7);return b;}
            tools.Children.Add(Tool("Chevron","Move up",()=>EditStructure(()=>AutomationEditing.Move(current!,current!.Steps.IndexOf(s),-1)),-90));
            tools.Children.Add(Tool("Chevron","Move down",()=>EditStructure(()=>AutomationEditing.Move(current!,current!.Steps.IndexOf(s),1)),90));
            tools.Children.Add(Tool("Copy","Duplicate",()=>EditStructure(()=>AutomationEditing.Duplicate(current!,current!.Steps.IndexOf(s)))));
            tools.Children.Add(Tool("Clear","Remove action",()=>EditStructure(()=>AutomationEditing.Remove(current!,current!.Steps.IndexOf(s)))));
            var toolStyle=new Style(typeof(StackPanel));toolStyle.Setters.Add(new Setter(OpacityProperty,0d));
            foreach(var property in new[]{ListBoxItem.IsSelectedProperty,IsMouseOverProperty,IsKeyboardFocusWithinProperty}){var trigger=new DataTrigger{Binding=new Binding(property.Name){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(ListBoxItem),1)},Value=true};trigger.Setters.Add(new Setter(OpacityProperty,1d));toolStyle.Triggers.Add(trigger);}tools.Style=toolStyle;
            Grid.SetColumn(tools,3);content.Children.Add(tools);
            var row=new ListBoxItem{Tag=s,Content=content,Padding=new Thickness(10+Math.Min(depth,6)*12,11,8,11)};
            Ui.Tip(row,AutomationCatalog.Help(s.Kind));AutomationProperties.SetName(row,L.F($"Action {index+1}: {AutomationCatalog.Label(s.Kind)}"));steps.Items.Add(row);if(s==selected)steps.SelectedItem=row;
            if(AutomationEditing.IsStart(s.Kind)||s.Kind==AutomationKind.Else)depth++;
        }
        if(steps.SelectedIndex<0&&steps.Items.Count>0)steps.SelectedIndex=0;refreshing=false;
        empty.Visibility=steps.Items.Count==0?Visibility.Visible:Visibility.Collapsed;stepCount.Text=L.F($"{steps.Items.Count} actions");
        RefreshEditor();ServiceChanged();
    }
    private void ConfigureNotice()
    {
        var row=new DockPanel();var dismiss=Ui.IconButton("Close",L.T("Dismiss notification"),()=>notice.Visibility=Visibility.Collapsed);dismiss.Width=dismiss.Height=dismiss.MinHeight=28;dismiss.Padding=new Thickness(7);dismiss.Margin=new Thickness(8,0,0,0);DockPanel.SetDock(dismiss,Dock.Right);row.Children.Add(dismiss);
        DockPanel.SetDock(noticeIcon,Dock.Left);row.Children.Add(noticeIcon);row.Children.Add(status);notice.Child=row;AutomationProperties.SetLiveSetting(notice,AutomationLiveSetting.Polite);
    }
    private void ShowMessage(string message,string icon,bool error)
    {
        if(closed)return;status.Text=message;noticeIcon.Content=Ui.Icon(icon,18);notice.Visibility=Visibility.Visible;
        bool dark=(Ui.Brush("Text") as SolidColorBrush)?.Color.R>160;
        var tint=error?Color.FromRgb(230,81,92):icon=="Check"?Color.FromRgb(31,153,106):(Ui.Brush("Accent") as SolidColorBrush)?.Color??Colors.SteelBlue;
        notice.Background=new SolidColorBrush(Color.FromArgb(dark?(byte)34:(byte)18,tint.R,tint.G,tint.B));notice.BorderBrush=new SolidColorBrush(Color.FromArgb(100,tint.R,tint.G,tint.B));
        AutomationProperties.SetLiveSetting(notice,error?AutomationLiveSetting.Assertive:AutomationLiveSetting.Polite);
        AutomationProperties.SetName(notice,message);
    }
    private void Report(string message)
    {
        string english=L.EnglishHint(message);bool error=english.StartsWith("Automation failed:",StringComparison.Ordinal)||english.Contains("unavailable",StringComparison.OrdinalIgnoreCase)||english.Contains("timed out",StringComparison.OrdinalIgnoreCase);
        ShowMessage(message,error?"Notifications":english is "Automation saved." or "Automation completed."?"Check":"Notifications",error);
    }
    private void ReportError(string message)=>ShowMessage(message,"Notifications",true);
    private void WorkflowMenu()
    {
        if(current==null)return;
        var menu=new ContextMenu();void Item(string label,string icon,Action action){var item=new MenuItem{Header=L.T(label),Icon=Ui.Icon(icon,16)};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Run settings","Settings",OpenRunSettings);Item("Duplicate","Copy",Duplicate);Item("Export","Save",Export);menu.Items.Add(new Separator());Item("Delete automation","Clear",Delete);
        menu.IsOpen=true;
    }
    private void OpenActions()
    {
        if(current==null){New();if(current==null)return;}
        var dialog=new Window{Title=L.T("Add action"),Width=640,Height=650,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.CanResize,MinWidth=500,MinHeight=420};
        UtilityWindowChrome.EnableBackdrop(dialog);var root=new DockPanel();var header=UtilityWindowChrome.Header(dialog,dialog.Title,dialog.Close,L.T("Close"),20);header.Margin=new Thickness(0,0,0,14);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var searchRow=new Grid{Margin=new Thickness(0,0,0,12)};var search=new TextBox{MinHeight=40,Padding=new Thickness(36,9,35,9)};
        AutomationProperties.SetName(search,L.T("Search actions"));var searchIcon=Ui.Icon("Search",17);searchIcon.HorizontalAlignment=HorizontalAlignment.Left;searchIcon.Margin=new Thickness(12,0,0,0);searchRow.Children.Add(search);searchRow.Children.Add(searchIcon);searchRow.Children.Add(Ui.SearchClearButton(search));
        var placeholder=Ui.Text(L.T("Search actions"),12,muted:true);placeholder.Margin=new Thickness(36,0,35,0);placeholder.IsHitTestVisible=false;searchRow.Children.Add(placeholder);DockPanel.SetDock(searchRow,Dock.Top);root.Children.Add(searchRow);
        var entries=new StackPanel();root.Children.Add(Scroll(entries));
        void Fill(){
            placeholder.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;entries.Children.Clear();
            foreach(var group in AutomationCatalog.Entries.Where(e=>(L.T(e.Label)+" "+L.T(e.Help)+" "+L.T(e.Group)).Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).GroupBy(e=>e.Group)){
                var title=Ui.Text(L.T(group.Key),11,true,muted:true);title.Margin=new Thickness(2,14,0,8);entries.Children.Add(title);
                foreach(var entry in group){
                    var button=Ui.Button(L.T(entry.Label),()=>{EditStructure(()=>AutomationEditing.Insert(current!,steps.SelectedIndex,entry.Kind));dialog.Close();});
                    var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                    var badge=new Border{Width=34,Height=34,CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,12,0),Child=Ui.Icon(AutomationCatalog.Icon(entry.Kind),18)};badge.SetResourceReference(Border.BackgroundProperty,"Selected");row.Children.Add(badge);
                    var copy=new StackPanel{VerticalAlignment=VerticalAlignment.Center};copy.Children.Add(Ui.Text(L.T(entry.Label),13,true));var help=Ui.Text(L.T(entry.Help),11,muted:true);help.TextWrapping=TextWrapping.NoWrap;help.TextTrimming=TextTrimming.CharacterEllipsis;help.Margin=new Thickness(0,3,8,0);copy.Children.Add(help);Grid.SetColumn(copy,1);row.Children.Add(copy);
                    var plus=Ui.Icon("Plus",16);Grid.SetColumn(plus,2);row.Children.Add(plus);button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,6);button.Padding=new Thickness(10);button.BorderThickness=new Thickness(0);Ui.Tip(button,L.T(entry.Help));entries.Children.Add(button);
                }
            }
            if(entries.Children.Count==0)entries.Children.Add(Ui.SearchEmptyState(L.T("No matching actions"),L.T("Try another search.")));
        }
        search.TextChanged+=(_,_)=>Fill();Fill();var card=Ui.Card(root,18);card.Margin=new Thickness(0);dialog.Content=card;dialog.Loaded+=(_,_)=>search.Focus();dialog.ShowDialog();
    }
    private void OpenRunSettings()
    {
        if(current==null||service.IsActive)return;
        var selected=current;
        var dialog=new AutomationRunSettingsWindow(selected,candidate=>{
            try{
                var replacement=drafts.Select(s=>s.Id==candidate.Id?candidate.Copy():s.Copy()).ToList();
                service.Save(replacement);Remember();drafts=replacement;current=drafts.Single(s=>s.Id==candidate.Id);dirty=false;RefreshScripts();Report(L.T("Automation saved."));return null;
            }catch(Exception e){return e.Message;}
        },ChooseWindowFor){Owner=this};dialog.ShowDialog();
    }
}
