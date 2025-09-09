using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace SymbolIndexer;

public class RoslynSymbolIndexer : ISymbolIndexer
{
    private readonly ILogger<RoslynSymbolIndexer> _logger;
    private readonly ConcurrentDictionary<string, Dictionary<string, SymbolLocation>> _symbolIndex = new();
    private readonly object _indexLock = new();

    public RoslynSymbolIndexer(ILogger<RoslynSymbolIndexer> logger)
    {
        _logger = logger;
    }

    public async Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column)
    {
        try
        {
            var results = new List<SymbolResult>();
            
            if (!File.Exists(file))
            {
                return results;
            }

            var sourceText = await File.ReadAllTextAsync(file);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: file);
            var root = await syntaxTree.GetRootAsync();

            // Find the position in the source text
            var lines = sourceText.Split('\n');
            if (line > lines.Length || line < 1)
            {
                return results;
            }

            var lineText = lines[line - 1]; // Convert to 0-based indexing
            var symbolsAtLocation = new HashSet<string>();

            if (column == 0) // Search entire line
            {
                // Find all tokens on the specified line
                var lineSpan = syntaxTree.GetText().Lines[line - 1].Span;
                var tokensOnLine = root.DescendantTokens().Where(t => lineSpan.IntersectsWith(t.Span));
                
                foreach (var token in tokensOnLine)
                {
                    if (token.Parent != null)
                    {
                        var nodeSymbols = ExtractSymbolsFromNode(token.Parent);
                        foreach (var symbol in nodeSymbols)
                        {
                            symbolsAtLocation.Add(symbol);
                        }
                    }
                }
            }
            else // Search specific column
            {
                if (column > lineText.Length || column < 1)
                {
                    return results;
                }

                // Calculate absolute position
                var position = 0;
                for (int i = 0; i < line - 1; i++)
                {
                    position += lines[i].Length + 1; // +1 for newline
                }
                position += column - 1; // Convert to 0-based

                // Find token at position
                var token = root.FindToken(position);
                var node = token.Parent;

                // Extract symbols from the context around this position
                var nodeSymbols = ExtractSymbolsFromNode(node);
                foreach (var symbol in nodeSymbols)
                {
                    symbolsAtLocation.Add(symbol);
                }
            }
            
            // Convert to results with deduplication and type classification
            foreach (var symbolName in symbolsAtLocation)
            {
                var location = FindSymbolInIndex(symbolName);
                var (symbolType, assemblyInfo) = ClassifySymbolTypeWithAssembly(symbolName, location, syntaxTree, root);
                results.Add(new SymbolResult(symbolName, location, symbolType, assemblyInfo));
            }

            return results.OrderBy(r => r.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying symbols at {File}:{Line}:{Column}", file, line, column);
            return [];
        }
    }

    public async Task RebuildIndexAsync(string projectPath)
    {
        try
        {
            _logger.LogInformation("Rebuilding symbol index for {ProjectPath}", projectPath);
            
            lock (_indexLock)
            {
                _symbolIndex.Clear();
            }

            var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("bin") && !f.Contains("obj"))
                .ToArray();

            foreach (var file in csFiles)
            {
                await UpdateFileAsync(file);
            }

            _logger.LogInformation("Rebuilt symbol index with {FileCount} files", csFiles.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rebuilding index for {ProjectPath}", projectPath);
        }
    }

    public async Task UpdateFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath) || !filePath.EndsWith(".cs"))
            {
                return;
            }

            var sourceText = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, path: filePath);
            var root = await syntaxTree.GetRootAsync();

            var symbols = ExtractSymbolsFromFile(root, filePath);

            lock (_indexLock)
            {
                _symbolIndex[filePath] = symbols;
            }

            _logger.LogDebug("Updated symbol index for {FilePath} with {Count} symbols", filePath, symbols.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file index for {FilePath}", filePath);
        }
    }

    private List<string> ExtractSymbolsFromNode(SyntaxNode? node)
    {
        var symbols = new List<string>();
        
        if (node == null) return symbols;

        // Extract identifiers from various node types
        switch (node)
        {
            case IdentifierNameSyntax identifier:
                symbols.Add(identifier.Identifier.ValueText);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                symbols.Add(memberAccess.Name.Identifier.ValueText);
                if (memberAccess.Expression is IdentifierNameSyntax expr)
                {
                    symbols.Add(expr.Identifier.ValueText);
                }
                break;
            case InvocationExpressionSyntax invocation:
                if (invocation.Expression is IdentifierNameSyntax methodName)
                {
                    symbols.Add(methodName.Identifier.ValueText);
                }
                else if (invocation.Expression is MemberAccessExpressionSyntax memberMethod)
                {
                    symbols.Add(memberMethod.Name.Identifier.ValueText);
                }
                break;
            case ObjectCreationExpressionSyntax objectCreation:
                if (objectCreation.Type is IdentifierNameSyntax typeName)
                {
                    symbols.Add(typeName.Identifier.ValueText);
                }
                break;
        }

        // Also check parent and child nodes for more context
        if (node.Parent != null)
        {
            symbols.AddRange(ExtractSymbolsFromNode(node.Parent).Take(3)); // Limit to avoid recursion
        }

        return symbols.Distinct().ToList();
    }

    private Dictionary<string, SymbolLocation> ExtractSymbolsFromFile(SyntaxNode root, string filePath)
    {
        var symbols = new Dictionary<string, SymbolLocation>();

        foreach (var node in root.DescendantNodes())
        {
            string? symbolName = null;
            var location = node.GetLocation();
            
            if (!location.IsInSource) continue;

            var lineSpan = location.GetLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1; // Convert to 1-based
            var column = lineSpan.StartLinePosition.Character + 1; // Convert to 1-based

            switch (node)
            {
                case ClassDeclarationSyntax classDecl:
                    symbolName = classDecl.Identifier.ValueText;
                    break;
                case MethodDeclarationSyntax methodDecl:
                    symbolName = methodDecl.Identifier.ValueText;
                    break;
                case PropertyDeclarationSyntax propDecl:
                    symbolName = propDecl.Identifier.ValueText;
                    break;
                case FieldDeclarationSyntax fieldDecl:
                    foreach (var variable in fieldDecl.Declaration.Variables)
                    {
                        symbols[variable.Identifier.ValueText] = new SymbolLocation(filePath, line, column);
                    }
                    break;
                case VariableDeclaratorSyntax varDecl:
                    symbolName = varDecl.Identifier.ValueText;
                    break;
                case ParameterSyntax param:
                    symbolName = param.Identifier.ValueText;
                    break;
            }

            if (!string.IsNullOrEmpty(symbolName))
            {
                symbols[symbolName] = new SymbolLocation(filePath, line, column);
            }
        }

        return symbols;
    }

    private SymbolLocation? FindSymbolInIndex(string symbolName)
    {
        lock (_indexLock)
        {
            foreach (var file in _symbolIndex.Values)
            {
                if (file.TryGetValue(symbolName, out var location))
                {
                    return location;
                }
            }
        }
        return null;
    }

    private static (SymbolType type, string? assemblyInfo) ClassifySymbolTypeWithAssembly(string symbolName, SymbolLocation? location, SyntaxTree syntaxTree, SyntaxNode root)
    {
        // If we found it in our codebase, it's user-defined
        if (location != null)
            return (SymbolType.UserDefined, null);

        // For now, use pattern matching approach. 
        // TODO: In a full implementation, we'd create a semantic model with references to get actual assembly info
        var assemblyInfo = GetAssemblyInfoForSymbol(symbolName);
        
        // Check for common framework types and namespaces
        if (IsFrameworkSymbol(symbolName))
            return (SymbolType.Framework, assemblyInfo);

        // Check if it's a known external package (has assembly info)
        if (!string.IsNullOrEmpty(assemblyInfo))
            return (SymbolType.External, assemblyInfo);

        // Everything else is unknown/undefined
        return (SymbolType.Unknown, null);
    }

    private static string? GetAssemblyInfoForSymbol(string symbolName)
    {
        // Map common symbols to their assemblies/packages
        var assemblyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // System types
            ["Console"] = "System.Console",
            ["Task"] = "System.Threading.Tasks", 
            ["string"] = "System.Runtime",
            ["int"] = "System.Runtime",
            ["DateTime"] = "System.Runtime",
            ["StringBuilder"] = "System.Text",
            ["List"] = "System.Collections",
            ["Dictionary"] = "System.Collections",
            ["File"] = "System.IO.FileSystem",
            ["Directory"] = "System.IO.FileSystem",
            ["Path"] = "System.IO.FileSystem",
            ["Regex"] = "System.Text.RegularExpressions",
            
            // Common third-party (examples)
            ["HttpClient"] = "System.Net.Http",
            ["IConfiguration"] = "Microsoft.Extensions.Configuration.Abstractions",
            ["ILogger"] = "Microsoft.Extensions.Logging.Abstractions",
            
            // JSON libraries (System.Text.Json vs Newtonsoft.Json)
            ["JsonSerializer"] = "System.Text.Json", // System.Text.Json is the modern choice
            ["JsonConvert"] = "Newtonsoft.Json",
            ["JObject"] = "Newtonsoft.Json",
            ["JArray"] = "Newtonsoft.Json",
            ["JToken"] = "Newtonsoft.Json",
            ["JsonProperty"] = "Newtonsoft.Json",
            ["JsonIgnore"] = "Newtonsoft.Json",
            ["SerializeObject"] = "Newtonsoft.Json",
            ["DeserializeObject"] = "Newtonsoft.Json"
        };

        if (assemblyMap.TryGetValue(symbolName, out var assembly))
            return assembly;

        // Pattern-based classification
        if (symbolName.StartsWith("System."))
            return "System.*";
        if (symbolName.StartsWith("Microsoft."))
            return "Microsoft.*";
        if (symbolName.StartsWith("Newtonsoft."))
            return "Newtonsoft.Json";

        return null; // Unknown
    }

    private static bool IsFrameworkSymbol(string symbolName)
    {
        // Common .NET framework types
        var frameworkTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Primitive and common types
            "Task", "string", "int", "bool", "double", "float", "decimal", "DateTime", "TimeSpan",
            "object", "void", "byte", "char", "long", "short", "uint", "ulong", "ushort", "sbyte",
            
            // Common System types
            "Console", "WriteLine", "ReadLine", "Environment", "Path", "File", "Directory",
            "StringBuilder", "List", "Dictionary", "Array", "Exception", "ArgumentException",
            "InvalidOperationException", "NotImplementedException", "NotSupportedException",
            "Guid", "Uri", "Version", "Encoding", "Regex", "Math", "Random", "Convert",
            
            // Collections
            "IEnumerable", "ICollection", "IList", "IDictionary", "ISet", "HashSet", "LinkedList",
            "Queue", "Stack", "ConcurrentDictionary", "ConcurrentQueue", "ConcurrentStack",
            
            // Async/Threading
            "CancellationToken", "CancellationTokenSource", "SemaphoreSlim", "Mutex", "Thread",
            "ThreadPool", "Timer", "Task", "ValueTask", "ConfigureAwait",
            
            // LINQ
            "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending", "GroupBy", "Join",
            "First", "FirstOrDefault", "Single", "SingleOrDefault", "Any", "All", "Count",
            "Sum", "Average", "Min", "Max", "ToList", "ToArray", "ToDictionary",
            
            // Common attributes
            "Obsolete", "Serializable", "NonSerialized", "DataContract", "DataMember",
            
            // var keyword (C# contextual keyword)
            "var"
        };

        return frameworkTypes.Contains(symbolName) || 
               symbolName.StartsWith("System.") ||
               symbolName.StartsWith("Microsoft.") ||
               symbolName.StartsWith("Windows.");
    }
}