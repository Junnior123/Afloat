using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Afloat;

internal static class SelfTest
{
    internal static int Run(string reportPath)
    {
        var log = new List<string>();
        var first = new Window { Title = "Afloat integration test A", Width = 340, Height = 220, Left = 80, Top = 100, ShowInTaskbar = false, ShowActivated = false };
        var second = new Window { Title = "Afloat integration test B", Width = 340, Height = 220, Left = 160, Top = 140, ShowInTaskbar = false, ShowActivated = false };
        var pins = new PinService();
        int exitCode = 0;
        void Check(bool result, string name) { if (!result) throw new InvalidOperationException("FAIL: " + name); log.Add("PASS: " + name); }
        try
        {
            first.Show(); second.Show(); Pump();
            var a = new WindowInteropHelper(first).Handle; var b = new WindowInteropHelper(second).Handle;
            var foreground = Native.GetForegroundWindow();
            Native.GetWindowRect(a, out var before);
            Check(!Native.IsTopmost(a), "Test window begins as a normal window");
            string pinMessage = pins.Toggle(a, true); Pump();
            Check(Native.IsTopmost(a) && pins.Entries.Count == 1, "Pin creates actual WS_EX_TOPMOST on native window");
            Check(pinMessage.Contains("Afloat integration test A") && pinMessage.Contains("고정"), "Pin notification names the affected window");
            Check(Native.GetForegroundWindow() == foreground, "Pin does not steal keyboard focus");
            Native.GetWindowRect(a, out var after);
            Check(before.Equals(after), "Pin preserves position and dimensions");
            string releaseMessage = pins.Toggle(a, true); Check(!Native.IsTopmost(a) && pins.Entries.Count == 0, "Second toggle unpins the same window");
            Check(releaseMessage.Contains("Afloat integration test A") && releaseMessage.Contains("해제"), "Release notification names the affected window");
            pins.Toggle(a, true); pins.Toggle(b, true);
            Check(Native.IsTopmost(a) && Native.IsTopmost(b) && pins.Entries.Count == 2, "Multiple independent windows can be pinned");
            var entry = pins.Entries.First(x => x.Handle == a);
            Check(Native.MoveToCorner(entry), "Corner placement succeeds");
            Native.GetWindowRect(a, out var corner);
            Check(!corner.Equals(before), "Corner placement changes bounds");
            Check(Native.MoveToCorner(entry), "Corner restore succeeds");
            Native.GetWindowRect(a, out var restored);
            Check(restored.Equals(before), "Corner restore returns exact original bounds");
            Check(pins.ReleaseAll() == 0 && !Native.IsTopmost(a) && !Native.IsTopmost(b), "Release all restores both windows");
            pins.Toggle(a, true); first.Close(); Pump(); pins.Refresh();
            Check(pins.Entries.Count == 0, "Closed windows are pruned safely");
            Check(pins.Toggle(IntPtr.Zero).Contains("먼저"), "Invalid/desktop target is rejected");
            Check(!Native.Eligible(b), "Production targets exclude Afloat's own windows");
            using var hotkey = new HotkeyService(b);
            uint vk = 0;
            foreach (uint candidate in new uint[] { 0x87, 0x86, 0x85, 0x84, 0x83 }) if (hotkey.Change(7, candidate)) { vk = candidate; break; }
            Check(vk != 0, "Global hotkey registers with Windows");
            using var rival = new HotkeyService(b);
            uint blocked = vk == 0x87 ? 0x86u : 0x87u;
            Check(Native.RegisterHotKey(b, 4999, 7 | Native.NoRepeat, blocked), "Reserve a conflicting shortcut");
            try
            {
                Check(!hotkey.Change(7, blocked), "Conflicting shortcut is rejected");
                Check(hotkey.Registered && hotkey.VirtualKey == vk, "Failed shortcut change preserves old registration");
            }
            finally { Native.UnregisterHotKey(b, 4999); }
            hotkey.Dispose();
            Check(rival.Change(7, vk), "Hotkey becomes reusable after unregister");
            Check(!HotkeyService.Valid(0, 65) && !HotkeyService.Valid(3, 0x7B) && !HotkeyService.Valid(11, 65), "Unmodified, F12 and Windows-key shortcuts rejected");
            string folder = Path.GetDirectoryName(Path.GetFullPath(reportPath))!; Directory.CreateDirectory(folder);
            string settingsPath = Path.Combine(folder, "test-settings.json");
            var settings = new Settings { Modifiers = 7, VirtualKey = 65, Notifications = false, CloseToTray = false, Animations = false, HardwareAcceleration = false, StartWithWindows = true, StartHidden = false, RestorePlacementOnUnpin = false };
            Check(settings.Save(settingsPath), "Settings save atomically");
            var loaded = Settings.Load(settingsPath);
            Check(loaded.Modifiers == 7 && loaded.VirtualKey == 65 && !loaded.Notifications && !loaded.CloseToTray && !loaded.Animations && !loaded.HardwareAcceleration && loaded.StartWithWindows && !loaded.StartHidden && !loaded.RestorePlacementOnUnpin, "Settings round trip");
            File.WriteAllText(settingsPath, "{bad json"); Check(Settings.Load(settingsPath).VirtualKey == 32, "Corrupt settings recover to defaults");
            File.WriteAllText(settingsPath, "{\"Modifiers\":0,\"VirtualKey\":0}"); Check(Settings.Load(settingsPath).Modifiers == 3, "Invalid saved shortcut recovers to defaults");
            File.Delete(settingsPath);
            var shell = new MainWindow();
            System.Windows.Application.Current.MainWindow = shell;
            shell.Show(); Pump();
            Check(shell.ExerciseAnimationsForTest(), "Repeated UI interactions keep animations resettable and active");
            var notification = new ToastWindow("알림 창 핸들 안정성 테스트", true);
            notification.Show(); Pump();
            Check(notification.IsLoaded && new WindowInteropHelper(notification).Handle != IntPtr.Zero, "Notification opens with a valid native window handle");
            notification.Close(); Pump();
            Check(shell.ExerciseCloseButtonForTest(), "Red close button hides the app to tray without a fatal error");
            Pump();
            log.Add("PASS: Cleanup is safe when called repeatedly");
            log.Add("ALL TESTS PASSED");
        }
        catch (Exception ex) { log.Add(ex.ToString()); exitCode = 1; }
        finally
        {
            pins.ReleaseAll(); first.Close(); second.Close();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllLines(reportPath, log);
        }
        return exitCode;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}

