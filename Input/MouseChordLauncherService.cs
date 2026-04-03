using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Point = System.Windows.Point;
using System.Windows;
using System.Windows.Threading;
using FrozenOCR.Native;

namespace FrozenOCR.Input;

internal sealed class MouseChordLauncherService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLbuttonDown = 0x0201;
    private const int WmLbuttonUp = 0x0202;
    private const int WmRbuttonDown = 0x0204;
    private const int WmRbuttonUp = 0x0205;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly LowLevelMouseProc _hookProc;
    private IntPtr _hookHandle;

    private bool _leftDown;
    private bool _rightDown;
    private bool _pendingActivation;
    private bool _activated;
    private NativeMethods.POINT _startPoint;

    public event EventHandler<Point>? ChordActivated;

    public MouseChordLauncherService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, OnTimerTick, _dispatcher);
        _timer.Stop();

        _hookProc = HookCallback;
        _hookHandle = SetHook(_hookProc);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg == WmLbuttonDown) _leftDown = true;
            else if (msg == WmLbuttonUp) _leftDown = false;
            else if (msg == WmRbuttonDown) _rightDown = true;
            else if (msg == WmRbuttonUp) _rightDown = false;

            _dispatcher.BeginInvoke(UpdateChordState);

        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void UpdateChordState()
    {
        if (_leftDown && _rightDown)
        {
            if (!_pendingActivation && !_activated)
            {
                _pendingActivation = true;
                if (NativeMethods.GetCursorPos(out var pt))
                {
                    _startPoint = pt;
                }
                _timer.Stop();
                _timer.Start();
            }
        }
        else
        {
            _pendingActivation = false;
            _timer.Stop();

            if (!_leftDown && !_rightDown)
            {
                _activated = false;
            }
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (!_pendingActivation || _activated || !_leftDown || !_rightDown)
        {
            _pendingActivation = false;
            return;
        }

        var ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        if (!ctrlDown)
        {
            _pendingActivation = false;
            return;
        }

        if (NativeMethods.GetCursorPos(out var current))
        {
            var dx = current.X - _startPoint.X;
            var dy = current.Y - _startPoint.Y;
            if ((dx * dx) + (dy * dy) > 25)
            {
                _pendingActivation = false;
                return;
            }
        }

        _pendingActivation = false;
        _activated = true;

        if (NativeMethods.GetCursorPos(out var pt))
        {
            ChordActivated?.Invoke(this, new Point(pt.X, pt.Y));
        }
    }

    private static IntPtr SetHook(LowLevelMouseProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = GetModuleHandle(curModule?.ModuleName);
        return SetWindowsHookEx(WhMouseLl, proc, moduleHandle, 0);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
