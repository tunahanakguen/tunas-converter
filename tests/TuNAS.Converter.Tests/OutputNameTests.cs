using System.IO;
using TuNAS.Converter.Models;
using TuNAS.Converter.Services;
using TuNAS.Converter.ViewModels;

namespace TuNAS.Converter.Tests;

public class OutputNameTests
{
    private static ClipItem Ready(string path) => new(path)
    {
        Info = new MediaInfo { Duration = TimeSpan.FromSeconds(10), VideoCodec = "h264", Width = 640, Height = 360 },
        State = ClipState.Ready,
    };

    [Fact]
    public void Name_is_suggested_and_user_input_is_kept()
    {
        var vm = new MainViewModel();

        vm.Clips.Add(Ready(@"C:\v\Urlaub.mp4"));
        Assert.True(vm.IsOutputNameEditable);
        Assert.Equal("Urlaub", vm.OutputName);

        vm.Clips.Add(Ready(@"C:\v\Strand.mp4"));
        Assert.False(vm.IsOutputNameEditable); // zwei getrennte Dateien: Originalnamen

        vm.JoinFiles = true;
        Assert.True(vm.IsOutputNameEditable);
        Assert.Equal("Urlaub_Strand", vm.OutputName);

        vm.OutputName = "Norwegen 2026";
        vm.Clips.Add(Ready(@"C:\v\Fjord.mp4"));
        Assert.Equal("Norwegen 2026", vm.OutputName); // eigener Name bleibt

        vm.JoinFiles = false;
        Assert.False(vm.IsOutputNameEditable);
    }

    [Fact]
    public void Join_name_combines_first_10_characters_of_each_file()
    {
        var vm = new MainViewModel { JoinFiles = true };
        vm.Clips.Add(Ready(@"C:\v\Urlaub_Strand.mp4"));
        vm.Clips.Add(Ready(@"C:\v\Geburtstag.avi"));
        vm.Clips.Add(Ready(@"C:\v\Fjord.mkv"));
        Assert.Equal("Urlaub_Str_Geburtstag_Fjord", vm.OutputName);

        vm.MoveClip(vm.Clips[2], 0); // Reihenfolge ändert den Vorschlag mit
        Assert.Equal("Fjord_Urlaub_Str_Geburtstag", vm.OutputName);

        vm.Clips.Add(Ready(@"C:\v\Hochzeit Rede Teil 2.mp4"));
        Assert.Equal(35, vm.OutputName.Length); // nie länger als 35 Zeichen
        Assert.Equal("Fjord_Urlaub_Str_Geburtstag_Hochzei", vm.OutputName);
    }

    [Fact]
    public void Player_volume_is_saved_with_other_settings()
    {
        var settings = AppSettings.Current;
        Assert.StartsWith(Path.GetTempPath(), AppSettings.Folder); // Testordner, nie die echten Einstellungen
        settings.PlayerVolume = 35;
        settings.PlayerMuted = true;
        settings.Save();

        var reloaded = AppSettings.Load();
        Assert.Equal(35, reloaded.PlayerVolume);
        Assert.True(reloaded.PlayerMuted);
        Assert.Equal(settings.OutputFolder, reloaded.OutputFolder); // Zielordner geht dabei nicht verloren
    }

    [Fact]
    public void Summary_shows_total_length_after_cuts()
    {
        var vm = new MainViewModel();
        var a = Ready(@"C:\v\a.mp4");
        var b = Ready(@"C:\v\b.mp4");
        vm.Clips.Add(a);
        Assert.False(vm.HasSummary); // erst ab zwei Dateien

        vm.Clips.Add(b);
        Assert.True(vm.HasSummary);
        Assert.Equal("2 Dateien · zusammen 00:20", vm.SummaryText);

        vm.JoinFiles = true;
        Assert.Equal("Zusammengefügtes Video: 2 Clips · Gesamtlänge 00:20", vm.SummaryText);

        var changed = false;
        vm.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(MainViewModel.SummaryText);
        b.SetRemovedRanges(new[] { new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)) });
        Assert.True(changed);
        Assert.Equal("Zusammengefügtes Video: 2 Clips · Gesamtlänge 00:16  (vorher 00:20)", vm.SummaryText);
    }
}
