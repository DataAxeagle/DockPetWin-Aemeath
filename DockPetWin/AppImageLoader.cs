using System.IO;
using System.Windows.Media.Imaging;

namespace DockPetWin;

internal static class AppImageLoader
{
    public const string AppIconPath = "Resources/App/pet-app-icon.png";

    public static BitmapImage? TryLoad(string relativePath)
    {
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(AppContext.BaseDirectory, normalizedPath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(fullPath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
