using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using FrozenOCR.Native;

namespace FrozenOCR.Input;

internal sealed class GlobalHotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly int _hotkeyId;
    private uint _modifiers;
    private uint _virtualKey;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkeyService(int hotkeyId, uint modifiers, uint virtualKey)
    {
        _hotkeyId = hotkeyId;
        _modifiers = modifiers;
        _virtualKey = virtualKey;

        var parameters = new HwndSourceParameters("FrozenOCR.HotkeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
        };
        parameters.ExtendedWindowStyle = 0x00000080; // WS_EX_TOOLWINDOW

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        if (!NativeMethods.RegisterHotKey(_source.Handle, _hotkeyId, modifiers, virtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "Failed to register global hotkey.");
        }
    }

    public uint Modifiers => _modifiers;
    public uint VirtualKey => _virtualKey;

    public bool TryUpdateHotkey(uint modifiers, uint virtualKey, out string? error)
    {
        error = null;
        if (modifiers == _modifiers && virtualKey == _virtualKey)
        {
            return true;
        }

        try
        {
            if (_source.Handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_source.Handle, _hotkeyId);
            }

            if (!NativeMethods.RegisterHotKey(_source.Handle, _hotkeyId, modifiers, virtualKey))
            {
                var err = Marshal.GetLastWin32Error();
                // Restore previous hotkey
                NativeMethods.RegisterHotKey(_source.Handle, _hotkeyId, _modifiers, _virtualKey);
                error = new Win32Exception(err).Message;
                return false;
            }

            _modifiers = modifiers;
            _virtualKey = virtualKey;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            if (_source.Handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_source.Handle, _hotkeyId);
            }
        }
        finally
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
    }
}

