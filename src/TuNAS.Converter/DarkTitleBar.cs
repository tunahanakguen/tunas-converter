using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TuNAS.Converter;

/// <summary>Dunkle Windows-Titelleiste passend zum dunklen Design (Windows 10 ab 20H1 und Windows 11).</summary>
public static class DarkTitleBar
{
    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Enable(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var on = 1;
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref on, sizeof(int));
        };
    }
}
