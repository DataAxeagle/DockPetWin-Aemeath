// See https://aka.ms/new-console-template for more information
var settings = DockPet.Core.AemeathDefaults.CreateDefaultSettings();
settings.Normalize();

var paths = DockPet.Core.UserDataPathProvider.ForCurrentPlatform();

Console.WriteLine($"{DockPet.Core.AemeathDefaults.AppName} macOS prototype");
Console.WriteLine($"Pet: {settings.PetName} ({settings.PetIdentifier})");
Console.WriteLine($"Default salutation: {settings.UserSalutation}");
Console.WriteLine($"UserData root: {paths.UserDataRoot}");
Console.WriteLine();
Console.WriteLine("First-run API prompt:");
Console.WriteLine(DockPet.Core.FirstRunGuide.ApiMissingBubble(settings.UserSalutation));
Console.WriteLine();
Console.WriteLine("Capability map:");

foreach (var capability in DockPet.Core.PlatformCapabilityMap.RequiredCapabilities)
{
    var nativeCheck = capability.NeedsNativeCheck ? "needs macOS verification" : "portable";
    Console.WriteLine($"- {capability.Name}: {capability.MacImplementation} ({nativeCheck})");
}
