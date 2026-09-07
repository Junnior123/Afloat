using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;

namespace Afloat;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var bootSettings = Settings.Load(Settings.DefaultPath);
        RenderOptions.ProcessRenderMode = bootSettings.HardwareAcceleration ? RenderMode.Default : RenderMode.SoftwareOnly;
        if (e.Args.Length >= 2 && e.Args[0] == "--self-test")
        {
            int code = SelfTest.Run(e.Args[1]); Shutdown(code); return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--render-preview")
        {
            var preview = new MainWindow(true) { ShowActivated = false };
            MainWindow = preview;
            if (e.Args.Length >= 3) preview.SetPreview(e.Args[2]);
            preview.Show();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    preview.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(preview);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(e.Args[1]); encoder.Save(stream);
                    using var icon = Afloat.MainWindow.CreateTrayIcon();
                    using var iconStream = File.Create(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "Afloat.ico")); icon.Save(iconStream);
                }
                finally { preview.Close(); Shutdown(); }
            }));
            return;
        }
        mutex = new Mutex(true, "Local\\Afloat.Desktop.Singleton", out bool created);
        if (!created)
        {
            MessageBox.Show("Afloat가 이미 실행 중이에요. 작업 표시줄의 숨겨진 아이콘에서 Afloat를 더블 클릭해 주세요.", "Afloat", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }
        var main = new MainWindow(); MainWindow = main;
        DispatcherUnhandledException += (_, args) =>
        {
            main.Cleanup(); args.Handled = true;
            MessageBox.Show("Afloat를 계속 실행할 수 없어 창 고정을 해제했습니다. 앱을 다시 열어 주세요.\n\n" + args.Exception.Message, "Afloat", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        SessionEnding += (_, _) => main.Cleanup();
        bool startHidden = bootSettings.StartHidden && Array.Exists(e.Args, arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
        if (startHidden) main.ShowActivated = false;
        main.Show();
        if (startHidden) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(main.Hide));
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow main) main.Cleanup();
        mutex?.Dispose(); base.OnExit(e);
    }
}

