using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Afloat;

internal static class Native
{
    internal const int WM_HOTKEY = 0x0312;
    internal const uint NoRepeat = 0x4000;
    internal const uint PositionFlags = 0x0001 | 0x0002 | 0x0010 | 0x0200;
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr param);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
    private delegate bool EnumCallback(IntPtr hwnd, IntPtr param);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    internal static string Title(IntPtr hwnd) { var b = new StringBuilder(1024); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
    internal static bool IsTopmost(IntPtr hwnd) => (GetWindowLong(hwnd, -20) & 8) != 0;
    internal static uint Pid(IntPtr hwnd) { GetWindowThreadProcessId(hwnd, out uint pid); return pid; }
    internal static bool SameWindow(WindowEntry e) => IsWindow(e.Handle) && Pid(e.Handle) == e.ProcessId && e.ProcessStart == StartTime(e.ProcessId);
    internal static long StartTime(uint pid) { try { using var p = Process.GetProcessById((int)pid); return p.StartTime.ToUniversalTime().Ticks; } catch { return 0; } }
    internal static bool Eligible(IntPtr hwnd, bool allowOwn = false)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return false;
        if (!allowOwn && Pid(hwnd) == Environment.ProcessId) return false;
        if (string.IsNullOrWhiteSpace(Title(hwnd)) || (GetWindowLong(hwnd, -20) & 0x80) != 0) return false;
        var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        return DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }
    internal static WindowEntry Describe(IntPtr hwnd)
    {
        uint pid = Pid(hwnd); string process = "Window";
        try { using var p = Process.GetProcessById((int)pid); process = p.ProcessName; } catch { }
        return new WindowEntry { Handle = hwnd, ProcessId = pid, ProcessStart = StartTime(pid), Title = Title(hwnd), ProcessName = process };
    }
    internal static List<WindowEntry> Windows()
    {
        var list = new List<WindowEntry>();
        EnumWindows((hwnd, _) => { if (Eligible(hwnd)) list.Add(Describe(hwnd)); return true; }, IntPtr.Zero);
        return list;
    }
    internal static bool SetTopmost(IntPtr hwnd, bool value) => SetWindowPos(hwnd, new IntPtr(value ? -1 : -2), 0, 0, 0, 0, PositionFlags) && IsTopmost(hwnd) == value;
    internal static bool MoveToCorner(WindowEntry entry)
    {
        if (!SameWindow(entry) || IsIconic(entry.Handle) || IsZoomed(entry.Handle)) return false;
        if (entry.OriginalRect is Rect original)
        {
            bool restored = SetWindowPos(entry.Handle, IntPtr.Zero, original.Left, original.Top, original.Width, original.Height, 0x0010 | 0x0004 | 0x0200);
            if (restored) entry.OriginalRect = null;
            return restored;
        }
        if (!GetWindowRect(entry.Handle, out Rect rect)) return false;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(entry.Handle, 2), ref monitor)) return false;
        double scale = Math.Max(96, GetDpiForWindow(entry.Handle)) / 96.0;
        int margin = (int)(20 * scale);
        int width = Math.Min((int)(480 * scale), monitor.Work.Width - margin * 2);
        int height = Math.Min((int)(300 * scale), monitor.Work.Height - margin * 2);
        bool ok = SetWindowPos(entry.Handle, IntPtr.Zero, monitor.Work.Left + margin, monitor.Work.Top + margin, width, height, 0x0010 | 0x0004 | 0x0200);
        if (ok) entry.OriginalRect = rect;
        return ok;
    }
}

public sealed class WindowEntry
{
    public IntPtr Handle { get; set; }
    public uint ProcessId { get; set; }
    public long ProcessStart { get; set; }
    public string Title { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Initial => ProcessName.Length == 0 ? "W" : ProcessName[..1].ToUpperInvariant();
    public string CornerLabel => OriginalRect == null ? "왼쪽 위" : "크기 복원";
    internal Native.Rect? OriginalRect { get; set; }
}

