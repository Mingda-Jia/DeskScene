using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskScene;

/// <summary>Records a key combination as text ("Ctrl+Alt+1"). Backspace/Delete clears it.</summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        ToolTip = "Click, then press a key combination such as Ctrl+Alt+1. Backspace clears it.";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Tab or Key.Escape or Key.Enter)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        e.Handled = true;
        if (key is Key.Back or Key.Delete) Text = "";
        else if (key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin))
            Text = Hotkeys.Format(key, Keyboard.Modifiers) ?? Text;
    }

    // Registered hotkeys would swallow the keys being typed, so pause them while editing.
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        App.Current.SuspendHotkeys();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        App.Current.Refresh();
    }
}
