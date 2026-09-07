using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Afloat;

public partial class MainWindow : Window
{
    private readonly bool preview;
    private readonly Settings settings;
    private readonly PinService pins = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly string settingsPath = Settings.DefaultPath;
    private readonly string legacySettingsPath = System.IO.Path.Combine(AppContext.BaseDirectory, ".data", "settings.json");
    private HotkeyService? hotkey;
    private HwndSource? source;
    private Forms.NotifyIcon? tray;
    private bool quitting;
    private bool cleanedUp;
    private uint candidateMods, candidateKey;
    private ToastWindow? toast;
    private string lastListSignature = "";

    public MainWindow() : this(false) { }
    internal MainWindow(bool preview)
    {
        this.preview = preview;
        if (preview) settings = new();
        else
        {
            string loadPath = System.IO.File.Exists(settingsPath) ? settingsPath : legacySettingsPath;
            settings = Settings.Load(loadPath);
            if (loadPath == legacySettingsPath && System.IO.File.Exists(legacySettingsPath)) settings.Save(settingsPath);
            settings.StartWithWindows = StartupService.IsEnabled();
        }
        InitializeComponent();
        using (var icon = CreateTrayIcon()) Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        NotificationToggle.IsChecked = settings.Notifications;
        TrayToggle.IsChecked = settings.CloseToTray;
        AnimationToggle.IsChecked = settings.Animations;
        HardwareToggle.IsChecked = settings.HardwareAcceleration;
        StartupToggle.IsChecked = settings.StartWithWindows;
        StartHiddenToggle.IsChecked = settings.StartHidden;
        RestorePlacementToggle.IsChecked = settings.RestorePlacementOnUnpin;
        AddHandler(Button.MouseEnterEvent, new MouseEventHandler(Button_MouseEnter), true);
        AddHandler(Button.MouseLeaveEvent, new MouseEventHandler(Button_MouseLeave), true);
        AddHandler(Button.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Button_MouseDown), true);
        AddHandler(Button.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(Button_MouseUp), true);
        UpdateShortcut();
        UpdatePins();
        SourceInitialized += (_, _) => { if (!preview) InitializeServices(); };
        Loaded += (_, _) => { if (preview) RootGrid.Opacity = 1; else AnimateElement(RootGrid, 0, 1, 0, 0, 260); };
        timer.Tick += (_, _) =>
        {
            pins.Refresh();
            string signature = string.Join("|", pins.Entries.Select(e => $"{e.Handle}:{e.Title}:{e.CornerLabel}"));
            if (signature != lastListSignature) { lastListSignature = signature; UpdatePins(); }
        };
    }

    private void InitializeServices()
    {
        var handle = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(handle);
        source.AddHook(WindowProc);
        hotkey = new HotkeyService(handle);
        if (!hotkey.Change(settings.Modifiers, settings.VirtualKey))
        {
            StatusLabel.Text = "단축키 변경 필요";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(216, 163, 76));
            Footer.Text = "다른 앱이 단축키를 사용하고 있어요. ‘변경’에서 새 조합을 지정해 주세요.";
        }
        CreateTray();
        if (pins.Entries.Count > 0) timer.Start();
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && hotkey != null && wParam.ToInt32() == hotkey.Id)
        {
            handled = true;
            if (Modal.Visibility == Visibility.Visible) return IntPtr.Zero;
            var target = Native.GetForegroundWindow();
            Notice(pins.Toggle(target, restorePlacement: settings.RestorePlacementOnUnpin));
            UpdatePins();
        }
        return IntPtr.Zero;
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Afloat 열기", null, (_, _) => Dispatcher.Invoke(Reveal));
        menu.Items.Add("모든 창 고정 해제", null, (_, _) => Dispatcher.Invoke(ReleaseAll));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(Quit));
        tray = new Forms.NotifyIcon { Text = "Afloat · 창을 항상 위에", Icon = CreateTrayIcon(), ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Reveal);
    }

    internal static System.Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        using var violet = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(120, 109, 214));
        using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var line = new System.Drawing.Pen(System.Drawing.Color.FromArgb(210, 205, 248), 2);
        g.FillEllipse(violet, 0, 0, 31, 31); g.DrawRectangle(line, 6, 13, 15, 11); g.FillRectangle(white, 12, 7, 14, 11);
        var handle = bitmap.GetHicon();
        try { using var icon = System.Drawing.Icon.FromHandle(handle); return (System.Drawing.Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    internal void Reveal()
    {
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private void UpdateShortcut()
    {
        var labels = HotkeyService.Labels(settings.Modifiers, settings.VirtualKey);
        ShortcutCaps.ItemsSource = labels;
        SettingsHotkey.Text = string.Join(" + ", labels);
    }
    private void UpdatePins()
    {
        PinnedList.ItemsSource = pins.Entries.ToArray();
        PinCount.Text = pins.Entries.Count.ToString();
        EmptyState.Visibility = pins.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReleaseAllButton.IsEnabled = pins.Entries.Count > 0;
        if (tray != null) tray.Text = $"Afloat · {pins.Entries.Count}개 창 고정";
        if (!preview) timer.IsEnabled = pins.Entries.Count > 0;
        if (preview) PinnedList.Opacity = 1; else AnimateElement(PinnedList, 0.35, 1, 5, 0, 180);
    }
    private void Notice(string message)
    {
        Footer.Text = message;
        if (!settings.Notifications || preview || IsActive) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ShowToast(message)));
    }
    private void ShowToast(string message)
    {
        if (quitting || cleanedUp) return;
        try
        {
            var previous = toast; toast = null;
            if (previous?.IsLoaded == true) previous.Close();
            var next = new ToastWindow(message, settings.Animations);
            next.Closed += (_, _) => { if (ReferenceEquals(toast, next)) toast = null; };
            toast = next;
            next.Show();
        }
        catch
        {
            toast = null;
            try { tray?.ShowBalloonTip(2200, "Afloat", message, Forms.ToolTipIcon.Info); } catch { }
        }
    }
    private void ReleaseAll()
    {
        int left = pins.ReleaseAll(settings.RestorePlacementOnUnpin); UpdatePins();
        Notice(left == 0 ? "모든 창의 고정을 해제했어요." : $"{left}개 창을 해제하지 못했어요. 실행 권한을 확인해 주세요.");
    }
    private void ReleaseAll_Click(object sender, RoutedEventArgs e) => ReleaseAll();
    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is WindowEntry entry) Notice(pins.Release(entry, settings.RestorePlacementOnUnpin) ? $"‘{PinService.DisplayTitle(entry.Title)}’ 창의 항상 위 고정을 해제했어요." : $"‘{PinService.DisplayTitle(entry.Title)}’ 창의 고정을 해제할 수 없어요.");
        UpdatePins();
    }
    private void Corner_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is WindowEntry entry)
        {
            bool restoring = entry.OriginalRect != null;
            Notice(Native.MoveToCorner(entry) ? (restoring ? "원래 위치와 크기로 복원했어요." : "창을 왼쪽 위에 작게 배치했어요.") : "일반 크기의 창에서 사용할 수 있어요. 최대화 / 최소화를 먼저 해제해 주세요.");
            UpdatePins();
        }
    }
    private void Home_Click(object sender, RoutedEventArgs e) => SelectPage(false);
    private void Settings_Click(object sender, RoutedEventArgs e) => SelectPage(true);
    private void SelectPage(bool showSettings)
    {
        HomePage.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        PageLabel.Text = showSettings ? "PREFERENCES" : "WORKSPACE";
        HomeNav.Background = (Brush)new BrushConverter().ConvertFrom(showSettings ? "Transparent" : "#DEDBF1")!;
        SettingsNav.Background = (Brush)new BrushConverter().ConvertFrom(showSettings ? "#DEDBF1" : "Transparent")!;
        UIElement activePage = showSettings ? (UIElement)SettingsPage : HomePage;
        if (preview) activePage.Opacity = 1;
        else AnimateElement(activePage, 0, 1, 14, 0, 220);
    }
    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        Modal.Visibility = Visibility.Visible; HotkeyDialog.Visibility = Visibility.Visible; PickerDialog.Visibility = Visibility.Collapsed; AnimateModalIn();
        candidateMods = 0; candidateKey = 0; SaveHotkeyButton.IsEnabled = false;
        CaptureLabel.Text = "단축키 입력 대기 중…"; CaptureHint.Text = "예: Ctrl + Alt + Space · Esc를 누르면 취소";
        Keyboard.Focus(SaveHotkeyButton.IsEnabled ? SaveHotkeyButton : this);
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Modal.Visibility != Visibility.Visible) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { CloseModal(); e.Handled = true; return; }
        if (HotkeyDialog.Visibility != Visibility.Visible) return;
        // Tab and plain Enter stay available for keyboard navigation / saving.
        if ((key == Key.Tab || key == Key.Enter) && Keyboard.Modifiers == ModifierKeys.None) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 4;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) || !HotkeyService.Valid(mods, vk))
        {
            SaveHotkeyButton.IsEnabled = false;
            CaptureHint.Text = "Ctrl 또는 Alt와 다른 키를 함께 눌러 주세요. Windows 키와 F12는 사용할 수 없어요."; return;
        }
        candidateMods = mods; candidateKey = vk; CaptureLabel.Text = string.Join(" + ", HotkeyService.Labels(mods, vk));
        CaptureHint.Text = "이 조합으로 창을 고정하고 해제합니다."; SaveHotkeyButton.IsEnabled = true;
    }
    private void SaveHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (!preview && (hotkey == null || !hotkey.Change(candidateMods, candidateKey)))
        { CaptureHint.Text = "다른 앱이 사용하는 단축키예요. 다른 조합을 눌러 주세요. 기존 단축키는 유지됩니다."; return; }
        settings.Modifiers = candidateMods; settings.VirtualKey = candidateKey;
        UpdateShortcut(); CloseModal();
        StatusLabel.Text = "단축키 대기 중"; StatusDot.Fill = new SolidColorBrush(Color.FromRgb(101, 167, 132));
        Notice(SaveSettings() ? "새 단축키를 저장했어요." : "단축키는 변경했지만 설정 파일을 저장하지 못했어요. 폴더의 쓰기 권한을 확인해 주세요.");
    }
    private bool SaveSettings() => preview || settings.Save(settingsPath);
    private void Preference_Click(object sender, RoutedEventArgs e)
    {
        settings.Notifications = NotificationToggle.IsChecked == true;
        settings.CloseToTray = TrayToggle.IsChecked == true;
        settings.Animations = AnimationToggle.IsChecked == true;
        settings.HardwareAcceleration = HardwareToggle.IsChecked == true;
        settings.StartHidden = StartHiddenToggle.IsChecked == true;
        settings.RestorePlacementOnUnpin = RestorePlacementToggle.IsChecked == true;
        bool requestedStartup = StartupToggle.IsChecked == true;
        if (requestedStartup != settings.StartWithWindows && !StartupService.Set(requestedStartup))
        {
            StartupToggle.IsChecked = settings.StartWithWindows;
            Footer.Text = "Windows 시작 설정을 변경하지 못했어요.";
            return;
        }
        settings.StartWithWindows = requestedStartup;
        if (!settings.Animations) ResetAnimationState();
        bool saved = SaveSettings();
        Footer.Text = saved ? (ReferenceEquals(sender, HardwareToggle) ? "하드웨어 가속 설정을 저장했어요. 다음 실행부터 적용됩니다." : "설정을 저장했어요.") : "설정을 저장할 수 없어요. 폴더의 쓰기 권한을 확인해 주세요.";
    }
    private void ResetAnimationState()
    {
        foreach (UIElement element in new UIElement[] { RootGrid, HomePage, SettingsPage, PinnedList, Modal })
        {
            element.BeginAnimation(OpacityProperty, null); element.Opacity = 1;
            if (element.RenderTransform is TranslateTransform translate) { translate.BeginAnimation(TranslateTransform.YProperty, null); translate.Y = 0; }
        }
        if (ModalCard.RenderTransform is ScaleTransform scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = 1; }
    }
    private static Button? ButtonFrom(object source)
    {
        DependencyObject? node = source as DependencyObject;
        while (node != null)
        {
            if (node is Button button) return button;
            try { node = VisualTreeHelper.GetParent(node); }
            catch { node = LogicalTreeHelper.GetParent(node); }
        }
        return null;
    }
    private void Button_MouseEnter(object sender, MouseEventArgs e) { if (ButtonFrom(e.OriginalSource) is Button button) AnimateButton(button, 1.015, 0.92, 130); }
    private void Button_MouseLeave(object sender, MouseEventArgs e) { if (ButtonFrom(e.OriginalSource) is Button button) AnimateButton(button, 1, 1, 150); }
    private void Button_MouseDown(object sender, MouseButtonEventArgs e) { if (ButtonFrom(e.OriginalSource) is Button button) AnimateButton(button, 0.97, 0.78, 80); }
    private void Button_MouseUp(object sender, MouseButtonEventArgs e) { if (ButtonFrom(e.OriginalSource) is Button button) AnimateButton(button, button.IsMouseOver ? 1.015 : 1, button.IsMouseOver ? 0.92 : 1, 120); }
    private void AnimateButton(Button button, double scaleValue, double opacity, int milliseconds)
    {
        if (!settings.Animations || !button.IsEnabled)
        {
            button.BeginAnimation(OpacityProperty, null); button.Opacity = 1;
            if (button.RenderTransform is ScaleTransform oldScale) { oldScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); oldScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); oldScale.ScaleX = oldScale.ScaleY = 1; }
            return;
        }
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = button.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1); button.RenderTransform = scale;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleValue, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleValue, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        button.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
    }
    private void ChooseWindow_Click(object sender, RoutedEventArgs e)
    {
        Modal.Visibility = Visibility.Visible; HotkeyDialog.Visibility = Visibility.Collapsed; PickerDialog.Visibility = Visibility.Visible; RefreshPicker(); AnimateModalIn();
    }
    private void RefreshPicker()
    {
        var entries = Native.Windows().Where(w => !pins.Entries.Any(p => p.Handle == w.Handle) && !Native.IsTopmost(w.Handle)).ToArray();
        WindowPicker.ItemsSource = entries; PickerEmpty.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RefreshPicker_Click(object sender, RoutedEventArgs e) => RefreshPicker();
    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is WindowEntry entry)
        {
            CloseModal();
            Notice(Native.SameWindow(entry) ? pins.Toggle(entry.Handle, restorePlacement: settings.RestorePlacementOnUnpin) : "선택한 창이 닫혔어요. 목록을 새로고침해 주세요.");
            UpdatePins();
        }
    }
    private void CloseModal()
    {
        if (preview || !settings.Animations) { FinishCloseModal(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => FinishCloseModal(); Modal.BeginAnimation(OpacityProperty, fade);
        if (ModalCard.RenderTransform is ScaleTransform scale) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, TimeSpan.FromMilliseconds(130))); scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, TimeSpan.FromMilliseconds(130))); }
    }
    private void FinishCloseModal() { Modal.Visibility = Visibility.Collapsed; Modal.Opacity = 1; HotkeyDialog.Visibility = Visibility.Collapsed; PickerDialog.Visibility = Visibility.Collapsed; Keyboard.Focus(HomeNav); }
    private void AnimateModalIn()
    {
        if (preview || !settings.Animations) { Modal.Opacity = 1; if (ModalCard.RenderTransform is ScaleTransform still) still.ScaleX = still.ScaleY = 1; return; }
        AnimateElement(Modal, 0, 1, 0, 0, 160);
        if (ModalCard.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = scale.ScaleY = 0.96;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
        }
    }
    private void AnimateElement(UIElement element, double fromOpacity, double toOpacity, double fromY, double toY, int milliseconds)
    {
        var translate = element.RenderTransform as TranslateTransform ?? new TranslateTransform(); element.RenderTransform = translate;
        element.BeginAnimation(OpacityProperty, null); translate.BeginAnimation(TranslateTransform.YProperty, null);
        if (!settings.Animations || preview) { element.Opacity = toOpacity; translate.Y = toY; return; }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.Opacity = fromOpacity;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(toOpacity, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
        translate.Y = fromY; translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(toY, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = ease });
    }
    private void CancelModal_Click(object sender, RoutedEventArgs e) => CloseModal();
    private void Modal_MouseDown(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == Modal) CloseModal(); }
    private void Dialog_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Exit_Click(object sender, RoutedEventArgs e) => Quit();
    internal void Quit()
    {
        int failures = pins.ReleaseAll(settings.RestorePlacementOnUnpin); UpdatePins();
        if (failures > 0) { Reveal(); Notice($"{failures}개 창의 고정을 해제하지 못했어요. 해제 후 다시 종료해 주세요."); return; }
        quitting = true; Cleanup(); Close(); System.Windows.Application.Current.Shutdown();
    }
    internal void Cleanup()
    {
        if (cleanedUp) return;
        cleanedUp = true;
        timer.Stop(); pins.ReleaseAll(settings.RestorePlacementOnUnpin); hotkey?.Dispose();
        if (source != null) { try { source.RemoveHook(WindowProc); } catch { } source = null; }
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); tray = null; }
        try { if (toast?.IsLoaded == true) toast.Close(); } catch { }
        toast = null;
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (preview || quitting) return;
        if (settings.CloseToTray && tray != null)
        {
            e.Cancel = true;
            Hide();
            Notice("Afloat는 트레이에서 계속 실행 중이에요.");
        }
        else
        {
            int failures = pins.ReleaseAll(settings.RestorePlacementOnUnpin); UpdatePins();
            if (failures > 0) { e.Cancel = true; Notice($"{failures}개 창을 해제하지 못했어요. 해제 후 다시 종료해 주세요."); return; }
            quitting = true; Cleanup();
            Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown()));
        }
    }
    internal void SetPreview(string view)
    {
        if (view == "settings") SelectPage(true);
        if (view == "hotkey") ChangeHotkey_Click(this, new RoutedEventArgs());
        if (view == "pinned")
        {
            pins.Entries.Add(new WindowEntry { Title = "잔잔한 재즈와 함께하는 오후 — YouTube", ProcessName = "chrome" });
            pins.Entries.Add(new WindowEntry { Title = "오늘의 작업 메모", ProcessName = "notepad" });
            UpdatePins();
        }
    }
    internal bool ExerciseCloseButtonForTest()
    {
        settings.CloseToTray = true;
        Close();
        bool hidden = !IsVisible;
        quitting = true;
        Cleanup();
        Cleanup();
        Close();
        return hidden;
    }
    internal bool ExerciseAnimationsForTest()
    {
        settings.Animations = true;
        for (int i = 0; i < 12; i++) SelectPage(i % 2 == 0);
        for (int i = 0; i < 12; i++) AnimateButton(HomeNav, i % 2 == 0 ? 0.97 : 1, i % 2 == 0 ? 0.8 : 1, 40);
        settings.Animations = false;
        ResetAnimationState();
        bool reset = RootGrid.Opacity == 1 && HomePage.Opacity == 1 && SettingsPage.Opacity == 1 && PinnedList.Opacity == 1;
        settings.Animations = true;
        SelectPage(false);
        return reset && HomePage.Visibility == Visibility.Visible;
    }
}

internal sealed class ToastWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2.1) };
    private readonly TranslateTransform slide = new();
    private bool closing;
    private readonly bool animate;
    internal ToastWindow(string message, bool animate)
    {
        this.animate = animate;
        Width = 380; Height = 68; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowActivated = false; ShowInTaskbar = false;
        IsHitTestVisible = false;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(249, 248, 253)), CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 14, 20, 14), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(221, 217, 239)) };
        border.Child = new TextBlock { Text = message, FontFamily = new FontFamily("Segoe UI, Malgun Gothic"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(80, 74, 111)), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        slide.Y = animate ? 18 : 0;
        border.RenderTransform = slide;
        Content = border;
        var foreground = Native.GetForegroundWindow();
        var screen = Forms.Screen.FromHandle(foreground).WorkingArea;
        double scale = foreground == IntPtr.Zero ? 1 : Math.Max(96, Native.GetDpiForWindow(foreground)) / 96.0;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = (screen.Right - 24 * scale) / scale - Width;
        Top = (screen.Bottom - 24 * scale) / scale - Height;
        Opacity = animate ? 0 : 1;
        timer.Tick += (_, _) => AnimateOut();
        Closed += (_, _) => timer.Stop();
        Loaded += (_, _) =>
        {
            timer.Start();
            if (animate)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            }
        };
    }
    private void AnimateOut()
    {
        if (closing) return; closing = true; timer.Stop();
        if (!animate) { Close(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(160)));
    }
}

