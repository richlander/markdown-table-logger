using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MarkdownTableLogger.Models;
using MarkdownTableLogger.Output;
using System.Text.RegularExpressions;

namespace MarkdownTableLogger;

public class MarkdownTableBuildLogger : Logger
{
    private readonly List<ProjectResult> _projectResults = new();
    private readonly List<ErrorDiagnostic> _diagnostics = new();
    private readonly Dictionary<string, int> _currentProjectErrors = new();
    private readonly Dictionary<string, int> _currentProjectWarnings = new();
    private string? _workspaceRoot;
    private readonly OutputGenerator _outputGenerator = new();
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;
    private string _mode = "projects"; // Default mode
    private bool _showHelpOnly = false;
    private bool _showStatus = false; // Status column (redundant with error/warning counts) - DEFAULT OFF
    private bool _showDescription = false; // Description column for error types - DEFAULT OFF (LLM-first)
    private bool _showMessage = false; // Message column for diagnostics - DEFAULT OFF (LLM-first)
    private DateTime _buildStartTime;
    private TimeSpan _buildDuration;
    private string _buildCommand = "dotnet build"; // Default assumption
    
    private static readonly Regex ProjectNameRegex = new(@"([^/\\]+)\.csproj$", RegexOptions.Compiled);

    public override void Initialize(IEventSource eventSource)
    {
        // Parse logger parameters for mode selection
        ParseParameters();
        
        eventSource.ProjectStarted += OnProjectStarted;
        eventSource.ProjectFinished += OnProjectFinished;
        eventSource.ErrorRaised += OnErrorRaised;
        eventSource.WarningRaised += OnWarningRaised;
        eventSource.BuildFinished += OnBuildFinished;
        eventSource.MessageRaised += OnMessageRaised;
        eventSource.BuildStarted += OnBuildStarted;
    }

    private void ParseParameters()
    {
        if (!string.IsNullOrEmpty(Parameters))
        {
            var parameters = Parameters.Split(';');
            foreach (var param in parameters)
            {
                var parts = param.Split('=');
                if (parts.Length == 2 && parts[0].Trim().ToLower() == "mode")
                {
                    _mode = parts[1].Trim().ToLower();
                }
                else if (parts.Length == 2 && parts[0].Trim().ToLower() == "columns")
                {
                    ParseColumnSettings(parts[1].Trim().ToLower());
                }
                else if (parts.Length == 1 && parts[0].Trim().ToLower() == "help")
                {
                    _showHelpOnly = true;
                    ShowHelp();
                    return;
                }
            }
        }
    }

    private void ParseColumnSettings(string columnSettings)
    {
        // When columns= is explicitly specified, start with everything OFF
        // Then only turn on what's requested
        _showStatus = false;
        _showDescription = false;
        _showMessage = false;
        
        if (string.IsNullOrEmpty(columnSettings))
        {
            // columns= with empty value - keep everything OFF
            return;
        }
        
        var columns = columnSettings.Split(',');
        foreach (var column in columns)
        {
            switch (column.Trim())
            {
                case "status":
                    _showStatus = true;
                    break;
                case "description":
                    _showDescription = true;
                    break;
                case "message":
                    _showMessage = true;
                    break;
                case "all":
                    _showStatus = true;
                    _showDescription = true;
                    _showMessage = true;
                    break;
            }
        }
    }

    private void ShowHelp()
    {
        Console.WriteLine("MarkdownTableLogger Modes:");
        Console.WriteLine();
        Console.WriteLine("projects   - Project results table (default)");
        Console.WriteLine("errors     - Error/warning diagnostics table only");
        Console.WriteLine("types      - Error/warning type summary only");
        Console.WriteLine("minimal    - Single line status only");
        Console.WriteLine("prompt     - Token-optimized LLM document (default for AI)");
        Console.WriteLine("prompt-verbose - Human-readable document with full error messages");
        Console.WriteLine();
        Console.WriteLine("Optional Columns:");
        Console.WriteLine();
        Console.WriteLine("status       - Show ✅/❌ status column (redundant with error counts)");
        Console.WriteLine("description  - Show error code descriptions (default: off)");
        Console.WriteLine("message      - Show full error messages (default: off)");
        Console.WriteLine("all          - Show all optional columns");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  --logger:\"MarkdownTableLogger.dll;mode=<mode>\"");
        Console.WriteLine("  --logger:\"MarkdownTableLogger.dll;mode=<mode>;columns=<cols>\"");
        Console.WriteLine("  --logger:\"StructuredLogger.dll;columns=message,description\"");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  mode=minimal                    # Single line");
        Console.WriteLine("  mode=projects;columns=status    # Projects with status icons");
        Console.WriteLine("  mode=errors                     # Minimal error table");
        Console.WriteLine("  mode=errors;columns=message     # Errors with full messages");
        Console.WriteLine("  mode=types;columns=description  # Types with descriptions");
        Console.WriteLine("  mode=prompt                     # Token-optimized LLM document");
        Console.WriteLine("  mode=prompt-verbose             # Human-readable with full messages");
    }

    private void OnProjectStarted(object sender, ProjectStartedEventArgs e)
    {
        _workspaceRoot ??= FindWorkspaceRoot(e.ProjectFile);
        var projectName = ExtractProjectName(e.ProjectFile);
        
        _currentProjectErrors[projectName] = 0;
        _currentProjectWarnings[projectName] = 0;
    }

    private void OnProjectFinished(object sender, ProjectFinishedEventArgs e)
    {
        var projectName = ExtractProjectName(e.ProjectFile);
        var errorCount = _currentProjectErrors.GetValueOrDefault(projectName, 0);
        var warningCount = _currentProjectWarnings.GetValueOrDefault(projectName, 0);
        
        // Only add the final result - remove any previous entries for this project
        _projectResults.RemoveAll(p => p.Project == projectName);
        
        _projectResults.Add(new ProjectResult(
            Project: projectName,
            Errors: errorCount,
            Warnings: warningCount,
            Succeeded: e.Succeeded
        ));
    }

    private void OnErrorRaised(object sender, BuildErrorEventArgs e)
    {
        var projectName = ExtractProjectName(e.ProjectFile);
        _currentProjectErrors[projectName] = _currentProjectErrors.GetValueOrDefault(projectName, 0) + 1;
        
        var relativePath = MakeWorkspaceRelative(e.File);
        
        _diagnostics.Add(new ErrorDiagnostic(
            File: relativePath,
            Line: e.LineNumber,
            Column: e.ColumnNumber,
            Code: e.Code ?? "Unknown",
            Message: e.Message
        ));
    }

    private void OnWarningRaised(object sender, BuildWarningEventArgs e)
    {
        var projectName = ExtractProjectName(e.ProjectFile);
        _currentProjectWarnings[projectName] = _currentProjectWarnings.GetValueOrDefault(projectName, 0) + 1;
        
        var relativePath = MakeWorkspaceRelative(e.File);
        
        _diagnostics.Add(new ErrorDiagnostic(
            File: relativePath,
            Line: e.LineNumber,
            Column: e.ColumnNumber,
            Code: e.Code ?? "Unknown",
            Message: e.Message
        ));
    }

    private void OnBuildStarted(object sender, BuildStartedEventArgs e)
    {
        _buildStartTime = DateTime.Now;
        
        // Try to capture some information about the build context
        // MSBuild doesn't give us the original CLI command, but we can infer some things
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_CLI_COMMAND")))
        {
            _buildCommand = $"dotnet {Environment.GetEnvironmentVariable("DOTNET_CLI_COMMAND")}";
        }
        else
        {
            // Fallback: try to reconstruct from available info
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 0 && args[0].Contains("dotnet"))
            {
                _buildCommand = string.Join(" ", args.Where(a => !a.Contains("MarkdownTableLogger")));
            }
        }
    }

    private void OnMessageRaised(object sender, BuildMessageEventArgs e)
    {
        // Suppress noisy messages but allow important ones through
        if (ShouldSuppressMessage(e.Message))
        {
            return;
        }
        
        // For important messages, we could optionally show them
        // Console.WriteLine($"Info: {e.Message}");
    }

    private void OnBuildFinished(object sender, BuildFinishedEventArgs e)
    {
        _buildDuration = DateTime.Now - _buildStartTime;
        
        try
        {
            GenerateOutputFiles();
            WriteConsoleOutput();
        }
        catch (Exception ex)
        {
            WriteLineWithColor($"MarkdownTableLogger error: {ex.Message}", ConsoleColor.Red);
        }
    }

    private static bool ShouldSuppressMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return true;
        
        // Suppress common noisy messages
        var suppressPatterns = new[]
        {
            "Restore complete",
            "Determining projects to restore",
            "All projects are up-to-date for restore",
            "You are using a preview version of .NET",
            "NETSDK1057",
            "warning NU1903" // Suppress known vulnerability warnings for prototype
        };
        
        return suppressPatterns.Any(pattern => message.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private void GenerateOutputFiles()
    {
        // Always generate project results
        _outputGenerator.WriteProjectResults(_projectResults, "dotnet-build-projects");
        
        // Always generate diagnostic type summary if there are errors or warnings  
        if (_diagnostics.Count > 0)
        {
            var diagnosticTypeSummary = _diagnostics
                .GroupBy(d => d.Code)
                .Select(g => new ErrorTypeSummary(
                    Code: g.Key,
                    Count: g.Count(),
                    Description: ErrorCodeDescriptions.GetDescription(g.Key)
                ))
                .OrderByDescending(e => e.Count)
                .ToList();
            
            _outputGenerator.WriteErrorTypeSummary(diagnosticTypeSummary, "dotnet-build-error-types");
            
            // Always generate prompt document (source of truth for enhanced table)
            _outputGenerator.WritePromptDocument(_projectResults, _diagnostics, _buildStartTime, _buildDuration, _buildCommand, true, "dotnet-build-prompt");
            
            // Generate verbose prompt document if requested
            if (_mode == "prompt-verbose")
            {
                _outputGenerator.WritePromptDocument(_projectResults, _diagnostics, _buildStartTime, _buildDuration, _buildCommand, false, "dotnet-build-prompt-verbose");
            }
            
            // Extract enhanced errors table from prompt document for standalone use
            _outputGenerator.WriteEnhancedErrorsTable("dotnet-build-prompt", "dotnet-build-errors-with-lookup-ranges");
            
            // Also generate basic JSON diagnostics for backward compatibility
            _outputGenerator.WriteErrorDiagnostics(_diagnostics, "dotnet-build-errors");
        }
        
        // Generate index files and move them to parent _logs directory
        _outputGenerator.WriteIndexFiles(_projectResults, _diagnostics, _buildStartTime, _buildDuration);
    }

    private void WriteConsoleOutput()
    {
        if (_showHelpOnly) return; // Don't show output if help was shown
        
        switch (_mode)
        {
            case "projects":
                WriteProjectsMode();
                break;
            case "errors":
                WriteErrorsMode();
                break;
            case "types":
                WriteTypesMode();
                break;
            case "minimal":
                WriteMinimalMode();
                break;
            case "prompt":
                WritePromptMode();
                break;
            case "prompt-verbose":
                WritePromptVerboseMode();
                break;
            default:
                WriteProjectsMode();
                break;
        }
    }

    private void WriteProjectsMode()
    {
        // Read from generated file
        var filePath = Path.Combine(_outputGenerator.LogsDirectory, "dotnet-build-projects.md");
        if (File.Exists(filePath))
        {
            Console.WriteLine(File.ReadAllText(filePath));
        }
        else
        {
            Console.WriteLine("No project results available.");
        }
    }

    private void WriteErrorsMode()
    {
        // Read enhanced errors table from generated file
        var filePath = Path.Combine(_outputGenerator.LogsDirectory, "dotnet-build-errors-with-lookup-ranges.md");
        if (File.Exists(filePath))
        {
            Console.WriteLine(File.ReadAllText(filePath));
        }
        else
        {
            Console.WriteLine("No errors found.");
        }
    }

    private void WriteTypesMode()
    {
        // Read from generated file
        var filePath = Path.Combine(_outputGenerator.LogsDirectory, "dotnet-build-error-types.md");
        if (File.Exists(filePath))
        {
            Console.WriteLine(File.ReadAllText(filePath));
        }
        else
        {
            Console.WriteLine("No error types found.");
        }
    }

    private void WriteMinimalMode()
    {
        var totalErrors = _diagnostics.Count(d => !d.Code.StartsWith("CS1998")); // Exclude warnings for error count
        var totalWarnings = _diagnostics.Count(d => d.Code.StartsWith("CS1998")); // Simple warning detection
        var failedProjects = _projectResults.Count(p => !p.Succeeded);
        
        if (failedProjects > 0)
        {
            Console.WriteLine($"❌ {failedProjects} failed, {totalErrors} errors, {totalWarnings} warnings");
        }
        else
        {
            Console.WriteLine($"✅ Build succeeded");
        }
    }

    private void WritePromptMode()
    {
        // Read from generated file
        var filePath = Path.Combine(_outputGenerator.LogsDirectory, "dotnet-build-prompt.md");
        if (File.Exists(filePath))
        {
            Console.WriteLine(File.ReadAllText(filePath));
        }
        else
        {
            Console.WriteLine("No prompt document generated.");
        }
    }

    private void WritePromptVerboseMode()
    {
        // Read from generated file
        var filePath = Path.Combine(_outputGenerator.LogsDirectory, "dotnet-build-prompt-verbose.md");
        if (File.Exists(filePath))
        {
            Console.WriteLine(File.ReadAllText(filePath));
        }
        else
        {
            Console.WriteLine("No verbose prompt document generated.");
        }
    }


    private static string ExtractProjectName(string? projectFile)
    {
        if (string.IsNullOrEmpty(projectFile)) return "Unknown";
        
        var match = ProjectNameRegex.Match(projectFile);
        return match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(projectFile);
    }

    private string MakeWorkspaceRelative(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return "Unknown";
        if (string.IsNullOrEmpty(_workspaceRoot)) return Path.GetFileName(filePath);
        
        var relativePath = Path.GetRelativePath(_workspaceRoot, filePath);
        return relativePath.StartsWith("..") ? Path.GetFileName(filePath) : relativePath;
    }

    private static string? FindWorkspaceRoot(string? projectFile)
    {
        if (string.IsNullOrEmpty(projectFile)) return Directory.GetCurrentDirectory();
        
        var dir = Path.GetDirectoryName(projectFile);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0 ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        
        return Directory.GetCurrentDirectory();
    }

    private static void WriteLineWithColor(string message, ConsoleColor color)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }
}