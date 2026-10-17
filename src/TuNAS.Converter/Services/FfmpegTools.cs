using System.Diagnostics;
using System.IO;
using System.Text;

namespace TuNAS.Converter.Services;

/// <summary>Findet ffmpeg.exe und ffprobe.exe: erst im Programmordner (Unterordner "ffmpeg"), dann im PATH.</summary>
public static class FfmpegTools
{
    public static string FfmpegPath { get; private set; } = "";
    public static string FfprobePath { get; private set; } = "";
    public static bool IsAvailable => FfmpegPath.Length > 0 && FfprobePath.Length > 0;

    public static void Locate()
    {
        FfmpegPath = Find("ffmpeg.exe") ?? "";
        FfprobePath = Find("ffprobe.exe") ?? "";
    }

    private static string? Find(string exe)
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg", exe);
        if (File.Exists(local)) return local;

        // Prozess-, Benutzer- und System-PATH: nach einer frischen Installation fehlt der Eintrag oft noch im Prozess.
        var dirs = new[]
            {
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .SelectMany(p => p!.Split(';'))
            .Select(d => d.Trim().Trim('"'))
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(dir), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Ungültiger PATH-Eintrag
            }
        }
        return null;
    }
}

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Startet ffmpeg/ffprobe als eigenen Prozess. Stürzt dieser ab, bleibt das Programm davon unberührt.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken ct,
        Action<string>? onStdOutLine = null,
        bool lowPriority = false)
    {
        using var process = new Process { StartInfo = CreateStartInfo(exe, args) };
        var stdout = new StringBuilder();
        var stderr = new TailBuffer(60);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (onStdOutLine != null) onStdOutLine(e.Data);
            else stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.Add(e.Data);
        };

        process.Start();
        if (lowPriority) TrySetLowPriority(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Für Vorschaubilder: liefert die Ausgabe als Bytes.</summary>
    public static async Task<byte[]> RunForBytesAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        using var process = new Process { StartInfo = CreateStartInfo(exe, args) };
        process.StartInfo.StandardOutputEncoding = null;
        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var buffer = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, ct);
            await process.WaitForExitAsync(ct);
            await stderrTask;
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        return process.ExitCode == 0 ? buffer.ToArray() : Array.Empty<byte>();
    }

    private static ProcessStartInfo CreateStartInfo(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static void TrySetLowPriority(Process process)
    {
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Behält nur die letzten Zeilen der Fehlerausgabe.</summary>
    private sealed class TailBuffer(int maxLines)
    {
        private readonly Queue<string> _lines = new();

        public void Add(string line)
        {
            lock (_lines)
            {
                _lines.Enqueue(line);
                while (_lines.Count > maxLines) _lines.Dequeue();
            }
        }

        public override string ToString()
        {
            lock (_lines) return string.Join(Environment.NewLine, _lines);
        }
    }
}
