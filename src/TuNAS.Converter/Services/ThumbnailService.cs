using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TuNAS.Converter.Models;

namespace TuNAS.Converter.Services;

public static class ThumbnailService
{
    /// <summary>Ein kleines Vorschaubild, aufgenommen nach ca. 10 % der Laufzeit (höchstens nach 5 Sekunden).</summary>
    public static async Task<ImageSource?> CreateAsync(string path, MediaInfo info, CancellationToken ct)
    {
        if (!info.HasVideo) return null;

        var at = TimeSpan.FromSeconds(Math.Min(5, info.Duration.TotalSeconds * 0.1));
        var bytes = await ProcessRunner.RunForBytesAsync(
            FfmpegTools.FfmpegPath,
            new[]
            {
                "-hide_banner", "-loglevel", "error", "-nostdin",
                "-ss", Format.Seconds(at), "-i", path,
                "-frames:v", "1", "-vf", "scale=160:-2",
                "-f", "image2pipe", "-c:v", "png", "-",
            },
            ct);

        if (bytes.Length == 0) return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
