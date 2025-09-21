using System.Text.Json.Serialization;

namespace SymbolIndexer;

[JsonSerializable(typeof(SymbolQueryRequest))]
[JsonSerializable(typeof(SymbolQueryResponse))]
[JsonSerializable(typeof(WorkspaceInfo))]
[JsonSerializable(typeof(LogManifest))]
[JsonSerializable(typeof(LogManifestEntry[]))]
[JsonSerializable(typeof(LogManifestEntry))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class JsonSourceGenerationContext : JsonSerializerContext
{
}
