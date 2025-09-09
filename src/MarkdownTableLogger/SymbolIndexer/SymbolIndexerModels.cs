using System.Collections.Generic;

namespace MarkdownTableLogger.SymbolIndexer;

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