using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

// MAUI is globally imported by the MAUI SDK, so the WinUI types have to be pinned explicitly.
using Application = Microsoft.UI.Xaml.Application;
using Color = Windows.UI.Color;
using Window = Microsoft.UI.Xaml.Window;

namespace DotnetManagerUI.Services;

/// <summary>
/// Colors the window caption with the native WinUI title bar API so it matches the web chrome
/// (--surface / --text in wwwroot/css/app.css).
/// <para>
/// The web UI resolves its theme from <c>prefers-color-scheme</c>, and the shell's
/// <see cref="FrameworkElement.ActualTheme"/> tracks the same "apps" light/dark preference, so the
/// caption follows the app theme and is repainted whenever that preference changes.
/// </para>
/// <para>
/// The class is named <c>CaptionTheme</c> rather than <c>TitleBarTheme</c> because Windows App SDK
/// already has a <see cref="Microsoft.UI.Windowing.TitleBarTheme"/> type.
/// </para>
/// </summary>
public static class CaptionTheme
{
    // The old host used COLORREF (0x00BBGGRR); these are the same values expressed as ARGB.
    private static readonly Color CaptionLight = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF); // --surface: #ffffff
    private static readonly Color CaptionDark = Color.FromArgb(0xFF, 0x16, 0x1D, 0x28);  // --surface: #161d28
    private static readonly Color TextLight = Color.FromArgb(0xFF, 0x1C, 0x24, 0x30);    // --text: #1c2430
    private static readonly Color TextDark = Color.FromArgb(0xFF, 0xE3, 0xE9, 0xF1);     // --text: #e3e9f1
    private static readonly Color MutedLight = Color.FromArgb(0xFF, 0x93, 0xA0, 0xB1);   // --text-faint: #93a0b1
    private static readonly Color MutedDark = Color.FromArgb(0xFF, 0x6C, 0x7A, 0x8C);    // --text-faint: #6c7a8c
    private static readonly Color HoverLight = Color.FromArgb(0xFF, 0xE9, 0xED, 0xF2);
    private static readonly Color HoverDark = Color.FromArgb(0xFF, 0x22, 0x2E, 0x40);
    private static readonly Color PressedLight = Color.FromArgb(0xFF, 0xDC, 0xE3, 0xEB);
    private static readonly Color PressedDark = Color.FromArgb(0xFF, 0x2B, 0x39, 0x50);
    private static readonly Color TransparentColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);

    /// <summary>Paints the caption now and keeps it in sync when the app theme flips.</summary>
    public static void Attach(Window window)
    {
        Apply(window);

        if (window.Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => Apply(window);
        }
    }

    public static void Apply(Window window)
    {
        var appWindow = window.AppWindow;
        if (appWindow is null)
        {
            return;
        }

        var titleBar = appWindow.TitleBar;
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            // Windows 10 keeps the system caption; the colors below are ignored there anyway.
            return;
        }

        var light = IsLightTheme(window);
        var caption = light ? CaptionLight : CaptionDark;
        var text = light ? TextLight : TextDark;
        var muted = light ? MutedLight : MutedDark;

        titleBar.BackgroundColor = caption;
        titleBar.ForegroundColor = text;
        titleBar.InactiveBackgroundColor = caption;
        titleBar.InactiveForegroundColor = muted;

        titleBar.ButtonBackgroundColor = TransparentColor;
        titleBar.ButtonInactiveBackgroundColor = TransparentColor;
        titleBar.ButtonHoverBackgroundColor = light ? HoverLight : HoverDark;
        titleBar.ButtonPressedBackgroundColor = light ? PressedLight : PressedDark;

        titleBar.ButtonForegroundColor = text;
        titleBar.ButtonHoverForegroundColor = text;
        titleBar.ButtonPressedForegroundColor = text;
        titleBar.ButtonInactiveForegroundColor = muted;
    }

    /// <summary>The theme the shell is actually rendering with.</summary>
    public static bool IsLightTheme(Window window) =>
        (window.Content as FrameworkElement)?.ActualTheme switch
        {
            ElementTheme.Dark => false,
            ElementTheme.Light => true,
            _ => Application.Current?.RequestedTheme != ApplicationTheme.Dark,
        };
}
