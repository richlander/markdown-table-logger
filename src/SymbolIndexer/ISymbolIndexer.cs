using System.Collections.Generic;
using System.Threading.Tasks;

namespace SymbolIndexer;

public interface ISymbolIndexer
{
    Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column);
    Task RebuildIndexAsync(string projectPath);
    Task UpdateFileAsync(string filePath);
}