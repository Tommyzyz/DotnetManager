namespace DotnetManager.RazorLib.Features.Tools;

/// <summary>
/// The only two places a .NET tool can live, and therefore the only scope switch the tools page needs.
/// <para>
/// <see cref="Local"/> is project scoped: the tool is declared in a <c>.config/dotnet-tools.json</c>
/// manifest below the selected project directory. <see cref="Global"/> is machine (user) scoped: the
/// tool lives in <c>~/.dotnet/tools</c> and is available from every directory.
/// </para>
/// </summary>
public enum ToolScope
{
    Global,

    Local,
}
