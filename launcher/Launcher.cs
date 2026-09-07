using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

// Small .NET Framework bootstrapper. The actual application is the .NET 8 WPF assembly.
// This keeps startup silent and works with SDK installations that omit native apphost.exe.
internal static class Launcher
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            string assembly = Path.Combine(folder, "Afloat.dll");
            if (!File.Exists(assembly)) throw new FileNotFoundException("압축 파일을 모두 풀어 주세요. Afloat.exe와 Afloat.dll은 같은 폴더에 있어야 합니다.");
            string root = null;
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"))
            { if (key != null) root = key.GetValue("InstallLocation") as string; }
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            string dotnet = Path.Combine(root, "dotnet.exe");
            string desktop = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
            bool hasRuntime = false;
            if (Directory.Exists(desktop)) foreach (string d in Directory.GetDirectories(desktop)) if (Path.GetFileName(d).StartsWith("8.")) hasRuntime = true;
            if (!File.Exists(dotnet) || !hasRuntime)
            {
                MessageBox.Show("Afloat에는 Microsoft .NET 8 Desktop Runtime (Windows x64)이 필요합니다.\n\nhttps://dotnet.microsoft.com/download/dotnet/8.0\n\nDesktop Runtime 설치 후 다시 실행해 주세요.", "Afloat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            string arguments = "\"" + assembly + "\"";
            foreach (string arg in args) arguments += " \"" + arg.Replace("\"", "\\\"") + "\"";
            var info = new ProcessStartInfo(dotnet, arguments) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            Process.Start(info);
            return 0;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Afloat", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}

