using System.Text.Json.Serialization;

namespace DotnetManager.RazorLib;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
