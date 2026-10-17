using System.IO;
using TuNAS.Converter.Models;

namespace TuNAS.Converter.Services;

public sealed record SizeEstimate(
    long Bytes,
    bool Lossless,
    long SourceBytes,
    bool ResolutionReduced,
    string? NoCopyReason);

/// <summary>
/// Schätzt die Endgröße. Bei verlustfreiem Umpacken ist das fast exakt (Anteil der behaltenen Teile).
/// Beim Neu-Kodieren werden ein paar kurze Stichproben wirklich umgewandelt und hochgerechnet,
/// statt nur grob zu raten – so bleibt die Schätzung auch beim Zusammenfügen vieler Clips realistisch.
/// </summary>
public static class SizeEstimator
{
    private static readonly TimeSpan SampleLength = TimeSpan.FromSeconds(3);

    public static async Task<SizeEstimate?> EstimateAsync(IReadOnlyList<ClipItem> clips, OutputFormat format, QualityLevel quality,
        bool join, CancellationToken ct)
    {
        var ready = clips.Where(c => c.IsReady && c.Info != null && c.KeptDuration > TimeSpan.Zero).ToList();
        if (ready.Count == 0) return null;
        join = join && ready.Count > 1;

        var usable = ready.Where(c => format.AudioOnly ? c.Info!.HasAudio : c.Info!.HasVideo).ToList();
        if (usable.Count == 0) return null;

        var sourceBytes = (long)usable.Sum(c => c.Info!.SizeBytes * Fraction(c));
        var totalSeconds = usable.Sum(c => c.KeptDuration.TotalSeconds);
        var noCopy = CommandBuilder.WhyNoCopy(usable, format, join);

        if (quality == QualityLevel.Original && noCopy == null)
        {
            var bytes = format.AudioOnly
                ? (long)usable.Sum(c => AudioBitRate(c.Info!) * c.KeptDuration.TotalSeconds / 8)
                : sourceBytes;
            return new SizeEstimate(bytes, Lossless: true, sourceBytes, ResolutionReduced: false, null);
        }

        var first = usable[0].Info!;
        var sampleRate = join ? 48000 : (first.SampleRate > 0 ? first.SampleRate : 44100);
        var channels = join ? 2 : (first.Channels > 0 ? first.Channels : 2);
        // Ton: pro Clip mit derselben Obergrenze wie beim Umwandeln, gewichtet nach Länge.
        var hasAudio = format.AudioOnly || usable.Any(c => c.Info!.HasAudio);
        var audioKbps = hasAudio
            ? usable.Sum(c => EncoderArgs.EstimatedAudioKbps(format, quality, sampleRate, channels, c.Info!.AudioBitRate)
                              * c.KeptDuration.TotalSeconds) / totalSeconds
            : 0;

        double videoKbps = 0;
        var reduced = false;
        if (!format.AudioOnly)
        {
            var target = join ? CommandBuilder.JoinTarget(usable, format) : null;
            reduced = usable.Any(c => target != null
                ? c.Info!.DisplayHeight > target.Height || c.Info!.DisplayWidth > target.Width
                : format.MaxHeight > 0 && c.Info!.DisplayHeight > format.MaxHeight);
            videoKbps = await SampleVideoKbpsAsync(usable, format, quality, target, ct);
        }

        var estimate = (long)((videoKbps + audioKbps) * 1000 / 8 * totalSeconds * 1.02);
        return new SizeEstimate(estimate, Lossless: false, sourceBytes, reduced, noCopy);
    }

    private static async Task<double> SampleVideoKbpsAsync(List<ClipItem> clips, OutputFormat format, QualityLevel quality,
        VideoTarget? target, CancellationToken ct)
    {
        // Alle behaltenen Stücke hintereinander gedacht; Stichproben gleichmäßig darüber verteilt.
        var timeline = clips.SelectMany(c => c.KeptSegments.Select(s => (Clip: c, Segment: s))).ToList();
        var total = timeline.Sum(t => t.Segment.Duration.TotalSeconds);
        var count = total < 12 ? 1 : 3;
        var length = Math.Min(SampleLength.TotalSeconds, total / count);

        var tempDir = Path.Combine(Path.GetTempPath(), "TuNAS Converter", "schaetzung-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            long bytes = 0;
            double seconds = 0;
            for (var i = 0; i < count; i++)
            {
                var position = Math.Max(0, total * (i + 0.5) / count - length / 2);
                var (clip, at, available) = Locate(timeline, position);
                var sampleSeconds = Math.Min(length, available);
                if (sampleSeconds < 0.2) continue;

                var file = Path.Combine(tempDir, $"probe{i}.mkv");
                var result = await ProcessRunner.RunAsync(
                    FfmpegTools.FfmpegPath,
                    CommandBuilder.SampleArgs(clip, at, TimeSpan.FromSeconds(sampleSeconds), format, quality, target, file),
                    ct,
                    lowPriority: true);
                if (result.ExitCode != 0 || !File.Exists(file))
                    throw new InvalidOperationException(result.StdErr);

                bytes += new FileInfo(file).Length;
                seconds += sampleSeconds;
            }
            return seconds > 0 ? bytes * 8 / seconds / 1000 : 0;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Findet zu einer Position auf der Gesamt-Zeitachse den Clip, die Zeit darin und wie viel bis zum Stückende bleibt.</summary>
    private static (ClipItem Clip, TimeSpan At, double Available) Locate(List<(ClipItem Clip, TimeRange Segment)> timeline, double position)
    {
        foreach (var (clip, segment) in timeline)
        {
            var len = segment.Duration.TotalSeconds;
            if (position < len)
                return (clip, segment.Start + TimeSpan.FromSeconds(position), len - position);
            position -= len;
        }
        var last = timeline[^1];
        return (last.Clip, last.Segment.Start, last.Segment.Duration.TotalSeconds);
    }

    private static double Fraction(ClipItem clip) =>
        clip.Info!.Duration.TotalSeconds > 0 ? clip.KeptDuration.TotalSeconds / clip.Info.Duration.TotalSeconds : 1;

    private static double AudioBitRate(MediaInfo info) =>
        info.AudioBitRate > 0 ? info.AudioBitRate : (info.HasVideo ? 128_000 : info.BitRate);
}
