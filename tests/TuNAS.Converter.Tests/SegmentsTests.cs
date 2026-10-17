using TuNAS.Converter.Models;

namespace TuNAS.Converter.Tests;

public class SegmentsTests
{
    private static TimeRange R(double a, double b) => new(TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    [Fact]
    public void Kept_without_cuts_is_whole_file()
    {
        var kept = Segments.Kept(TimeSpan.FromSeconds(10), Array.Empty<TimeRange>());
        Assert.Equal(new[] { R(0, 10) }, kept);
    }

    [Fact]
    public void Kept_is_complement_of_removed()
    {
        var kept = Segments.Kept(TimeSpan.FromSeconds(10), new[] { R(2, 3), R(7, 10) });
        Assert.Equal(new[] { R(0, 2), R(3, 7) }, kept);
    }

    [Fact]
    public void Overlapping_and_reversed_ranges_are_merged()
    {
        var normalized = Segments.Normalize(new[] { R(5, 4), R(3.5, 4.5), R(8, 12) }, TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { R(3.5, 5), R(8, 10) }, normalized);
    }

    [Fact]
    public void Edited_and_source_time_convert_both_ways()
    {
        // 10 s Video, 2–3 und 7–10 entfernt → behalten 0–2 und 3–7 (6 s)
        var kept = Segments.Kept(TimeSpan.FromSeconds(10), new[] { R(2, 3), R(7, 10) });
        Assert.Equal(TimeSpan.FromSeconds(6), Segments.Total(kept));

        Assert.Equal(TimeSpan.FromSeconds(1), Segments.ToEdited(kept, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(2), Segments.ToEdited(kept, TimeSpan.FromSeconds(2.5))); // in entfernter Stelle
        Assert.Equal(TimeSpan.FromSeconds(3), Segments.ToEdited(kept, TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.FromSeconds(6), Segments.ToEdited(kept, TimeSpan.FromSeconds(9)));

        Assert.Equal(TimeSpan.FromSeconds(1), Segments.ToSource(kept, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(3), Segments.ToSource(kept, TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(5), Segments.ToSource(kept, TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.FromSeconds(7), Segments.ToSource(kept, TimeSpan.FromSeconds(99)));
    }

    [Fact]
    public void Cut_points_mark_where_something_was_removed()
    {
        var kept = Segments.Kept(TimeSpan.FromSeconds(10), new[] { R(0, 1), R(4, 5) }); // behalten 1–4, 5–10
        Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(3) }, Segments.CutPoints(kept, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void List_shows_new_length_first_after_cut()
    {
        var clip = new ClipItem(@"C:\x\video.mp4")
        {
            Info = new MediaInfo { Duration = TimeSpan.FromSeconds(50.9), SizeBytes = 8 << 20, VideoCodec = "h264", Width = 720, Height = 1280 },
            State = ClipState.Ready,
        };
        Assert.StartsWith("00:50", clip.DetailsText);

        var changed = new List<string?>();
        clip.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        clip.SetRemovedRanges(new[] { R(44, 48) });

        Assert.StartsWith("00:46 (vorher 00:50, 1 Stelle herausgeschnitten)", clip.DetailsText);
        Assert.Contains(nameof(ClipItem.DetailsText), changed);
    }

    [Fact]
    public void Tiny_ranges_are_ignored()
    {
        var normalized = Segments.Normalize(new[] { R(1, 1.01) }, TimeSpan.FromSeconds(10));
        Assert.Empty(normalized);
    }
}
