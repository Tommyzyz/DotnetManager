using System.Globalization;
using System.Resources;
using DotnetManager.RazorLib.Features.Common;

namespace DotnetManager.RazorLib.Resources;

/// <summary>
/// Resolves UI strings against an explicitly supplied culture.
/// <para>
/// This deliberately does not use <c>IStringLocalizer</c>, which reads the ambient
/// <see cref="CultureInfo.CurrentUICulture"/>. That value is scoped to the execution context, so setting it
/// inside a language-switch handler only affects the render happening in that same flow: pages rendered by a
/// later navigation run in a different flow and would keep the old language until the app is restarted.
/// </para>
/// </summary>
public sealed class UiLocalizer
{
    private static readonly ResourceManager ResourceManager =
        new("DotnetManager.RazorLib.Resources.Strings", typeof(UiLocalizer).Assembly);

    private readonly LocalizationState _localization;

    public UiLocalizer(LocalizationState localization) => _localization = localization;

    public CultureInfo Culture => _localization.Culture;

    public string this[string name] => ResourceManager.GetString(name, Culture) ?? name;

    public string this[string name, params object[] arguments] =>
        string.Format(Culture, ResourceManager.GetString(name, Culture) ?? name, arguments);
}
