using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TuNAS.Converter.Services;

namespace TuNAS.Converter.Models;

public enum ClipState { Loading, Ready, Error }

/// <summary>Eine Datei in der Liste, mit ihren Schnitten.</summary>
public partial class ClipItem : ObservableObject
{
    private List<TimeRange> _removed = new();

    public ClipItem(string path)
    {
        FilePath = path;
        FileName = System.IO.Path.GetFileName(path);
    }

    public string FilePath { get; }
    public string FileName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText), nameof(KeptDuration), nameof(CanEdit))]
    private MediaInfo? _info;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText), nameof(IsReady), nameof(HasError), nameof(IsLoading), nameof(CanEdit))]
    private ClipState _state = ClipState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private string? _errorMessage;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    /// <summary>Platz in der Liste (1, 2, 3 …) = Reihenfolge beim Zusammenfügen.</summary>
    [ObservableProperty]
    private int _position;

    public IReadOnlyList<TimeRange> RemovedRanges => _removed;

    public bool IsReady => State == ClipState.Ready;
    public bool HasError => State == ClipState.Error;
    public bool IsLoading => State == ClipState.Loading;
    public bool CanEdit => IsReady && Info is { Duration.TotalSeconds: > 0 };
    public bool HasCuts => _removed.Count > 0;

    public List<TimeRange> KeptSegments =>
        Info == null ? new List<TimeRange>() : Segments.Kept(Info.Duration, _removed);

    public TimeSpan KeptDuration =>
        KeptSegments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);

    public void SetRemovedRanges(IEnumerable<TimeRange> ranges)
    {
        _removed = Segments.Normalize(ranges, Info?.Duration ?? TimeSpan.Zero);
        OnPropertyChanged(nameof(RemovedRanges));
        OnPropertyChanged(nameof(HasCuts));
        OnPropertyChanged(nameof(KeptDuration));
        OnPropertyChanged(nameof(DetailsText));
    }

    public string DetailsText => State switch
    {
        ClipState.Loading => "Wird gelesen …",
        ClipState.Error => "Kann nicht geöffnet werden: " + ErrorMessage,
        _ => BuildDetails(),
    };

    private string BuildDetails()
    {
        if (Info == null) return "";
        var parts = new List<string>();
        if (HasCuts)
        {
            var n = _removed.Count;
            parts.Add($"{Format.Time(KeptDuration)} (vorher {Format.Time(Info.Duration)}, {n} {(n == 1 ? "Stelle" : "Stellen")} herausgeschnitten)");
        }
        else
        {
            parts.Add(Format.Time(Info.Duration));
        }
        if (Info.HasVideo) parts.Add($"{Info.DisplayWidth}×{Info.DisplayHeight}");
        parts.Add(Format.Size(Info.SizeBytes));
        var codecs = string.Join(" / ", new[] { Info.VideoCodec, Info.AudioCodec }.Where(c => c != null));
        if (codecs.Length > 0) parts.Add(codecs.ToUpperInvariant());
        return string.Join(" · ", parts);
    }
}
