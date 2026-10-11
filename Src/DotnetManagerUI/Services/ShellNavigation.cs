using DotnetManager.RazorLib.Abstractions;

namespace DotnetManagerUI.Services;

/// <summary>
/// The native shell raises navigation requests through this sink; the bridge component inside the
/// Blazor tree (<c>ShellNavigator</c>) turns them into real route changes with the NavigationManager
/// the router listens to.
/// </summary>
public sealed class ShellNavigation : IShellNavigation
{
    public event Action<string>? Requested;

    public void Request(string route)
    {
        if (!string.IsNullOrWhiteSpace(route))
        {
            Requested?.Invoke(route);
        }
    }
}
