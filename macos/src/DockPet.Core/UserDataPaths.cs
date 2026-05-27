namespace DockPet.Core;

public sealed record UserDataPaths(string AppRoot, string UserDataRoot, string SettingsPath, string AssetPacksRoot, string AgentsRoot);

public static class UserDataPathProvider
{
    public static UserDataPaths ForCurrentPlatform()
    {
        var appRoot = ResolveAppRoot();
        var userDataRoot = Path.Combine(appRoot, "UserData");
        return new UserDataPaths(
            AppRoot: appRoot,
            UserDataRoot: userDataRoot,
            SettingsPath: Path.Combine(userDataRoot, "settings.json"),
            AssetPacksRoot: Path.Combine(userDataRoot, "AssetPacks"),
            AgentsRoot: Path.Combine(userDataRoot, "Agents"));
    }

    public static string ResolveAppRoot()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AemeathDefaults.AppName);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, AemeathDefaults.AppName);
    }
}
