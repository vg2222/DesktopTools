using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace DesktopTools.Extras;

/// <summary>Feature-local watermark using the same font and padding as editable text.</summary>
internal static class AutomationTextInput
{
    private static Style? style;
    internal static void Hint(TextBox input,string hint)
    {
        // These inputs do not otherwise use Tag; the template binds it as display-only text.
        input.Tag=hint;
        style??=CreateStyle();input.Style=style;
    }
    private static Style CreateStyle()
    {
        var style=new Style(typeof(TextBox),(Style)Application.Current.FindResource(typeof(TextBox)));
        var template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>
<Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9'>
<Grid><ScrollViewer x:Name='PART_ContentHost'/>
<Grid Margin='2,0'><TextBlock x:Name='Watermark' Text='{Binding Tag,RelativeSource={RelativeSource TemplatedParent}}' Margin='{TemplateBinding Padding}' FontFamily='{TemplateBinding FontFamily}' FontSize='{TemplateBinding FontSize}' FontWeight='{TemplateBinding FontWeight}' FontStyle='{TemplateBinding FontStyle}' FontStretch='{TemplateBinding FontStretch}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' Foreground='{DynamicResource Muted}' TextWrapping='NoWrap' TextTrimming='CharacterEllipsis' IsHitTestVisible='False' Visibility='Collapsed'/></Grid>
</Grid></Border>
<ControlTemplate.Triggers><Trigger Property='Text' Value=''><Setter TargetName='Watermark' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.42'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>");
        style.Setters.Add(new Setter(Control.TemplateProperty,template));return style;
    }
}