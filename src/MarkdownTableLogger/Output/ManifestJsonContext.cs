using System.Text.Json.Serialization;

namespace MarkdownTableLogger.Output;

[JsonSerializable(typeof(LogManifest))]
[JsonSerializable(typeof(LogManifestEntry))]
[JsonSerializable(typeof(LogManifestEntry[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
internal partial class ManifestJsonContext : JsonSerializerContext
{
}
