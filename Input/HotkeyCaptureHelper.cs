using System.Collections.Generic;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Input;
using FrozenOCR.Native;

namespace FrozenOCR.Input;

internal static class HotkeyCaptureHelper
{
    public static bool TryCapture(KeyEventArgs e, out uint modifiers, out uint key, out string? error)
    {
        modifiers = 0;
        key = 0;
        error = null;

        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(actualKey))
        {
            error = "Include a non-modifier key.";
            return false;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            error = "Use at least one modifier (Ctrl/Alt/Shift/Win).";
            return false;
        }

        if ((mods & ModifierKeys.Control) != 0) modifiers |= NativeMethods.MOD_CONTROL;
        if ((mods & ModifierKeys.Alt) != 0) modifiers |= NativeMethods.MOD_ALT;
        if ((mods & ModifierKeys.Shift) != 0) modifiers |= NativeMethods.MOD_SHIFT;
        if ((mods & ModifierKeys.Windows) != 0) modifiers |= NativeMethods.MOD_WIN;

        key = (uint)KeyInterop.VirtualKeyFromKey(actualKey);
        if (key == 0)
        {
            error = "Unsupported key.";
            return false;
        }

        return true;
    }

    public static string FormatHotkey(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");

        var keyName = KeyInterop.KeyFromVirtualKey((int)key).ToString();
        if (!string.IsNullOrWhiteSpace(keyName))
        {
            parts.Add(keyName);
        }

        return string.Join(" + ", parts);
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
    }
}
