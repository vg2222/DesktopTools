using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;
using DesktopTools.Extras;

internal static class InlineFormattingChecks
{
    internal static async Task RunAsync()
    {
        using var controller=new AppController(true); controller.ApplyTheme();
        DesktopTools.Localization.L.Use("en");
        var canvas=new Canvas();var owner=new Window{Width=720,Height=430,Content=canvas};owner.Show();
        try
        {
            var editor=new AnnotationTextEditor(new Point(80,150),new Size(700,400),Colors.Crimson,24){Text="Example Пример"};canvas.Children.Add(editor);await Task.Delay(80);
            var popup=(Popup)typeof(AnnotationTextEditor).GetField("formatting",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
            if(!popup.IsOpen)throw new Exception("Formatting toolbar did not open");
            var card=(Border)popup.Child;var toolbar=(StackPanel)card.Child;
            toolbar.Children.OfType<ComboBox>().Last().SelectedItem="32";
            toolbar.Children.OfType<Button>().Single(b=>b.Content as string=="B").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            toolbar.Children.OfType<Button>().Single(b=>b.Content as string=="I").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(editor.FontSize!=32||editor.FontWeight!=FontWeights.Bold||editor.FontStyle!=FontStyles.Italic)throw new Exception("Formatting controls did not update editor");
            Annotation? annotation=null;editor.CommitRequested+=()=>{annotation=new Annotation{Kind=AnnotationKind.Text,Points=[editor.ExportOrigin],Text=editor.Text,FontSize=editor.FontSize,FontFamily=editor.FontFamily.Source,Bold=editor.FontWeight==FontWeights.Bold,Italic=editor.FontStyle==FontStyles.Italic};canvas.Children.Remove(editor);};
            card.UpdateLayout();var png=new PngBitmapEncoder();var rendered=new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth),(int)Math.Ceiling(card.ActualHeight),96,96,PixelFormats.Pbgra32);rendered.Render(card);png.Frames.Add(BitmapFrame.Create(rendered));using(var stream=File.Create("inline-format-toolbar.png"))png.Save(stream);
            toolbar.Children.OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(30);
            if(annotation is not {Bold:true,Italic:true,FontSize:32}||popup.IsOpen)throw new Exception("Commit lost formatting or retained popup");
            var normal=AnnotationRenderer.Bounds(annotation with{Bold=false,Italic=false});var styled=AnnotationRenderer.Bounds(annotation);if(normal==styled)throw new Exception("Renderer ignored text style");
            var cancelled=new AnnotationTextEditor(new Point(40,120),new Size(700,400),Colors.Blue,24);canvas.Children.Add(cancelled);await Task.Delay(40);var cancelPopup=(Popup)typeof(AnnotationTextEditor).GetField("formatting",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(cancelled)!;bool canceled=false;cancelled.CancelRequested+=()=>{canceled=true;canvas.Children.Remove(cancelled);};
            ((StackPanel)((Border)cancelPopup.Child).Child).Children.OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(30);if(!canceled||cancelPopup.IsOpen)throw new Exception("Cancel retained toolbar");
        }
        finally{owner.Close();}
        var pickerChrome = new ColorPickerWindow("#2563EB", _ => true);
        if (pickerChrome.WindowStyle != WindowStyle.None || !pickerChrome.AllowsTransparency || pickerChrome.Background != Brushes.Transparent)
            throw new Exception("Color picker uses an opaque system backing behind rounded corners");
        pickerChrome.Close();
        controller.Settings.DrawingEnabled = true;
        controller.ToggleDraw();
        try
        {
            controller.SetTool("Text");
            var drawing = (OverlayWindow)typeof(AppController).GetField("overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            typeof(OverlayWindow).GetMethod("OpenText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawing, [new Point(90, 150)]);
            var drawingEditor = (AnnotationTextEditor)typeof(OverlayWindow).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawing)!;
            drawingEditor.Text = "Keep this text"; drawing.Activate(); drawingEditor.Focus(); await Task.Delay(40);
            if (!drawing.IsActive) throw new Exception("Drawing overlay could not be activated for the color-picker regression");
            var drawingPopup = (Popup)typeof(AnnotationTextEditor).GetField("formatting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawingEditor)!;
            var drawingToolbar = (StackPanel)((Border)drawingPopup.Child).Child;
            var colorButton = drawingToolbar.Children.OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Color");
            OverlayState stateDuringPicker = OverlayState.Hidden; bool textRetained = false, guardedPicker = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                stateDuringPicker = controller.State; guardedPicker = controller.IsBusy;
                textRetained = ReferenceEquals(typeof(OverlayWindow).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawing), drawingEditor);
                Application.Current.Windows.OfType<ColorPickerWindow>().Single().Close();
            };
            timer.Start(); colorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (stateDuringPicker != OverlayState.Draw || !textRetained || !guardedPicker) throw new Exception("Opening text color picker did not preserve a guarded drawing session and unfinished text");
        }
        finally { controller.HideAnnotations(); }
        var pixels = Enumerable.Repeat((byte)255, 640 * 360 * 4).ToArray();
        var source = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgra32, null, pixels, 640 * 4); source.Freeze();
        foreach (string layout in new[] { "A", "B" })
        {
            var window = new ScreenshotEditorWindow(source, _ => { }, _ => { }, editorLayout: layout); window.Show();
            try
            {
                typeof(ScreenshotEditorWindow).GetMethod("OpenText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { new Point(80, 120) });
                var editor = (AnnotationTextEditor)typeof(ScreenshotEditorWindow).GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                editor.Text = "Styled Пример"; editor.FontSize = 32; editor.FontWeight = FontWeights.Bold; editor.FontStyle = FontStyles.Italic; editor.Foreground = Brushes.Crimson;
                await Task.Delay(40);
                var popup = (Popup)typeof(AnnotationTextEditor).GetField("formatting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                var toolbar = (StackPanel)((Border)popup.Child).Child;
                toolbar.Children.OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var document = (ScreenshotEditDocument)typeof(ScreenshotEditorWindow).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                if (document.Items.Single() is not { Bold: true, Italic: true, FontSize: 32 } annotation || annotation.Color != Colors.Crimson) throw new Exception(layout + ": screenshot commit lost formatting");
                var exported = document.Export(); var actual = new byte[pixels.Length]; exported.CopyPixels(actual, 640 * 4, 0);
                if (actual.SequenceEqual(pixels)) throw new Exception(layout + ": formatted text was absent from export");
                document.Undo(); if (document.Items.Count != 0 || document.CanUndo) throw new Exception(layout + ": text commit was not one undo step");
                document.Redo(); if (document.Items.Single() != annotation) throw new Exception(layout + ": redo lost text formatting");
                var original = new byte[pixels.Length]; source.CopyPixels(original, 640 * 4, 0); if (!original.SequenceEqual(pixels)) throw new Exception("Text export modified original");
                typeof(ScreenshotEditorWindow).GetMethod("OpenText", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { new Point(160, 240) });
                editor = (AnnotationTextEditor)typeof(ScreenshotEditorWindow).GetField("_editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                editor.Text = "Cancel this"; await Task.Delay(30);
                popup = (Popup)typeof(AnnotationTextEditor).GetField("formatting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                ((StackPanel)((Border)popup.Child).Child).Children.OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (document.Items.Count != 1 || popup.IsOpen) throw new Exception(layout + ": cancel committed text or retained toolbar");
            }
            finally { window.Close(); }
        }
    }
}
