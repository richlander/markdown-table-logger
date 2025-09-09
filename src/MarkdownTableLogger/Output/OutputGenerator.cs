using System.Text;
using System.Text.Json;
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
    private readonly SymbolIndexerClient _symbolClient;
    
    public OutputGenerator()
    {
        _buildTimestamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
        _logsDirectory = Path.Combine("_logs", _buildTimestamp);
        Directory.CreateDirectory(_logsDirectory);
        _symbolClient = new SymbolIndexerClient(new SymbolPidFileManager());
    }

    public void WriteProjectResults(List<ProjectResult> results, string baseFileName)
    {
        var json = JsonSerializer.Serialize(results, _jsonOptions);
        var markdown = GenerateProjectResultsMarkdown(results);
        
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.json"), json);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.md"), markdown);
    }

    public void WriteErrorDiagnostics(List<ErrorDiagnostic> errors, string baseFileName)
    {
        var json = JsonSerializer.Serialize(errors, _jsonOptions);
        var markdown = GenerateErrorDiagnosticsMarkdown(errors);
        
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.json"), json);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.md"), markdown);
    }

    public void WriteErrorTypeSummary(List<ErrorTypeSummary> summary, string baseFileName)
    {
        var json = JsonSerializer.Serialize(summary, _jsonOptions);
        var markdown = GenerateErrorTypeSummaryMarkdown(summary);
        
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.json"), json);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.md"), markdown);
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
                var message = TruncateMessage(error.Message ?? "", 50);
                sb.AppendLine($"| {error.File} | {error.Line} | {error.Column} | {error.Code} | {message} |");
            }
        }
        else
        {
            sb.AppendLine("| File | Line | Col | Code |");
            sb.AppendLine("|------|------|-----|------|");
            
            foreach (var error in errors)
            {
                sb.AppendLine($"| {error.File} | {error.Line} | {error.Column} | {error.Code} |");
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

    public string GeneratePromptDocument(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration, string? command = null, bool concise = false)
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
        sb.AppendLine("This document contains build results in markdown tables. A peephole view for each error is also provided to aid comprehension of the problem.");
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
        
        // Diagnostics table
        sb.AppendLine("## Build Errors");
        sb.AppendLine();
        sb.AppendLine("| File | Line | Col | Code |");
        sb.AppendLine("|------|------|-----|------|");
        
        foreach (var diagnostic in diagnostics)
        {
            sb.AppendLine($"| {diagnostic.File} | {diagnostic.Line} | {diagnostic.Column} | {diagnostic.Code} |");
        }
        sb.AppendLine();
        
        // Context blocks for each error (only if there are errors)
        if (diagnostics.Count > 0)
        {
            foreach (var diagnostic in diagnostics)
            {
                var (context, startLine, endLine) = GenerateErrorContextWithRange(diagnostic, concise);
                
                // Include line:column and range in heading for LLM precision
                sb.AppendLine($"### {diagnostic.File}:{diagnostic.Line}:{diagnostic.Column} (lines {startLine}-{endLine}):");
                sb.AppendLine();
                sb.AppendLine("```csharp");
                sb.AppendLine(context);
                sb.AppendLine("```");
                
                // Add symbol references section
                var symbolsSection = GenerateSymbolReferencesSection(diagnostic);
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
    
    public void WritePromptDocument(List<ProjectResult> projects, List<ErrorDiagnostic> diagnostics, DateTime startTime, TimeSpan duration, string? command = null, bool concise = false, string baseFileName = "dotnet-build-prompt")
    {
        var markdown = GeneratePromptDocument(projects, diagnostics, startTime, duration, command, concise);
        File.WriteAllText(Path.Combine(_logsDirectory, $"{baseFileName}.md"), markdown);
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

    private string GenerateSymbolReferencesSection(ErrorDiagnostic diagnostic)
    {
        try
        {
            // Query symbols at the error location
            // Convert path to relative path for indexer (remove directory prefixes like "test-project/")
            var fileName = Path.GetFileName(diagnostic.File);
            var symbols = _symbolClient.QuerySymbolsAsync(fileName, diagnostic.Line, 0).Result;
            
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

    private static string TruncateMessage(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message) || message.Length <= maxLength)
            return message;
        
        return message[..(maxLength - 3)] + "...";
    }
}