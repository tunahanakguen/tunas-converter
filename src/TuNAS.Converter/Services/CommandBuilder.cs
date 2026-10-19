using System.IO;
using System.Text;
using TuNAS.Converter.Models;

namespace TuNAS.Converter.Services;

/// <summary>Gemeinsames Bildformat beim Zusammenfügen unterschiedlicher Clips.</summary>
public sealed record VideoTarget(int Width, int Height, double Fps);

public sealed class ConversionStep
{
    public required string Title { get; init; }
    public required List<string> Args { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? OutputFile { get; init; }

    /// <summary>Das Ergebnis dieses Schritts ist eine fertige Datei für den Nutzer (kein Zwischenteil).</summary>
    public bool IsFinal { get; init; }
}

public sealed class ConversionPlan
{
    public List<ConversionStep> Steps { get; } = new();
    public List<string> Problems { get; } = new();
    public string TempDirectory { get; init; } = "";

    /// <summary>Beim Zusammenfügen hängt alles zusammen: ein Fehler bricht den ganzen Auftrag ab.</summary>
    public bool StopOnError { get; init; }
    public bool Lossless { get; set; }
}

/// <summary>Baut die ffmpeg-Befehle. Jeder Clip wird in einem eigenen ffmpeg-Prozess verarbeitet,
/// damit der Speicherbedarf auch bei vielen Clips klein bleibt.</summary>
public static class CommandBuilder
{
    private static readonly string[] Common =
        { "-y", "-hide_banner", "-nostdin", "-loglevel", "error", "-progress", "pipe:1", "-nostats" };

    /// <summary>Warum verlustfreies Übernehmen nicht geht – oder null, wenn es geht.</summary>
    public static string? WhyNoCopy(IReadOnlyList<ClipItem> clips, OutputFormat format, bool join)
    {
        if (clips.Count == 0) return "Keine Dateien.";
        foreach (var clip in clips)
        {
            var info = clip.Info!;
            if (format.AudioOnly)
            {
                if (!info.HasAudio) return $"„{clip.FileName}“ hat keinen Ton.";
                if (!format.AllowsCopy(info.AudioCodec, format.CopyAudio))
                    return $"Der Ton von „{clip.FileName}“ ({info.AudioCodec}) passt nicht direkt in {format.Name}.";
                continue;
            }
            if (!info.HasVideo) return $"„{clip.FileName}“ hat kein Bild.";
            if (!format.AllowsCopy(info.VideoCodec, format.CopyVideo))
                return $"Das Bild von „{clip.FileName}“ ({info.VideoCodec}) passt nicht direkt in {format.Name}.";
            if (info.HasAudio && !format.AllowsCopy(info.AudioCodec, format.CopyAudio))
                return $"Der Ton von „{clip.FileName}“ ({info.AudioCodec}) passt nicht direkt in {format.Name}.";
        }

        if (join && clips.Count > 1)
        {
            var a = clips[0].Info!;
            foreach (var clip in clips.Skip(1))
            {
                var b = clip.Info!;
                var sameVideo = format.AudioOnly
                    || (a.VideoCodec == b.VideoCodec && a.Width == b.Width && a.Height == b.Height && Math.Abs(a.Fps - b.Fps) < 0.01);
                var sameAudio = a.HasAudio == b.HasAudio
                    && (!a.HasAudio || (a.AudioCodec == b.AudioCodec && a.SampleRate == b.SampleRate && a.Channels == b.Channels));
                if (!sameVideo || !sameAudio)
                    return "Die Clips haben unterschiedliche Formate, Auflösungen oder Bildraten.";
            }
        }
        return null;
    }

    /// <summary>Zeichen pro Datei im vorgeschlagenen Namen beim Zusammenfügen.</summary>
    public const int JoinNamePartLength = 10;

    /// <summary>
    /// Namensvorschlag beim Zusammenfügen: die ersten 10 Zeichen jeder Datei in Listen-Reihenfolge, mit „_“ verbunden,
    /// insgesamt höchstens 35 Zeichen. Beispiel: „Urlaub_Strand“ + „Geburtstag“ + „Fjord“ → „Urlaub_Str_Geburtstag_Fjord“.
    /// </summary>
    public static string DefaultJoinName(IEnumerable<ClipItem> clips)
    {
        var parts = clips
            .Select(c => CleanFileName(Path.GetFileNameWithoutExtension(c.FileName)))
            .Where(n => n != null)
            .Select(n => n!.Length > JoinNamePartLength ? n[..JoinNamePartLength] : n)
            .Select(n => n.Trim(' ', '_', '-', '.'))
            .Where(n => n.Length > 0);
        var name = Shorten(string.Join("_", parts));
        return name.Length > 0 ? name : $"Zusammengefügt {DateTime.Now:yyyy-MM-dd HH-mm}";
    }

    /// <summary>Entfernt Zeichen, die Windows in Dateinamen nicht erlaubt; leer → null.</summary>
    public static string? CleanFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.', ' ');
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <param name="outputName">Vom Nutzer gewählter Dateiname ohne Endung. Gilt beim Zusammenfügen
    /// und wenn genau eine Datei umgewandelt wird; sonst behalten die Dateien ihre Originalnamen.</param>
    public static ConversionPlan Build(IReadOnlyList<ClipItem> clips, OutputFormat format, QualityLevel quality, bool join,
        string outputFolder, string? outputName = null)
    {
        var ready = clips.Where(c => c.IsReady && c.Info != null && c.KeptDuration > TimeSpan.Zero).ToList();
        join = join && ready.Count > 1;

        var tempDir = Path.Combine(Path.GetTempPath(), "TuNAS Converter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(outputFolder);

        var plan = new ConversionPlan { TempDirectory = tempDir, StopOnError = join };
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var name = CleanFileName(outputName);
        if (join)
            BuildJoined(plan, ready, format, quality, outputFolder, tempDir, reserved, name ?? DefaultJoinName(ready));
        else
            BuildSeparate(plan, ready, format, quality, outputFolder, tempDir, reserved, ready.Count == 1 ? name : null);

        return plan;
    }

    private static void BuildSeparate(ConversionPlan plan, List<ClipItem> clips, OutputFormat format, QualityLevel quality,
        string outputFolder, string tempDir, HashSet<string> reserved, string? singleName)
    {
        var allLossless = true;
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var problem = Unsuitable(clip, format);
            if (problem != null)
            {
                plan.Problems.Add(problem);
                continue;
            }

            var baseName = singleName ?? Path.GetFileNameWithoutExtension(clip.FileName);
            var output = UniqueOutput(outputFolder, baseName, format.Extension, reserved, clip.FilePath);
            var lossless = quality == QualityLevel.Original && WhyNoCopy(new[] { clip }, format, false) == null;
            allLossless &= lossless;
            plan.Steps.Add(lossless
                ? CopyStep(new[] { clip }, format, output, Path.Combine(tempDir, $"liste{i + 1}.txt"), clip.FileName, isFinal: true)
                : EncodeStep(clip, format, quality, null, forceAudio: false, output, clip.FileName, isFinal: true));
        }
        plan.Lossless = allLossless && plan.Steps.Count > 0;
    }

    private static void BuildJoined(ConversionPlan plan, List<ClipItem> clips, OutputFormat format, QualityLevel quality,
        string outputFolder, string tempDir, HashSet<string> reserved, string name)
    {
        foreach (var clip in clips)
        {
            var problem = Unsuitable(clip, format);
            if (problem != null) plan.Problems.Add(problem);
        }
        if (plan.Problems.Count > 0) return;

        var output = UniqueOutput(outputFolder, name, format.Extension, reserved, null);
        var total = clips.Aggregate(TimeSpan.Zero, (sum, c) => sum + c.KeptDuration);

        if (quality == QualityLevel.Original && WhyNoCopy(clips, format, join: true) == null)
        {
            plan.Lossless = true;
            plan.Steps.Add(CopyStep(clips, format, output, Path.Combine(tempDir, "liste.txt"), "Zusammenfügen (verlustfrei)", isFinal: true));
            return;
        }

        // Jeder Clip wird einzeln in dasselbe Format gebracht, danach werden die Teile verlustfrei aneinandergehängt.
        var target = format.AudioOnly ? null : JoinTarget(clips, format);
        var forceAudio = format.AudioOnly || clips.Any(c => c.Info!.HasAudio);
        var parts = new List<string>();
        for (var i = 0; i < clips.Count; i++)
        {
            var part = Path.Combine(tempDir, $"teil{i + 1:D3}{format.Extension}");
            parts.Add(part);
            plan.Steps.Add(EncodeStep(clips[i], format, quality, target, forceAudio, part,
                $"Clip {i + 1} von {clips.Count}: {clips[i].FileName}", isFinal: false, joinAudio: true));
        }

        var list = Path.Combine(tempDir, "teile.txt");
        WriteConcatList(list, parts.Select(p => (p, (TimeSpan?)null, (TimeSpan?)null)));
        var args = new List<string>(Common) { "-f", "concat", "-safe", "0", "-i", list, "-c", "copy" };
        if (format.FastStart) args.AddRange(new[] { "-movflags", "+faststart" });
        args.Add(output);
        plan.Steps.Add(new ConversionStep
        {
            Title = "Teile zusammenfügen",
            Args = args,
            Duration = TimeSpan.FromSeconds(Math.Max(1, total.TotalSeconds * 0.03)),
            OutputFile = output,
            IsFinal = true,
        });
    }

    private static string? Unsuitable(ClipItem clip, OutputFormat format)
    {
        var info = clip.Info!;
        if (format.AudioOnly && !info.HasAudio) return $"„{clip.FileName}“ hat keinen Ton und wurde übersprungen.";
        if (!format.AudioOnly && !info.HasVideo) return $"„{clip.FileName}“ hat kein Bild – für {format.Name} bitte ein Audioformat wählen.";
        return null;
    }

    /// <summary>Verlustfrei: Stücke werden nur ausgeschnitten und umgepackt („Stream Copy“).
    /// Geschnitten wird dabei am nächsten Schlüsselbild, also evtl. ein paar Zehntelsekunden ungenau.</summary>
    private static ConversionStep CopyStep(IReadOnlyList<ClipItem> clips, OutputFormat format, string output, string listFile, string title, bool isFinal)
    {
        var entries = new List<(string, TimeSpan?, TimeSpan?)>();
        foreach (var clip in clips)
        {
            var duration = clip.Info!.Duration;
            foreach (var seg in clip.KeptSegments)
            {
                TimeSpan? inpoint = seg.Start > TimeSpan.Zero ? seg.Start : null;
                TimeSpan? outpoint = seg.End < duration - TimeSpan.FromMilliseconds(20) ? seg.End : null;
                entries.Add((clip.FilePath, inpoint, outpoint));
            }
        }
        WriteConcatList(listFile, entries);

        var args = new List<string>(Common) { "-f", "concat", "-safe", "0", "-i", listFile };
        if (format.AudioOnly) args.AddRange(new[] { "-vn", "-map", "0:a:0" });
        else args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a:0?" });
        args.AddRange(new[] { "-c", "copy", "-avoid_negative_ts", "make_zero" });
        if (format.FastStart) args.AddRange(new[] { "-movflags", "+faststart" });
        args.Add(output);

        return new ConversionStep
        {
            Title = title,
            Args = args,
            Duration = clips.Aggregate(TimeSpan.Zero, (sum, c) => sum + c.KeptDuration),
            OutputFile = output,
            IsFinal = isFinal,
        };
    }

    /// <summary>Neu kodieren. Jedes behaltene Stück wird bildgenau per -ss/-t geöffnet und mit concat verbunden.</summary>
    private static ConversionStep EncodeStep(ClipItem clip, OutputFormat format, QualityLevel quality, VideoTarget? target,
        bool forceAudio, string output, string title, bool isFinal, bool joinAudio = false)
    {
        var info = clip.Info!;
        var segments = clip.KeptSegments;
        var wantVideo = !format.AudioOnly;
        var wantAudio = format.AudioOnly || info.HasAudio || forceAudio;

        var args = new List<string>(Common);
        foreach (var seg in segments)
            args.AddRange(new[] { "-ss", Format.Seconds(seg.Start), "-t", Format.Seconds(seg.Duration), "-i", clip.FilePath });

        var audioNorm = joinAudio ? ",aformat=sample_rates=48000:channel_layouts=stereo" : "";
        var graph = new StringBuilder();
        var concatInputs = new StringBuilder();
        for (var k = 0; k < segments.Count; k++)
        {
            if (wantVideo)
            {
                graph.Append($"[{k}:v:0]{VideoChain(format, target)}[v{k}];");
                concatInputs.Append($"[v{k}]");
            }
            if (wantAudio)
            {
                if (info.HasAudio)
                    graph.Append($"[{k}:a:0]aresample=async=1:first_pts=0{audioNorm}[a{k}];");
                else
                    graph.Append($"anullsrc=r=48000:cl=stereo,atrim=duration={Format.Seconds(segments[k].Duration)}{audioNorm}[a{k}];");
                concatInputs.Append($"[a{k}]");
            }
        }
        graph.Append(concatInputs)
            .Append($"concat=n={segments.Count}:v={(wantVideo ? 1 : 0)}:a={(wantAudio ? 1 : 0)}")
            .Append(wantVideo ? "[v]" : "")
            .Append(wantAudio ? "[a]" : "");

        args.AddRange(new[] { "-filter_complex", graph.ToString() });
        if (wantVideo) args.AddRange(new[] { "-map", "[v]" });
        if (wantAudio) args.AddRange(new[] { "-map", "[a]" });
        if (wantVideo) args.AddRange(EncoderArgs.Video(format, quality, info.EffectiveVideoBitRate));
        if (wantAudio) args.AddRange(EncoderArgs.Audio(format, quality, info.AudioBitRate));
        if (format.FastStart) args.AddRange(new[] { "-movflags", "+faststart" });
        args.Add(output);

        return new ConversionStep
        {
            Title = title,
            Args = args,
            Duration = clip.KeptDuration,
            OutputFile = output,
            IsFinal = isFinal,
        };
    }

    /// <summary>Bildfilter: Zeitstempel zurücksetzen, Größe anpassen (gerade Pixelzahl ist Pflicht für H.264/VP9).</summary>
    public static string VideoChain(OutputFormat format, VideoTarget? target)
    {
        string scale;
        if (target != null)
        {
            var fps = Format.Seconds(TimeSpan.FromSeconds(target.Fps));
            scale = $"scale={target.Width}:{target.Height}:force_original_aspect_ratio=decrease,"
                  + $"pad={target.Width}:{target.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps}";
        }
        else if (format.MaxHeight > 0)
        {
            scale = $"scale=-2:trunc(min({format.MaxHeight}\\,ih)/2)*2";
        }
        else
        {
            scale = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        }
        return "setpts=PTS-STARTPTS," + scale;
    }

    /// <summary>Beim Zusammenfügen: Auflösung des größten Clips, höchste Bildrate (maximal 60).</summary>
    public static VideoTarget? JoinTarget(IReadOnlyList<ClipItem> clips, OutputFormat format)
    {
        var withVideo = clips.Select(c => c.Info!).Where(i => i.HasVideo && i.DisplayWidth > 0 && i.DisplayHeight > 0).ToList();
        if (withVideo.Count == 0) return null;

        var biggest = withVideo.OrderByDescending(i => i.DisplayWidth * i.DisplayHeight).First();
        double width = biggest.DisplayWidth, height = biggest.DisplayHeight;
        if (format.MaxHeight > 0 && height > format.MaxHeight)
        {
            width = width * format.MaxHeight / height;
            height = format.MaxHeight;
        }
        var fps = withVideo.Max(i => i.Fps);
        if (fps <= 0) fps = 30;
        return new VideoTarget(Even(width), Even(height), Math.Min(60, Math.Round(fps, 3)));
    }

    public static List<string> SampleArgs(ClipItem clip, TimeSpan at, TimeSpan length, OutputFormat format, QualityLevel quality,
        VideoTarget? target, string output)
    {
        var args = new List<string>
        {
            "-y", "-hide_banner", "-nostdin", "-loglevel", "error",
            "-ss", Format.Seconds(at), "-t", Format.Seconds(length), "-i", clip.FilePath,
            "-an", "-vf", VideoChain(format, target),
        };
        args.AddRange(EncoderArgs.Video(format, quality, clip.Info!.EffectiveVideoBitRate));
        args.AddRange(new[] { "-f", "matroska", output });
        return args;
    }

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);

    private static void WriteConcatList(string path, IEnumerable<(string File, TimeSpan? In, TimeSpan? Out)> entries)
    {
        var sb = new StringBuilder("ffconcat version 1.0\n");
        foreach (var (file, inpoint, outpoint) in entries)
        {
            sb.Append("file '").Append(file.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
            if (inpoint != null) sb.Append("inpoint ").Append(Format.Seconds(inpoint.Value)).Append('\n');
            if (outpoint != null) sb.Append("outpoint ").Append(Format.Seconds(outpoint.Value)).Append('\n');
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Höchstlänge eines Dateinamens ohne Endung (inklusive eines angehängten „ (2)“).</summary>
    public const int MaxNameLength = 35;

    /// <summary>Kürzt einen Namen auf <paramref name="max"/> Zeichen, ohne Leerzeichen oder Punkt am Ende.</summary>
    public static string Shorten(string name, int max = MaxNameLength) =>
        name.Length <= max ? name : name[..max].TrimEnd(' ', '.', '-', '_');

    /// <summary>Freier Dateiname im Zielordner, höchstens 35 Zeichen ohne Endung;
    /// überschreibt nie die Quelldatei oder eine vorhandene Datei.</summary>
    private static string UniqueOutput(string folder, string baseName, string extension, HashSet<string> reserved, string? sourcePath)
    {
        var candidate = Path.Combine(folder, Shorten(baseName) + extension);
        for (var n = 2; File.Exists(candidate) || reserved.Contains(candidate)
                       || (sourcePath != null && string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase)); n++)
        {
            var suffix = $" ({n})";
            candidate = Path.Combine(folder, Shorten(baseName, MaxNameLength - suffix.Length) + suffix + extension);
        }
        reserved.Add(candidate);
        return candidate;
    }
}
