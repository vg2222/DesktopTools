using DesktopTools.Core;
using System.Windows.Input;

internal static class DrawingBindingTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Drawing shortcuts match exact modifiers and custom tools", () =>
        {
            var bindings = DrawingBindings.Defaults; bindings["Pen"] = "Alt+Q";
            check(DrawingBindings.Validate(bindings, out _), "Custom binding rejected");
            check(DrawingBindings.Find(bindings, Key.Q, ModifierKeys.Alt) == "Pen" && DrawingBindings.Find(bindings, Key.Q, ModifierKeys.Alt | ModifierKeys.Shift) == null && DrawingBindings.Find(bindings, Key.P, ModifierKeys.None) == null, "Modifier mismatch or old binding triggered");
        });
        test("Drawing shortcut validation protects Escape typing and aliases", () =>
        {
            var bindings = DrawingBindings.Defaults; bindings["Pen"] = "Control+Z";
            check(!DrawingBindings.Validate(bindings, out _), "Aliased duplicate accepted");
            bindings = DrawingBindings.Defaults; bindings["FinishText"] = "Shift+Enter";
            check(!DrawingBindings.Validate(bindings, out _), "Typing-sensitive finish accepted");
            bindings = DrawingBindings.Defaults; bindings["Pen"] = "Esc";
            check(!DrawingBindings.Validate(bindings, out _), "Emergency Escape reassigned");
        });
        test("Settings recover invalid drawing bindings without shared defaults", () =>
        {
            var settings = new AppSettings(); settings.DrawingShortcuts["Pen"] = "V"; SettingsStore.Validate(settings);
            check(settings.DrawingShortcuts["Pen"] == "P", "Invalid bindings survived");
            settings.DrawingShortcuts["Pen"] = "Q"; check(new AppSettings().DrawingShortcuts["Pen"] == "P", "Defaults shared mutable state");
        });
    }
}
