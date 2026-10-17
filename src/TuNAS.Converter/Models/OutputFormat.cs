namespace TuNAS.Converter.Models;

public enum QualityLevel { Original, Hoch, Mittel, Klein }

public enum VideoEncoder { None, X264, Vp9, Mpeg4Xvid, Wmv2, Mpeg2, Flv1 }

public enum AudioEncoder { None, Aac, Mp3, Opus, Vorbis, Wma, Mp2, Ac3, Flac, Pcm, AmrNb }

/// <summary>Ein Zielformat, z. B. „MP4“: Container, Codecs und was verlustfrei übernommen werden kann.</summary>
public sealed record OutputFormat(
    string Name,
    string Extension,
    string Icon,
    string Description,
    VideoEncoder Video,
    AudioEncoder Audio,
    string[] CopyVideo,
    string[] CopyAudio,
    int MaxHeight = 0,
    string[]? ExtraVideoArgs = null)
{
    public bool AudioOnly => Video == VideoEncoder.None;

    /// <summary>Container mit Index am Dateianfang, damit Videos im Browser/auf dem Handy sofort starten.</summary>
    public bool FastStart => Extension is ".mp4" or ".mov" or ".m4a" or ".3gp";

    /// <summary>Darf ein vorhandener Codec unverändert übernommen werden? "*" = jeder.</summary>
    public bool AllowsCopy(string? codec, string[] allowed) =>
        codec != null && (allowed.Contains("*") || allowed.Contains(codec, StringComparer.OrdinalIgnoreCase));
}

public static class FormatCatalog
{
    private const string IconVideo = "";
    private const string IconMusic = "";
    private const string IconPhone = "";
    private const string IconGlobe = "";

    // MP4 kann auch VP9/AV1 und Opus aufnehmen (z. B. YouTube-Videos) – dann verlustfrei umpacken statt neu kodieren.
    private static readonly string[] Mp4Video = { "h264", "hevc", "mpeg4", "av1", "vp9" };
    private static readonly string[] Mp4Audio = { "aac", "mp3", "ac3", "eac3", "alac", "opus", "flac" };
    private static readonly string[] None = Array.Empty<string>();
    private static readonly string[] Any = { "*" };

    public static IReadOnlyList<OutputFormat> Main { get; } = new[]
    {
        new OutputFormat("MP4", ".mp4", IconVideo, "Läuft überall: PC, Handy, Fernseher, Web",
            VideoEncoder.X264, AudioEncoder.Aac, Mp4Video, Mp4Audio),
        new OutputFormat("MKV", ".mkv", IconVideo, "Flexibel, mehrere Ton- und Untertitelspuren",
            VideoEncoder.X264, AudioEncoder.Aac, Any, Any),
        new OutputFormat("AVI", ".avi", IconVideo, "Klassisches Format für ältere Geräte (XviD)",
            VideoEncoder.Mpeg4Xvid, AudioEncoder.Mp3, new[] { "mpeg4", "h264", "msmpeg4v3", "mjpeg" }, new[] { "mp3", "ac3", "mp2", "pcm_s16le" }),
        new OutputFormat("MOV", ".mov", IconVideo, "Apple QuickTime",
            VideoEncoder.X264, AudioEncoder.Aac, new[] { "h264", "hevc", "mpeg4", "prores", "mjpeg" }, new[] { "aac", "mp3", "ac3", "alac", "pcm_s16le" }),
        new OutputFormat("WMV", ".wmv", IconVideo, "Windows Media Video",
            VideoEncoder.Wmv2, AudioEncoder.Wma, new[] { "wmv1", "wmv2", "wmv3", "vc1" }, new[] { "wmav1", "wmav2" }),
        new OutputFormat("WebM", ".webm", IconGlobe, "Für Webseiten (VP9 + Opus)",
            VideoEncoder.Vp9, AudioEncoder.Opus, new[] { "vp8", "vp9", "av1" }, new[] { "opus", "vorbis" }),
        new OutputFormat("Handy", ".mp4", IconPhone, "MP4 bis 720p, klein und für jedes Handy",
            VideoEncoder.X264, AudioEncoder.Aac, None, None, MaxHeight: 720,
            ExtraVideoArgs: new[] { "-profile:v", "high", "-level", "4.0" }),
        new OutputFormat("MP3", ".mp3", IconMusic, "Nur den Ton speichern",
            VideoEncoder.None, AudioEncoder.Mp3, None, new[] { "mp3" }),
    };

    public static IReadOnlyList<OutputFormat> More { get; } = new[]
    {
        new OutputFormat("FLV", ".flv", IconVideo, "Flash Video (H.264)",
            VideoEncoder.X264, AudioEncoder.Aac, new[] { "h264", "flv1" }, new[] { "aac", "mp3" }),
        new OutputFormat("MPEG", ".mpg", IconVideo, "MPEG-2, z. B. für DVD-Player",
            VideoEncoder.Mpeg2, AudioEncoder.Mp2, new[] { "mpeg1video", "mpeg2video" }, new[] { "mp2", "ac3", "mp3" }),
        new OutputFormat("3GP", ".3gp", IconPhone, "Für sehr alte Handys (bis 480p)",
            VideoEncoder.X264, AudioEncoder.Aac, None, None, MaxHeight: 480,
            ExtraVideoArgs: new[] { "-profile:v", "baseline", "-level", "3.0" }),
        new OutputFormat("M4A (AAC)", ".m4a", IconMusic, "Ton in AAC, z. B. für iPhone",
            VideoEncoder.None, AudioEncoder.Aac, None, new[] { "aac", "alac" }),
        new OutputFormat("WAV", ".wav", IconMusic, "Unkomprimierter Ton (verlustfrei, groß)",
            VideoEncoder.None, AudioEncoder.Pcm, None, new[] { "pcm_s16le" }),
        new OutputFormat("FLAC", ".flac", IconMusic, "Verlustfrei komprimierter Ton",
            VideoEncoder.None, AudioEncoder.Flac, None, new[] { "flac" }),
        new OutputFormat("OGG", ".ogg", IconMusic, "Ogg Vorbis",
            VideoEncoder.None, AudioEncoder.Vorbis, None, new[] { "vorbis" }),
        new OutputFormat("Opus", ".opus", IconMusic, "Moderner Ton-Codec, sehr klein",
            VideoEncoder.None, AudioEncoder.Opus, None, new[] { "opus" }),
        new OutputFormat("WMA", ".wma", IconMusic, "Windows Media Audio",
            VideoEncoder.None, AudioEncoder.Wma, None, new[] { "wmav2" }),
        new OutputFormat("AC3", ".ac3", IconMusic, "Dolby Digital",
            VideoEncoder.None, AudioEncoder.Ac3, None, new[] { "ac3" }),
        new OutputFormat("AMR", ".amr", IconMusic, "Sprachaufnahmen, sehr klein",
            VideoEncoder.None, AudioEncoder.AmrNb, None, new[] { "amr_nb" }),
    };
}
