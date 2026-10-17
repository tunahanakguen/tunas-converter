using TuNAS.Converter.Models;

namespace TuNAS.Converter.Services;

/// <summary>
/// Übersetzt „Hoch / Mittel / Klein“ in ffmpeg-Einstellungen.
/// Video nutzt eine feste Bildqualität (CRF bzw. q), keine feste Datenrate:
/// ruhige Szenen werden automatisch kleiner.
/// "Original" heißt hier: verlustfrei war nicht möglich, also höchste Stufe.
/// </summary>
public static class EncoderArgs
{
    /// <summary>Untergrenze für die Obergrenze: sehr kleine Quellen nicht unbrauchbar machen.</summary>
    private const long MinCapBitRate = 100_000;
    private const long MinAudioCapBitRate = 48_000;

    /// <summary>
    /// Video-Einstellungen. <paramref name="sourceVideoBitRate"/> ist die Datenrate des Originalbilds:
    /// Sie dient als Obergrenze, damit das Ergebnis nie größer wird als das Original
    /// (wichtig, wenn ein sparsames Format wie VP9 oder HEVC in H.264 umgewandelt wird).
    /// </summary>
    public static List<string> Video(OutputFormat format, QualityLevel q, long sourceVideoBitRate = 0)
    {
        var cap = sourceVideoBitRate > 0 ? Math.Max(MinCapBitRate, sourceVideoBitRate) : 0;
        var args = format.Video switch
        {
            VideoEncoder.X264 => new List<string>
            {
                "-c:v", "libx264", "-preset", "medium",
                "-crf", Pick(q, "18", "20", "23", "28"), "-pix_fmt", "yuv420p",
            },
            VideoEncoder.Vp9 => new List<string>
            {
                // -b:v 0 = reine Qualitätssteuerung; mit Obergrenze wird daraus „Qualität, aber höchstens so groß“.
                "-c:v", "libvpx-vp9", "-crf", Pick(q, "28", "31", "36", "42"), "-b:v", cap > 0 ? Rate(cap) : "0",
                "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p",
            },
            VideoEncoder.Mpeg4Xvid => new List<string> { "-c:v", "mpeg4", "-vtag", "xvid", "-q:v", Pick(q, "2", "3", "5", "8") },
            VideoEncoder.Wmv2 => new List<string> { "-c:v", "wmv2", "-q:v", Pick(q, "2", "3", "5", "8") },
            VideoEncoder.Mpeg2 => new List<string> { "-c:v", "mpeg2video", "-q:v", Pick(q, "2", "3", "5", "8") },
            VideoEncoder.Flv1 => new List<string> { "-c:v", "flv", "-q:v", Pick(q, "2", "3", "5", "8") },
            _ => new List<string>(),
        };
        if (cap > 0 && format.Video == VideoEncoder.X264)
            args.AddRange(new[] { "-maxrate", Rate(cap), "-bufsize", Rate(cap * 2) });
        if (format.ExtraVideoArgs != null) args.AddRange(format.ExtraVideoArgs);
        return args;
    }

    private static string Rate(long bitsPerSecond) => (bitsPerSecond / 1000) + "k";

    /// <summary>
    /// Ton-Einstellungen. Auch hier gilt: nie mehr Datenrate als im Original (<paramref name="sourceAudioBitRate"/>),
    /// sonst wird die Datei größer, ohne besser zu klingen. Ausgenommen: verlustfreie Formate und Codecs mit festen Stufen.
    /// </summary>
    public static List<string> Audio(OutputFormat format, QualityLevel q, long sourceAudioBitRate = 0)
    {
        var capKbps = CappedAudioKbps(format, q, sourceAudioBitRate);
        if (capKbps != null)
        {
            var encoder = format.Audio switch
            {
                AudioEncoder.Aac => "aac",
                AudioEncoder.Mp3 => "libmp3lame",
                AudioEncoder.Opus => "libopus",
                AudioEncoder.Vorbis => "libvorbis",
                _ => "wmav2",
            };
            return new List<string> { "-c:a", encoder, "-b:a", $"{capKbps.Value:0}k" };
        }
        return UncappedAudio(format, q);
    }

    /// <summary>Datenrate in kbit/s, wenn die Obergrenze greift – sonst null.</summary>
    private static double? CappedAudioKbps(OutputFormat format, QualityLevel q, long sourceAudioBitRate)
    {
        if (sourceAudioBitRate <= 0) return null;
        if (format.Audio is not (AudioEncoder.Aac or AudioEncoder.Mp3 or AudioEncoder.Opus or AudioEncoder.Vorbis or AudioEncoder.Wma))
            return null;
        var nominal = NominalAudioKbps(format, q, 48000, 2);
        var source = Math.Max(MinAudioCapBitRate, sourceAudioBitRate) / 1000.0;
        return nominal > source * 1.1 ? Math.Round(source) : null;
    }

    private static List<string> UncappedAudio(OutputFormat format, QualityLevel q) => format.Audio switch
    {
        AudioEncoder.Aac => new() { "-c:a", "aac", "-b:a", Pick(q, "256k", "192k", "160k", "96k") },
        AudioEncoder.Mp3 => new() { "-c:a", "libmp3lame", "-q:a", Pick(q, "0", "0", "2", "5") },
        AudioEncoder.Opus => new() { "-c:a", "libopus", "-b:a", Pick(q, "192k", "160k", "128k", "64k") },
        AudioEncoder.Vorbis => new() { "-c:a", "libvorbis", "-q:a", Pick(q, "8", "6", "4", "2") },
        AudioEncoder.Wma => new() { "-c:a", "wmav2", "-b:a", Pick(q, "256k", "192k", "160k", "96k") },
        AudioEncoder.Mp2 => new() { "-c:a", "mp2", "-b:a", Pick(q, "320k", "256k", "192k", "128k") },
        AudioEncoder.Ac3 => new() { "-c:a", "ac3", "-b:a", Pick(q, "640k", "448k", "384k", "192k") },
        AudioEncoder.Flac => new() { "-c:a", "flac" },
        AudioEncoder.Pcm => new() { "-c:a", "pcm_s16le" },
        AudioEncoder.AmrNb => new() { "-c:a", "libopencore_amrnb", "-ar", "8000", "-ac", "1", "-b:a", "12.2k" },
        _ => new(),
    };

    /// <summary>Ungefähre Datenrate des Tons in kbit/s, für die Größenschätzung – mit derselben Obergrenze wie beim Umwandeln.</summary>
    public static double EstimatedAudioKbps(OutputFormat format, QualityLevel q, int sampleRate, int channels, long sourceAudioBitRate) =>
        CappedAudioKbps(format, q, sourceAudioBitRate) ?? NominalAudioKbps(format, q, sampleRate, channels);

    /// <summary>Ungefähre Datenrate des Tons in kbit/s ohne Obergrenze.</summary>
    public static double NominalAudioKbps(OutputFormat format, QualityLevel q, int sampleRate, int channels)
    {
        var pcm = Math.Max(8000, sampleRate) * Math.Max(1, channels) * 16 / 1000.0;
        return format.Audio switch
        {
            AudioEncoder.Aac => KbpsOf(Pick(q, "256k", "192k", "160k", "96k")),
            AudioEncoder.Mp3 => KbpsOf(Pick(q, "245k", "245k", "190k", "130k")),
            AudioEncoder.Opus => KbpsOf(Pick(q, "192k", "160k", "128k", "64k")),
            AudioEncoder.Vorbis => KbpsOf(Pick(q, "256k", "192k", "128k", "96k")),
            AudioEncoder.Wma => KbpsOf(Pick(q, "256k", "192k", "160k", "96k")),
            AudioEncoder.Mp2 => KbpsOf(Pick(q, "320k", "256k", "192k", "128k")),
            AudioEncoder.Ac3 => KbpsOf(Pick(q, "640k", "448k", "384k", "192k")),
            AudioEncoder.Flac => pcm * 0.6,
            AudioEncoder.Pcm => pcm,
            AudioEncoder.AmrNb => 12.2,
            _ => 0,
        };
    }

    private static double KbpsOf(string value) => double.Parse(value.TrimEnd('k'), System.Globalization.CultureInfo.InvariantCulture);

    private static string Pick(QualityLevel q, string original, string hoch, string mittel, string klein) => q switch
    {
        QualityLevel.Original => original,
        QualityLevel.Hoch => hoch,
        QualityLevel.Mittel => mittel,
        _ => klein,
    };
}
