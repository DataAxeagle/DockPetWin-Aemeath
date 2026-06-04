using System.Diagnostics;
using System.Windows.Forms;

var appPath = FindDockPetApp();
var layoutEditorMode = IsLayoutEditorLauncher(args);

if (appPath is null)
{
    MessageBox.Show(
        "Cannot find DockPetWin.exe. Build DockPetWin first, or put this launcher next to DockPetWin.exe.",
        "DockPetWin",
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);
    return;
}

var startInfo = new ProcessStartInfo
{
    FileName = appPath,
    WorkingDirectory = Path.GetDirectoryName(appPath)!,
    UseShellExecute = false
};

if (layoutEditorMode)
{
    startInfo.ArgumentList.Add("--home-layout-editor");
    startInfo.Environment["DOCKPET_HOME_LAYOUT_EDITOR"] = "1";
}

foreach (var arg in args.Where(arg => !IsHomeLayoutEditorArg(arg)))
{
    startInfo.ArgumentList.Add(arg);
}

Process.Start(startInfo);

static bool IsLayoutEditorLauncher(string[] args)
{
    if (args.Any(IsHomeLayoutEditorArg))
    {
        return true;
    }

    var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
    if (string.Equals(processName, "DockPetHomeLayoutEditor", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("HomeLayoutEditor", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("小屋编辑器", StringComparison.OrdinalIgnoreCase)
        || processName.Contains("布局编辑器", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return false;
}

static bool IsHomeLayoutEditorArg(string arg)
{
    return string.Equals(arg, "--home-layout-editor", StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "/home-layout-editor", StringComparison.OrdinalIgnoreCase);
}

static string? FindDockPetApp()
{
    var launcherPath = Environment.ProcessPath;
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    var sideBySideCandidates = new List<string>();
    for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
    {
        foreach (var candidate in ProjectAppPaths(directory.FullName))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (launcherPath is not null
                && string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(launcherPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return candidate;
        }

        foreach (var candidate in SideBySideAppPaths(directory.FullName))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (launcherPath is not null
                && string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(launcherPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sideBySideCandidates.Add(candidate);
        }
    }

    return sideBySideCandidates.FirstOrDefault();
}

static IEnumerable<string> ProjectAppPaths(string root)
{
    yield return Path.Combine(root, "DockPetWin", "bin", "Debug", "net8.0-windows", "DockPetWin.exe");
    yield return Path.Combine(root, "DockPetWin", "bin", "Release", "net8.0-windows", "DockPetWin.exe");
    yield return Path.Combine(root, "DockPetWin", "bin", "Release", "net8.0-windows", "win-x64", "DockPetWin.exe");
    yield return Path.Combine(root, "DockPetWin", "DockPetWin.exe");
}

static IEnumerable<string> SideBySideAppPaths(string root)
{
    yield return Path.Combine(root, "DockPetWin.exe");
}
