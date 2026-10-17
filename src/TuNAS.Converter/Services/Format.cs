using System.Globalization;

namespace TuNAS.Converter.Services;

/// <summary>Zahlen und Zeiten so anzeigen, wie man sie liest.</summary>
public static class Format
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Time(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    public static string TimePrecise(TimeSpan t) =>
        (t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.f") : t.ToString(@"mm\:ss\.f"));

    public static string Size(long bytes)
    {
        if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0", De) + " GB";
        if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0", De) + " MB";
        return Math.Max(1, bytes / 1024).ToString(De) + " KB";
    }

    /// <summary>Sekunden als Text für ffmpeg (immer mit Punkt).</summary>
    public static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
