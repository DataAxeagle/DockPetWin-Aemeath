namespace DockPetWin;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        HomeWindow.EnsurePlacementConfigFile();
        HomeWindow.EnsureFurnitureConfigFile();
        base.OnStartup(e);
    }
}
