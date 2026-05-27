namespace DockPet.Core;

public sealed record PlatformCapability(string Name, string WindowsImplementation, string MacImplementation, bool NeedsNativeCheck);

public static class PlatformCapabilityMap
{
    public static IReadOnlyList<PlatformCapability> RequiredCapabilities { get; } =
    [
        new("Pet transparent window", "WPF transparent topmost Window", "Avalonia transparent borderless Window", true),
        new("Tray menu", "Windows Forms NotifyIcon", "macOS menu bar status item or Avalonia TrayIcon", true),
        new("Screen edge movement", "Windows taskbar working area", "macOS screen safe area around Dock and menu bar", true),
        new("Home window", "WPF HomeWindow", "Avalonia HomeWindow", false),
        new("Settings window", "WPF SettingsWindow", "Avalonia SettingsWindow", false),
        new("Chat window", "WPF AgentChatWindow", "Avalonia ChatWindow", false),
        new("User data", "Runtime UserData folder", "~/Library/Application Support/DockPetWin-Aemeath/UserData", false)
    ];
}
