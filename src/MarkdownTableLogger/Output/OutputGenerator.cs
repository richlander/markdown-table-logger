using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MarkdownTableLogger.Models;
using MarkdownTableLogger.SymbolIndexer;

namespace MarkdownTableLogger.Output;

public class OutputGenerator
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    private readonly string _logsDirectory;
    private readonly string _buildTimestamp;
    private readonly SymbolIndexerProcessClient _symbolClient;
    private readonly List<(string fileName, string description, long sizeBytes)> _generatedFiles;
    
    public string LogsDirectory => _logsDirectory;
    public string BuildId => _buildTimestamp;
    
    public OutputGenerator()
    {
        _buildTimestamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        _logsDirectory = Path.Combine("_logs", _buildTimestamp);
        Directory.CreateDirectory(_logsDirectory);
        _symbolClient = new SymbolIndexerProcessClient();
        _generatedFiles = new List<(string fileName, string description, long sizeBytes)>();
    }

    public void WriteProjectResults(List<ProjectResult> results, string baseFileName, bool showStatus = false)
    {
        var json = JsonSerializer.Serialize(results, _jsonOptions);
        var markdown = GenerateProjectResultsMarkdown(results, showStatus);

        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.json"), json);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.md"), markdown);

        TrackGeneratedFile($"{baseFileName}.json", "Project results in JSON format");
        TrackGeneratedFile($"{baseFileName}.md", "Project results in markdown table format");
    }

    public void WriteErrorDiagnostics(List<ErrorDiagnostic> errors, string baseFileName, bool showMessage = true)
    {
        var json = JsonSerializer.Serialize(errors, _jsonOptions);
        var markdown = GenerateErrorDiagnosticsMarkdown(errors, showMessage);

        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.json"), json);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.md"), markdown);

        TrackGeneratedFile($"{baseFileName}.json", "Error diagnostics in JSON format");
        TrackGeneratedFile($"{baseFileName}.md", "Error diagnostics in markdown table format");
    }
    
    public void WriteEnhancedErrorDiagnostics(List<EnhancedErrorDiagnostic> enhancedErrors, string baseFileName)
    {
        var json = JsonSerializer.Serialize(enhancedErrors, _jsonOptions);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.json"), json);
        
        TrackGeneratedFile($"{baseFileName}.json", "Enhanced error diagnostics with anchor and line references");
    }
    
    public void WriteEnhancedErrorsTable(string promptFileName, string outputFileName)
    {
        var promptFilePath = Path.Combine(_logsDirectory, $"{promptFileName}.md");
        var outputFilePath = Path.Combine(_logsDirectory, $"{outputFileName}.md");
        
        if (File.Exists(promptFilePath))
        {
            var promptContent = File.ReadAllText(promptFilePath);
            var relativePromptPath = $"{promptFileName}.md"; // Just the filename, relative within same directory
            var enhancedTable = ExtractErrorsTable(promptContent, relativePromptPath);
            File.WriteAllText(outputFilePath, enhancedTable);
            
            TrackGeneratedFile($"{outputFileName}.md", "Enhanced errors table with navigation links");
        }
    }

    public void WriteErrorTypeSummary(List<ErrorTypeSummary> summary, string baseFileName, bool showDescription = true)
    {
        var json = JsonSerializer.Serialize(summary, _jsonOptions);
        var markdown = GenerateErrorTypeSummaryMarkdown(summary, showDescription);

        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.json"), json);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.md"), markdown);

        TrackGeneratedFile($"{baseFileName}.json", "Error type summary in JSON format");
        TrackGeneratedFile($"{baseFileName}.md", "Error type summary in markdown table format");
    }

    public string GenerateProjectResultsMarkdown(List<ProjectResult> results, bool showStatus = false)
    {
        if (results.Count == 0) return "";
        
        var sb = new StringBuilder();
        if (showStatus)
        {
            sb.AppendLine("| Project | Errors | Warnings | Status |");
            sb.AppendLine("|---------|--------|----------|--------|");
            
            foreach (var result in results)
            {
                var status = result.Succeeded ? "✅" : "❌";
                sb.AppendLine($"| {result.Project} | {result.Errors} | {result.Warnings} | {status} |");
            }
        }
        else
        {
            sb.AppendLine("| Project | Errors | Warnings |");
            sb.AppendLine("|---------|--------|----------|");
            
            foreach (var result in results)
            {
                sb.AppendLine($"| {result.Project} | {result.Errors} | {result.Warnings} |");
            }
        }
        
        return sb.ToString();
    }

    public string GenerateErrorDiagnosticsMarkdown(List<ErrorDiagnostic> errors, bool showMessage = true)
    {
        var sb = new StringBuilder();
        if (showMessage)
        {
            sb.AppendLine("| File | Line | Col | Code | Message |");
            sb.AppendLine("|------|------|-----|------|---------|");
            
            foreach (var error in errors)
            {
                var displayPath = GetDisplayPath(error.File);
                var message = TruncateMessage(error.Message ?? "", 50);
                sb.AppendLine($"| {displayPath} | {error.Line} | {error.Column} | {error.Code} | {message} |");
            }
        }
        else
        {
            sb.AppendLine("| File | Line | Col | Code |");
            sb.AppendLine("|------|------|-----|------|");
            
            foreach (var error in errors)
            {
                var displayPath = GetDisplayPath(error.File);
                sb.AppendLine($"| {displayPath} | {error.Line} | {error.Column} | {error.Code} |");
            }
        }
        
        return sb.ToString();
    }

    public string GenerateErrorTypeSummaryMarkdown(List<ErrorTypeSummary> summary, bool showDescription = true)
    {
        var sb = new StringBuilder();
        if (showDescription)
        {
            sb.AppendLine("| Code | Count | Description |");
            sb.AppendLine("|------|-------|-------------|");
            
            foreach (var item in summary)
            {
                var description = item.Description ?? "Unknown error";
                sb.AppendLine($"| {item.Code} | {item.Count} | {description} |");
            }
        }
        else
        {
            sb.AppendLine("| Code | Count |");
            sb.AppendLine("|------|-------|");
            
            foreach (var item in summary)
            {
                sb.AppendLine($"| {item.Code} | {item.Count} |");
            }
        }
        
        return sb.ToString();
    }

    public async Task<string> GeneratePromptDocumentAsync(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration, string? command = null, bool concise = false)
    {
        // Pass 1: Generate the complete document with current table structure
        var pass1Document = await GeneratePromptDocumentPass1Async(projects, diagnostics, startTime, duration, command, concise);

        // Pass 2: Enhance the errors table with Anchor and Lines columns
        return EnhanceErrorsTableWithLineReferences(pass1Document, diagnostics);
    }
    
    private async Task<string> GeneratePromptDocumentPass1Async(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration, string? command = null, bool concise = false)
    {
        var sb = new StringBuilder();
        
        // Header with metadata
        sb.AppendLine($"# {GetRepositoryName()} build log");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(command))
        {
            sb.AppendLine($"Command: {command}");
        }
        sb.AppendLine($"Time: {startTime:yyyy-MM-ddTHH:mm:ss}");
        sb.AppendLine($"Duration: {duration.TotalSeconds:F1}s");
        sb.AppendLine();
        sb.AppendLine("This document contains build results in markdown tables. For efficient navigation:");
        sb.AppendLine();
        sb.AppendLine("**Sequential reading:** Review 'Projects' and 'Build Errors' sections first");
        sb.AppendLine();
        sb.AppendLine("**Random access:** Jump directly to specific errors using the 'Build Errors' table:");
        sb.AppendLine("- Section column: Use as anchor with `sed -n '/### anchor/,/^###/p'` to extract detailed analysis");
        sb.AppendLine("- Lines column: Use as range with `sed -n 'start,end p'` to extract source context");
        sb.AppendLine();
        sb.AppendLine("Example: For error Program.cs:42:15 with lines 27-42:");
        sb.AppendLine("```bash");
        sb.AppendLine("sed -n '/### Program.cs:42:15/,/^###/p' build.log  # Get error details");
        sb.AppendLine("sed -n '27,42p' build.log                         # Get source context");
        sb.AppendLine("```");
        sb.AppendLine();
        
        // Projects table
        sb.AppendLine("## Projects");
        sb.AppendLine();
        sb.AppendLine("| Project | Errors | Warnings |");
        sb.AppendLine("|---------|--------|----------|");
        
        foreach (var project in projects)
        {
            sb.AppendLine($"| {project.Project} | {project.Errors} | {project.Warnings} |");
        }
        sb.AppendLine();
        
        // Diagnostics table (will be enhanced in pass 2)
        sb.AppendLine("## Build Errors");
        sb.AppendLine();
        sb.AppendLine("| File | Line | Col | Code |");
        sb.AppendLine("|------|------|-----|------|");
        
        foreach (var diagnostic in diagnostics)
        {
            var displayPath = GetDisplayPath(diagnostic.File);
            sb.AppendLine($"| {displayPath} | {diagnostic.Line} | {diagnostic.Column} | {diagnostic.Code} |");
        }
        sb.AppendLine();
        
        // Context blocks for each error (only if there are errors)
        if (diagnostics.Count > 0)
        {
            sb.AppendLine("## Error Details");
            sb.AppendLine();
            
            foreach (var diagnostic in diagnostics)
            {
                var (context, startLine, endLine) = GenerateErrorContextWithRange(diagnostic, concise);
                var displayPath = GetDisplayPath(diagnostic.File);

                // Clean header with structured metadata
                sb.AppendLine($"### {displayPath}:{diagnostic.Line}:{diagnostic.Column}");
                sb.AppendLine();
                sb.AppendLine($"- File: {displayPath}");
                sb.AppendLine($"- Lines: {startLine}-{endLine}");
                sb.AppendLine($"- Error: {diagnostic.Code}");
                if (!string.IsNullOrEmpty(diagnostic.Message))
                {
                    sb.AppendLine($"- Message: {diagnostic.Message}");
                }
                sb.AppendLine();
                sb.AppendLine("```csharp");
                sb.AppendLine(context);
                sb.AppendLine("```");

                // Add symbol references section
                var symbolsSection = await GenerateSymbolReferencesSectionAsync(diagnostic);
                if (!string.IsNullOrEmpty(symbolsSection))
                {
                    sb.AppendLine();
                    sb.AppendLine(symbolsSection);
                }

                sb.AppendLine();
            }
        }
        
        return sb.ToString();
    }
    
    private string EnhanceErrorsTableWithLineReferences(string pass1Document, List<ErrorDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
            return pass1Document;
            
        var lines = pass1Document.Split('\n');
        var errorSectionMap = new Dictionary<string, (string anchor, int startLine, int endLine)>();
        
        // Find all error sections and their line ranges in the document
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("### ") && !line.Contains("("))
            {
                // Parse new format: "### test-project/Program.cs:11:27"
                var errorKey = line.Substring(4).Trim(); // Remove "### "
                var anchor = GenerateAnchor(errorKey);
                
                // Find actual markdown line numbers where this section appears
                int sectionStart = i + 1; // Line after the ### header
                int sectionEnd = i + 1;
                
                // Find the end of this section (next ### or ## or end of document)
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].StartsWith("### ") || lines[j].StartsWith("## "))
                    {
                        sectionEnd = j - 1;
                        break;
                    }
                    sectionEnd = j;
                }
                
                errorSectionMap[errorKey] = (anchor, sectionStart + 1, sectionEnd + 1); // Convert to 1-based line numbers
            }
        }
        
        // Now find and replace the errors table
        var result = new StringBuilder();
        bool inErrorsTable = false;
        bool tableHeaderFound = false;
        bool tableSeparatorFound = false;
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            if (line == "## Build Errors")
            {
                inErrorsTable = true;
                result.AppendLine(line);
                continue;
            }
            
            if (inErrorsTable && line.StartsWith("## ") && line != "## Build Errors")
            {
                inErrorsTable = false;
                result.AppendLine(line);
                continue;
            }
            
            if (inErrorsTable)
            {
                if (line == "| File | Line | Col | Code |" && !tableHeaderFound)
                {
                    // Replace with enhanced header
                    result.AppendLine("| File | Line | Col | Code | Section | Lines |");
                    tableHeaderFound = true;
                    continue;
                }
                
                if (line == "|------|------|-----|------|" && !tableSeparatorFound)
                {
                    // Replace with enhanced separator
                    result.AppendLine("|------|------|-----|------|---------|-------|");
                    tableSeparatorFound = true;
                    continue;
                }
                
                // Check if this is a table row (starts and ends with |)
                if (line.StartsWith("| ") && line.EndsWith(" |") && tableHeaderFound && tableSeparatorFound)
                {
                    // Parse the existing row to extract file:line:col
                    var parts = line.Split('|').Select(p => p.Trim()).ToArray();
                    if (parts.Length >= 5) // Should be ["", "File", "Line", "Col", "Code", ""]
                    {
                        var file = parts[1];
                        var lineNum = parts[2];
                        var col = parts[3];
                        var code = parts[4];
                        
                        var errorKey = $"{file}:{lineNum}:{col}";
                        
                        if (errorSectionMap.TryGetValue(errorKey, out var sectionInfo))
                        {
                            var (anchor, startLine, endLine) = sectionInfo;
                            result.AppendLine($"| {file} | {lineNum} | {col} | {code} | {errorKey} | {startLine}-{endLine} |");
                        }
                        else
                        {
                            // Fallback if we can't find the section
                            result.AppendLine($"| {file} | {lineNum} | {col} | {code} |  |  |");
                        }
                        continue;
                    }
                }
            }
            
            result.AppendLine(line);
        }
        
        return result.ToString();
    }
    
    private static string GenerateAnchor(string headerText)
    {
        // GitHub converts headers to lowercase, replaces spaces with hyphens, removes special chars
        // "### test-project/Program.cs:11:27 (lines 7-15):" becomes 
        // "#test-projectprogramcs1127-lines-7-15"
        return "#" + headerText.ToLower()
            .Replace("/", "")
            .Replace("\\", "") 
            .Replace(".", "")
            .Replace(":", "")
            .Replace("(", "")
            .Replace(")", "")
            .Replace(" ", "-")
            .Replace("--", "-")
            .Trim('-');
    }
    
    private List<EnhancedErrorDiagnostic> GenerateEnhancedDiagnostics(string markdown, List<ErrorDiagnostic> originalDiagnostics)
    {
        var lines = markdown.Split('\n');
        var errorSectionMap = new Dictionary<string, (string anchor, string lineRange)>();
        
        // Find all error sections and their line ranges in the document
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("### ") && !line.Contains("("))
            {
                // Parse new format: "### test-project/Program.cs:11:27"
                var errorKey = line.Substring(4).Trim(); // Remove "### "
                var anchor = GenerateAnchor(errorKey);
                
                // Find actual markdown line numbers where this section appears
                int sectionStart = i + 1; // Line after the ### header
                int sectionEnd = i + 1;
                
                // Find the end of this section (next ### or ## or end of document)
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].StartsWith("### ") || lines[j].StartsWith("## "))
                    {
                        sectionEnd = j - 1;
                        break;
                    }
                    sectionEnd = j;
                }
                
                var markdownLineRange = $"{sectionStart + 1}-{sectionEnd + 1}"; // Convert to 1-based line numbers
                errorSectionMap[errorKey] = (anchor, markdownLineRange);
            }
        }
        
        // Create enhanced diagnostics by matching with original diagnostics
        var enhancedDiagnostics = new List<EnhancedErrorDiagnostic>();
        
        foreach (var diagnostic in originalDiagnostics)
        {
            var displayPath = GetDisplayPath(diagnostic.File);
            var errorKey = $"{displayPath}:{diagnostic.Line}:{diagnostic.Column}";

            if (errorSectionMap.TryGetValue(errorKey, out var sectionInfo))
            {
                var (anchor, lineRange) = sectionInfo;
                enhancedDiagnostics.Add(new EnhancedErrorDiagnostic(
                    displayPath,
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.Code,
                    diagnostic.Message,
                    anchor,
                    lineRange
                ));
            }
            else
            {
                // Fallback without enhancement
                enhancedDiagnostics.Add(new EnhancedErrorDiagnostic(
                    displayPath,
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.Code,
                    diagnostic.Message,
                    null,
                    null
                ));
            }
        }
        
        return enhancedDiagnostics;
    }
    
    public async Task WritePromptDocumentAsync(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration, string? command = null, bool concise = false, string baseFileName = "dotnet-build-prompt")
    {
        var markdown = await GeneratePromptDocumentAsync(projects, diagnostics, startTime, duration, command, concise);
        var filePath = Path.Combine(_logsDirectory, $"{baseFileName}.md");
        
        // Generate enhanced JSON diagnostics with anchor and line references if there are errors
        if (diagnostics.Count > 0)
        {
            var enhancedDiagnostics = GenerateEnhancedDiagnostics(markdown, diagnostics);
            WriteEnhancedErrorDiagnostics(enhancedDiagnostics, "dotnet-build-errors-with-lookup-ranges-enhanced");
        }
        
        File.WriteAllText(filePath, markdown);
        
        var description = concise ? "LLM-optimized build results (concise)" : "LLM-optimized build results with detailed error context";
        TrackGeneratedFile($"{baseFileName}.md", description);
    }
    
    private (string context, int startLine, int endLine) GenerateErrorContextWithRange(ErrorDiagnostic diagnostic, bool concise = false)
    {
        try
        {
            // Try to read the file and extract context
            if (File.Exists(diagnostic.File))
            {
                var lines = File.ReadAllLines(diagnostic.File);
                var errorLineIndex = diagnostic.Line - 1; // Convert to 0-based index
                
                if (errorLineIndex >= 0 && errorLineIndex < lines.Length)
                {
                    var sb = new StringBuilder();
                    var contextLines = new List<(int lineNum, string content)>();
                    
                    // Collect context with line numbers (git patch style: more context for fixability)
                    int contextWindow = 4; // Show 4 lines before/after for better fix context
                    
                    // Add lines before
                    for (int i = Math.Max(0, errorLineIndex - contextWindow); i < errorLineIndex; i++)
                    {
                        var line = lines[i];
                        if (concise && IsTestComment(line)) continue;
                        contextLines.Add((i + 1, line)); // Convert to 1-based line numbers
                    }
                    
                    // Add the error line
                    var errorLine = lines[errorLineIndex];
                    var annotation = concise ? $" // ← {diagnostic.Code}" : $" // ← {diagnostic.Code}: {diagnostic.Message}";
                    contextLines.Add((errorLineIndex + 1, errorLine + annotation));
                    
                    // Add lines after
                    for (int i = errorLineIndex + 1; i < Math.Min(lines.Length, errorLineIndex + contextWindow + 1); i++)
                    {
                        var line = lines[i];
                        if (concise && IsTestComment(line)) continue;
                        contextLines.Add((i + 1, line));
                    }
                    
                    // Output the context (clean code without line number comments)
                    foreach (var (lineNum, content) in contextLines)
                    {
                        sb.AppendLine(content);
                    }
                    
                    // Return context with line range metadata
                    if (contextLines.Count > 0)
                    {
                        var startLine = contextLines.First().lineNum;
                        var endLine = contextLines.Last().lineNum;
                        return (sb.ToString().TrimEnd(), startLine, endLine);
                    }
                    
                    return (sb.ToString().TrimEnd(), diagnostic.Line, diagnostic.Line);
                }
            }
        }
        catch (Exception)
        {
            // Fall back to placeholder if file reading fails
        }
        
        // Fallback when file can't be read
        if (concise)
        {
            return ($"// Unable to read {diagnostic.File}:{diagnostic.Line}\n// {diagnostic.Code}", diagnostic.Line, diagnostic.Line);
        }
        return ($"// Unable to read context for {diagnostic.File}:{diagnostic.Line}\n" +
               $"// {diagnostic.Code}: {diagnostic.Message}", diagnostic.Line, diagnostic.Line);
    }
    
    // Backward compatibility method for any remaining calls
    private string GenerateErrorContext(ErrorDiagnostic diagnostic, bool concise = false)
    {
        var (context, _, _) = GenerateErrorContextWithRange(diagnostic, concise);
        return context;
    }
    
    private static bool IsTestComment(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("// This should cause") || 
               trimmed.StartsWith("// Test") ||
               trimmed.StartsWith("// Example");
    }

    private async Task<string> GenerateSymbolReferencesSectionAsync(ErrorDiagnostic diagnostic)
    {
        try
        {
            // Query symbols at the error location
            // Use relative path and actual column position for better symbol resolution
            var relativePath = GetDisplayPath(diagnostic.File);
            var symbols = await _symbolClient.QuerySymbolsAsync(relativePath, diagnostic.Line, diagnostic.Column);

            if (symbols.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("**Referenced symbols:**");

            foreach (var symbol in symbols.Take(10)) // Limit to 10 symbols to avoid clutter
            {
                var symbolInfo = FormatSymbolInfo(symbol);
                sb.AppendLine($"- `{symbol.Name}` - {symbolInfo}");
            }

            return sb.ToString();
        }
        catch
        {
            // If symbol lookup fails, don't add the section
            return "";
        }
    }
    
    private static string FormatSymbolInfo(SymbolResult symbol)
    {
        if (symbol.Location != null)
        {
            var relativePath = GetRelativePath(symbol.Location.File);
            return $"{relativePath}:{symbol.Location.Line},{symbol.Location.Column}";
        }
        
        var baseInfo = symbol.Type switch
        {
            SymbolType.Framework => ".NET Libraries",
            SymbolType.External => "external package", 
            SymbolType.UserDefined => "not found in codebase", // Shouldn't happen if Location is null
            SymbolType.Unknown => "undefined symbol",
            _ => "not found in codebase"
        };

        // Add assembly/package info if available
        if (!string.IsNullOrEmpty(symbol.AssemblyInfo))
        {
            return $"{baseInfo} ({symbol.AssemblyInfo})";
        }

        return baseInfo;
    }
    
    private static string GetRelativePath(string fullPath)
    {
        // Try to make the path relative to current working directory for cleaner output
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var relativePath = Path.GetRelativePath(currentDir, fullPath);
            return relativePath.Replace('\\', '/'); // Use forward slashes for consistency
        }
        catch
        {
            return fullPath;
        }
    }

    private static string GetDisplayPath(string filePath)
    {
        // Convert full path to workspace-relative path for display
        if (string.IsNullOrEmpty(filePath)) return "Unknown";

        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var relativePath = Path.GetRelativePath(currentDir, filePath);

            // If the path goes outside the workspace, just show the filename
            if (relativePath.StartsWith(".."))
                return Path.GetFileName(filePath);

            return relativePath.Replace('\\', '/'); // Use forward slashes for consistency
        }
        catch
        {
            return Path.GetFileName(filePath);
        }
    }

    private static string GetRepositoryName()
    {
        try
        {
            return Path.GetFileName(Directory.GetCurrentDirectory());
        }
        catch
        {
            return "build";
        }
    }
    
    private string GetRepoRelativeDirectory()
    {
        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var logsFullPath = Path.GetFullPath(_logsDirectory);
            var relativePath = Path.GetRelativePath(currentDir, logsFullPath);
            return relativePath.Replace('\\', '/'); // Use forward slashes for consistency
        }
        catch
        {
            return _logsDirectory; // Fallback to original path
        }
    }

    private static string TruncateMessage(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message) || message.Length <= maxLength)
            return message;
        
        return message[..(maxLength - 3)] + "...";
    }
    
    private string ExtractErrorsTable(string promptContent, string promptFilePath)
    {
        var lines = promptContent.Split('\n');
        var tableLines = new List<string>();
        bool inErrorsSection = false;
        bool tableFinished = false;
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            if (line == "## Build Errors")
            {
                inErrorsSection = true;
                continue; // Skip the header itself
            }
            
            if (inErrorsSection && !tableFinished)
            {
                // Stop when we hit the first ### (detailed sections) or another ##
                if (line.StartsWith("### ") || (line.StartsWith("## ") && line != "## Build Errors"))
                {
                    tableFinished = true;
                    break;
                }
                
                tableLines.Add(line);
            }
        }
        
        // Remove trailing empty lines
        while (tableLines.Count > 0 && string.IsNullOrWhiteSpace(tableLines[^1]))
        {
            tableLines.RemoveAt(tableLines.Count - 1);
        }
        
        var tableContent = string.Join('\n', tableLines);
        var repoRelativeDirectory = GetRepoRelativeDirectory();
        return tableContent + $"\n\nPrompt file: {promptFilePath}\nDirectory: {repoRelativeDirectory}\n";
    }
    
    private void TrackGeneratedFile(string fileName, string description)
    {
        var filePath = Path.Combine(_logsDirectory, fileName);
        var sizeBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
        _generatedFiles.Add((fileName, description, sizeBytes));
    }
    
    public void WriteIndexFiles(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration)
    {
        // Generate index files
        var indexMarkdown = GenerateIndexMarkdown(projects, diagnostics, startTime, duration);
        var indexJson = GenerateIndexJson(projects, diagnostics, startTime, duration);
        
        // Write to logs directory first
        var indexMdPath = Path.Combine(_logsDirectory, "index.md");
        var indexJsonPath = Path.Combine(_logsDirectory, "index.json");
        
        File.WriteAllText(indexMdPath, indexMarkdown);
        File.WriteAllText(indexJsonPath, indexJson);
        
        // Move to parent _logs directory
        var parentLogsDir = Path.GetDirectoryName(_logsDirectory);
        var parentIndexMdPath = Path.Combine(parentLogsDir!, "index.md");
        var parentIndexJsonPath = Path.Combine(parentLogsDir!, "index.json");
        
        File.Copy(indexMdPath, parentIndexMdPath, true);
        File.Copy(indexJsonPath, parentIndexJsonPath, true);
    }
    
    private string GenerateIndexMarkdown(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration)
    {
        var sb = new StringBuilder();
        var repoName = GetRepositoryName();
        var totalErrors = diagnostics.Count;
        var totalWarnings = projects.Sum(p => p.Warnings);
        var buildStatus = totalErrors == 0 ? "Success" : "Failed";
        
        sb.AppendLine($"# Latest Build Log Index - {repoName}");
        sb.AppendLine();
        sb.AppendLine($"**Build Status:** {buildStatus}  ");
        sb.AppendLine($"**Timestamp:** {startTime:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"**Duration:** {duration.TotalSeconds:F1}s  ");
        sb.AppendLine($"**Directory:** `{_buildTimestamp}/`");
        sb.AppendLine();
        
        sb.AppendLine("## Quick Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Projects:** {projects.Count}");
        sb.AppendLine($"- **Errors:** {totalErrors}");
        sb.AppendLine($"- **Warnings:** {totalWarnings}");
        sb.AppendLine();
        
        sb.AppendLine("## Key Files");
        sb.AppendLine();
        sb.AppendLine("| File | Description | Size |");
        sb.AppendLine("|------|-------------|------|");
        
        // Order files by importance for LLMs
        var priorityFiles = new[]
        {
            ("dotnet-build-prompt.md", "LLM-optimized build results with error context"),
            ("dotnet-build-errors.json", "Structured error data"),
            ("dotnet-build-projects.json", "Project results summary"),
            ("dotnet-build-errors.md", "Human-readable errors table")
        };
        
        foreach (var (fileName, description) in priorityFiles)
        {
            var fileInfo = _generatedFiles.FirstOrDefault(f => f.fileName == fileName);
            var size = fileInfo.sizeBytes > 0 ? FormatFileSize(fileInfo.sizeBytes) : "0 B";
            var fullPath = $"{_buildTimestamp}/{fileName}";
            sb.AppendLine($"| [{fileName}]({fullPath}) | {description} | {size} |");
        }
        
        sb.AppendLine();
        sb.AppendLine("## All Generated Files");
        sb.AppendLine();
        foreach (var (fileName, description, sizeBytes) in _generatedFiles.OrderBy(f => f.fileName))
        {
            var size = FormatFileSize(sizeBytes);
            var fullPath = $"{_buildTimestamp}/{fileName}";
            sb.AppendLine($"- [`{fileName}`]({fullPath}) - {description} ({size})");
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"*Generated by MarkdownTableLogger at {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");
        
        return sb.ToString();
    }
    
    private string GenerateIndexJson(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration)
    {
        var indexData = new
        {
            Repository = GetRepositoryName(),
            BuildTimestamp = startTime,
            Duration = duration.TotalSeconds,
            Directory = _buildTimestamp,
            Status = new
            {
                Success = diagnostics.Count == 0,
                ErrorCount = diagnostics.Count,
                WarningCount = projects.Sum(p => p.Warnings),
                ProjectCount = projects.Count
            },
            KeyFiles = new
            {
                PromptFile = $"{_buildTimestamp}/dotnet-build-prompt.md",
                ErrorsJson = $"{_buildTimestamp}/dotnet-build-errors.json",
                ProjectsJson = $"{_buildTimestamp}/dotnet-build-projects.json",
                ErrorsMarkdown = $"{_buildTimestamp}/dotnet-build-errors.md"
            },
            AllFiles = _generatedFiles.Select(f => new
            {
                FileName = f.fileName,
                Description = f.description,
                SizeBytes = f.sizeBytes,
                RelativePath = $"{_buildTimestamp}/{f.fileName}"
            }).OrderBy(f => f.FileName).ToArray(),
            GeneratedAt = DateTime.Now
        };
        
        return JsonSerializer.Serialize(indexData, _jsonOptions);
    }
    
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
