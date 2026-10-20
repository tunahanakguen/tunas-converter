using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using TuNAS.Converter.Models;
using TuNAS.Converter.Services;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace TuNAS.Converter;

/// <summary>
/// Schneidefenster: Anfang markieren, Ende markieren, „Herausschneiden“ –
/// die Stelle verschwindet sofort. Zeitleiste, Zeitanzeige und Abspielen zeigen danach das geschnittene Video;
/// entfernte Stellen werden beim Abspielen übersprungen.
///
/// Intern gibt es zwei Zeiten: die Zeit im Originalvideo (für den Player) und die Zeit im
/// geschnittenen Video (für alles, was man sieht). <see cref="Segments.ToEdited"/> rechnet um.
/// </summary>
public partial class CutWindow : Window
{
    private readonly ClipItem _clip;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _frame;
    private readonly Stack<List<TimeRange>> _undo = new();
    private readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    private List<TimeRange> _removed;
    private List<TimeRange> _kept = new();
    private TimeSpan _keptTotal;

    // Markierungen und aktuelle Position als Zeit im Originalvideo.
    private TimeSpan? _markStart;
    private TimeSpan? _markEnd;
    private TimeSpan _current;

    private LibVLC? _libVlc;
    private MediaPlayer? _player;
    private bool _dragging;
    private bool _settingSlider;
    private bool _ended;
    private bool _startingUp;

    // Lautstärke 0–100 und Stumm; wird in den Einstellungen gemerkt.
    private int _volume = Math.Clamp(AppSettings.Current.PlayerVolume, 0, 100);
    private bool _muted = AppSettings.Current.PlayerMuted;
    private bool _restoringVolume;

    public CutWindow(ClipItem clip)
    {
        InitializeComponent();
        DarkTitleBar.Enable(this);
        _clip = clip;
        _duration = clip.Info!.Duration;
        _frame = TimeSpan.FromSeconds(1.0 / (clip.Info.Fps > 0 ? clip.Info.Fps : 25));
        _removed = clip.RemovedRanges.ToList();

        Title = "Schneiden – " + clip.FileName;
        Position.SmallChange = _frame.TotalSeconds;
        Position.LargeChange = 5;
        _playTimer.Tick += (_, _) => PollPlayer();
        _restoringVolume = true; // gemerkte Werte setzen, ohne „lauter drehen hebt Stumm auf“ auszulösen
        VolumeSlider.Value = _volume;
        _restoringVolume = false;
        UpdateVolumeDisplay();

        Loaded += OnLoaded;
        Closed += OnClosed;
        RecalculateTimeline();
    }

    public IReadOnlyList<TimeRange> RemovedRanges => _removed;

    private TimeSpan EditedCurrent => Segments.ToEdited(_kept, _current);

    // ---------- Player ----------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _libVlc = new LibVLC("--no-video-title-show", "--no-sub-autodetect-file");
            _player = new MediaPlayer(_libVlc) { EnableKeyInput = false, EnableMouseInput = false };
            Video.MediaPlayer = _player;
            _player.TimeChanged += OnPlayerTimeChanged;
            _player.Playing += OnPlayerPlaying;
            _player.Paused += OnPlayerPaused;
            _player.EndReached += OnPlayerEndReached;

            // Angehalten gestartet zeigt VLC kein Bild. Darum kurz stumm abspielen und beim ersten Bild anhalten.
            using var media = new Media(_libVlc, _clip.FilePath, FromType.FromPath);
            _startingUp = true;
            _player.Play(media);
            _player.Mute = true;
        }
        catch (Exception ex)
        {
            Logger.Write("Player konnte nicht starten: " + ex);
            SelectionText.Text = "Die Vorschau kann nicht angezeigt werden. Schneiden über die Zeitleiste geht trotzdem.";
        }
        Redraw();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _playTimer.Stop();
        AppSettings.Current.PlayerVolume = _volume;
        AppSettings.Current.PlayerMuted = _muted;
        AppSettings.Current.Save();
        var player = _player;
        var libVlc = _libVlc;
        _player = null;
        _libVlc = null;
        if (player == null) return;

        player.TimeChanged -= OnPlayerTimeChanged;
        player.Playing -= OnPlayerPlaying;
        player.Paused -= OnPlayerPaused;
        player.EndReached -= OnPlayerEndReached;
        Video.MediaPlayer = null;

        // Stop kann kurz blockieren – nicht im Fenster-Thread.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            player.Stop();
            player.Dispose();
            libVlc?.Dispose();
        });
    }

    // ---------- Lautstärke ----------

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _volume = (int)Math.Round(e.NewValue);
        if (!_restoringVolume && _volume > 0 && _muted && e.OldValue < e.NewValue) _muted = false; // lauter drehen hebt Stumm auf
        ApplyVolume();
        UpdateVolumeDisplay();
    }

    private void OnMuteClick(object sender, RoutedEventArgs e) => ToggleMute();

    private void ToggleMute()
    {
        _muted = !_muted;
        ApplyVolume();
        UpdateVolumeDisplay();
    }

    private void ChangeVolume(int delta) => VolumeSlider.Value = Math.Clamp(_volume + delta, 0, 100);

    private void ApplyVolume()
    {
        if (_player == null || _startingUp) return;
        _player.Volume = _volume;
        _player.Mute = _muted || _volume == 0;
    }

    private void UpdateVolumeDisplay()
    {
        var silent = _muted || _volume == 0;
        MuteButton.Content = silent ? "" : _volume < 34 ? "" : _volume < 67 ? "" : "";
        VolumeText.Text = silent ? "stumm" : $"{_volume} %";
    }

    // Ereignisse von VLC kommen aus einem eigenen Thread: nur weiterreichen, nie den Player direkt darin steuern.
    private void OnPlayerTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => OnSourceTime(TimeSpan.FromMilliseconds(e.Time)));

    private void OnPlayerPlaying(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_startingUp && _player != null)
            {
                _startingUp = false;
                _player.SetPause(true);
                ApplyVolume();
                SeekToSource(_kept.Count > 0 ? _kept[0].Start : TimeSpan.Zero);
                return;
            }
            ApplyVolume(); // VLC setzt die Lautstärke beim Start der Tonausgabe manchmal zurück
            PlayButton.Content = "";
            _playTimer.Start();
        });

    private void OnPlayerPaused(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            PlayButton.Content = "";
            _playTimer.Stop();
        });

    private void OnPlayerEndReached(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _ended = true;
            _playTimer.Stop();
            PlayButton.Content = "";
            SetCurrent(_kept.Count > 0 ? _kept[^1].End : _duration);
        });

    /// <summary>Während des Abspielens öfter nachsehen, damit entfernte Stellen sauber übersprungen werden.</summary>
    private void PollPlayer()
    {
        if (_player is { IsPlaying: true }) OnSourceTime(TimeSpan.FromMilliseconds(_player.Time));
    }

    /// <summary>Der Player meldet eine Zeit im Originalvideo. Liegt sie in einer entfernten Stelle: überspringen.</summary>
    private void OnSourceTime(TimeSpan source)
    {
        if (_player == null) return;
        if (_player.IsPlaying && _kept.Count > 0)
        {
            var next = _kept.FirstOrDefault(s => source < s.End - TimeSpan.FromMilliseconds(5));
            if (next == default)
            {
                // Hinter dem letzten behaltenen Stück: anhalten.
                _player.SetPause(true);
                SetCurrent(_kept[^1].End);
                return;
            }
            if (source < next.Start)
            {
                _player.Time = (long)next.Start.TotalMilliseconds;
                SetCurrent(next.Start);
                return;
            }
        }
        if (!_dragging) SetCurrent(source);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlay();

    private void TogglePlay()
    {
        if (_player == null) return;
        if (_ended || EditedCurrent >= _keptTotal - TimeSpan.FromMilliseconds(50))
        {
            // Am Ende: von vorne abspielen.
            Restart();
            SeekToEdited(TimeSpan.Zero);
            _player.SetPause(false);
            return;
        }
        _player.SetPause(_player.IsPlaying);
    }

    private void Restart()
    {
        if (_player == null || !_ended) return;
        _ended = false;
        _player.Stop();
        _player.Play();
    }

    private void OnStepForward(object sender, RoutedEventArgs e) => StepFrames(1);

    private void OnStepBack(object sender, RoutedEventArgs e) => StepFrames(-1);

    private void StepFrames(int frames)
    {
        if (_player == null) return;
        if (_player.IsPlaying) _player.SetPause(true);
        SeekToEdited(EditedCurrent + TimeSpan.FromTicks(_frame.Ticks * frames));
    }

    private void SeekToEdited(TimeSpan edited)
    {
        if (edited < TimeSpan.Zero) edited = TimeSpan.Zero;
        if (edited > _keptTotal) edited = _keptTotal;
        SeekToSource(Segments.ToSource(_kept, edited));
    }

    private void SeekToSource(TimeSpan source)
    {
        if (_player != null)
        {
            if (_ended)
            {
                Restart();
                _player.SetPause(true);
            }
            _player.Time = (long)source.TotalMilliseconds;
        }
        SetCurrent(source);
    }

    private void SetCurrent(TimeSpan source)
    {
        _current = source < TimeSpan.Zero ? TimeSpan.Zero : (source > _duration ? _duration : source);
        var edited = EditedCurrent;
        _settingSlider = true;
        Position.Value = edited.TotalSeconds;
        _settingSlider = false;
        TimeText.Text = _removed.Count == 0
            ? $"{Format.TimePrecise(edited)} / {Format.TimePrecise(_keptTotal)}"
            : $"{Format.TimePrecise(edited)} / {Format.TimePrecise(_keptTotal)}   (Original {Format.TimePrecise(_duration)})";
        if (_markStart != null && _markEnd == null) Redraw();
    }

    // ---------- Zeitleiste (zeigt das geschnittene Video) ----------

    private void RecalculateTimeline()
    {
        _kept = Segments.Kept(_duration, _removed);
        _keptTotal = Segments.Total(_kept);
        Position.Maximum = Math.Max(0.1, _keptTotal.TotalSeconds);
        SetCurrent(_current);
        UpdateTexts();
        Redraw();
    }

    private void OnPositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_settingSlider) return;
        SeekToEdited(TimeSpan.FromSeconds(e.NewValue));
    }

    private void OnDragStarted(object sender, DragStartedEventArgs e) => _dragging = true;

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _dragging = false;
        SeekToEdited(TimeSpan.FromSeconds(Position.Value));
    }

    private void OnRangesSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        Ranges.Children.Clear();
        var width = Ranges.ActualWidth;
        if (width <= 0 || _keptTotal <= TimeSpan.Zero) return;

        double X(TimeSpan edited) => edited.TotalSeconds / _keptTotal.TotalSeconds * width;

        // Rote Striche: hier wurde etwas herausgeschnitten.
        foreach (var point in Segments.CutPoints(_kept, _duration))
            AddBar(X(point) - 1.5, 3, (Brush)FindResource("Danger"));

        // Blau: aktuelle Auswahl.
        if (_markStart != null)
        {
            var a = Segments.ToEdited(_kept, _markStart.Value);
            var b = _markEnd != null ? Segments.ToEdited(_kept, _markEnd.Value) : EditedCurrent;
            if (b < a) (a, b) = (b, a);
            AddBar(X(a), Math.Max(2, X(b) - X(a)), (Brush)FindResource("SelectionBlue"));
            AddBar(X(Segments.ToEdited(_kept, _markStart.Value)) - 1, 2, (Brush)FindResource("Accent"));
            if (_markEnd != null) AddBar(X(Segments.ToEdited(_kept, _markEnd.Value)) - 1, 2, (Brush)FindResource("Accent"));
        }
    }

    private void AddBar(double x, double width, Brush brush)
    {
        var rect = new Rectangle { Width = width, Height = Ranges.ActualHeight, Fill = brush };
        Canvas.SetLeft(rect, x);
        Ranges.Children.Add(rect);
    }

    // ---------- Schneiden ----------

    private void OnMarkStart(object sender, RoutedEventArgs e) => MarkStart();

    private void OnMarkEnd(object sender, RoutedEventArgs e) => MarkEnd();

    private void MarkStart()
    {
        _markStart = _current;
        if (_markEnd != null && _markEnd <= _markStart) _markEnd = null;
        UpdateTexts();
        Redraw();
    }

    private void MarkEnd()
    {
        _markEnd = _current;
        _markStart ??= _kept.Count > 0 ? _kept[0].Start : TimeSpan.Zero;
        UpdateTexts();
        Redraw();
    }

    private void OnCut(object sender, RoutedEventArgs e) => Cut();

    private void Cut()
    {
        if (_markStart == null || _markEnd == null)
        {
            SelectionText.Text = "Bitte zuerst Anfang und Ende markieren.";
            return;
        }
        var start = _markStart.Value <= _markEnd.Value ? _markStart.Value : _markEnd.Value;
        var end = _markStart.Value <= _markEnd.Value ? _markEnd.Value : _markStart.Value;
        if (end - start < Segments.MinLength)
        {
            SelectionText.Text = "Die Markierung ist zu kurz.";
            return;
        }

        var after = Segments.Normalize(_removed.Append(new TimeRange(start, end)), _duration);
        if (Segments.Kept(_duration, after).Count == 0)
        {
            SelectionText.Text = "Es muss etwas vom Video übrig bleiben.";
            return;
        }

        var cutAtEdited = Segments.ToEdited(_kept, start);
        _undo.Push(_removed);
        _removed = after;
        _markStart = null;
        _markEnd = null;
        RecalculateTimeline();
        if (_player?.IsPlaying == true) _player.SetPause(true);
        SeekToEdited(cutAtEdited); // direkt an die Schnittstelle springen
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void Undo()
    {
        if (_undo.Count == 0) return;
        var source = _current;
        _removed = _undo.Pop();
        RecalculateTimeline();
        SeekToSource(source);
    }

    private void OnResetAll(object sender, RoutedEventArgs e)
    {
        if (_removed.Count == 0) return;
        var source = _current;
        _undo.Push(_removed);
        _removed = new List<TimeRange>();
        _markStart = null;
        _markEnd = null;
        RecalculateTimeline();
        SeekToSource(source);
    }

    private void UpdateTexts()
    {
        if (_markStart != null && _markEnd != null)
            SelectionText.Text = $"Markiert: {Format.TimePrecise(Segments.ToEdited(_kept, _markStart.Value))} – "
                               + $"{Format.TimePrecise(Segments.ToEdited(_kept, _markEnd.Value))}. Jetzt „Herausschneiden“ klicken.";
        else if (_markStart != null)
            SelectionText.Text = $"Anfang bei {Format.TimePrecise(Segments.ToEdited(_kept, _markStart.Value))}. Jetzt zum Ende gehen und „Ende“ klicken.";
        else
            SelectionText.Text = "Anfang und Ende markieren, dann „Herausschneiden“ klicken.";

        SummaryText.Text = _removed.Count == 0
            ? $"Länge: {Format.Time(_duration)}"
            : $"Neue Länge: {Format.Time(_keptTotal)}  (vorher {Format.Time(_duration)}, "
              + $"{_removed.Count} {(_removed.Count == 1 ? "Stelle" : "Stellen")} herausgeschnitten)";
    }

    // ---------- Tastatur ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                break;
            case Key.Left:
                if (shift) SeekToEdited(EditedCurrent - TimeSpan.FromSeconds(5)); else StepFrames(-1);
                break;
            case Key.Right:
                if (shift) SeekToEdited(EditedCurrent + TimeSpan.FromSeconds(5)); else StepFrames(1);
                break;
            case Key.I:
                MarkStart();
                break;
            case Key.O:
                MarkEnd();
                break;
            case Key.Delete:
                Cut();
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.M:
                ToggleMute();
                break;
            case Key.Up:
                ChangeVolume(+5);
                break;
            case Key.Down:
                ChangeVolume(-5);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
