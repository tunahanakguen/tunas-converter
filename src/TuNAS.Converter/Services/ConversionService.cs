using System.Globalization;
using System.IO;

namespace TuNAS.Converter.Services;

public sealed record StepFailure(string Title, string Message);

public sealed class ConversionResult
{
    public List<string> Outputs { get; } = new();
    public List<StepFailure> Failures { get; } = new();
    public bool Cancelled { get; set; }
}

public readonly record struct ConversionProgress(double Fraction, string Title);

/// <summary>Führt die Schritte nacheinander aus. Ein fehlgeschlagener Schritt bringt das Programm nicht zum Absturz.</summary>
public static class ConversionService
{
    public static async Task<ConversionResult> RunAsync(ConversionPlan plan, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var result = new ConversionResult();
        var total = Math.Max(0.1, plan.Steps.Sum(s => Math.Max(0.1, s.Duration.TotalSeconds)));
        double done = 0;

        try
        {
            foreach (var step in plan.Steps)
            {
                var stepSeconds = Math.Max(0.1, step.Duration.TotalSeconds);
                var doneBefore = done;
                progress.Report(new ConversionProgress(done / total, step.Title));

                void OnLine(string line)
                {
                    if (!line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
                    if (!long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) || us < 0) return;
                    var current = Math.Min(stepSeconds, us / 1_000_000.0);
                    progress.Report(new ConversionProgress((doneBefore + current) / total, step.Title));
                }

                ProcessResult run;
                var args = step.Args;
                try
                {
                    run = await ProcessRunner.RunAsync(FfmpegTools.FfmpegPath, args, ct, OnLine, lowPriority: true);
                    if (!Succeeded(run, step) && step.FallbackArgs != null)
                    {
                        Logger.Write($"„{step.Title}“: Umpacken fehlgeschlagen, zweiter Versuch mit neu kodiertem Ton.\n{run.StdErr}");
                        TryDelete(step.OutputFile);
                        args = step.FallbackArgs;
                        run = await ProcessRunner.RunAsync(FfmpegTools.FfmpegPath, args, ct, OnLine, lowPriority: true);
                    }
                }
                catch (OperationCanceledException)
                {
                    TryDelete(step.OutputFile);
                    result.Cancelled = true;
                    break;
                }

                if (!Succeeded(run, step))
                {
                    TryDelete(step.OutputFile);
                    result.Failures.Add(new StepFailure(step.Title, Explain(run.StdErr)));
                    Logger.Write($"ffmpeg-Fehler bei „{step.Title}“ (Code {run.ExitCode}):\n{run.StdErr}\nBefehl: {string.Join(" ", args)}");
                    if (plan.StopOnError) break;
                }
                else if (step.IsFinal)
                {
                    result.Outputs.Add(step.OutputFile!);
                }

                done += stepSeconds;
            }
        }
        finally
        {
            TryDeleteDirectory(plan.TempDirectory);
        }

        if (result.Cancelled || (plan.StopOnError && result.Failures.Count > 0))
        {
            foreach (var output in result.Outputs) TryDelete(output);
            result.Outputs.Clear();
        }
        else
        {
            progress.Report(new ConversionProgress(1, "Fertig"));
        }
        return result;
    }

    private static bool Succeeded(ProcessResult run, ConversionStep step) =>
        run.ExitCode == 0 && step.OutputFile != null && File.Exists(step.OutputFile);

    /// <summary>Macht aus ffmpeg-Fehlertext einen verständlichen Satz.</summary>
    private static string Explain(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = string.Join(" ", lines.TakeLast(3));
        if (text.Contains("No space left", StringComparison.OrdinalIgnoreCase)) return "Die Festplatte ist voll.";
        if (text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)) return "Kein Zugriff auf den Zielordner oder die Datei.";
        if (text.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase) || text.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
            return "Dieser Codec fehlt in der installierten FFmpeg-Version.";
        if (text.Contains("Invalid data", StringComparison.OrdinalIgnoreCase)) return "Die Datei ist beschädigt oder unvollständig.";
        return text.Length > 0 ? text : "Unbekannter Fehler beim Umwandeln.";
    }

    private static void TryDelete(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
