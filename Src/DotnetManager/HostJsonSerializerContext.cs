using System.Text.Json.Serialization;
using DotnetManager.RazorLib.Abstractions;

namespace DotnetManager.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(HeadlessRequest))]
[JsonSerializable(typeof(HeadlessResult))]
[JsonSerializable(typeof(string[]))]
public partial class HostJsonSerializerContext : JsonSerializerContext
{
}
