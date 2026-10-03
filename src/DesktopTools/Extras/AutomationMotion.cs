using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopTools.Core;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal static class AutomationMotion
{
    internal static Dictionary<AutomationStep,double> CaptureRows(ListBox list)
    {
        var positions=new Dictionary<AutomationStep,double>();
        if(!Motion.Enabled||!list.IsVisible)return positions;
        foreach(ListBoxItem row in list.Items)if(row.Tag is AutomationStep step&&row.IsLoaded)positions[step]=row.TranslatePoint(new Point(),list).Y;
        return positions;
    }
    internal static void MoveRows(ListBox list,IReadOnlyDictionary<AutomationStep,double> previous)
    {
        if(!list.IsVisible)return;list.UpdateLayout();
        foreach(ListBoxItem row in list.Items){
            var translation=new TranslateTransform();row.RenderTransform=translation;row.Opacity=1;
            if(!Motion.Enabled||row.Tag is not AutomationStep step)continue;
            double y=row.TranslatePoint(new Point(),list).Y;
            if(y+row.ActualHeight<0||y>list.ActualHeight)continue;
            if(previous.TryGetValue(step,out double old)){
                double offset=old-y;
                if(Math.Abs(offset)>.5)translation.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(offset,0,TimeSpan.FromMilliseconds(190)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
            }else row.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(160)){FillBehavior=FillBehavior.Stop});
        }
    }
    internal static void RowTools(ListBoxItem row,FrameworkElement tools)
    {
        tools.Opacity=0;
        void Update(){
            double current=tools.Opacity,visible=row.IsSelected||row.IsMouseOver||row.IsKeyboardFocusWithin?1:0;
            tools.BeginAnimation(UIElement.OpacityProperty,null);tools.Opacity=visible;
            if(Motion.Enabled&&row.IsLoaded&&Math.Abs(current-visible)>.01)tools.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(current,visible,TimeSpan.FromMilliseconds(100)){FillBehavior=FillBehavior.Stop});
        }
        row.Loaded+=(_,_)=>Update();row.Selected+=(_,_)=>Update();row.Unselected+=(_,_)=>Update();
        row.MouseEnter+=(_,_)=>Update();row.MouseLeave+=(_,_)=>Update();row.IsKeyboardFocusWithinChanged+=(_,_)=>Update();
        row.Unloaded+=(_,_)=>tools.BeginAnimation(UIElement.OpacityProperty,null);
    }
    internal static void Dismiss(FrameworkElement element,Action complete)
    {
        if(!Motion.Enabled){complete();return;}
        var fade=new DoubleAnimation(element.Opacity,0,TimeSpan.FromMilliseconds(100)){FillBehavior=FillBehavior.Stop};
        fade.Completed+=(_,_)=>complete();element.BeginAnimation(UIElement.OpacityProperty,fade);
    }
}