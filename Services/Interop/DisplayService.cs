using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using OpenTaskbar.Models;

namespace OpenTaskbar.Services;

public sealed record DisplayInfo(
    nint Handle,
    string DeviceName,
    int Index,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    bool IsPrimary,
    uint Dpi)
{
    public double Width => Bounds.Width;
    public double Height => Bounds.Height;
}

public static class DisplayService
{
    private const uint MonitorInfoFPrimary = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MonitorInfoEx
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    internal static nint GetWindowMonitor(nint window) => MonitorFromWindow(window, 2);

    internal static DisplayInfo? GetDisplay(nint monitor) =>
        monitor == 0 ? null : GetDisplays().FirstOrDefault(display => display.Handle == monitor);

    [DllImport("shcore.dll")]
    private static extern uint GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public static uint GetDpi(nint monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
                return dpiX;
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
        return 96;
    }

    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        var monitorHandles = new List<nint>();

        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            monitorHandles.Add(monitor);
            return true;
        }, 0);

        var index = 1;
        foreach (var handle in monitorHandles)
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(handle, ref info)) continue;

            var isPrimary = (info.dwFlags & MonitorInfoFPrimary) != 0;
            var dpi = GetDpi(handle);
            var scale = 96d / dpi;

            var pixelWidth = info.rcMonitor.Right - info.rcMonitor.Left;
            var pixelHeight = info.rcMonitor.Bottom - info.rcMonitor.Top;

            var bounds = new Rect(
                info.rcMonitor.Left * scale,
                info.rcMonitor.Top * scale,
                pixelWidth * scale,
                pixelHeight * scale);

            var workArea = new Rect(
                info.rcWork.Left * scale,
                info.rcWork.Top * scale,
                (info.rcWork.Right - info.rcWork.Left) * scale,
                (info.rcWork.Bottom - info.rcWork.Top) * scale);

            var devName = info.szDevice ?? $"DISPLAY{index}";
            var label = $"{index}: {pixelWidth}x{pixelHeight}{(isPrimary ? " *" : "")}";

            list.Add(new DisplayInfo(handle, devName, index, label, bounds, workArea, isPrimary, dpi));
            index++;
        }

        // Put primary monitor first, then by index
        return list.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Index).ToList();
    }

    public static DisplayInfo GetTargetDisplay(string? targetDeviceName)
    {
        var displays = GetDisplays();
        if (displays.Count == 0)
        {
            var fallbackWork = SystemParameters.WorkArea;
            return new DisplayInfo(0, "DISPLAY1", 1, "Display 1",
                new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                fallbackWork, true, 96);
        }

        if (!string.IsNullOrWhiteSpace(targetDeviceName))
        {
            var match = displays.FirstOrDefault(d =>
                string.Equals(d.DeviceName, targetDeviceName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.Index.ToString(), targetDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
    }

    public static IReadOnlyList<nint> GetSecondaryMonitors(string? targetDeviceName)
    {
        var target = GetTargetDisplay(targetDeviceName);
        var displays = GetDisplays();
        return displays
            .Where(d => d.Handle != target.Handle)
            .Select(d => d.Handle)
            .ToList();
    }
}
