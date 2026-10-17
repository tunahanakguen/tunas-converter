namespace TuNAS.Converter.Models;

public readonly record struct TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

public static class Segments
{
    /// <summary>Kürzere Stücke werden ignoriert (unter einem Bild bei 25 fps).</summary>
    public static readonly TimeSpan MinLength = TimeSpan.FromMilliseconds(40);

    /// <summary>Sortiert, begrenzt auf die Dateilänge und fasst überlappende Bereiche zusammen.</summary>
    public static List<TimeRange> Normalize(IEnumerable<TimeRange> ranges, TimeSpan duration)
    {
        var sorted = ranges
            .Select(r => r.Start <= r.End ? r : new TimeRange(r.End, r.Start))
            .Select(r => new TimeRange(Clamp(r.Start, duration), Clamp(r.End, duration)))
            .Where(r => r.Duration >= MinLength)
            .OrderBy(r => r.Start)
            .ToList();

        var merged = new List<TimeRange>();
        foreach (var r in sorted)
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new TimeRange(last.Start, r.End > last.End ? r.End : last.End);
            }
            else
            {
                merged.Add(r);
            }
        }
        return merged;
    }

    /// <summary>Die Teile, die nach dem Herausschneiden übrig bleiben.</summary>
    public static List<TimeRange> Kept(TimeSpan duration, IEnumerable<TimeRange> removed)
    {
        var kept = new List<TimeRange>();
        var pos = TimeSpan.Zero;
        foreach (var r in Normalize(removed, duration))
        {
            if (r.Start - pos >= MinLength) kept.Add(new TimeRange(pos, r.Start));
            pos = r.End;
        }
        if (duration - pos >= MinLength) kept.Add(new TimeRange(pos, duration));
        return kept;
    }

    public static TimeSpan Total(IEnumerable<TimeRange> segments) =>
        segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);

    /// <summary>
    /// Zeit im Originalvideo → Zeit im geschnittenen Video.
    /// Liegt die Zeit in einem entfernten Stück, zählt sie als Anfang des nächsten behaltenen Stücks.
    /// </summary>
    public static TimeSpan ToEdited(IReadOnlyList<TimeRange> kept, TimeSpan source)
    {
        var edited = TimeSpan.Zero;
        foreach (var seg in kept)
        {
            if (source < seg.Start) return edited;
            if (source <= seg.End) return edited + (source - seg.Start);
            edited += seg.Duration;
        }
        return edited;
    }

    /// <summary>Zeit im geschnittenen Video → Zeit im Originalvideo.</summary>
    public static TimeSpan ToSource(IReadOnlyList<TimeRange> kept, TimeSpan edited)
    {
        if (kept.Count == 0) return TimeSpan.Zero;
        if (edited < TimeSpan.Zero) edited = TimeSpan.Zero;
        foreach (var seg in kept)
        {
            if (edited < seg.Duration) return seg.Start + edited;
            edited -= seg.Duration;
        }
        return kept[^1].End;
    }

    /// <summary>Stellen im geschnittenen Video, an denen etwas entfernt wurde (für Markierungen in der Zeitleiste).</summary>
    public static List<TimeSpan> CutPoints(IReadOnlyList<TimeRange> kept, TimeSpan duration)
    {
        var points = new List<TimeSpan>();
        var edited = TimeSpan.Zero;
        for (var i = 0; i < kept.Count; i++)
        {
            if (i == 0 && kept[0].Start > TimeSpan.Zero) points.Add(TimeSpan.Zero);
            edited += kept[i].Duration;
            var removedAfter = i < kept.Count - 1 || kept[i].End < duration;
            if (removedAfter) points.Add(edited);
        }
        return points;
    }

    private static TimeSpan Clamp(TimeSpan t, TimeSpan duration) =>
        t < TimeSpan.Zero ? TimeSpan.Zero : (t > duration ? duration : t);
}
