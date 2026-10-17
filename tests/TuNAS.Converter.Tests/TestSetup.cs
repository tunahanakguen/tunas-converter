using System.IO;
using System.Runtime.CompilerServices;

namespace TuNAS.Converter.Tests;

internal static class TestSetup
{
    /// <summary>Läuft vor allen Tests: Einstellungen und Protokoll in einen Testordner umleiten,
    /// damit die echten Einstellungen des Nutzers nie verändert werden.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TuNAS-Tests-Settings-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("TUNAS_APPDATA", folder);
    }
}
