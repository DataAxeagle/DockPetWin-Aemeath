namespace DockPetWin;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        HomeWindow.EnsurePlacementConfigFile();
        HomeWindow.EnsureFurnitureConfigFile();
        base.OnStartup(e);

        if (e.Args.Any(IsHomeLayoutEditorArg) || IsHomeLayoutEditorEnvironmentSet() || IsHomeLayoutEditorProcess())
        {
            var editor = HomeWindow.CreateLayoutEditorWindow();
            MainWindow = editor;
            editor.Show();
            return;
        }

        if (TryGetHomeDirectAction(e.Args, out var actionId, out var startDebugMode))
        {
            var home = HomeWindow.CreateDirectDiagnosticWindow(actionId, startDebugMode);
            MainWindow = home;
            home.Show();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static bool IsHomeLayoutEditorArg(string arg)
    {
        return string.Equals(arg, "--home-layout-editor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "/home-layout-editor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHomeLayoutEditorEnvironmentSet()
    {
        var value = Environment.GetEnvironmentVariable("DOCKPET_HOME_LAYOUT_EDITOR");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHomeLayoutEditorProcess()
    {
        var processName = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "");
        return processName.Contains("HomeLayoutEditor", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("小屋编辑器", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("布局编辑器", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHomeDirectAction(string[] args, out string actionId, out bool startDebugMode)
    {
        actionId = "";
        startDebugMode = false;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--home-direct", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "/home-direct", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(arg, "--home-direct-debug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "/home-direct-debug", StringComparison.OrdinalIgnoreCase))
            {
                startDebugMode = true;
                return true;
            }

            const string prefix = "--home-direct=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                actionId = arg[prefix.Length..];
                return true;
            }

            const string debugPrefix = "--home-direct-debug=";
            if (arg.StartsWith(debugPrefix, StringComparison.OrdinalIgnoreCase))
            {
                actionId = arg[debugPrefix.Length..];
                startDebugMode = true;
                return true;
            }
        }

        return false;
    }
}
