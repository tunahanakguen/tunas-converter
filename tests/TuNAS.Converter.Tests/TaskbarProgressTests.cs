using System.IO;
using System.Windows.Shell;
using TuNAS.Converter.Models;
using TuNAS.Converter.Services;
using TuNAS.Converter.ViewModels;

namespace TuNAS.Converter.Tests;

public class TaskbarProgressTests : IClassFixture<MediaFixture>
{
    private readonly MediaFixture _media;

    public TaskbarProgressTests(MediaFixture media) => _media = media;

    [Fact]
    public async Task Taskbar_bar_runs_during_conversion_and_disappears_after()
    {
        Assert.StartsWith(Path.GetTempPath(), AppSettings.Folder); // nie die echten Einstellungen

        var vm = new MainViewModel { OutputFolder = Path.Combine(_media.Folder, "taskbar-" + Guid.NewGuid().ToString("N")) };
        vm.Clips.Add(new ClipItem(_media.Mp4A)
        {
            Info = await ProbeService.ProbeAsync(_media.Mp4A, CancellationToken.None),
            State = ClipState.Ready,
        });
        vm.SelectedFormat = FormatCatalog.Main.Single(f => f.Name == "MKV");
        vm.Quality = QualityLevel.Mittel;

        var states = new List<TaskbarItemProgressState>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TaskbarState)) states.Add(vm.TaskbarState);
        };

        await vm.ConvertCommand.ExecuteAsync(null);

        Assert.Contains(TaskbarItemProgressState.Normal, states);
        Assert.Equal(TaskbarItemProgressState.None, vm.TaskbarState);
        Assert.False(vm.ResultIsError, vm.ResultText);
    }
}
