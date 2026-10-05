using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DotnetManager;

/// <summary>
/// Colors the native window caption to match the app chrome (--surface / --text in wwwroot/css/app.css).
/// The web UI resolves its theme through prefers-color-scheme, so the caption follows the same
/// "apps" light/dark preference and must be re-applied when that preference changes.
/// </summary>
public static class TitleBarTheme
{
    private const uint DwmwaUseImmersiveDarkMode = 20;
    private const uint DwmwaCaptionColor = 35;
    private const uint DwmwaTextColor = 36;

    // COLORREF layout is 0x00BBGGRR.
    private const int CaptionLight = 0x00FFFFFF; // --surface: #ffffff
    private const int CaptionDark = 0x00281D16;  // --surface: #161d28
    private const int TextLight = 0x0030241C;    // --text: #1c2430
    private const int TextDark = 0x00F1E9E3;     // --text: #e3e9f1

    // All parameters are blittable, so the built-in marshaller is trim/AOT safe here.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd, uint attribute, ref int value, uint valueSize);

    public static void Apply(nint hwnd)
    {
        bool light = IsAppLightTheme();

        // Windows 10 ignores the caption color attributes below; immersive dark mode is its fallback.
        int darkMode = light ? 0 : 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, (uint)sizeof(int));

        int caption = light ? CaptionLight : CaptionDark;
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, (uint)sizeof(int));

        int text = light ? TextLight : TextDark;
        _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, (uint)sizeof(int));
    }

    public static bool IsAppLightTheme()
    {
        using var personalize = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return personalize?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
