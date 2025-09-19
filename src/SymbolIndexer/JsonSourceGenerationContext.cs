using System.Text.Json.Serialization;

namespace SymbolIndexer;

[JsonSerializable(typeof(SymbolQueryRequest))]
[JsonSerializable(typeof(SymbolQueryResponse))]
[JsonSerializable(typeof(DiscoveryInfo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class JsonSourceGenerationContext : JsonSerializerContext
{
}