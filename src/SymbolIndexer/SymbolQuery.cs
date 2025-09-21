using System.Collections.Generic;

namespace SymbolIndexer;

public record SymbolQuery(string File, int Line, int Column);

public record SymbolLocation(string File, int Line, int Column);

public record SymbolResult(string Name, SymbolLocation? Location, SymbolType Type = SymbolType.Unknown, string? AssemblyInfo = null);

public enum SymbolType
{
    Unknown,
    UserDefined,     // Defined in current codebase
    Framework,       // .NET framework types (System.*, etc.)
    External         // Third-party packages
}

public record SymbolQueryRequest(int Version, string Type, string File, int Line, int Column);

public record SymbolQueryResponse(List<SymbolResult> Symbols, string? Error = null);

public record IndexedSymbol(
    string Name,
    SymbolLocation Location,
    SymbolType Type,
    string? AssemblyInfo,
    string? Namespace,
    Microsoft.CodeAnalysis.SymbolKind Kind,
    string? ContainingType,
    string? Signature);

public static class IndexedSymbolExtensions
{
    public static SymbolResult ToSymbolResult(this IndexedSymbol indexed)
    {
        return new SymbolResult(indexed.Name, indexed.Location, indexed.Type, indexed.AssemblyInfo);
    }
}