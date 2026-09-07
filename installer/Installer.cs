using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Afloat Setup")]
[assembly: AssemblyDescription("Afloat 1.1.0 Installer")]
[assembly: AssemblyCompany("Junnior123")]
[assembly: AssemblyProduct("Afloat")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal static class InstallerProgram
{
    internal const string Product = "Afloat";
    internal const string AppVersion = "1.1.0";
    internal static readonly string InstallDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Product);
    internal static readonly string DesktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Afloat.lnk");
    internal static readonly string MenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", Product);
    internal static readonly string MenuShortcut = Path.Combine(MenuDirectory, "Afloat.lnk");
    internal static readonly Dictionary<string, string> Payload = new Dictionary<string, string>
    {
        { "Afloat.exe", "payload.Afloat.exe" }, { "Afloat.dll", "payload.Afloat.dll" },
        { "Afloat.deps.json", "payload.Afloat.deps.json" }, { "Afloat.runtimeconfig.json", "payload.Afloat.runtimeconfig.json" },
        { "사용 안내.md", "payload.guide.md" }
    };

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length >= 2 && string.Equals(args[0], "/self-test", StringComparison.OrdinalIgnoreCase)) return Verify(args[1]);
        if (args.Length >= 2 && string.Equals(args[0], "/render-preview", StringComparison.OrdinalIgnoreCase)) return RenderPreview(args[1]);
        if (args.Length > 0 && string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase)) { Application.Run(new UninstallForm()); return 0; }
        Application.Run(new InstallForm()); return 0;
    }

    private static int RenderPreview(string path)
    {
        try
        {
            using (InstallForm form = new InstallForm())
            {
                form.Show(); Application.DoEvents();
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
                form.Close();
            }
            return 0;
        }
        catch { return 1; }
    }

    private static int Verify(string report)
    {
        List<string> lines = new List<string>();
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (KeyValuePair<string, string> item in Payload)
            {
                using (Stream stream = assembly.GetManifestResourceStream(item.Value))
                {
                    if (stream == null || stream.Length == 0) throw new InvalidDataException(item.Value);
                    if (item.Key == "Afloat.dll" && stream.Length < 30000) throw new InvalidDataException("Afloat.dll payload is incomplete");
                    using (SHA256 hash = SHA256.Create()) lines.Add("PASS: " + item.Key + " embedded · " + BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").Substring(0, 12));
                }
            }
            using (Stream icon = assembly.GetManifestResourceStream("installer.Afloat.ico")) if (icon == null || icon.Length == 0) throw new InvalidDataException("installer icon");
            lines.Add("PASS: installer icon embedded");
            using (Stream logo = assembly.GetManifestResourceStream("installer.Afloat-logo.png")) if (logo == null || logo.Length == 0) throw new InvalidDataException("installer logo");
            lines.Add("PASS: supplied Afloat logo embedded");
            string shortcut = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "Afloat-installer-test.lnk");
            Shortcut(shortcut);
            if (!File.Exists(shortcut)) throw new IOException("shortcut test failed");
            File.Delete(shortcut); lines.Add("PASS: Windows shortcut creation available");
            lines.Add(RuntimeVersion() == null ? "INFO: .NET 8 Desktop Runtime is not installed" : "PASS: .NET Desktop Runtime detected · " + RuntimeVersion());
            lines.Add("ALL INSTALLER TESTS PASSED");
            File.WriteAllLines(report, lines.ToArray()); return 0;
        }
        catch (Exception ex) { lines.Add(ex.ToString()); File.WriteAllLines(report, lines.ToArray()); return 1; }
    }

    internal static bool IsAppRunning()
    {
        try { using (Mutex.OpenExisting("Local\\Afloat.Desktop.Singleton")) return true; }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch { return false; }
    }

    internal static string RuntimeVersion()
    {
        string root = null;
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"))
                if (key != null) root = key.GetValue("InstallLocation") as string;
        }
        catch { }
        if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        string shared = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(shared)) return null;
        System.Version best = null;
        foreach (string directory in Directory.GetDirectories(shared))
        {
            System.Version found;
            if (System.Version.TryParse(Path.GetFileName(directory).Split('-')[0], out found) && found.Major >= 8 && (best == null || found > best)) best = found;
        }
        return best == null ? null : best.ToString();
    }

    internal static Icon InstallerIcon()
    {
        Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("installer.Afloat.ico");
        return stream == null ? SystemIcons.Application : new Icon(stream);
    }

    internal static Image InstallerLogo()
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("installer.Afloat-logo.png"))
        {
            if (stream == null) return null;
            using (Image image = Image.FromStream(stream)) return new Bitmap(image);
        }
    }

    internal static void Extract()
    {
        Directory.CreateDirectory(InstallDirectory);
        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (KeyValuePair<string, string> item in Payload)
        {
            string target = Path.Combine(InstallDirectory, item.Key);
            string temporary = target + ".installing";
            using (Stream input = assembly.GetManifestResourceStream(item.Value))
            {
                if (input == null) throw new MissingManifestResourceException(item.Value);
                using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            }
            if (new FileInfo(temporary).Length == 0) throw new InvalidDataException(item.Key + " 파일이 비어 있습니다.");
            if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
        }
        string installerCopy = Path.Combine(InstallDirectory, "Uninstall.exe");
        File.Copy(Application.ExecutablePath, installerCopy, true);
    }

    internal static void Shortcut(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Type type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) throw new InvalidOperationException("Windows 바로가기 서비스를 찾지 못했습니다.");
        object shell = Activator.CreateInstance(type);
        object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        Type shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(InstallDirectory, "Afloat.exe") });
        shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { InstallDirectory });
        shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Afloat · 창을 항상 위에" });
        shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(InstallDirectory, "Afloat.exe") + ",0" });
        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }

    internal static void RegisterUninstall()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Afloat"))
        {
            key.SetValue("DisplayName", "Afloat"); key.SetValue("DisplayVersion", AppVersion); key.SetValue("Publisher", "Junnior123");
            key.SetValue("DisplayIcon", Path.Combine(InstallDirectory, "Afloat.exe")); key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("UninstallString", "\"" + Path.Combine(InstallDirectory, "Uninstall.exe") + "\" /uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            long size = 0; foreach (string file in Directory.GetFiles(InstallDirectory)) size += new FileInfo(file).Length;
            key.SetValue("EstimatedSize", (int)Math.Max(1, size / 1024), RegistryValueKind.DWord);
        }
    }

    internal static void DeleteIfExists(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    internal static void RemoveStartupEntry() { try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) if (key != null) key.DeleteValue("Afloat", false); } catch { } }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] internal static extern bool MoveFileEx(string existing, string replacement, int flags);
}

internal sealed class InstallForm : Form
{
    private readonly Color Ink = Color.FromArgb(37, 38, 49), Muted = Color.FromArgb(121, 123, 136), Accent = Color.FromArgb(107, 101, 223), Pale = Color.FromArgb(242, 241, 248);
    private readonly Label runtimeTitle = new Label(), runtimeDetail = new Label(), status = new Label();
    private readonly CheckBox desktop = new CheckBox(), menu = new CheckBox(), launch = new CheckBox();
    private readonly Button install = new Button(), runtimeButton = new Button();
    private readonly ProgressBar progress = new ProgressBar();

    internal InstallForm()
    {
        Text = "Afloat 설치"; ClientSize = new Size(720, 610); MinimumSize = MaximumSize = Size; StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 248, 251); ForeColor = Ink; Font = new Font("Segoe UI", 9F); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; Icon = InstallerProgram.InstallerIcon();
        Build(); CheckRuntime();
    }

    private void Build()
    {
        LogoPanel logo = new LogoPanel(); logo.SetBounds(42, 37, 58, 58);
        Controls.Add(logo);
        Controls.Add(Position(L("Afloat", 26, FontStyle.Bold, Ink), 119, 38, 300, 40));
        Controls.Add(Position(L("작은 창, 편안한 몰입.", 10, FontStyle.Regular, Muted), 121, 77, 300, 24));
        Label version = Position(L("VERSION 1.1.0", 8, FontStyle.Bold, Color.FromArgb(148, 145, 163)), 560, 51, 110, 22); version.TextAlign = ContentAlignment.MiddleRight; Controls.Add(version);

        RoundedPanel runtimeCard = new RoundedPanel(18, Color.White); runtimeCard.SetBounds(42, 127, 636, 112); runtimeCard.Padding = new Padding(22); Controls.Add(runtimeCard);
        runtimeTitle.AutoSize = false; runtimeTitle.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold); runtimeTitle.ForeColor = Ink; runtimeTitle.SetBounds(22, 20, 380, 24); runtimeCard.Controls.Add(runtimeTitle);
        runtimeDetail.AutoSize = false; runtimeDetail.Font = new Font(Font.FontFamily, 9F); runtimeDetail.ForeColor = Muted; runtimeDetail.SetBounds(22, 55, 405, 36); runtimeCard.Controls.Add(runtimeDetail);
        StyleButton(runtimeButton, "런타임 받기", false); runtimeButton.SetBounds(468, 34, 142, 42); runtimeCard.Controls.Add(runtimeButton);

        Controls.Add(Position(L("설치 옵션", 11, FontStyle.Bold, Ink), 45, 270, 200, 28));
        RoundedPanel options = new RoundedPanel(18, Color.White); options.SetBounds(42, 305, 636, 147); Controls.Add(options);
        Option(desktop, "바탕 화면 바로가기 만들기", true, 18); Option(menu, "시작 메뉴 바로가기 만들기", true, 57); Option(launch, "완료를 누르면 Afloat 실행", true, 96);
        options.Controls.Add(desktop); options.Controls.Add(menu); options.Controls.Add(launch);

        progress.SetBounds(42, 483, 636, 5); progress.Style = ProgressBarStyle.Continuous; progress.Visible = false; Controls.Add(progress);
        status.AutoSize = false; status.ForeColor = Muted; status.SetBounds(42, 500, 410, 42); status.Text = "사용자 폴더에 설치되어 관리자 권한이 필요하지 않아요."; Controls.Add(status);
        StyleButton(install, "설치하기", true); install.SetBounds(520, 505, 158, 48); install.Click += InstallClick; Controls.Add(install);
        Label credit = Position(L("· 이 앱은 ChatGPT를 통해 제작되었습니다.", 8, FontStyle.Regular, Color.FromArgb(164, 162, 176)), 370, 575, 308, 20); credit.TextAlign = ContentAlignment.MiddleRight; Controls.Add(credit);
    }

    private void CheckRuntime()
    {
        string found = InstallerProgram.RuntimeVersion();
        bool ready = found != null;
        runtimeTitle.Text = ready ? "●  .NET Desktop Runtime 준비됨" : "●  .NET Desktop Runtime이 필요해요";
        runtimeTitle.ForeColor = ready ? Color.FromArgb(75, 147, 108) : Color.FromArgb(201, 126, 65);
        runtimeDetail.Text = ready ? ".NET " + found + " (x64)을 확인했습니다. 바로 설치할 수 있어요." : ".NET 8 Desktop Runtime (x64)을 설치한 뒤 다시 실행해 주세요.";
        runtimeButton.Text = ready ? "다시 확인" : "런타임 받기";
        runtimeButton.Click -= RuntimeRecheck; runtimeButton.Click += RuntimeRecheck;
        install.Enabled = ready;
    }
    private void RuntimeRecheck(object sender, EventArgs e) { if (InstallerProgram.RuntimeVersion() == null) Process.Start("https://dotnet.microsoft.com/download/dotnet/8.0"); CheckRuntime(); }

    private async void InstallClick(object sender, EventArgs e)
    {
        if (InstallerProgram.IsAppRunning()) { MessageBox.Show("업데이트하려면 트레이 메뉴에서 Afloat를 먼저 종료해 주세요.", "Afloat", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        install.Enabled = false; runtimeButton.Enabled = false; desktop.Enabled = menu.Enabled = launch.Enabled = false; progress.Visible = true; progress.Style = ProgressBarStyle.Marquee; status.Text = "Afloat를 설치하고 있어요…";
        try
        {
            await System.Threading.Tasks.Task.Run(delegate
            {
                InstallerProgram.Extract();
                if (desktop.Checked) InstallerProgram.Shortcut(InstallerProgram.DesktopShortcut); else InstallerProgram.DeleteIfExists(InstallerProgram.DesktopShortcut);
                if (menu.Checked) InstallerProgram.Shortcut(InstallerProgram.MenuShortcut); else InstallerProgram.DeleteIfExists(InstallerProgram.MenuShortcut);
                InstallerProgram.RegisterUninstall();
            });
            progress.Style = ProgressBarStyle.Continuous; progress.Value = 100; status.Text = "설치가 완료됐어요."; install.Text = "완료"; install.Enabled = true;
            install.Click -= InstallClick; install.Click += FinishClick;
        }
        catch (Exception ex) { progress.Visible = false; status.Text = "설치를 완료하지 못했어요."; install.Enabled = true; MessageBox.Show(ex.Message, "Afloat 설치 오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void FinishClick(object sender, EventArgs e)
    {
        try
        {
            if (launch.Checked)
            {
                string executable = Path.Combine(InstallerProgram.InstallDirectory, "Afloat.exe");
                Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = InstallerProgram.InstallDirectory, UseShellExecute = true });
            }
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Afloat를 실행하지 못했어요. 바탕 화면이나 시작 메뉴에서 다시 실행해 주세요.\n\n" + ex.Message, "Afloat", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Option(CheckBox box, string text, bool value, int y) { box.Text = text; box.Checked = value; box.AutoSize = false; box.SetBounds(22, y, 590, 34); box.FlatStyle = FlatStyle.Flat; box.ForeColor = Ink; box.Font = new Font(Font.FontFamily, 10F); }
    private Label L(string text, float size, FontStyle style, Color color) { return new Label { Text = text, AutoSize = false, Font = new Font("Segoe UI", size, style), ForeColor = color, BackColor = Color.Transparent }; }
    private T Position<T>(T control, int x, int y, int width, int height) where T : Control { control.SetBounds(x, y, width, height); return control; }
    private void StyleButton(Button button, string text, bool primary) { button.Text = text; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.BackColor = primary ? Accent : Pale; button.ForeColor = primary ? Color.White : Ink; button.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold); button.Cursor = Cursors.Hand; }
}

internal sealed class UninstallForm : Form
{
    internal UninstallForm()
    {
        Text = "Afloat 제거"; ClientSize = new Size(510, 245); StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(248, 248, 251); Font = new Font("Segoe UI", 9F); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; Icon = InstallerProgram.InstallerIcon();
        Label title = new Label { Text = "Afloat를 제거할까요?", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = false }; title.SetBounds(36, 34, 430, 40); Controls.Add(title);
        Label detail = new Label { Text = "앱 파일과 바로가기를 삭제합니다. 개인 설정은 다음 설치를 위해 남겨둡니다.", ForeColor = Color.FromArgb(121, 123, 136), AutoSize = false }; detail.SetBounds(38, 88, 430, 42); Controls.Add(detail);
        Button cancel = Button("취소", Color.FromArgb(242, 241, 248), Color.FromArgb(37, 38, 49)); cancel.SetBounds(258, 164, 96, 42); cancel.Click += delegate { Close(); }; Controls.Add(cancel);
        Button remove = Button("제거", Color.FromArgb(107, 101, 223), Color.White); remove.SetBounds(365, 164, 105, 42); remove.Click += RemoveClick; Controls.Add(remove);
    }
    private Button Button(string text, Color back, Color fore) { Button b = new Button { Text = text, BackColor = back, ForeColor = fore, FlatStyle = FlatStyle.Flat, Font = new Font(Font.FontFamily, 9F, FontStyle.Bold), Cursor = Cursors.Hand }; b.FlatAppearance.BorderSize = 0; return b; }
    private void RemoveClick(object sender, EventArgs e)
    {
        if (InstallerProgram.IsAppRunning()) { MessageBox.Show("트레이 메뉴에서 Afloat를 종료한 뒤 다시 제거해 주세요.", "Afloat", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            InstallerProgram.DeleteIfExists(InstallerProgram.DesktopShortcut); InstallerProgram.DeleteIfExists(InstallerProgram.MenuShortcut);
            InstallerProgram.RemoveStartupEntry();
            try { if (Directory.Exists(InstallerProgram.MenuDirectory)) Directory.Delete(InstallerProgram.MenuDirectory, false); } catch { }
            using (RegistryKey parent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true)) if (parent != null) parent.DeleteSubKeyTree("Afloat", false);
            foreach (string file in Directory.GetFiles(InstallerProgram.InstallDirectory)) if (!string.Equals(file, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) InstallerProgram.DeleteIfExists(file);
            InstallerProgram.MoveFileEx(Application.ExecutablePath, null, 4);
            MessageBox.Show("Afloat를 제거했습니다.", "Afloat", MessageBoxButtons.OK, MessageBoxIcon.Information); Close();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Afloat 제거 오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

internal sealed class RoundedPanel : Panel
{
    private readonly int radius; private readonly Color fill;
    internal RoundedPanel(int radius, Color fill) { this.radius = radius; this.fill = fill; BackColor = fill; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Color.FromArgb(248, 248, 251));
    }
    private GraphicsPath Path()
    {
        GraphicsPath path = new GraphicsPath(); int d = radius * 2; Rectangle r = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath path = Path())
        {
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
        }
    }
}

internal sealed class LogoPanel : Control
{
    private readonly Image logo = InstallerProgram.InstallerLogo();
    internal LogoPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); BackColor = Color.FromArgb(248, 248, 251); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality; e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (logo != null) e.Graphics.DrawImage(logo, new Rectangle(0, 0, Width, Height), new Rectangle(5, 6, 57, 57), GraphicsUnit.Pixel);
    }
    protected override void Dispose(bool disposing) { if (disposing && logo != null) logo.Dispose(); base.Dispose(disposing); }
}
