using System.Diagnostics;
using System.Windows.Forms;

var root = AppContext.BaseDirectory;
var appPath = Path.Combine(root, "DockPetWin", "bin", "Debug", "net8.0-windows", "DockPetWin.exe");

if (!File.Exists(appPath))
{
    MessageBox.Show(
        $"找不到桌宠主程序：\n{appPath}\n\n请先构建 DockPetWin Debug 版本。",
        "DockPetWin",
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);
    return;
}

Process.Start(new ProcessStartInfo
{
    FileName = appPath,
    WorkingDirectory = Path.GetDirectoryName(appPath)!,
    UseShellExecute = true
});
