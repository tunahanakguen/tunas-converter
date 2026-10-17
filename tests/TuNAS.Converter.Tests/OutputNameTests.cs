using TuNAS.Converter.Models;
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
        Assert.StartsWith("Zusammengefügt", vm.OutputName);

        vm.OutputName = "Norwegen 2026";
        vm.Clips.Add(Ready(@"C:\v\Fjord.mp4"));
        Assert.Equal("Norwegen 2026", vm.OutputName); // eigener Name bleibt

        vm.JoinFiles = false;
        Assert.False(vm.IsOutputNameEditable);
    }
}
