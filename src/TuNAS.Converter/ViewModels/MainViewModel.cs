using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TuNAS.Converter.Models;
using TuNAS.Converter.Services;

namespace TuNAS.Converter.ViewModels;

public enum Verdict { None, Lossless, NearlySame, Visible }

public partial class MainViewModel : ObservableObject
{
    public static readonly string[] VideoExtensions =
    {
        ".avi", ".mp4", ".m4v", ".mkv", ".mov", ".qt", ".wmv", ".asf", ".wm", ".flv", ".f4v", ".swf", ".webm",
        ".mpg", ".mpeg", ".mpe", ".vob", ".vro", ".ts", ".mts", ".m2ts", ".m2t", ".tod", ".3gp", ".3g2", ".3gpp",
        ".rm", ".rmvb", ".ogv", ".divx", ".dv", ".mxf", ".y4m",
    };

    public static readonly string[] AudioExtensions =
    {
        ".mp3", ".wav", ".wma", ".flac", ".ac3", ".aac", ".m4a", ".m4r", ".ogg", ".oga", ".opus", ".amr", ".aif",
        ".aiff", ".aifc", ".au", ".ape", ".caf", ".dts", ".mka", ".mp2", ".mpc", ".tta", ".wv", ".voc", ".w64", ".ra",
    };

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly Channel<ClipItem> _loadQueue = Channel.CreateUnbounded<ClipItem>();
    private CancellationTokenSource? _estimateCts;
    private CancellationTokenSource? _convertCts;
    private long _lastEstimateBytes;

    public MainViewModel()
    {
        _outputFolder = _settings.OutputFolder;
        Clips.CollectionChanged += OnClipsChanged;
        _ = Task.Run(LoadLoopAsync);
    }

    /// <summary>Wird vom Fenster gesetzt: öffnet das Schneidefenster und meldet, ob etwas geändert wurde.</summary>
    public Func<ClipItem, bool>? EditRequested { get; set; }

    public ObservableCollection<ClipItem> Clips { get; } = new();
    public IReadOnlyList<OutputFormat> MainFormats => FormatCatalog.Main;
    public IReadOnlyList<OutputFormat> MoreFormats => FormatCatalog.More;
    public bool HasClips => Clips.Count > 0;
    public bool IsSettingsVisible => SelectedFormat != null;
    public bool CanConvert => !IsConverting && SelectedFormat != null && Clips.Any(c => c.IsReady);
    public bool CanJoin => Clips.Count(c => c.IsReady) > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsVisible), nameof(CanConvert), nameof(SettingsTitle))]
    private OutputFormat? _selectedFormat;

    [ObservableProperty]
    private QualityLevel _quality = QualityLevel.Mittel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsTitle))]
    private bool _joinFiles;

    [ObservableProperty]
    private string _outputFolder;

    [ObservableProperty]
    private string _estimateText = "";

    [ObservableProperty]
    private Verdict _verdict;

    [ObservableProperty]
    private string _verdictText = "";

    [ObservableProperty]
    private string? _hintText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConvert))]
    private bool _isConverting;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = "";

    /// <summary>Balken im Taskleisten-Symbol: läuft beim Umwandeln mit, rot bei Fehler, sonst ausgeblendet.</summary>
    [ObservableProperty]
    private TaskbarItemProgressState _taskbarState = TaskbarItemProgressState.None;

    /// <summary>Fortschritt für die Taskleiste, 0 bis 1.</summary>
    [ObservableProperty]
    private double _taskbarProgress;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private bool _resultIsError;

    // ---------- Dateiname der Zieldatei ----------

    [ObservableProperty]
    private string _outputName = "";

    private bool _settingDefaultName;
    private bool _nameEdited;
    private string _nameMode = "";

    /// <summary>Den Namen kann man eingeben, wenn genau eine Datei entsteht (Zusammenfügen oder nur ein Clip).</summary>
    public bool IsOutputNameEditable => _nameMode.Length > 0;
    public string OutputExtension => SelectedFormat?.Extension ?? "";

    partial void OnOutputNameChanged(string value)
    {
        if (!_settingDefaultName) _nameEdited = true;
    }

    /// <summary>Schlägt einen Namen vor; ein selbst eingegebener Name bleibt, solange sich die Art der Ausgabe nicht ändert.</summary>
    private void UpdateDefaultName()
    {
        var ready = Clips.Where(c => c.IsReady).ToList();
        var mode = JoinFiles && ready.Count > 1 ? "join" : ready.Count == 1 ? "single:" + ready[0].FilePath : "";
        if (mode != _nameMode)
        {
            _nameMode = mode;
            _nameEdited = false;
            OnPropertyChanged(nameof(IsOutputNameEditable));
        }
        if (_nameEdited) return;

        _settingDefaultName = true;
        OutputName = mode == "join" ? CommandBuilder.DefaultJoinName(ready)
            : mode.Length > 0 ? CommandBuilder.Shorten(Path.GetFileNameWithoutExtension(ready[0].FileName))
            : "";
        _settingDefaultName = false;
    }

    // ---------- Gesamtlänge ----------

    /// <summary>Summenzeile unter der Liste, sobald mindestens zwei Dateien gelesen sind.</summary>
    public bool HasSummary => Clips.Count(c => c.IsReady) > 1;

    /// <summary>Gesamtlänge nach allen Schnitten, z. B. „Zusammengefügtes Video: 3 Clips · Gesamtlänge 12:34 (vorher 15:00)“.</summary>
    public string SummaryText
    {
        get
        {
            var ready = Clips.Where(c => c.IsReady && c.Info != null).ToList();
            if (ready.Count < 2) return "";
            var kept = ready.Aggregate(TimeSpan.Zero, (sum, c) => sum + c.KeptDuration);
            var original = ready.Aggregate(TimeSpan.Zero, (sum, c) => sum + c.Info!.Duration);
            var before = kept < original - TimeSpan.FromMilliseconds(500) ? $"  (vorher {Format.Time(original)})" : "";
            return JoinFiles
                ? $"Zusammengefügtes Video: {ready.Count} Clips · Gesamtlänge {Format.Time(kept)}{before}"
                : $"{ready.Count} Dateien · zusammen {Format.Time(kept)}{before}";
        }
    }

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(SummaryText));
    }

    public string SettingsTitle => SelectedFormat == null
        ? ""
        : $"In {SelectedFormat.Name} umwandeln" + (JoinFiles && CanJoin ? " – alle Clips zu einer Datei" : "");

    partial void OnSelectedFormatChanged(OutputFormat? value)
    {
        OnPropertyChanged(nameof(OutputExtension));
        UpdateDefaultName();
        RequestEstimate();
    }

    partial void OnQualityChanged(QualityLevel value) => RequestEstimate();

    partial void OnJoinFilesChanged(bool value)
    {
        RaiseSummaryChanged();
        UpdateDefaultName();
        RequestEstimate();
    }

    partial void OnOutputFolderChanged(string value)
    {
        _settings.OutputFolder = value;
        _settings.Save();
    }

    // ---------- Dateien hinzufügen ----------

    [RelayCommand]
    private void AddVideos() => PickFiles("Videos hinzufügen",
        "Videodateien|" + Pattern(VideoExtensions) + "|Alle Dateien (*.*)|*.*");

    [RelayCommand]
    private void AddAudio() => PickFiles("Audiodateien hinzufügen",
        "Audiodateien|" + Pattern(AudioExtensions) + "|Alle Dateien (*.*)|*.*");

    private void PickFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        if (dialog.ShowDialog() == true) AddPaths(dialog.FileNames);
    }

    /// <summary>Dateien und Ordner (per Drag &amp; Drop) hinzufügen. Gelesen wird nacheinander im Hintergrund.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var file in ExpandPaths(paths))
        {
            if (Clips.Any(c => string.Equals(c.FilePath, file, StringComparison.OrdinalIgnoreCase))) continue;
            var clip = new ClipItem(file);
            Clips.Add(clip);
            _loadQueue.Writer.TryWrite(clip);
        }
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        var known = new HashSet<string>(VideoExtensions.Concat(AudioExtensions), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(f => known.Contains(Path.GetExtension(f))).OrderBy(f => f).ToList(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                foreach (var f in files) yield return f;
            }
            else if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>Eine Datei nach der anderen: so bleibt der Speicherbedarf klein, auch bei vielen Clips.</summary>
    private async Task LoadLoopAsync()
    {
        await foreach (var clip in _loadQueue.Reader.ReadAllAsync())
        {
            if (!Application.Current.Dispatcher.Invoke(() => Clips.Contains(clip))) continue;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var info = await ProbeService.ProbeAsync(clip.FilePath, timeout.Token);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    clip.Info = info;
                    clip.State = ClipState.Ready;
                });
                var thumbnail = await ThumbnailService.CreateAsync(clip.FilePath, info, timeout.Token);
                if (thumbnail != null)
                    await Application.Current.Dispatcher.InvokeAsync(() => clip.Thumbnail = thumbnail);
            }
            catch (Exception ex)
            {
                Logger.Write($"Lesen fehlgeschlagen: {clip.FilePath}: {ex}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (clip.State == ClipState.Ready) return; // nur das Vorschaubild fehlt
                    clip.ErrorMessage = ex is OperationCanceledException ? "Zeitüberschreitung beim Lesen." : ex.Message;
                    clip.State = ClipState.Error;
                });
            }
        }
    }

    // ---------- Liste bearbeiten ----------

    [RelayCommand]
    private void RemoveClip(ClipItem clip)
    {
        Clips.Remove(clip);
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var clip in Clips) clip.PropertyChanged -= OnClipPropertyChanged;
        Clips.Clear();
        SelectedFormat = null;
    }

    [RelayCommand]
    private void EditClip(ClipItem clip)
    {
        if (!clip.CanEdit) return;
        EditRequested?.Invoke(clip);
    }

    /// <summary>Verschiebt einen Clip an eine neue Stelle (z. B. per Ziehen mit der Maus).</summary>
    public void MoveClip(ClipItem clip, int newIndex)
    {
        var oldIndex = Clips.IndexOf(clip);
        if (oldIndex < 0 || IsConverting) return;
        newIndex = Math.Clamp(newIndex, 0, Clips.Count - 1);
        if (newIndex != oldIndex) Clips.Move(oldIndex, newIndex);
    }

    [RelayCommand]
    private void MoveUp(ClipItem clip) => MoveClip(clip, Clips.IndexOf(clip) - 1);

    [RelayCommand]
    private void MoveDown(ClipItem clip) => MoveClip(clip, Clips.IndexOf(clip) + 1);

    private void OnClipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auf Änderungen jedes Clips hören (Lesen fertig, Schnitte), egal wie er in die Liste kam.
        // Beim Verschieben bleibt der Clip derselbe – dann nichts an- oder abmelden.
        if (e.Action != NotifyCollectionChangedAction.Move)
        {
            if (e.OldItems != null)
                foreach (ClipItem clip in e.OldItems) clip.PropertyChanged -= OnClipPropertyChanged;
            if (e.NewItems != null)
                foreach (ClipItem clip in e.NewItems) clip.PropertyChanged += OnClipPropertyChanged;
        }
        for (var i = 0; i < Clips.Count; i++) Clips[i].Position = i + 1;

        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(CanConvert));
        OnPropertyChanged(nameof(CanJoin));
        OnPropertyChanged(nameof(SettingsTitle));
        RaiseSummaryChanged();
        UpdateDefaultName();
        RequestEstimate();
    }

    private void OnClipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClipItem.State) or nameof(ClipItem.RemovedRanges))
        {
            OnPropertyChanged(nameof(CanConvert));
            OnPropertyChanged(nameof(CanJoin));
            OnPropertyChanged(nameof(SettingsTitle));
            RaiseSummaryChanged();
            if (e.PropertyName == nameof(ClipItem.State)) UpdateDefaultName();
            RequestEstimate();
        }
    }

    // ---------- Format und Qualität ----------

    [RelayCommand]
    private void SelectFormat(OutputFormat format)
    {
        ResultText = null;
        SelectedFormat = format;
    }

    [RelayCommand]
    private void CloseSettings() => SelectedFormat = null;

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Zielordner wählen", InitialDirectory = Directory.Exists(OutputFolder) ? OutputFolder : null };
        if (dialog.ShowDialog() == true) OutputFolder = dialog.FolderName;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OutputFolder}\"") { UseShellExecute = true });
    }

    /// <summary>Schätzung neu berechnen, kurz verzögert, damit schnelle Klicks nicht jedes Mal ffmpeg starten.</summary>
    private async void RequestEstimate()
    {
        _estimateCts?.Cancel();
        var format = SelectedFormat;
        if (format == null || IsConverting) return;

        var cts = new CancellationTokenSource();
        _estimateCts = cts;
        var clips = Clips.Where(c => c.IsReady).ToList();
        if (clips.Count == 0)
        {
            EstimateText = Clips.Count == 0 ? "Noch keine Dateien." : "Dateien werden gelesen …";
            Verdict = Verdict.None;
            VerdictText = "";
            HintText = null;
            return;
        }

        EstimateText = "Größe wird berechnet …";
        try
        {
            await Task.Delay(400, cts.Token);
            var quality = Quality;
            var join = JoinFiles;
            var estimate = await Task.Run(() => SizeEstimator.EstimateAsync(clips, format, quality, join, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            ShowEstimate(estimate, format, quality);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Write("Schätzung fehlgeschlagen: " + ex);
            if (!cts.IsCancellationRequested)
            {
                EstimateText = "Größe konnte nicht geschätzt werden.";
                Verdict = Verdict.None;
                VerdictText = "";
            }
        }
    }

    private void ShowEstimate(SizeEstimate? estimate, OutputFormat format, QualityLevel quality)
    {
        if (estimate == null)
        {
            EstimateText = format.AudioOnly ? "Keine der Dateien hat Ton." : "Keine der Dateien hat ein Bild.";
            Verdict = Verdict.None;
            VerdictText = "";
            HintText = null;
            return;
        }

        _lastEstimateBytes = estimate.Bytes;
        EstimateText = $"ca. {Format.Size(estimate.Bytes)}   (Original: {Format.Size(estimate.SourceBytes)})";
        HintText = null;

        if (estimate.Lossless)
        {
            Verdict = Verdict.Lossless;
            VerdictText = "Wie Original – verlustfrei, nur umgepackt. Geht in Sekunden.";
            return;
        }

        if (quality == QualityLevel.Original && estimate.NoCopyReason != null)
            HintText = "Verlustfrei geht hier nicht: " + estimate.NoCopyReason + " Es wird in höchster Qualität umgewandelt.";

        var lossless = format.Audio is AudioEncoder.Flac or AudioEncoder.Pcm && format.AudioOnly;
        if (lossless)
        {
            Verdict = Verdict.Lossless;
            VerdictText = "Ton verlustfrei gespeichert.";
        }
        else if (estimate.ResolutionReduced)
        {
            Verdict = Verdict.Visible;
            VerdictText = "Kleinere Auflösung als das Original.";
        }
        else
        {
            (Verdict, VerdictText) = quality switch
            {
                QualityLevel.Original or QualityLevel.Hoch => (Verdict.NearlySame, "Kaum sichtbarer Unterschied zum Original."),
                QualityLevel.Mittel => (Verdict.NearlySame, "Leichter Unterschied, nur bei genauem Hinsehen."),
                _ => (Verdict.Visible, "Sichtbar schlechter, dafür klein."),
            };
            if (format.AudioOnly && quality == QualityLevel.Klein) VerdictText = "Hörbar schlechter, dafür klein.";
        }

        if (estimate.Bytes > estimate.SourceBytes * 1.1 && estimate.SourceBytes > 0)
        {
            const string bigger = "Achtung: Die Datei wird größer als das Original. Tipp: „Mittel“ oder „Klein“ wählen, oder MKV.";
            HintText = HintText == null ? bigger : HintText + Environment.NewLine + bigger;
        }
    }

    // ---------- Umwandeln ----------

    [RelayCommand]
    private async Task ConvertAsync()
    {
        var format = SelectedFormat;
        if (format == null || IsConverting) return;
        if (!FfmpegTools.IsAvailable)
        {
            ShowResult("FFmpeg wurde nicht gefunden. Bitte FFmpeg installieren.", isError: true);
            return;
        }

        _estimateCts?.Cancel();
        ConversionPlan plan;
        try
        {
            plan = CommandBuilder.Build(Clips.ToList(), format, Quality, JoinFiles, OutputFolder,
                IsOutputNameEditable ? OutputName : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowResult("Der Zielordner kann nicht benutzt werden: " + ex.Message, isError: true);
            return;
        }

        if (plan.Steps.Count == 0)
        {
            ShowResult(string.Join(Environment.NewLine, plan.Problems.DefaultIfEmpty("Nichts zu tun.")), isError: true);
            return;
        }

        var estimate = _lastEstimateBytes;
        _convertCts = new CancellationTokenSource();
        IsConverting = true;
        ResultText = null;
        Progress = 0;
        ProgressText = "Starte …";
        TaskbarProgress = 0;
        TaskbarState = TaskbarItemProgressState.Normal;
        var progress = new Progress<ConversionProgress>(p =>
        {
            Progress = p.Fraction * 100;
            ProgressText = $"{p.Fraction:P0} – {p.Title}";
            TaskbarProgress = p.Fraction;
        });

        var failed = false;
        try
        {
            var result = await ConversionService.RunAsync(plan, progress, _convertCts.Token);
            ReportResult(result, plan, estimate);
            failed = !result.Cancelled && (result.Failures.Count > 0 || result.Outputs.Count == 0);
        }
        catch (Exception ex)
        {
            Logger.Write("Umwandeln fehlgeschlagen: " + ex);
            ShowResult("Beim Umwandeln ist ein Fehler aufgetreten: " + ex.Message, isError: true);
            failed = true;
        }
        finally
        {
            IsConverting = false;
            _convertCts.Dispose();
            _convertCts = null;
            FinishTaskbar(failed);
            RequestEstimate();
        }
    }

    /// <summary>Nach dem Umwandeln verschwindet der Balken; bei einem Fehler leuchtet er kurz rot.</summary>
    private async void FinishTaskbar(bool failed)
    {
        if (!failed)
        {
            TaskbarState = TaskbarItemProgressState.None;
            return;
        }
        TaskbarProgress = 1;
        TaskbarState = TaskbarItemProgressState.Error;
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (!IsConverting) TaskbarState = TaskbarItemProgressState.None;
    }

    [RelayCommand]
    private void Cancel() => _convertCts?.Cancel();

    private void ReportResult(ConversionResult result, ConversionPlan plan, long estimate)
    {
        if (result.Cancelled)
        {
            ShowResult("Abgebrochen. Halbfertige Dateien wurden gelöscht.", isError: true);
            return;
        }

        var lines = new List<string>();
        if (result.Outputs.Count > 0)
        {
            var actual = result.Outputs.Sum(f => new FileInfo(f).Length);
            lines.Add(result.Outputs.Count == 1
                ? $"Fertig: {Path.GetFileName(result.Outputs[0])} – {Format.Size(actual)}"
                : $"Fertig: {result.Outputs.Count} Dateien – zusammen {Format.Size(actual)}");
            if (estimate > 0 && result.Failures.Count == 0)
                lines.Add($"Geschätzt waren ca. {Format.Size(estimate)}.");
        }
        lines.AddRange(plan.Problems);
        lines.AddRange(result.Failures.Select(f => $"Fehler bei „{f.Title}“: {f.Message}"));
        ShowResult(string.Join(Environment.NewLine, lines), isError: result.Failures.Count > 0 || result.Outputs.Count == 0);
    }

    private void ShowResult(string text, bool isError)
    {
        ResultText = text;
        ResultIsError = isError;
    }

    private static string Pattern(IEnumerable<string> extensions) => string.Join(";", extensions.Select(e => "*" + e));
}
