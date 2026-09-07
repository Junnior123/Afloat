using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace Afloat;

public sealed class Settings
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Afloat", "settings.json");
    public uint Modifiers { get; set; } = 3;
    public uint VirtualKey { get; set; } = 32;
    public bool Notifications { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool Animations { get; set; } = true;
    public bool HardwareAcceleration { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartHidden { get; set; } = true;
    public bool RestorePlacementOnUnpin { get; set; } = true;
    public static Settings Load(string path)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new();
            if (!HotkeyService.Valid(s.Modifiers, s.VirtualKey)) { s.Modifiers = 3; s.VirtualKey = 32; }
            return s;
        }
        catch { return new(); }
    }
    public bool Save(string path)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true); return true; }
        catch { return false; }
    }
}

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue("Afloat", $"\"{Path.Combine(AppContext.BaseDirectory, "Afloat.exe")}\" --startup");
            else key.DeleteValue("Afloat", false);
            return true;
        }
        catch { return false; }
    }
    internal static bool IsEnabled()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return !string.IsNullOrWhiteSpace(key?.GetValue("Afloat") as string); }
        catch { return false; }
    }
}

internal sealed class HotkeyService : IDisposable
{
    private readonly IntPtr window;
    private int id = 4100;
    internal int Id => id;
    internal bool Registered { get; private set; }
    internal uint Modifiers { get; private set; }
    internal uint VirtualKey { get; private set; }
    internal HotkeyService(IntPtr hwnd) { window = hwnd; }
    internal static bool Valid(uint modifiers, uint key) => (modifiers & 3) != 0 && (modifiers & ~7u) == 0 && key is >= 0x20 and <= 0xFE && key != 0x7B && key is not (0x5B or 0x5C);
    internal bool Change(uint modifiers, uint key)
    {
        if (!Valid(modifiers, key)) return false;
        if (Registered && modifiers == Modifiers && key == VirtualKey) return true;
        int next = id == 4100 ? 4101 : 4100;
        if (!Native.RegisterHotKey(window, next, modifiers | Native.NoRepeat, key)) return false;
        if (Registered) Native.UnregisterHotKey(window, id);
        id = next; Modifiers = modifiers; VirtualKey = key; Registered = true; return true;
    }
    internal static string[] Labels(uint mods, uint key)
    {
        var labels = new List<string>();
        if ((mods & 2) != 0) labels.Add("Ctrl"); if ((mods & 1) != 0) labels.Add("Alt"); if ((mods & 4) != 0) labels.Add("Shift");
        string name = KeyInterop.KeyFromVirtualKey((int)key).ToString();
        if (key >= 48 && key <= 57) name = ((char)key).ToString();
        labels.Add(name == "Space" ? "Space" : name); return labels.ToArray();
    }
    internal string Display => string.Join(" + ", Labels(Modifiers, VirtualKey));
    public void Dispose() { if (Registered) Native.UnregisterHotKey(window, id); Registered = false; }
}

internal sealed class PinService
{
    internal List<WindowEntry> Entries { get; } = new();
    internal string Toggle(IntPtr handle, bool allowOwn = false, bool restorePlacement = true)
    {
        var existing = Entries.FirstOrDefault(x => x.Handle == handle);
        if (existing != null) return Release(existing, restorePlacement) ? $"‘{DisplayTitle(existing.Title)}’ 창의 항상 위 고정을 해제했어요." : $"‘{DisplayTitle(existing.Title)}’ 창을 해제하지 못했어요. 실행 권한을 확인해 주세요.";
        if (!Native.Eligible(handle, allowOwn)) return "고정할 창을 먼저 선택한 뒤 단축키를 눌러 주세요.";
        string title = Native.Title(handle);
        if (Native.IsTopmost(handle)) return Native.SetTopmost(handle, false) ? $"‘{DisplayTitle(title)}’ 창의 기존 항상 위 고정을 해제했어요." : $"‘{DisplayTitle(title)}’ 창의 고정을 변경할 수 없어요.";
        if (!Native.SetTopmost(handle, true)) return $"‘{DisplayTitle(title)}’ 창을 고정할 수 없어요. 관리자 권한 앱인지 확인해 주세요.";
        Entries.Add(Native.Describe(handle)); return $"‘{DisplayTitle(title)}’ 창을 항상 위에 고정했어요.";
    }
    internal static string DisplayTitle(string title)
    {
        string clean = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(clean)) return "이름 없는 창";
        return clean.Length <= 54 ? clean : clean[..51] + "…";
    }
    internal bool Release(WindowEntry entry, bool restorePlacement = true)
    {
        if (!Native.SameWindow(entry)) { Entries.Remove(entry); return true; }
        if (restorePlacement && entry.OriginalRect != null) Native.MoveToCorner(entry);
        if (!Native.SetTopmost(entry.Handle, false)) return false;
        Entries.Remove(entry); return true;
    }
    internal int ReleaseAll(bool restorePlacement = true) { foreach (var e in Entries.ToArray()) Release(e, restorePlacement); return Entries.Count; }
    internal void Refresh()
    {
        foreach (var e in Entries.ToArray())
        {
            if (!Native.SameWindow(e) || !Native.IsTopmost(e.Handle)) { Entries.Remove(e); continue; }
            e.Title = Native.Title(e.Handle);
        }
    }
}

