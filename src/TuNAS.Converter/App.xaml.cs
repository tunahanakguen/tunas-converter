using System.Windows;
using System.Windows.Threading;
using TuNAS.Converter.Services;

namespace TuNAS.Converter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Unerwartete Fehler protokollieren und melden, statt das Programm zu beenden.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Write("Hintergrundfehler: " + args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Logger.Write("Schwerer Fehler: " + args.ExceptionObject);

        base.OnStartup(e);
        LibVLCSharp.Shared.Core.Initialize();
        FfmpegTools.Locate();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Length > 0 && FfmpegTools.IsAvailable) window.AddFiles(e.Args); // z. B. „Öffnen mit“

        if (!FfmpegTools.IsAvailable)
        {
            MessageBox.Show(window,
                "FFmpeg wurde nicht gefunden.\n\nLege ffmpeg.exe und ffprobe.exe in den Unterordner „ffmpeg“ neben dem Programm " +
                "oder installiere FFmpeg (z. B. mit: winget install Gyan.FFmpeg) und starte TuNAS Converter neu.",
                "TUNAS CONVERTER", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Write("Unerwarteter Fehler: " + e.Exception);
        MessageBox.Show(MainWindow!, "Es ist ein unerwarteter Fehler aufgetreten:\n\n" + e.Exception.Message +
                        "\n\nDas Programm läuft weiter. Details stehen im Protokoll unter %AppData%\\TuNAS Converter.",
            "TUNAS CONVERTER", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
