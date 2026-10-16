namespace DotnetManager.RazorLib.Abstractions;

/// <summary>
/// The routes the shell exposes. The native navigation (a WinUI NavigationView) and the in-page
/// navigation agree on these names, so the strings live next to the pages they point at.
/// </summary>
public static class ShellRoutes
{
    public const string Overview = "/";

    /// <summary>Both tool scopes live here; the page switches between global and local.</summary>
    public const string Tools = "/tools";

    public const string ProjectPackages = "/nuget/packages";

    public const string PackageSources = "/nuget/sources";

    public const string NuGetCaches = "/nuget/caches";

    public const string NuGetConfigFiles = "/nuget/config";

    public const string Workloads = "/workloads";

    /// <summary>Machine scoped: the SDKs and runtimes installed on this computer.</summary>
    public const string Sdk = "/sdk";

    public const string Output = "/output";

    public const string Settings = "/settings";
}

/// <summary>
/// Lets a native shell ask the Blazor router to change page: the host raises <see cref="Request"/>
/// (from its own menu) and <see cref="Requested"/> is handled inside the Blazor tree, which is the
/// only place that owns the <c>NavigationManager</c> the router listens to.
/// <para>
/// A host that has no native shell simply does not register an implementation, and the bridge
/// component in the layout stays quiet.
/// </para>
/// </summary>
public interface IShellNavigation
{
    event Action<string>? Requested;

    /// <summary>Requests navigation to one of the <see cref="ShellRoutes"/>.</summary>
    void Request(string route);
}
