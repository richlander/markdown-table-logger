using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownTableLogger.SymbolIndexer;

public static class SymbolProtocol
{
    public const int ProtocolVersion = 1;

    public static string ReadLengthPrefixedString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return new string(reader.ReadChars(length));
    }

    public static void WriteLengthPrefixedString(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(value.ToCharArray());
    }

    public static async Task WriteRequestAsync(Stream stream, SymbolQueryRequest request, CancellationToken cancellationToken)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var json = JsonSerializer.Serialize(request, JsonSourceGenerationContext.Default.SymbolQueryRequest);
        WriteLengthPrefixedString(writer, json);
        await writer.BaseStream.FlushAsync(cancellationToken);
    }

    public static Task<SymbolQueryResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var json = ReadLengthPrefixedString(reader);
        var response = JsonSerializer.Deserialize(json, JsonSourceGenerationContext.Default.SymbolQueryResponse);
        return Task.FromResult(response ?? throw new InvalidOperationException("Failed to deserialize response"));
    }
}