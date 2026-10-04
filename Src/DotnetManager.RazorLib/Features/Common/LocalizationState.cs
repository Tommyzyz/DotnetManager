using System.Globalization;

namespace DotnetManager.RazorLib.Features.Common;

/// <summary>Holds the selected UI language and notifies the shell so the whole tree re-renders.</summary>
public sealed class LocalizationState
{
    public const string DefaultLanguage = "zh-Hans";

    private static readonly string[] Codes = [DefaultLanguage, "en"];

    public static IReadOnlyList<string> SupportedLanguages => Codes;

    public string Language { get; private set; } = DefaultLanguage;

    /// <summary>The culture to resolve UI strings with. Never rely on the ambient thread culture for this.</summary>
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);

    public event Action? Changed;

    public void Initialize(string? language) => Apply(Normalize(language), notify: false);

    public void SetLanguage(string language) => Apply(Normalize(language), notify: true);

    public static string DisplayName(string code) => code switch
    {
        "zh-Hans" => "简体中文",
        "en" => "English",
        _ => code,
    };

    private static string Normalize(string? language) =>
        !string.IsNullOrWhiteSpace(language) && Codes.Contains(language, StringComparer.OrdinalIgnoreCase)
            ? Codes.First(c => string.Equals(c, language, StringComparison.OrdinalIgnoreCase))
            : DefaultLanguage;

    private void Apply(string language, bool notify)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        Language = language;
        Culture = culture;

        // Culture resolution for UI strings goes through LocalizationState.Culture (see UiLocalizer).
        // These assignments only cover formatting of dates and numbers.
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (notify)
        {
            Changed?.Invoke();
        }
    }
}
