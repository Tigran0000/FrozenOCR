using System;
using FrozenOCR.Native;

namespace FrozenOCR.Display;

internal sealed class MonitorService
{
    public MonitorInfo GetMonitorUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var pt))
        {
            throw new InvalidOperationException("Failed to get cursor position.");
        }

        return GetMonitorForPoint(pt);
    }

    public MonitorInfo GetMonitorForPoint(NativeMethods.POINT pt)
    {
        var hmon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hmon == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to resolve monitor under cursor.");
        }

        var mi = new NativeMethods.MONITORINFOEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
        };

        if (!NativeMethods.GetMonitorInfo(hmon, ref mi))
        {
            throw new InvalidOperationException("Failed to query monitor info.");
        }

        uint dpiX = 96, dpiY = 96;
        try
        {
            _ = NativeMethods.GetDpiForMonitor(hmon, NativeMethods.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
            if (dpiX == 0) dpiX = 96;
            if (dpiY == 0) dpiY = 96;
        }
        catch (DllNotFoundException)
        {
            // shcore.dll isn't available on some systems; default to 96 DPI.
        }

        var left = mi.rcMonitor.Left;
        var top = mi.rcMonitor.Top;
        var width = mi.rcMonitor.Right - mi.rcMonitor.Left;
        var height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

        return new MonitorInfo(
            Handle: hmon,
            PixelLeft: left,
            PixelTop: top,
            PixelWidth: width,
            PixelHeight: height,
            DpiX: dpiX,
            DpiY: dpiY
        );
    }
}

