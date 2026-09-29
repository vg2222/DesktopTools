using System.Windows;
using System.Windows.Controls;
using DesktopTools.Extras;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed class TranslationLanguagePicker : Grid
{
    private readonly ComboBox from = new(), to = new();
    private readonly Action<string> changed;
    private bool synchronizing;
    internal string Direction => ((TranslationLanguage)from.SelectedItem).Code + "-" + ((TranslationLanguage)to.SelectedItem).Code;
    internal TranslationLanguagePicker(string direction, Action<string> changed)
    {
        this.changed = changed;
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        ColumnDefinitions.Add(new ColumnDefinition());
        var languages = TranslationPacks.Languages;
        from.ItemsSource = to.ItemsSource = languages;
        from.MinWidth = to.MinWidth = 125;
        from.MaxDropDownHeight = to.MaxDropDownHeight = 360;
        ToolTip = L.T("Installed Windows languages appear first. Extra offline languages require a one-time model download.");
        System.Windows.Automation.AutomationProperties.SetName(from, L.T("Source language"));
        System.Windows.Automation.AutomationProperties.SetName(to, L.T("Target language"));
        Children.Add(from);
        var swap = Ui.IconButton("Swap", L.T("Swap languages"), () =>
        {
            string[] codes = Direction.Split('-'); SetDirection(codes[1] + "-" + codes[0]); changed(Direction);
        });
        SetColumn(swap, 1); Children.Add(swap); SetColumn(to, 2); Children.Add(to);
        SetDirection(direction);
        from.SelectionChanged += (_, _) => Changed(from);
        to.SelectionChanged += (_, _) => Changed(to);
    }
    internal void SetDirection(string direction)
    {
        var languages = (IReadOnlyList<TranslationLanguage>)from.ItemsSource;
        string[] codes = direction.Split('-');
        synchronizing = true;
        try
        {
            from.SelectedItem = languages.FirstOrDefault(language => language.Code == codes[0]) ?? languages.First(language => language.Code == "en");
            to.SelectedItem = codes.Length == 2 ? languages.FirstOrDefault(language => language.Code == codes[1]) : null;
            if (to.SelectedItem == null || Equals(from.SelectedItem, to.SelectedItem))
                to.SelectedItem = languages.First(language => !Equals(language, from.SelectedItem));
        }
        finally { synchronizing = false; }
    }
    private void Changed(ComboBox edited)
    {
        if (synchronizing || from.SelectedItem == null || to.SelectedItem == null) return;
        if (Equals(from.SelectedItem, to.SelectedItem))
        {
            var other = ReferenceEquals(edited, from) ? to : from;
            synchronizing = true;
            try { other.SelectedItem = ((IReadOnlyList<TranslationLanguage>)other.ItemsSource).First(language => !Equals(language, edited.SelectedItem)); }
            finally { synchronizing = false; }
        }
        changed(Direction);
    }
}
