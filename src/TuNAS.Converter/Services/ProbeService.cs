using System.Globalization;
using System.IO;
using System.Text.Json;
using TuNAS.Converter.Models;

namespace TuNAS.Converter.Services;

/// <summary>Liest die technischen Daten einer Datei mit ffprobe.</summary>
public static class ProbeService
{
    public static async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(
            FfmpegTools.FfprobePath,
            new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path },
            ct);

        if (result.ExitCode != 0)
            throw new InvalidDataException(FirstLine(result.StdErr) ?? "Unbekanntes Dateiformat.");

        using var doc = JsonDocument.Parse(result.StdOut);
        var root = doc.RootElement;
        var format = root.TryGetProperty("format", out var f) ? f : default;

        JsonElement? video = null, audio = null;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = Str(s, "codec_type");
                if (type == "video" && video == null && !IsCoverArt(s)) video = s;
                else if (type == "audio" && audio == null) audio = s;
            }
        }

        if (video == null && audio == null)
            throw new InvalidDataException("Die Datei enthält weder Bild noch Ton.");

        var duration = Seconds(format, "duration");
        if (duration <= 0 && video != null) duration = Seconds(video.Value, "duration");
        if (duration <= 0 && audio != null) duration = Seconds(audio.Value, "duration");

        var size = Long(format, "size");
        if (size <= 0) size = new FileInfo(path).Length;

        var bitRate = Long(format, "bit_rate");
        if (bitRate <= 0 && duration > 0) bitRate = (long)(size * 8 / duration);

        return new MediaInfo
        {
            Duration = TimeSpan.FromSeconds(Math.Max(0, duration)),
            SizeBytes = size,
            BitRate = bitRate,
            FormatName = Str(format, "format_name") ?? "",
            VideoCodec = video == null ? null : Str(video.Value, "codec_name"),
            Width = video == null ? 0 : Int(video.Value, "width"),
            Height = video == null ? 0 : Int(video.Value, "height"),
            Rotation = video == null ? 0 : Rotation(video.Value),
            Fps = video == null ? 0 : FrameRate(video.Value),
            VideoBitRate = video == null ? 0 : Long(video.Value, "bit_rate"),
            AudioCodec = audio == null ? null : Str(audio.Value, "codec_name"),
            SampleRate = audio == null ? 0 : Int(audio.Value, "sample_rate"),
            Channels = audio == null ? 0 : Int(audio.Value, "channels"),
            AudioBitRate = audio == null ? 0 : Long(audio.Value, "bit_rate"),
        };
    }

    /// <summary>Cover-Bilder in MP3/M4A sind technisch ein "Video", aber kein echtes.</summary>
    private static bool IsCoverArt(JsonElement s) =>
        s.TryGetProperty("disposition", out var d)
        && d.TryGetProperty("attached_pic", out var a)
        && a.ValueKind == JsonValueKind.Number && a.GetInt32() == 1;

    private static double FrameRate(JsonElement s)
    {
        foreach (var key in new[] { "avg_frame_rate", "r_frame_rate" })
        {
            var text = Str(s, key);
            if (text == null) continue;
            var parts = text.Split('/');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den)
                && num > 0 && den > 0)
            {
                var fps = num / den;
                if (fps is > 1 and < 300) return fps;
            }
        }
        return 0;
    }

    private static int Rotation(JsonElement s)
    {
        if (s.TryGetProperty("side_data_list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number)
                    return r.GetInt32();
            }
        }
        if (s.TryGetProperty("tags", out var tags) && int.TryParse(Str(tags, "rotate"), out var rotate))
            return rotate;
        return 0;
    }

    private static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static double Seconds(JsonElement e, string name) =>
        double.TryParse(Str(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static long Long(JsonElement e, string name) =>
        long.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;

    private static int Int(JsonElement e, string name) =>
        int.TryParse(Str(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
