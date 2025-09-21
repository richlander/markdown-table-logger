using System;

namespace SymbolIndexer;

public class SymbolIndexerOptions
{
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
