namespace TuNAS.Converter.Models;

/// <summary>Technische Daten einer Datei, wie ffprobe sie liefert.</summary>
public sealed class MediaInfo
{
    public TimeSpan Duration { get; init; }
    public long SizeBytes { get; init; }

    /// <summary>Datenrate der ganzen Datei in Bit pro Sekunde.</summary>
    public long BitRate { get; init; }

    public string FormatName { get; init; } = "";

    public string? VideoCodec { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Rotation { get; init; }
    public double Fps { get; init; }
    public long VideoBitRate { get; init; }

    public string? AudioCodec { get; init; }
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public long AudioBitRate { get; init; }

    public bool HasVideo => VideoCodec != null;
    public bool HasAudio => AudioCodec != null;

    /// <summary>Breite so, wie das Bild angezeigt wird (Hochkant-Videos sind gedreht gespeichert).</summary>
    public int DisplayWidth => Math.Abs(Rotation) % 180 == 90 ? Height : Width;
    public int DisplayHeight => Math.Abs(Rotation) % 180 == 90 ? Width : Height;

    public long EffectiveVideoBitRate
    {
        get
        {
            if (VideoBitRate > 0) return VideoBitRate;
            var audio = HasAudio ? (AudioBitRate > 0 ? AudioBitRate : 128_000) : 0;
            return Math.Max(0, BitRate - audio);
        }
    }
}
