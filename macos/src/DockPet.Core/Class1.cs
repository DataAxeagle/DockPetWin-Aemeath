namespace DockPet.Core;

public static class AemeathDefaults
{
    public const string AppName = "DockPetWin-Aemeath";
    public const string PetName = "爱弥斯";
    public const string PetIdentifier = "Aemeath";
    public const string DefaultUserSalutation = "漂泊者";
    public const string DefaultAssetPackId = "my-pink-character";

    public static AppSettings CreateDefaultSettings() => new()
    {
        PetName = PetName,
        PetIdentifier = PetIdentifier,
        UserSalutation = DefaultUserSalutation,
        SelectedAssetPackId = DefaultAssetPackId
    };
}
