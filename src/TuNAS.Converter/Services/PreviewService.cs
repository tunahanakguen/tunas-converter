using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TuNAS.Converter.Models;

namespace TuNAS.Converter.Services;

/// <summary>
/// Manche Formate (vor allem MPEG-TS wie .ts/.m2ts und MPEG-PS wie .mpg/.vob) haben kein Inhaltsverzeichnis.
/// Der Player muss beim Springen raten, ist langsam und zeigt danach graue Klötzchen, bis das nächste Schlüsselbild kommt.
/// Für das Schneidefenster wird darum einmalig eine Vorschau-Kopie mit Inhaltsverzeichnis (MKV) erzeugt:
/// Bild nur kopiert, Ton schnell als AC3 neu kodiert (verträgt auch beschädigten Ton). Die Kopie dient nur zum Ansehen,
/// umgewandelt wird immer die Originaldatei.
/// </summary>
public static class PreviewService
{
    private static readonly HashSet<string> WithoutIndex = new(StringComparer.OrdinalIgnoreCase)
    {
        "mpegts", "mpeg", "mpegvideo", "h264", "hevc",
    };

    public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "TuNAS Converter", "Vorschau");

    public static bool NeedsPreview(MediaInfo info) =>
        info.HasVideo && info.FormatName.Split(',').Any(WithoutIndex.Contains);

    /// <summary>Ffmpeg-Befehl für die Vorschau-Kopie.</summary>
    public static List<string> Args(string source, string output) => new()
    {
        "-y", "-hide_banner", "-nostdin", "-loglevel", "error", "-progress", "pipe:1", "-nostats",
        "-fflags", "+genpts+discardcorrupt", "-err_detect", "ignore_err",
        "-i", source,
        "-map", "0:v:0", "-map", "0:a:0?", "-c:v", "copy", "-c:a", "ac3", "-b:a", "192k", "-sn", "-dn",
        "-f", "matroska", output,
    };

    /// <summary>Liefert den Pfad der Vorschau-Kopie (beim zweiten Öffnen sofort) – oder null, wenn sie nicht erstellt werden konnte.</summary>
    public static async Task<string?> GetAsync(string source, TimeSpan duration, IProgress<double> progress, CancellationToken ct)
    {
        var file = new FileInfo(source);
        var target = Path.Combine(Folder, Key(file) + ".mkv");
        if (File.Exists(target)) return target;

        Directory.CreateDirectory(Folder);
        var temp = target + ".teil";
        var seconds = Math.Max(1, duration.TotalSeconds);
        void OnLine(string line)
        {
            if (!line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
            if (long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0)
                progress.Report(Math.Min(1, us / 1_000_000.0 / seconds));
        }

        try
        {
            var run = await ProcessRunner.RunAsync(FfmpegTools.FfmpegPath, Args(source, temp), ct, OnLine);
            if (run.ExitCode != 0 || !File.Exists(temp))
            {
                Logger.Write($"Vorschau-Kopie für „{file.Name}“ fehlgeschlagen (Code {run.ExitCode}):\n{run.StdErr}");
                TryDelete(temp);
                return null;
            }
            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>Vorschau-Kopien sind so groß wie das Original: beim Beenden aufräumen.</summary>
    public static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var f in Directory.EnumerateFiles(Folder)) TryDelete(f); // noch geöffnete Dateien bleiben einfach liegen
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Gleiche Datei (Pfad, Größe, Änderungszeit) → gleiche Vorschau-Kopie.</summary>
    private static string Key(FileInfo file)
    {
        var text = $"{file.FullName.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
