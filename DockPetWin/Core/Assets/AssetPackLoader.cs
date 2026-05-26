using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DockPetWin.Core.Assets;

public sealed class AssetPackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatAssetPack LoadDefaultPack()
    {
        PrepareCustomPacksDirectory();
        var root = Path.Combine(AppContext.BaseDirectory, "Resources", "DefaultCat");
        return TryLoadPack(root, null);
    }

    public CatAssetPack LoadSelectedPack(string selectedID)
    {
        PrepareCustomPacksDirectory();
        var fallback = LoadDefaultPackWithoutPreparing();
        if (!string.IsNullOrWhiteSpace(selectedID) && selectedID != "default-lizz")
        {
            var customRoot = Path.Combine(CustomPacksRoot(), selectedID);
            if (Directory.Exists(customRoot))
            {
                return MergeWithFallback(TryLoadPack(customRoot, fallback), fallback);
            }
        }

        return fallback;
    }

    public IReadOnlyList<string> CustomPackIDs()
    {
        PrepareCustomPacksDirectory();
        var root = CustomPacksRoot();
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    public string CustomPacksRoot()
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "UserData",
            "AssetPacks");
    }

    public void PrepareCustomPacksDirectory()
    {
        var root = CustomPacksRoot();
        Directory.CreateDirectory(root);
        CopyDefaultPackIfNeeded(root);
        CopyBundledPackIfNeeded("HuihuiCat", "huihui-pet", root, "huihui-pet", "Huihui Pet");
        CreateTemplatePackIfNeeded(root);
    }

    public string ValidationSummary(string selectedID)
    {
        try
        {
            var pack = LoadSelectedPack(selectedID);
            var issues = new List<string>();
            if (pack.LoadError is not null)
            {
                issues.Add(pack.LoadError);
            }

            if (pack.WalkFrames.Count == 0) issues.Add("缺少散步动画帧");
            if (pack.RestingPoses.Count == 0) issues.Add("缺少休息姿态");
            if (pack.HeldPoses.Count == 0) issues.Add("缺少抱起姿态");
            if (pack.DialoguePoses.Count == 0) issues.Add("缺少对话姿态");
            if (pack.TransitionPoses.Count == 0) issues.Add("缺少过渡姿态");

            return issues.Count == 0
                ? $"资源包可用：{pack.Manifest.Name} ({pack.Manifest.Id})"
                : $"资源包可加载，缺失项会使用默认形象：{string.Join("、", issues)}";
        }
        catch (Exception ex)
        {
            return $"资源包不可用：{ex.Message}";
        }
    }

    private CatAssetPack LoadDefaultPackWithoutPreparing()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Resources", "DefaultCat");
        return TryLoadPack(root, null);
    }

    private static CatAssetPack TryLoadPack(string root, CatAssetPack? fallback)
    {
        string? loadError = null;
        AssetManifest manifest;
        var manifestPath = Path.Combine(root, "manifest.json");
        try
        {
            manifest = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<AssetManifest>(File.ReadAllText(manifestPath), JsonOptions) ?? new AssetManifest()
                : new AssetManifest();
        }
        catch (Exception ex)
        {
            manifest = new AssetManifest();
            loadError = $"manifest.json 无法解析：{ex.Message}";
        }

        IReadOnlyList<BitmapImage> SafeLoad(Func<IReadOnlyList<BitmapImage>> load)
        {
            try
            {
                return load();
            }
            catch
            {
                return [];
            }
        }

        var pack = new CatAssetPack(
            manifest,
            SafeLoad(() => LoadWalkFrames(root, manifest)),
            SafeLoad(() => LoadImages(Path.Combine(root, NormalizePath(manifest.Poses.Resting)))),
            SafeLoad(() => LoadImages(Path.Combine(root, NormalizePath(manifest.Poses.Transition)))),
            SafeLoad(() => LoadImages(Path.Combine(root, NormalizePath(manifest.Poses.Held)))),
            SafeLoad(() => LoadImages(Path.Combine(root, NormalizePath(manifest.Poses.Dialogue)))),
            root,
            loadError);

        return fallback is null ? pack : MergeWithFallback(pack, fallback);
    }

    private static CatAssetPack MergeWithFallback(CatAssetPack pack, CatAssetPack fallback)
    {
        return new CatAssetPack(
            pack.Manifest,
            pack.WalkFrames.Count > 0 ? pack.WalkFrames : fallback.WalkFrames,
            pack.RestingPoses.Count > 0 ? pack.RestingPoses : fallback.RestingPoses,
            pack.TransitionPoses.Count > 0 ? pack.TransitionPoses : fallback.TransitionPoses,
            pack.HeldPoses.Count > 0 ? pack.HeldPoses : fallback.HeldPoses,
            pack.DialoguePoses.Count > 0 ? pack.DialoguePoses : fallback.DialoguePoses,
            pack.RootPath,
            pack.LoadError);
    }

    private static void CopyDefaultPackIfNeeded(string customRoot)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Resources", "DefaultCat");
        var destination = Path.Combine(customRoot, "default-lizz");
        if (!Directory.Exists(source) || Directory.Exists(destination))
        {
            return;
        }

        CopyDirectory(source, destination);
    }

    private static void CopyBundledPackIfNeeded(
        string resourceFolderName,
        string destinationFolderName,
        string customRoot,
        string? manifestId = null,
        string? manifestName = null)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Resources", resourceFolderName);
        var destination = Path.Combine(customRoot, destinationFolderName);
        if (!Directory.Exists(source))
        {
            return;
        }

        if (!Directory.Exists(destination))
        {
            CopyDirectory(source, destination);
        }

        if (!string.IsNullOrWhiteSpace(manifestId) || !string.IsNullOrWhiteSpace(manifestName))
        {
            EnsureManifestIdentity(destination, manifestId, manifestName);
        }
    }

    private static void EnsureManifestIdentity(string root, string? manifestId, string? manifestName)
    {
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<AssetManifest>(File.ReadAllText(manifestPath), JsonOptions) ?? new AssetManifest();
            if (!string.IsNullOrWhiteSpace(manifestId))
            {
                manifest.Id = manifestId;
            }

            if (!string.IsNullOrWhiteSpace(manifestName))
            {
                manifest.Name = manifestName;
            }

            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
        }
        catch
        {
            // UserData manifest normalization is best-effort only.
        }
    }

    private static void CreateTemplatePackIfNeeded(string customRoot)
    {
        var root = Path.Combine(customRoot, "my-pet");
        if (Directory.Exists(root))
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(root, "poses", "resting"));
        Directory.CreateDirectory(Path.Combine(root, "poses", "held"));
        Directory.CreateDirectory(Path.Combine(root, "poses", "dialogue"));
        Directory.CreateDirectory(Path.Combine(root, "poses", "transition"));
        Directory.CreateDirectory(Path.Combine(root, "animations", "walk"));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
        {
          "id": "my-pet",
          "name": "My Pet",
          "author": "Your Name",
          "canvas_width": 512,
          "canvas_height": 512,
          "default_anchor": { "x": 0.5, "y": 0.88 },
          "poses": {
            "resting": "poses/resting",
            "held": "poses/held",
            "dialogue": "poses/dialogue",
            "transition": "poses/transition"
          },
          "display_sizes": {
            "held": { "width": 650, "height": 1236 }
          },
          "animations": {
            "walk": {
              "fps": 3,
              "video": "animations/walk/walk.mp4",
              "video_frame_count": 4,
              "frames": []
            }
          }
        }
        """);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: false);
        }
    }

    private static IReadOnlyList<BitmapImage> LoadWalkFrames(string root, AssetManifest manifest)
    {
        if (manifest.Animations.Walk.Frames.Length > 0)
        {
            return manifest.Animations.Walk.Frames
                .Select(frame => Path.Combine(root, NormalizePath(frame)))
                .Where(File.Exists)
                .Select(LoadImage)
                .ToList();
        }

        var directoryFrames = LoadImages(Path.Combine(root, "animations", "walk"));
        if (directoryFrames.Count > 0)
        {
            return directoryFrames;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Animations.Walk.Video))
        {
            var videoPath = Path.Combine(root, NormalizePath(manifest.Animations.Walk.Video));
            if (File.Exists(videoPath))
            {
                var extracted = ExtractWalkVideoFrames(root, manifest, videoPath);
                if (extracted.Count > 0)
                {
                    return extracted;
                }
            }
        }

        return [];
    }

    private static IReadOnlyList<BitmapImage> LoadImages(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.png")
            .OrderBy(Path.GetFileName)
            .Select(LoadImage)
            .ToList();
    }

    private static BitmapImage LoadImage(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(file, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static IReadOnlyList<BitmapImage> ExtractWalkVideoFrames(string root, AssetManifest manifest, string videoPath)
    {
        var frameCount = Math.Clamp(manifest.Animations.Walk.VideoFrameCount, 2, 24);
        var cacheDirectory = VideoCacheDirectory(root, manifest, videoPath, frameCount);
        Directory.CreateDirectory(cacheDirectory);

        var cachedFrames = Directory.EnumerateFiles(cacheDirectory, "walk_*.png")
            .OrderBy(Path.GetFileName)
            .ToList();
        if (cachedFrames.Count == frameCount)
        {
            return cachedFrames.Select(LoadImage).ToList();
        }

        foreach (var staleFrame in cachedFrames)
        {
            File.Delete(staleFrame);
        }

        using var tempDirectory = TemporaryDirectory.Create();
        var sourceFrames = ExtractVideoFramesWithFfmpeg(videoPath, tempDirectory.Path, frameCount);
        if (sourceFrames.Count == 0)
        {
            return [];
        }

        var outputFrames = new List<string>();
        for (var i = 0; i < sourceFrames.Count; i++)
        {
            var keyed = NormalizeWalkFrame(ApplyGreenScreenKey(sourceFrames[i]));
            var outputPath = Path.Combine(cacheDirectory, $"walk_{i + 1:00}.png");
            SavePng(keyed, outputPath);
            outputFrames.Add(outputPath);
        }

        return outputFrames.Select(LoadImage).ToList();
    }

    private static IReadOnlyList<string> ExtractVideoFramesWithFfmpeg(string videoPath, string outputDirectory, int frameCount)
    {
        var duration = ProbeVideoDurationSeconds(videoPath);
        if (duration is null or <= 0)
        {
            return [];
        }

        var frames = new List<string>();
        for (var i = 0; i < frameCount; i++)
        {
            var timestamp = FrameTimestamp(duration.Value, frameCount, i);
            var outputPath = Path.Combine(outputDirectory, $"source_{i + 1:00}.png");
            var exitCode = RunProcess(
                "ffmpeg",
                [
                    "-hide_banner",
                    "-loglevel",
                    "error",
                    "-y",
                    "-ss",
                    timestamp.ToString("0.###", CultureInfo.InvariantCulture),
                    "-i",
                    videoPath,
                    "-frames:v",
                    "1",
                    outputPath
                ]);

            if (exitCode == 0 && File.Exists(outputPath))
            {
                frames.Add(outputPath);
            }
        }

        return frames;
    }

    private static double? ProbeVideoDurationSeconds(string videoPath)
    {
        var output = RunProcessWithOutput(
            "ffprobe",
            [
                "-v",
                "error",
                "-show_entries",
                "format=duration",
                "-of",
                "default=noprint_wrappers=1:nokey=1",
                videoPath
            ]);

        return double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            ? duration
            : null;
    }

    private static double FrameTimestamp(double duration, int frameCount, int index)
    {
        if (frameCount == 1)
        {
            return duration / 2;
        }

        var start = duration * 0.12;
        var end = duration * 0.88;
        var step = (end - start) / (frameCount - 1);
        return Math.Clamp(start + (step * index), 0, Math.Max(0, duration - 0.001));
    }

    private static int RunProcess(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = Process.Start(ProcessStartInfo(fileName, arguments, redirectOutput: false));
            if (process is null)
            {
                return -1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private static string? RunProcessWithOutput(string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = Process.Start(ProcessStartInfo(fileName, arguments, redirectOutput: true));
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static ProcessStartInfo ProcessStartInfo(string fileName, IReadOnlyList<string> arguments, bool redirectOutput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static string VideoCacheDirectory(string root, AssetManifest manifest, string videoPath, int frameCount)
    {
        var info = new FileInfo(videoPath);
        var identity = $"v3-ffmpeg-normalized|{root}|{manifest.Id}|{videoPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{frameCount}|{manifest.CanvasWidth}|{manifest.CanvasHeight}";
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..16];
        return Path.Combine(
            AppContext.BaseDirectory,
            "UserData",
            "VideoCache",
            SanitizePathSegment(manifest.Id),
            hash);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "asset-pack" : sanitized;
    }

    private static BitmapSource ApplyGreenScreenKey(string sourcePath)
    {
        var bitmap = LoadWritableBitmap(sourcePath);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        for (var y = 0; y < bitmap.PixelHeight; y++)
        {
            for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                var offset = (y * stride) + (x * 4);
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                RgbToHsv(red, green, blue, out var hue, out var saturation, out var value);

                var greenDominance = green - Math.Max(red, blue);
                var greenHueDistance = Math.Abs(hue - 120);
                var isKeyCandidate = greenHueDistance <= 56
                    && saturation >= 0.22
                    && value >= 0.27
                    && greenDominance >= 18
                    && green >= 95;

                if (!isKeyCandidate)
                {
                    continue;
                }

                var dominanceAlpha = 1.0 - Clamp01((greenDominance - 18) / 52.0);
                var hueAlpha = Clamp01((greenHueDistance - 16) / 40.0);
                var alpha = (byte)Math.Round(255 * Math.Max(dominanceAlpha, hueAlpha));

                pixels[offset + 1] = Math.Min(green, Math.Max(red, blue));
                pixels[offset + 3] = alpha;
            }
        }

        var output = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        output.Freeze();
        return output;
    }

    private static BitmapSource NormalizeWalkFrame(BitmapSource keyedFrame)
    {
        var bounds = AlphaBounds(keyedFrame);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return keyedFrame;
        }

        var targetWidth = 1100;
        var targetHeight = 650;
        var horizontalPadding = 34;
        var topPadding = 18;
        var bottomPadding = 8;
        var availableWidth = targetWidth - (horizontalPadding * 2);
        var availableHeight = targetHeight - topPadding - bottomPadding;
        var scale = Math.Min(
            availableWidth / (double)bounds.Width,
            availableHeight / (double)bounds.Height);
        var drawWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));
        var offsetX = Math.Max(0, (targetWidth - drawWidth) / 2);
        var offsetY = Math.Max(topPadding, targetHeight - bottomPadding - drawHeight);

        var cropped = new CroppedBitmap(keyedFrame, new System.Windows.Int32Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        var resized = new TransformedBitmap(cropped, new ScaleTransform(drawWidth / (double)bounds.Width, drawHeight / (double)bounds.Height));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(resized, new System.Windows.Rect(offsetX, offsetY, drawWidth, drawHeight));
        }

        var normalized = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        normalized.Render(visual);
        normalized.Freeze();
        return normalized;
    }

    private static BitmapSource LoadWritableBitmap(string sourcePath)
    {
        var image = LoadImage(sourcePath);
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static void SavePng(BitmapSource bitmap, string outputPath)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static PixelBounds AlphaBounds(BitmapSource image)
    {
        var stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        var minX = image.PixelWidth;
        var minY = image.PixelHeight;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < image.PixelHeight; y++)
        {
            for (var x = 0; x < image.PixelWidth; x++)
            {
                var alpha = pixels[(y * stride) + (x * 4) + 3];
                if (alpha <= 8)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX || maxY < minY
            ? new PixelBounds(0, 0, 0, 0)
            : new PixelBounds(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void RgbToHsv(byte red, byte green, byte blue, out double hue, out double saturation, out double value)
    {
        var r = red / 255.0;
        var g = green / 255.0;
        var b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        hue = delta == 0
            ? 0
            : max == r
                ? 60 * (((g - b) / delta) % 6)
                : max == g
                    ? 60 * (((b - r) / delta) + 2)
                    : 60 * (((r - g) / delta) + 4);
        if (hue < 0)
        {
            hue += 360;
        }

        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }

    private static double Clamp01(double value)
    {
        return Math.Max(0, Math.Min(1, value));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar);
    }

    private readonly record struct PixelBounds(int Left, int Top, int Width, int Height);

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DockPetWin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Temporary files are best-effort cleanup only.
            }
        }
    }
}
