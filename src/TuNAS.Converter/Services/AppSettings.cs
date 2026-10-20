using System.IO;
using System.Text.Json;

namespace TuNAS.Converter.Services;

/// <summary>Einstellungen und Protokoll liegen unter %AppData%\TuNAS Converter.</summary>
public sealed class AppSettings
{
    /// <summary>Mit der Umgebungsvariable TUNAS_APPDATA lässt sich ein anderer Ordner wählen (für Tests).</summary>
    public static string Folder { get; } =
        Environment.GetEnvironmentVariable("TUNAS_APPDATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuNAS Converter");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Eine gemeinsame Instanz für das ganze Programm, damit sich Fenster nicht gegenseitig Werte überschreiben.</summary>
    public static AppSettings Current { get; } = Load();

    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "TuNAS Converter");

    /// <summary>Lautstärke im Schneidefenster, 0–100.</summary>
    public int PlayerVolume { get; set; } = 80;

    public bool PlayerMuted { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Logger.Write("Einstellungen konnten nicht gelesen werden: " + ex.Message);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Write("Einstellungen konnten nicht gespeichert werden: " + ex.Message);
        }
    }
}

public static class Logger
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                File.AppendAllText(Path.Combine(AppSettings.Folder, "protokoll.txt"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
