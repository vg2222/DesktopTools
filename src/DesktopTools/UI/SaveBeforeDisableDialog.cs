using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal interface IUnsavedWork
{
    bool HasUnsavedChanges { get; }
    Task<bool> SaveCopyAsync(Window owner);
}

internal sealed class SaveBeforeDisableDialog : Window
{
    internal bool Saved { get; private set; }
    private bool saving;
    internal SaveBeforeDisableDialog(Func<Task<bool>> save)
    {
        Title = L.T("Save changes"); Width = 460; SizeToContent = SizeToContent.Height; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        var body = new StackPanel(); var header = UtilityWindowChrome.Header(this, Title, Close, L.T("Cancel"), 20);
        var message = Ui.Text(L.T("Save a copy before disabling this editor. Cancel keeps your work open.")); message.Margin = new Thickness(0, 12, 0, 20); body.Children.Add(message);
        var buttons = new WrapPanel(); var cancel = Ui.Button(L.T("Cancel"), Close); cancel.IsCancel = true; cancel.IsDefault = true;
        var confirm = Ui.Button(L.T("Save copy"), () => { }, true); confirm.Name = "SaveBeforeDisable";
        confirm.Click += async (_, _) =>
        {
            saving = true; confirm.IsEnabled = cancel.IsEnabled = false;
            try { Saved = await save(); }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { saving = false; confirm.IsEnabled = cancel.IsEnabled = true; }
            if (Saved) Close();
        };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        Content = UtilityWindowChrome.DialogCard(this, header, body, buttons, 24);
        Closing += (_, e) => e.Cancel = saving;
        Loaded += (_, _) => cancel.Focus(); Motion.WindowEntrance(this);
    }
    internal static bool Prepare(Window window)
    {
        if (window is not IUnsavedWork { HasUnsavedChanges: true } work) return true;
        window.Show(); window.Activate();
        SaveBeforeDisableDialog? dialog = null;
        dialog = new SaveBeforeDisableDialog(() => work.SaveCopyAsync(dialog!)) { Owner = window };
        dialog.ShowDialog(); return dialog.Saved && !work.HasUnsavedChanges;
    }
}
