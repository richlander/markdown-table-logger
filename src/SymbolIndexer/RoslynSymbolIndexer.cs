using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private readonly ConcurrentDictionary<string, Dictionary<string, IndexedSymbol>> _symbolIndex = new();
    private readonly ConcurrentDictionary<string, SyntaxTree> _syntaxTrees = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _indexLock = new();
    private readonly object _compilationLock = new();
    private ImmutableArray<MetadataReference> _metadataReferences;
    private CSharpCompilation? _compilation;

    public RoslynSymbolIndexer(ILogger<RoslynSymbolIndexer> logger)
    {
        _logger = logger;
    }

    public async Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column)
    {
        try
        {
            var results = new List<SymbolResult>();

            var fullPath = Path.GetFullPath(file);
            if (!File.Exists(fullPath))
            {
                return results;
            }

            var tree = await EnsureSyntaxTreeAsync(fullPath);
            var compilation = GetCompilationSnapshot();
            if (tree == null || compilation == null)
            {
                return results;
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var text = await tree.GetTextAsync();
            if (line < 1 || line > text.Lines.Count)
            {
                return results;
            }

            var nodes = column <= 0
                ? GetNodesForLine(tree, text, line)
                : GetNodesForPosition(tree, text, line, column);

            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var node in nodes)
            {
                foreach (var symbol in ResolveSymbols(semanticModel, node))
                {
                    if (symbol == null || symbol.Kind == SymbolKind.Namespace && string.IsNullOrEmpty(symbol.Name))
                    {
                        continue;
                    }

                    if (!seen.Add(symbol))
                    {
                        continue;
                    }

                    LogDebug($"Resolved symbol {symbol.Name} ({symbol.Kind}) from node {node.Kind()}");
                    results.Add(CreateSymbolResult(symbol));
                }
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
            
            var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("bin") && !f.Contains("obj"))
                .ToArray();

            foreach (var file in csFiles)
            {
                await UpdateFileAsync(file);
            }

            lock (_indexLock)
            {
                _symbolIndex.Clear();
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
                // File deleted or invalid - remove from compilation
                RemoveFileFromCompilation(filePath);
                return;
            }

            var sourceText = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, new CSharpParseOptions(LanguageVersion.Latest), path: filePath);

            // Update syntax tree cache
            _syntaxTrees[filePath] = syntaxTree;

            // Use incremental compilation update instead of full rebuild
            UpdateCompilationIncremental(filePath, syntaxTree);

            var semanticModel = GetCompilationSnapshot()?.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            if (semanticModel != null)
            {
                var symbols = ExtractDeclaredSymbols(semanticModel, syntaxTree);
                lock (_indexLock)
                {
                    _symbolIndex[filePath] = symbols;
                }

                _logger.LogDebug("Updated symbol index for {FilePath} with {Count} symbols", filePath, symbols.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file index for {FilePath}", filePath);
        }
    }

    public async Task RemoveFileAsync(string filePath)
    {
        try
        {
            RemoveFileFromCompilation(filePath);
            _logger.LogDebug("Removed file from index: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing file from index: {FilePath}", filePath);
        }
    }

    private async Task<SyntaxTree?> EnsureSyntaxTreeAsync(string fullPath)
    {
        if (_syntaxTrees.TryGetValue(fullPath, out var cached))
        {
            return cached;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        var sourceText = await File.ReadAllTextAsync(fullPath);
        var tree = CSharpSyntaxTree.ParseText(sourceText, new CSharpParseOptions(LanguageVersion.Latest), path: fullPath);
        _syntaxTrees[fullPath] = tree;
        RebuildCompilation();
        return tree;
    }

    private Dictionary<string, IndexedSymbol> ExtractDeclaredSymbols(SemanticModel semanticModel, SyntaxTree tree)
    {
        var symbols = new Dictionary<string, IndexedSymbol>();
        var root = tree.GetRoot();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ClassDeclarationSyntax classDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(classDecl));
                    break;
                case StructDeclarationSyntax structDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(structDecl));
                    break;
                case RecordDeclarationSyntax recordDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(recordDecl));
                    break;
                case InterfaceDeclarationSyntax interfaceDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(interfaceDecl));
                    break;
                case EnumDeclarationSyntax enumDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(enumDecl));
                    break;
                case MethodDeclarationSyntax methodDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(methodDecl));
                    break;
                case PropertyDeclarationSyntax propertyDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(propertyDecl));
                    break;
                case EventDeclarationSyntax eventDecl:
                    AddSymbol(semanticModel.GetDeclaredSymbol(eventDecl));
                    break;
                case EventFieldDeclarationSyntax eventFieldDecl:
                    foreach (var variable in eventFieldDecl.Declaration.Variables)
                    {
                        AddSymbol(semanticModel.GetDeclaredSymbol(variable));
                    }
                    break;
                case FieldDeclarationSyntax fieldDecl:
                    foreach (var variable in fieldDecl.Declaration.Variables)
                    {
                        AddSymbol(semanticModel.GetDeclaredSymbol(variable));
                    }
                    break;
                case VariableDeclaratorSyntax variable:
                    AddSymbol(semanticModel.GetDeclaredSymbol(variable));
                    break;
                case ParameterSyntax parameter:
                    AddSymbol(semanticModel.GetDeclaredSymbol(parameter));
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    AddSymbol(semanticModel.GetDeclaredSymbol(localFunction));
                    break;
            }
        }

        return symbols;

        void AddSymbol(ISymbol? declared)
        {
            if (declared == null)
            {
                return;
            }

            var location = declared.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
            {
                return;
            }

            var span = location.GetLineSpan();
            var symbolLocation = new SymbolLocation(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
            var (_, symbolType, assemblyInfo) = ClassifySymbol(declared);

            var indexedSymbol = new IndexedSymbol(
                Name: declared.Name,
                Location: symbolLocation,
                Type: symbolType,
                AssemblyInfo: assemblyInfo,
                Namespace: declared.ContainingNamespace?.ToDisplayString(),
                Kind: declared.Kind,
                ContainingType: declared.ContainingType?.ToDisplayString(),
                Signature: declared.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)
            );

            symbols[declared.Name] = indexedSymbol;
        }
    }

    private IEnumerable<SyntaxNode> GetNodesForLine(SyntaxTree tree, SourceText text, int line)
    {
        var span = text.Lines[line - 1].Span;
        var root = tree.GetRoot();
        return root.DescendantNodes()
            .Where(n => !n.Span.IsEmpty && span.OverlapsWith(n.Span))
            .Take(25);
    }

    private IEnumerable<SyntaxNode> GetNodesForPosition(SyntaxTree tree, SourceText text, int line, int column)
    {
        var lineInfo = text.Lines[line - 1];
        var adjustedColumn = Math.Max(0, column - 1);
        var position = Math.Min(lineInfo.Start + adjustedColumn, text.Length);
        var token = tree.GetRoot().FindToken(Math.Max(0, position));

        var current = token.Parent;
        while (current != null)
        {
            yield return current;
            current = current.Parent;
        }
    }

    private IEnumerable<ISymbol?> ResolveSymbols(SemanticModel model, SyntaxNode node)
    {
        if (node == null)
        {
            yield break;
        }

        switch (node)
        {
            case IdentifierNameSyntax identifier:
                var info = model.GetSymbolInfo(identifier);
                if (info.Symbol != null)
                {
                    yield return info.Symbol;
                }
                foreach (var candidate in info.CandidateSymbols)
                {
                    yield return candidate;
                }
                break;
            case MemberAccessExpressionSyntax memberAccess:
                foreach (var symbol in ResolveSymbols(model, memberAccess.Name))
                {
                    yield return symbol;
                }
                break;
            case QualifiedNameSyntax qualified:
                foreach (var symbol in ResolveSymbols(model, qualified.Right))
                {
                    yield return symbol;
                }
                break;
            case SimpleNameSyntax simple:
                var simpleInfo = model.GetSymbolInfo(simple);
                if (simpleInfo.Symbol != null)
                {
                    yield return simpleInfo.Symbol;
                }
                foreach (var candidate in simpleInfo.CandidateSymbols)
                {
                    yield return candidate;
                }
                break;
            default:
                var declared = model.GetDeclaredSymbol(node);
                if (declared != null)
                {
                    yield return declared;
                }
                break;
        }
    }

    private SymbolResult CreateSymbolResult(ISymbol symbol)
    {
        var (location, symbolType, assemblyInfo) = ClassifySymbol(symbol);
        return new SymbolResult(symbol.Name, location, symbolType, assemblyInfo);
    }

    private (SymbolLocation? location, SymbolType type, string? assemblyInfo) ClassifySymbol(ISymbol symbol)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (sourceLocation != null)
        {
            var span = sourceLocation.GetLineSpan();
            return (new SymbolLocation(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1), SymbolType.UserDefined, null);
        }

        var assembly = symbol.ContainingAssembly;
        if (assembly == null)
        {
            return (null, SymbolType.Unknown, null);
        }

        var assemblyName = assembly.Identity.Name;
        if (IsFrameworkAssembly(assemblyName, assembly))
        {
            return (null, SymbolType.Framework, assemblyName);
        }

        return (null, SymbolType.External, assemblyName);
    }

    private bool IsFrameworkAssembly(string assemblyName, IAssemblySymbol assemblySymbol)
    {
        // Check for standard .NET framework assemblies
        if (assemblyName.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check if assembly is from the runtime directory (more accurate framework detection)
        try
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!string.IsNullOrEmpty(runtimeDir))
            {
                foreach (var location in assemblySymbol.Locations)
                {
                    if (location.IsInMetadata && location.MetadataModule != null)
                    {
                        var moduleName = location.MetadataModule.Name;
                        var assemblyLocation = Path.GetDirectoryName(moduleName);
                        if (!string.IsNullOrEmpty(assemblyLocation) &&
                            Path.GetFullPath(assemblyLocation).StartsWith(Path.GetFullPath(runtimeDir), StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // If we can't determine location, fall back to name-based detection
            _logger.LogDebug(ex, "Failed to determine assembly location for {AssemblyName}", assemblyName);
        }

        // Check for well-known framework assemblies that don't start with System/Microsoft
        var knownFrameworkAssemblies = new[]
        {
            "runtime", "netcore", "dotnet", "coreclr", "corefx"
        };

        return knownFrameworkAssemblies.Any(prefix =>
            assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildCompilation()
    {
        lock (_compilationLock)
        {
            if (_metadataReferences.IsDefaultOrEmpty)
            {
                _metadataReferences = LoadMetadataReferences();
            }

            var trees = _syntaxTrees.Values.ToImmutableArray();
            _compilation = CSharpCompilation.Create(
                assemblyName: "SymbolIndexer.Workspace",
                syntaxTrees: trees,
                references: _metadataReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }

    private void UpdateCompilationIncremental(string filePath, SyntaxTree newTree)
    {
        lock (_compilationLock)
        {
            if (_compilation == null)
            {
                RebuildCompilation();
                return;
            }

            // Find existing tree for this file
            var oldTree = _compilation.SyntaxTrees.FirstOrDefault(t =>
                string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (oldTree != null)
            {
                // Replace existing tree (incremental update)
                _compilation = _compilation.ReplaceSyntaxTree(oldTree, newTree);
                LogDebug($"Incrementally updated compilation for {filePath}");
            }
            else
            {
                // Add new tree
                _compilation = _compilation.AddSyntaxTrees(newTree);
                LogDebug($"Added new tree to compilation for {filePath}");
            }
        }
    }

    private void RemoveFileFromCompilation(string filePath)
    {
        lock (_compilationLock)
        {
            if (_compilation == null) return;

            var treeToRemove = _compilation.SyntaxTrees.FirstOrDefault(t =>
                string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (treeToRemove != null)
            {
                _compilation = _compilation.RemoveSyntaxTrees(treeToRemove);
                LogDebug($"Removed tree from compilation for {filePath}");
            }
        }

        // Remove from syntax tree cache
        _syntaxTrees.TryRemove(filePath, out _);

        // Remove from symbol index
        lock (_indexLock)
        {
            _symbolIndex.TryRemove(filePath, out _);
        }
    }

    private CSharpCompilation? GetCompilationSnapshot()
    {
        lock (_compilationLock)
        {
            return _compilation;
        }
    }

    private ImmutableArray<MetadataReference> LoadMetadataReferences()
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Load standard framework references
        void AddReference(Type type)
        {
            var location = type.GetTypeInfo().Assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                references.Add(location);
            }
        }

        // Core framework types
        AddReference(typeof(object));            // System.Private.CoreLib
        AddReference(typeof(Console));           // System.Console
        AddReference(typeof(Enumerable));        // System.Linq
        AddReference(typeof(Task));              // System.Threading.Tasks
        AddReference(typeof(List<>));            // System.Collections
        AddReference(typeof(Attribute));         // System.Runtime
        AddReference(typeof(System.Text.Json.JsonSerializer)); // System.Text.Json
        AddReference(typeof(System.IO.File));    // System.IO.FileSystem

        // Load comprehensive framework references
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(runtimeDir))
        {
            var frameworkAssemblies = new[]
            {
                "System.Runtime.dll",
                "System.Collections.dll",
                "System.Console.dll",
                "System.IO.FileSystem.dll",
                "System.Linq.dll",
                "System.Text.Json.dll",
                "System.Threading.Tasks.dll",
                "Microsoft.CSharp.dll",
                "netstandard.dll"
            };

            foreach (var assembly in frameworkAssemblies)
            {
                var path = Path.Combine(runtimeDir, assembly);
                if (File.Exists(path))
                {
                    references.Add(path);
                }
            }
        }

        // Load project-specific references from current workspace
        LoadProjectReferences(references);

        _logger.LogDebug("Loaded {Count} metadata references for compilation", references.Count);
        return ImmutableArray.CreateRange<MetadataReference>(references.Select(path => MetadataReference.CreateFromFile(path)));
    }

    private void LoadProjectReferences(HashSet<string> references)
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var projectFiles = Directory.GetFiles(currentDir, "*.csproj", SearchOption.AllDirectories)
                .Where(f => !f.Contains("bin") && !f.Contains("obj"))
                .ToArray();

            foreach (var projectFile in projectFiles)
            {
                LoadReferencesFromProject(projectFile, references);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load project-specific references");
        }
    }

    private void LoadReferencesFromProject(string projectFile, HashSet<string> references)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(projectFile) ?? "";
            var projectName = Path.GetFileNameWithoutExtension(projectFile);

            // Look for NuGet packages in typical locations
            var nugetPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
                Path.Combine(projectDir, "packages")
            };

            foreach (var nugetPath in nugetPaths.Where(Directory.Exists))
            {
                LoadNuGetReferences(nugetPath, references);
            }

            // Look for project output assemblies
            var outputPaths = new[]
            {
                Path.Combine(projectDir, "bin", "Debug"),
                Path.Combine(projectDir, "bin", "Release")
            };

            foreach (var outputPath in outputPaths.Where(Directory.Exists))
            {
                LoadOutputReferences(outputPath, references);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load references from project {ProjectFile}", projectFile);
        }
    }

    private void LoadNuGetReferences(string nugetPath, HashSet<string> references)
    {
        try
        {
            var libDirs = Directory.GetDirectories(nugetPath, "*", SearchOption.AllDirectories)
                .Where(d => Path.GetFileName(d).StartsWith("lib", StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => Directory.GetDirectories(d))
                .Where(d => Path.GetFileName(d).StartsWith("net", StringComparison.OrdinalIgnoreCase))
                .Take(100); // Limit to prevent excessive loading

            foreach (var libDir in libDirs)
            {
                var assemblies = Directory.GetFiles(libDir, "*.dll", SearchOption.TopDirectoryOnly);
                foreach (var assembly in assemblies.Take(20)) // Limit per package
                {
                    if (File.Exists(assembly))
                    {
                        references.Add(assembly);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load NuGet references from {NuGetPath}", nugetPath);
        }
    }

    private void LoadOutputReferences(string outputPath, HashSet<string> references)
    {
        try
        {
            var assemblies = Directory.GetFiles(outputPath, "*.dll", SearchOption.AllDirectories)
                .Take(50); // Reasonable limit

            foreach (var assembly in assemblies)
            {
                if (File.Exists(assembly))
                {
                    references.Add(assembly);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load output references from {OutputPath}", outputPath);
        }
    }

    private void LogDebug(string message)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_LOGS_DEBUG") == "1")
        {
            _logger.LogDebug(message);
        }
    }
}
