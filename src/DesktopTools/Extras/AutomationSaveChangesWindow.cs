using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using DesktopTools.Localization;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal enum AutomationSaveChoice { Cancel, Saved, Discard }

internal sealed class AutomationSaveChangesWindow : Window
{
    internal AutomationSaveChoice Choice {get;private set;}
    private bool saving;
    internal AutomationSaveChangesWindow(Func<string?> save)
    {
        Title=L.T("Save changes");Width=560;MinWidth=300;MaxWidth=620;MaxHeight=520;SizeToContent=SizeToContent.Height;
        ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        UtilityWindowChrome.EnableBackdrop(this);
        var header=UtilityWindowChrome.Header(this,Title,Close,L.T("Cancel"),20);
        var body=new StackPanel();var message=new Grid{Margin=new Thickness(0,16,0,12)};message.ColumnDefinitions.Add(new(){Width=new GridLength(36)});message.ColumnDefinitions.Add(new());message.Children.Add(Ui.Icon("Save",24));var question=Ui.Text(L.T("Save automation before closing?"),15);Grid.SetColumn(question,1);message.Children.Add(question);body.Children.Add(message);
        var error=Ui.Text("",12);var errorScroll=new ScrollViewer{Content=error,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};errorScroll.SetResourceReference(StyleProperty,"PageScroller");
        var errorBox=new Border{Child=errorScroll,Padding=new Thickness(12),CornerRadius=new CornerRadius(8),Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,14),BorderThickness=new Thickness(1)};errorBox.SetResourceReference(Border.BackgroundProperty,"Field");errorBox.BorderBrush=System.Windows.Media.Brushes.IndianRed;AutomationProperties.SetLiveSetting(errorBox,AutomationLiveSetting.Assertive);
        var footer=new StackPanel();footer.Children.Add(errorBox);var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=Ui.Button(L.T("Cancel"),Close);cancel.Name="CancelAutomationSave";cancel.IsCancel=true;
        var discard=Ui.Button(L.T("Don't save"),()=>{Choice=AutomationSaveChoice.Discard;Close();});discard.Name="DiscardAutomationSave";
        var confirm=Ui.Button(L.T("Save"),()=>{},true);confirm.Name="SaveAutomationChanges";confirm.IsDefault=true;confirm.Content=Ui.IconLabel("Save",L.T("Save"),16,true,12);
        confirm.Click+=(_,_)=>{
            if(saving)return;saving=true;confirm.IsEnabled=cancel.IsEnabled=discard.IsEnabled=false;
            try{
                string? failure=save();
                if(failure==null){Choice=AutomationSaveChoice.Saved;}
                else{error.Text=failure;AutomationProperties.SetName(errorBox,failure);errorBox.Visibility=Visibility.Visible;Motion.Transition(errorBox);}
            }catch(Exception e){error.Text=e.Message;AutomationProperties.SetName(errorBox,e.Message);errorBox.Visibility=Visibility.Visible;Motion.Transition(errorBox);}
            finally{saving=false;confirm.IsEnabled=cancel.IsEnabled=discard.IsEnabled=true;}
            if(Choice==AutomationSaveChoice.Saved)Close();
        };
        buttons.Children.Add(cancel);buttons.Children.Add(discard);buttons.Children.Add(confirm);footer.Children.Add(buttons);
        var card=UtilityWindowChrome.DialogCard(this,header,body,footer,24);Content=card;
        Closing+=(_,e)=>e.Cancel=saving;
        Loaded+=(_,_)=>{Motion.ModalEntrance(card);confirm.Focus();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!saving){e.Handled=true;Close();}};
    }
}