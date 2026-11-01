using System.Windows.Input;

namespace DeskScene.Tests;

public class HotkeysTests
{
    [Theory]
    [InlineData(Key.D1, ModifierKeys.Control | ModifierKeys.Alt, "Ctrl+Alt+1")]
    [InlineData(Key.F9, ModifierKeys.Windows | ModifierKeys.Shift, "Windows+Shift+F9")]
    public void Formats_key_combinations(Key key, ModifierKeys modifiers, string expected)
    {
        Assert.Equal(expected, Hotkeys.Format(key, modifiers));
    }

    [Fact]
    public void Requires_a_modifier()
    {
        Assert.Null(Hotkeys.Format(Key.A, ModifierKeys.None));
        Assert.False(Hotkeys.TryParse("A", out _, out _));
        Assert.False(Hotkeys.TryParse("not a key", out _, out _));
    }

    [Fact]
    public void Parses_to_win32_modifiers_and_virtual_key()
    {
        Assert.True(Hotkeys.TryParse("Ctrl+Alt+1", out var modifiers, out var key));
        Assert.Equal(1u | 2u, modifiers); // MOD_ALT | MOD_CONTROL
        Assert.Equal(0x31u, key);         // '1'
    }
}
