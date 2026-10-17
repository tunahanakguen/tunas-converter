using System.Diagnostics;
using System.IO;
using TuNAS.Converter.Models;
using TuNAS.Converter.Services;

namespace TuNAS.Converter.Tests;

/// <summary>Erzeugt einmal kurze Testvideos mit ffmpeg (Testbild + Ton).</summary>
public sealed class MediaFixture : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "TuNAS-Tests-" + Guid.NewGuid().ToString("N"));

    public string Mp4A => Path.Combine(Folder, "a.mp4");   // 640x360, 25 fps, H.264 + AAC, 8 s
    public string Mp4B => Path.Combine(Folder, "b.mp4");   // gleiches Format, 6 s
    public string Avi => Path.Combine(Folder, "c.avi");    // 320x240, 30 fps, XviD + MP3, 5 s
    public string MkvSilent => Path.Combine(Folder, "d.mkv"); // 1280x720, H.264 ohne Ton, 4 s
    public string Wav => Path.Combine(Folder, "e.wav");    // nur Ton, 5 s
    public string Vp9 => Path.Combine(Folder, "f.mkv");    // VP9 + Opus wie bei YouTube, 6 s
    public string LowBitrate => Path.Combine(Folder, "g.mp4"); // H.264 mit sehr kleiner Datenrate, 6 s

    public MediaFixture()
    {
        FfmpegTools.Locate();
        Assert.True(FfmpegTools.IsAvailable, "ffmpeg/ffprobe nicht gefunden");
        Directory.CreateDirectory(Folder);

        Generate(Mp4A, "testsrc2=size=640x360:rate=25:duration=8", "sine=frequency=440:sample_rate=44100:duration=8",
            "-c:v libx264 -pix_fmt yuv420p -g 25 -c:a aac -ac 2");
        Generate(Mp4B, "testsrc2=size=640x360:rate=25:duration=6", "sine=frequency=660:sample_rate=44100:duration=6",
            "-c:v libx264 -pix_fmt yuv420p -g 25 -c:a aac -ac 2");
        Generate(Avi, "testsrc=size=320x240:rate=30:duration=5", "sine=frequency=330:sample_rate=48000:duration=5",
            "-c:v mpeg4 -vtag xvid -q:v 4 -c:a libmp3lame -ac 2");
        Generate(MkvSilent, "testsrc2=size=1280x720:rate=30:duration=4", null, "-c:v libx264 -pix_fmt yuv420p");
        Generate(Wav, null, "sine=frequency=550:sample_rate=44100:duration=5", "-c:a pcm_s16le");
        Generate(Vp9, "testsrc2=size=640x360:rate=25:duration=6", "sine=frequency=440:sample_rate=48000:duration=6",
            "-c:v libvpx-vp9 -b:v 300k -deadline realtime -cpu-used 8 -g 25 -c:a libopus -ac 2");
        Generate(LowBitrate, "testsrc2=size=1280x720:rate=25:duration=6", "sine=frequency=440:sample_rate=44100:duration=6",
            "-c:v libx264 -b:v 250k -pix_fmt yuv420p -c:a aac -b:a 64k -ac 2");
    }

    private void Generate(string output, string? video, string? audio, string codecArgs)
    {
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
        if (video != null) args.AddRange(new[] { "-f", "lavfi", "-i", video });
        if (audio != null) args.AddRange(new[] { "-f", "lavfi", "-i", audio });
        args.AddRange(codecArgs.Split(' '));
        args.Add(output);
        var psi = new ProcessStartInfo(FfmpegTools.FfmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err);
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

public class FfmpegIntegrationTests : IClassFixture<MediaFixture>
{
    private readonly MediaFixture _media;
    private readonly string _out;

    public FfmpegIntegrationTests(MediaFixture media)
    {
        _media = media;
        _out = Path.Combine(media.Folder, "out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_out);
    }

    private static async Task<ClipItem> Clip(string path, params (double Start, double End)[] cuts)
    {
        var clip = new ClipItem(path)
        {
            Info = await ProbeService.ProbeAsync(path, CancellationToken.None),
            State = ClipState.Ready,
        };
        clip.SetRemovedRanges(cuts.Select(c => new TimeRange(TimeSpan.FromSeconds(c.Start), TimeSpan.FromSeconds(c.End))));
        return clip;
    }

    private static OutputFormat Fmt(string name) =>
        FormatCatalog.Main.Concat(FormatCatalog.More).Single(f => f.Name == name);

    private static async Task<ConversionResult> Run(ConversionPlan plan)
    {
        var result = await ConversionService.RunAsync(plan, new Progress<ConversionProgress>(), CancellationToken.None);
        Assert.False(result.Cancelled);
        Assert.True(result.Failures.Count == 0, string.Join("\n", result.Failures.Select(f => f.Title + ": " + f.Message)));
        return result;
    }

    [Fact]
    public async Task Probe_reads_video_and_audio()
    {
        var info = await ProbeService.ProbeAsync(_media.Mp4A, CancellationToken.None);
        Assert.Equal(640, info.Width);
        Assert.Equal(360, info.Height);
        Assert.Equal(25, info.Fps, 1);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
        Assert.InRange(info.Duration.TotalSeconds, 7.9, 8.1);
    }

    [Fact]
    public async Task Reencode_with_cuts_has_kept_length()
    {
        var clip = await Clip(_media.Mp4A, (1, 3), (6, 7)); // behalten: 0–1, 3–6, 7–8 = 5 s
        var plan = CommandBuilder.Build(new[] { clip }, Fmt("MP4"), QualityLevel.Mittel, join: false, _out);
        var result = await Run(plan);

        var info = await ProbeService.ProbeAsync(result.Outputs.Single(), CancellationToken.None);
        Assert.InRange(info.Duration.TotalSeconds, 4.8, 5.3);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public async Task Original_avi_to_avi_is_lossless_copy()
    {
        var clip = await Clip(_media.Avi, (0, 1));
        var plan = CommandBuilder.Build(new[] { clip }, Fmt("AVI"), QualityLevel.Original, join: false, _out);
        Assert.True(plan.Lossless);
        var result = await Run(plan);

        var info = await ProbeService.ProbeAsync(result.Outputs.Single(), CancellationToken.None);
        Assert.Equal("mpeg4", info.VideoCodec);
        Assert.InRange(info.Duration.TotalSeconds, 3.5, 4.6); // verlustfrei: Schnitt am Schlüsselbild
    }

    [Fact]
    public async Task Join_identical_clips_is_lossless_and_sums_up()
    {
        var a = await Clip(_media.Mp4A);
        var b = await Clip(_media.Mp4B);
        var plan = CommandBuilder.Build(new[] { a, b }, Fmt("MP4"), QualityLevel.Original, join: true, _out);
        Assert.True(plan.Lossless);
        var result = await Run(plan);

        var info = await ProbeService.ProbeAsync(result.Outputs.Single(), CancellationToken.None);
        Assert.InRange(info.Duration.TotalSeconds, 13.7, 14.4);
    }

    [Fact]
    public async Task Join_mixed_clips_normalizes_and_keeps_audio()
    {
        // Unterschiedliche Auflösung, Bildrate, Codec und ein Clip ohne Ton.
        var a = await Clip(_media.Mp4A, (2, 5));   // 5 s
        var c = await Clip(_media.Avi);            // 5 s
        var d = await Clip(_media.MkvSilent);      // 4 s, ohne Ton
        var plan = CommandBuilder.Build(new[] { a, c, d }, Fmt("MP4"), QualityLevel.Mittel, join: true, _out);
        Assert.False(plan.Lossless);
        var result = await Run(plan);

        var info = await ProbeService.ProbeAsync(result.Outputs.Single(), CancellationToken.None);
        Assert.Equal(1280, info.Width);
        Assert.Equal(720, info.Height);
        Assert.True(info.HasAudio);
        Assert.InRange(info.Duration.TotalSeconds, 13.6, 14.5);
    }

    [Fact]
    public async Task Many_clips_join_without_problems()
    {
        var clips = new List<ClipItem>();
        for (var i = 0; i < 10; i++) clips.Add(await Clip(i % 2 == 0 ? _media.Mp4A : _media.Avi, (1, 2)));
        var plan = CommandBuilder.Build(clips, Fmt("MKV"), QualityLevel.Klein, join: true, _out);
        var result = await Run(plan);

        var info = await ProbeService.ProbeAsync(result.Outputs.Single(), CancellationToken.None);
        var expected = clips.Sum(c => c.KeptDuration.TotalSeconds);
        Assert.InRange(info.Duration.TotalSeconds, expected - 0.5, expected + 0.6);
    }

    [Fact]
    public async Task Mp3_from_video_with_cut()
    {
        var clip = await Clip(_media.Mp4A, (0, 4));
        var plan = CommandBuilder.Build(new[] { clip }, Fmt("MP3"), QualityLevel.Hoch, join: false, _out);
        var result = await Run(plan);

        var info = await ProbeService.ProbeAsync(result.Outputs.Single(), CancellationToken.None);
        Assert.False(info.HasVideo);
        Assert.Equal("mp3", info.AudioCodec);
        Assert.InRange(info.Duration.TotalSeconds, 3.8, 4.2);
    }

    [Fact]
    public async Task Vp9_mkv_to_mp4_original_is_lossless_and_smaller()
    {
        // Der Fall „Lysebotn.mkv“: VP9 + Opus, geschnitten, als MP4 in Originalqualität.
        var clip = await Clip(_media.Vp9, (2, 4));
        var plan = CommandBuilder.Build(new[] { clip }, Fmt("MP4"), QualityLevel.Original, join: false, _out);
        Assert.True(plan.Lossless);
        var result = await Run(plan);

        var output = result.Outputs.Single();
        var info = await ProbeService.ProbeAsync(output, CancellationToken.None);
        Assert.Equal("vp9", info.VideoCodec);
        Assert.Equal("opus", info.AudioCodec);
        Assert.True(new FileInfo(output).Length < new FileInfo(_media.Vp9).Length);
    }

    [Theory]
    [InlineData("WebM", QualityLevel.Original)]
    [InlineData("WebM", QualityLevel.Hoch)]
    [InlineData("MOV", QualityLevel.Hoch)]
    [InlineData("Handy", QualityLevel.Hoch)]
    public async Task Reencode_never_gets_much_bigger_than_source(string format, QualityLevel quality)
    {
        // Quelle mit sehr kleiner Datenrate: ohne Obergrenze würde das Ergebnis deutlich größer.
        var clip = await Clip(_media.LowBitrate, (0, 1));
        var plan = CommandBuilder.Build(new[] { clip }, Fmt(format), quality, join: false, _out);
        var result = await Run(plan);

        var keptSourceBytes = new FileInfo(_media.LowBitrate).Length * 5.0 / 6.0;
        var output = new FileInfo(result.Outputs.Single()).Length;
        Assert.True(output < keptSourceBytes * 1.3, $"{output} Bytes statt höchstens ~{keptSourceBytes:0}");
    }

    [Fact]
    public async Task Join_uses_chosen_file_name_and_never_overwrites()
    {
        var a = await Clip(_media.Mp4A);
        var b = await Clip(_media.Mp4B);
        var first = await Run(CommandBuilder.Build(new[] { a, b }, Fmt("MP4"), QualityLevel.Original, join: true, _out, "Urlaub: Norwegen?"));
        Assert.Equal("Urlaub Norwegen.mp4", Path.GetFileName(first.Outputs.Single())); // ungültige Zeichen entfernt

        var second = await Run(CommandBuilder.Build(new[] { a, b }, Fmt("MP4"), QualityLevel.Original, join: true, _out, "Urlaub Norwegen"));
        Assert.Equal("Urlaub Norwegen (2).mp4", Path.GetFileName(second.Outputs.Single()));
    }

    [Fact]
    public async Task Long_names_are_cut_to_35_characters()
    {
        var a = await Clip(_media.Mp4A);
        var name = "Ein sehr langer Dateiname für unseren Sommerurlaub in Norwegen";
        var first = await Run(CommandBuilder.Build(new[] { a }, Fmt("MKV"), QualityLevel.Original, join: false, _out, name));
        var second = await Run(CommandBuilder.Build(new[] { a }, Fmt("MKV"), QualityLevel.Original, join: false, _out, name));

        var n1 = Path.GetFileNameWithoutExtension(first.Outputs.Single());
        var n2 = Path.GetFileNameWithoutExtension(second.Outputs.Single());
        Assert.Equal("Ein sehr langer Dateiname für unser", n1);
        Assert.Equal(35, n1.Length);
        Assert.True(n2.Length <= 35 && n2.EndsWith(" (2)"), n2);
    }

    [Fact]
    public async Task Single_clip_uses_chosen_name_but_many_keep_originals()
    {
        var a = await Clip(_media.Mp4A);
        var c = await Clip(_media.Avi);
        var single = CommandBuilder.Build(new[] { a }, Fmt("MKV"), QualityLevel.Original, join: false, _out, "Mein Clip");
        Assert.Equal("Mein Clip.mkv", Path.GetFileName(single.Steps.Single().OutputFile));

        var many = CommandBuilder.Build(new[] { a, c }, Fmt("MKV"), QualityLevel.Original, join: false, _out, "Mein Clip");
        Assert.Equal(new[] { "a.mkv", "c.mkv" }, many.Steps.Select(s => Path.GetFileName(s.OutputFile)));
    }

    [Fact]
    public async Task Video_format_skips_audio_only_file_with_message()
    {
        var wav = await Clip(_media.Wav);
        var plan = CommandBuilder.Build(new[] { wav }, Fmt("MP4"), QualityLevel.Mittel, join: false, _out);
        Assert.Empty(plan.Steps);
        Assert.Single(plan.Problems);
    }

    [Theory]
    [InlineData("MP4")] [InlineData("MKV")] [InlineData("AVI")] [InlineData("MOV")] [InlineData("WMV")]
    [InlineData("WebM")] [InlineData("Handy")] [InlineData("MP3")] [InlineData("FLV")] [InlineData("MPEG")]
    [InlineData("3GP")] [InlineData("M4A (AAC)")] [InlineData("WAV")] [InlineData("FLAC")] [InlineData("OGG")]
    [InlineData("Opus")] [InlineData("WMA")] [InlineData("AC3")] [InlineData("AMR")]
    public async Task Every_format_converts(string name)
    {
        var clip = await Clip(_media.Mp4A, (0, 5)); // 3 s behalten
        var plan = CommandBuilder.Build(new[] { clip }, Fmt(name), QualityLevel.Mittel, join: false, _out);
        var result = await Run(plan);

        var info = await ProbeService.ProbeAsync(result.Outputs.Single(), CancellationToken.None);
        Assert.InRange(info.Duration.TotalSeconds, 2.7, 3.4);
    }

    [Theory]
    [InlineData(QualityLevel.Hoch)]
    [InlineData(QualityLevel.Klein)]
    public async Task Estimate_is_close_to_real_size(QualityLevel quality)
    {
        var a = await Clip(_media.Mp4A, (1, 2));
        var c = await Clip(_media.Avi);
        var format = Fmt("MP4");

        var estimate = await SizeEstimator.EstimateAsync(new[] { a, c }, format, quality, join: true, CancellationToken.None);
        Assert.NotNull(estimate);

        var result = await Run(CommandBuilder.Build(new[] { a, c }, format, quality, join: true, _out));
        var actual = new FileInfo(result.Outputs.Single()).Length;
        var ratio = (double)estimate!.Bytes / actual;
        Assert.InRange(ratio, 0.6, 1.5);
    }
}
