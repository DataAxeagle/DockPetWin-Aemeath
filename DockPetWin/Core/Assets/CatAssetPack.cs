using System.IO;
using System.Windows.Media.Imaging;
using WpfSize = System.Windows.Size;

namespace DockPetWin.Core.Assets;

public sealed class CatAssetPack
{
    public CatAssetPack(
        AssetManifest manifest,
        IReadOnlyList<BitmapImage> walkFrames,
        IReadOnlyList<BitmapImage> restingPoses,
        IReadOnlyList<BitmapImage> transitionPoses,
        IReadOnlyList<BitmapImage> heldPoses,
        IReadOnlyList<BitmapImage> dialoguePoses,
        IReadOnlyList<BitmapImage> singingFrames,
        string rootPath,
        string? loadError = null)
    {
        Manifest = manifest;
        WalkFrames = walkFrames;
        RestingPoses = restingPoses;
        TransitionPoses = transitionPoses;
        HeldPoses = heldPoses;
        DialoguePoses = dialoguePoses;
        SingingFrames = singingFrames;
        RootPath = rootPath;
        LoadError = loadError;
    }

    public AssetManifest Manifest { get; }
    public IReadOnlyList<BitmapImage> WalkFrames { get; }
    public IReadOnlyList<BitmapImage> RestingPoses { get; }
    public IReadOnlyList<BitmapImage> RestingBasePoses => RestingPoses
        .Where(image => !IsRestingBlinkFrame(image))
        .ToList();
    public IReadOnlyList<BitmapImage> RestingBlinkFrames => RestingPoses
        .Where(IsRestingBlinkFrame)
        .OrderBy(image => image.UriSource?.LocalPath, StringComparer.OrdinalIgnoreCase)
        .ToList();
    public IReadOnlyList<BitmapImage> TransitionPoses { get; }
    public IReadOnlyList<BitmapImage> HeldPoses { get; }
    public IReadOnlyList<BitmapImage> DialoguePoses { get; }
    public IReadOnlyList<BitmapImage> SingingFrames { get; }
    public string RootPath { get; }
    public string? LoadError { get; }

    public double WalkFps => Math.Clamp(Manifest.Animations.Walk.Fps, 1, 24);
    public double SingFps => Math.Clamp(Manifest.Animations.Sing.Fps, 1, 24);
    public double SourceWidth => Manifest.CanvasWidth > 0 ? Manifest.CanvasWidth : 1254;
    public double SourceHeight => Manifest.CanvasHeight > 0 ? Manifest.CanvasHeight : 1254;
    public WpfSize DefaultSourceSize => new(SourceWidth, SourceHeight);
    public WpfSize HeldSourceSize => Manifest.DisplaySizes.Held is { Width: > 0, Height: > 0 } held
        ? new WpfSize(held.Width, held.Height)
        : DefaultSourceSize;

    private static bool IsRestingBlinkFrame(BitmapImage image)
    {
        var fileName = Path.GetFileNameWithoutExtension(image.UriSource?.LocalPath ?? string.Empty);
        return fileName.StartsWith("blink_", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("resting_blink_", StringComparison.OrdinalIgnoreCase);
    }
}
