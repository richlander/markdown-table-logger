using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace SymbolIndexer;

public class UsingBasedSymbolIndexer : ISymbolIndexer
{
    private readonly ILogger<UsingBasedSymbolIndexer> _logger;
    private readonly HashSet<string> _knownFrameworkNamespaces;

    public UsingBasedSymbolIndexer(ILogger<UsingBasedSymbolIndexer> logger)
    {
        _logger = logger;
        _knownFrameworkNamespaces = LoadKnownFrameworkNamespaces();
    }

    public async Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column)
    {
        try
        {
            if (!File.Exists(file))
            {
                return [];
            }

            var fileContent = await File.ReadAllTextAsync(file);
            var tree = CSharpSyntaxTree.ParseText(fileContent);
            var root = tree.GetCompilationUnitRoot();

            // Extract using statements from the file
            var usingNamespaces = root.Usings
                .Select(u => u.Name?.ToString())
                .Where(ns => !string.IsNullOrEmpty(ns))
                .Cast<string>()
                .ToList();

            // Find identifier at the specified position
            var identifier = GetIdentifierAtPosition(tree, line, column);
            if (string.IsNullOrEmpty(identifier))
            {
                return [];
            }

            LogDebug($"Resolving identifier '{identifier}' with usings: [{string.Join(", ", usingNamespaces)}]");

            // Resolve the identifier using C# resolution rules
            return await ResolveIdentifierAsync(identifier, usingNamespaces, file);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying symbols at {File}:{Line}:{Column}", file, line, column);
            return [];
        }
    }

    public async Task RebuildIndexAsync(string projectPath)
    {
        // This implementation doesn't need to maintain an index
        // Each query is self-contained by reading the target file
        _logger.LogInformation("Using-based symbol indexer doesn't require rebuild - each query is self-contained");
        await Task.CompletedTask;
    }

    public async Task UpdateFileAsync(string filePath)
    {
        // No persistent index to update
        await Task.CompletedTask;
    }

    public async Task RemoveFileAsync(string filePath)
    {
        // No persistent index to clean up
        await Task.CompletedTask;
    }

    private async Task<List<SymbolResult>> ResolveIdentifierAsync(string identifier, List<string> usingNamespaces, string sourceFile)
    {
        var results = new List<SymbolResult>();

        // 1. Check if it's defined in the current file's namespace or as a local type
        var localDefinition = await FindLocalDefinitionAsync(identifier, sourceFile);
        if (localDefinition != null)
        {
            results.Add(new SymbolResult(identifier, localDefinition, SymbolType.UserDefined, null));
            return results;
        }

        // 2. Try to resolve using each 'using' namespace
        foreach (var ns in usingNamespaces)
        {
            var fullyQualified = $"{ns}.{identifier}";

            if (IsKnownFrameworkType(fullyQualified))
            {
                var symbolType = IsFrameworkNamespace(ns) ? SymbolType.Framework : SymbolType.External;
                results.Add(new SymbolResult(identifier, null, symbolType, ns));
                LogDebug($"Resolved '{identifier}' as {symbolType} type in namespace '{ns}'");
                return results;
            }
        }

        // 3. Check if it's defined elsewhere in the current workspace
        var workspaceDefinition = await FindInWorkspaceAsync(identifier, sourceFile);
        if (workspaceDefinition != null)
        {
            results.Add(new SymbolResult(identifier, workspaceDefinition, SymbolType.UserDefined, null));
            return results;
        }

        // 4. Check for global types (no namespace needed)
        if (IsKnownGlobalType(identifier))
        {
            results.Add(new SymbolResult(identifier, null, SymbolType.Framework, "System"));
            return results;
        }

        // 5. If we can't resolve it, mark as unknown
        results.Add(new SymbolResult(identifier, null, SymbolType.Unknown, null));
        LogDebug($"Could not resolve identifier '{identifier}'");
        return results;
    }

    private string? GetIdentifierAtPosition(SyntaxTree tree, int line, int column)
    {
        try
        {
            var text = tree.GetText();
            if (line < 1 || line > text.Lines.Count)
                return null;

            var lineText = text.Lines[line - 1];
            var position = lineText.Start + Math.Max(0, column - 1);

            if (position >= text.Length)
                return null;

            var token = tree.GetRoot().FindToken(position);

            // Look for identifier tokens
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                return token.ValueText;
            }

            // If not directly on an identifier, look at the containing node
            var node = token.Parent;
            while (node != null)
            {
                if (node is IdentifierNameSyntax identifier)
                {
                    return identifier.Identifier.ValueText;
                }
                if (node is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Name.Identifier.ValueText;
                }
                node = node.Parent;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<SymbolLocation?> FindLocalDefinitionAsync(string identifier, string sourceFile)
    {
        try
        {
            var content = await File.ReadAllTextAsync(sourceFile);

            // Look for class, interface, struct, enum, record declarations
            var patterns = new[]
            {
                $@"\b(class|interface|struct|enum|record)\s+{Regex.Escape(identifier)}\b",
                $@"\b(public|private|protected|internal)\s+(class|interface|struct|enum|record)\s+{Regex.Escape(identifier)}\b"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(content, pattern);
                if (match.Success)
                {
                    var lineNumber = content.Take(match.Index).Count(c => c == '\n') + 1;
                    var column = match.Index - content.LastIndexOf('\n', match.Index);
                    return new SymbolLocation(sourceFile, lineNumber, column);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<SymbolLocation?> FindInWorkspaceAsync(string identifier, string sourceFile)
    {
        try
        {
            var workspaceRoot = GetWorkspaceRoot(sourceFile);
            var csFiles = Directory.EnumerateFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("bin") && !f.Contains("obj") && !string.Equals(f, sourceFile, StringComparison.OrdinalIgnoreCase));

            foreach (var file in csFiles)
            {
                var definition = await FindLocalDefinitionAsync(identifier, file);
                if (definition != null)
                {
                    return definition;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string GetWorkspaceRoot(string filePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? ".");

        while (directory != null)
        {
            // Look for common workspace indicators
            if (directory.GetFiles("*.sln").Any() ||
                directory.GetFiles("*.csproj").Any() ||
                directory.GetDirectories(".git").Any())
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private bool IsKnownFrameworkType(string fullyQualifiedName)
    {
        // Check against known .NET types
        var wellKnownTypes = new HashSet<string>
        {
            "System.String", "System.Int32", "System.Int64", "System.Boolean", "System.Double", "System.Decimal",
            "System.DateTime", "System.TimeSpan", "System.Guid", "System.Object", "System.Exception",
            "System.Console", "System.Convert", "System.Math", "System.Environment",
            "System.IO.File", "System.IO.Directory", "System.IO.Path", "System.IO.Stream",
            "System.Collections.Generic.List", "System.Collections.Generic.Dictionary",
            "System.Collections.Generic.IEnumerable", "System.Collections.Generic.ICollection",
            "System.Threading.Tasks.Task", "System.Threading.CancellationToken",
            "System.Text.StringBuilder", "System.Text.Encoding",
            "System.Linq.Enumerable"
        };

        return wellKnownTypes.Contains(fullyQualifiedName);
    }

    private bool IsKnownGlobalType(string identifier)
    {
        // Global types that don't need namespace qualification
        var globalTypes = new HashSet<string> { "string", "int", "long", "bool", "double", "decimal", "object", "var" };
        return globalTypes.Contains(identifier);
    }

    private bool IsFrameworkNamespace(string namespaceName)
    {
        return _knownFrameworkNamespaces.Any(ns => namespaceName.StartsWith(ns, StringComparison.OrdinalIgnoreCase));
    }

    private HashSet<string> LoadKnownFrameworkNamespaces()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System",
            "Microsoft.Extensions",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore"
        };
    }

    private void LogDebug(string message)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_LOGS_DEBUG") == "1")
        {
            _logger.LogDebug(message);
        }
    }
}