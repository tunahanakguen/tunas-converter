using System.IO;
using TuNAS.Converter.Models;
using TuNAS.Converter.Services;
using TuNAS.Converter.ViewModels;

namespace TuNAS.Converter.Tests;

public class OrderTests
{
    private static ClipItem Ready(string path) => new(path)
    {
        Info = new MediaInfo { Duration = TimeSpan.FromSeconds(10), VideoCodec = "h264", Width = 640, Height = 360, AudioCodec = "aac" },
        State = ClipState.Ready,
    };

    private static string Names(MainViewModel vm) => string.Join(",", vm.Clips.Select(c => $"{c.Position}{Path.GetFileNameWithoutExtension(c.FileName)}"));

    [Fact]
    public void Arrows_and_dragging_change_order_and_numbers()
    {
        var vm = new MainViewModel();
        var a = Ready(@"C:\v\a.mp4");
        var b = Ready(@"C:\v\b.mp4");
        var c = Ready(@"C:\v\c.mp4");
        vm.Clips.Add(a); vm.Clips.Add(b); vm.Clips.Add(c);
        Assert.Equal("1a,2b,3c", Names(vm));

        vm.MoveDownCommand.Execute(a);
        Assert.Equal("1b,2a,3c", Names(vm));

        vm.MoveUpCommand.Execute(c);
        Assert.Equal("1b,2c,3a", Names(vm));

        vm.MoveUpCommand.Execute(b); // schon oben: bleibt
        Assert.Equal("1b,2c,3a", Names(vm));

        vm.MoveClip(a, 0); // per Ziehen ganz nach oben
        Assert.Equal("1a,2b,3c", Names(vm));
    }

    [Fact]
    public void Cuts_are_still_noticed_after_moving()
    {
        var vm = new MainViewModel { JoinFiles = true };
        var a = Ready(@"C:\v\a.mp4");
        var b = Ready(@"C:\v\b.mp4");
        vm.Clips.Add(a); vm.Clips.Add(b);
        vm.MoveClip(b, 0);

        b.SetRemovedRanges(new[] { new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(4)) });
        Assert.Contains("Gesamtlänge 00:16", vm.SummaryText);
    }

    [Fact]
    public void Joined_video_follows_list_order()
    {
        var a = Ready(@"C:\v\a.mp4");
        var b = Ready(@"C:\v\b.mp4");
        var c = Ready(@"C:\v\c.mp4");
        var format = FormatCatalog.Main.Single(f => f.Name == "MKV");
        var output = Path.Combine(Path.GetTempPath(), "tunas-order-" + Guid.NewGuid().ToString("N"));

        var plan = CommandBuilder.Build(new[] { c, a, b }, format, QualityLevel.Mittel, join: true, output);
        var order = plan.Steps.Where(s => !s.IsFinal).Select(s => s.Title).ToList();
        Assert.Equal(new[] { "c.mp4", "a.mp4", "b.mp4" }, order.Select(t => t[(t.IndexOf(':') + 2)..]));
        try { Directory.Delete(output, true); } catch (IOException) { }
    }
}
