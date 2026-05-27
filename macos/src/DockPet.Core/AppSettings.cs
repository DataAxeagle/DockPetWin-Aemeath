namespace DockPet.Core;

public sealed class AppSettings
{
    public string PetName { get; set; } = AemeathDefaults.PetName;
    public string PetIdentifier { get; set; } = AemeathDefaults.PetIdentifier;
    public string UserSalutation { get; set; } = AemeathDefaults.DefaultUserSalutation;
    public string SelectedAssetPackId { get; set; } = AemeathDefaults.DefaultAssetPackId;
    public bool RemindersEnabled { get; set; } = true;
    public double PetScalePercent { get; set; } = 20;
    public double StartPositionPercent { get; set; } = 75;
    public AgentApiSettings Api { get; set; } = new();

    public void Normalize()
    {
        PetName = string.IsNullOrWhiteSpace(PetName) ? AemeathDefaults.PetName : PetName.Trim();
        PetIdentifier = string.IsNullOrWhiteSpace(PetIdentifier) ? AemeathDefaults.PetIdentifier : PetIdentifier.Trim();
        UserSalutation = string.IsNullOrWhiteSpace(UserSalutation) ? AemeathDefaults.DefaultUserSalutation : UserSalutation.Trim();
        SelectedAssetPackId = string.IsNullOrWhiteSpace(SelectedAssetPackId) ? AemeathDefaults.DefaultAssetPackId : SelectedAssetPackId.Trim();
        PetScalePercent = Math.Clamp(PetScalePercent, 4, 100);
        StartPositionPercent = Math.Clamp(StartPositionPercent, 0, 100);
        Api.Normalize();
    }
}

public sealed class AgentApiSettings
{
    public string Provider { get; set; } = "deepseek";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-v4-flash";
    public string DeepSeekApiKey { get; set; } = "";
    public string TavilyApiKey { get; set; } = "";
    public bool EnableTools { get; set; } = true;

    public void Normalize()
    {
        Provider = string.IsNullOrWhiteSpace(Provider) ? "deepseek" : Provider.Trim().ToLowerInvariant();
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.deepseek.com" : BaseUrl.Trim();
        Model = string.IsNullOrWhiteSpace(Model) ? "deepseek-v4-flash" : Model.Trim();
        DeepSeekApiKey = DeepSeekApiKey.Trim();
        TavilyApiKey = TavilyApiKey.Trim();
    }

    public bool IsReadyForChat => !string.IsNullOrWhiteSpace(DeepSeekApiKey);
}
