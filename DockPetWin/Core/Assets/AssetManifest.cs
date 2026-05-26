using System.Text.Json.Serialization;

namespace DockPetWin.Core.Assets;

public sealed class AssetManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "default-lizz";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Lizz";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "Auwuua";

    [JsonPropertyName("canvas_width")]
    public int CanvasWidth { get; set; } = 1254;

    [JsonPropertyName("canvas_height")]
    public int CanvasHeight { get; set; } = 1254;

    [JsonPropertyName("default_anchor")]
    public Anchor DefaultAnchor { get; set; } = new();

    [JsonPropertyName("poses")]
    public StaticPoses Poses { get; set; } = new();

    [JsonPropertyName("display_sizes")]
    public DisplaySizes DisplaySizes { get; set; } = new();

    [JsonPropertyName("animations")]
    public Animations Animations { get; set; } = new();
}

public sealed class Anchor
{
    [JsonPropertyName("x")]
    public double X { get; set; } = 0.5;

    [JsonPropertyName("y")]
    public double Y { get; set; } = 0.88;
}

public sealed class StaticPoses
{
    [JsonPropertyName("resting")]
    public string Resting { get; set; } = "poses/resting";

    [JsonPropertyName("held")]
    public string Held { get; set; } = "poses/held";

    [JsonPropertyName("dialogue")]
    public string Dialogue { get; set; } = "poses/dialogue";

    [JsonPropertyName("transition")]
    public string Transition { get; set; } = "poses/transition";
}

public sealed class DisplaySizes
{
    [JsonPropertyName("held")]
    public DisplaySize? Held { get; set; }
}

public sealed class DisplaySize
{
    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public sealed class Animations
{
    [JsonPropertyName("walk")]
    public Animation Walk { get; set; } = new();
}

public sealed class Animation
{
    [JsonPropertyName("fps")]
    public double Fps { get; set; } = 3;

    [JsonPropertyName("video")]
    public string? Video { get; set; }

    [JsonPropertyName("video_frame_count")]
    public int VideoFrameCount { get; set; } = 4;

    [JsonPropertyName("frames")]
    public string[] Frames { get; set; } = [];
}
