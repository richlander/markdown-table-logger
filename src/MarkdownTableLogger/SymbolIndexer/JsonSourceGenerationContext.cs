using System.Text.Json.Serialization;

namespace MarkdownTableLogger.SymbolIndexer;

[JsonSerializable(typeof(SymbolQueryRequest))]
[JsonSerializable(typeof(SymbolQueryResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class JsonSourceGenerationContext : JsonSerializerContext
{
}