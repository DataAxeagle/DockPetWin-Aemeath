namespace DockPet.Core;

public static class FirstRunGuide
{
    public static string ApiMissingBubble(string salutation = AemeathDefaults.DefaultUserSalutation)
    {
        salutation = string.IsNullOrWhiteSpace(salutation) ? AemeathDefaults.DefaultUserSalutation : salutation.Trim();
        return $"{salutation}，欢迎回到拉海洛。要让我真正听见你的声音，还需要先在设置里填入 DeepSeek API。";
    }

    public static string FirstChatGreeting(string salutation = AemeathDefaults.DefaultUserSalutation)
    {
        salutation = string.IsNullOrWhiteSpace(salutation) ? AemeathDefaults.DefaultUserSalutation : salutation.Trim();
        return $"{salutation}，任务结束了吗？拉海洛的风还和以前一样，我也一直在这里。";
    }
}
