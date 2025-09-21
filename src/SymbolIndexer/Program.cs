using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SymbolIndexer;

internal static class Program
{
    private const int DefaultStartupTimeoutSeconds = 5;
    private const int DefaultIdleTimeoutSeconds = 300;
    private const int DefaultLogListCount = 20;

    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("dotnet-logs - workspace-aware build log CLI with symbol daemon management");

        root.AddCommand(CreateDaemonCommand());
        root.AddCommand(CreateWorkspaceCommand());
        root.AddCommand(CreateLogsCommand());
        root.AddCommand(CreateSymbolsCommand());

        return await root.InvokeAsync(args);
    }

    private static Command CreateDaemonCommand()
    {
        var daemonCommand = new Command("daemon", "Manage the background symbol indexer daemon");

        var idleTimeoutOption = new Option<int?>("--idle-timeout", description: "Idle timeout in seconds before daemon exits (defaults to 300)");
        var jsonOption = new Option<bool>("--json", description: "Output JSON instead of text");
        var quietOption = new Option<bool>("--quiet", () => true, "Suppress console logging when running the daemon");

        var startCommand = new Command("start", "Ensure the daemon for this workspace is running");
        startCommand.AddOption(idleTimeoutOption);
        startCommand.AddOption(jsonOption);
        startCommand.SetHandler(async (int? idleSeconds, bool json) =>
        {
            var info = await EnsureDaemonAsync(idleSeconds);
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(info, JsonSourceGenerationContext.Default.WorkspaceInfo));
            }
            else
            {
                WriteWorkspaceInfo(info, prefix: "Daemon running");
            }
        }, idleTimeoutOption, jsonOption);

        var runCommand = new Command("run", "Run the daemon in the foreground (internal use)");
        runCommand.AddOption(idleTimeoutOption);
        runCommand.AddOption(quietOption);
        runCommand.SetHandler(async (int? idleSeconds, bool quiet) =>
        {
            var idleTimeout = idleSeconds.HasValue && idleSeconds.Value > 0
                ? TimeSpan.FromSeconds(idleSeconds.Value)
                : TimeSpan.FromSeconds(DefaultIdleTimeoutSeconds);

            await RunDaemonAsync(idleTimeout, quiet);
        }, idleTimeoutOption, quietOption);

        var shutdownCommand = new Command("shutdown", "Request the daemon stop");
        shutdownCommand.SetHandler(async () => await ShutdownDaemonAsync());

        var statusCommand = new Command("status", "Show daemon status without starting it");
        statusCommand.AddOption(jsonOption);
        statusCommand.SetHandler((bool json) =>
        {
            var pidManager = new SymbolPidFileManager();
            var pidFile = pidManager.FindRunningServer();
            var info = BuildWorkspaceInfo(pidFile, pidManager);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(info, JsonSourceGenerationContext.Default.WorkspaceInfo));
            }
            else if (info.IsRunning)
            {
                WriteWorkspaceInfo(info, prefix: "Daemon status");
            }
            else
            {
                Console.WriteLine("Daemon not running for this workspace.");
                Console.WriteLine($"PID directory: {info.LocalPidDirectory}");
            }
        }, jsonOption);

        daemonCommand.AddCommand(startCommand);
        daemonCommand.AddCommand(runCommand);
        daemonCommand.AddCommand(shutdownCommand);
        daemonCommand.AddCommand(statusCommand);

        return daemonCommand;
    }

    private static Command CreateWorkspaceCommand()
    {
        var workspaceCommand = new Command("workspace", "Workspace utilities");

        var jsonOption = new Option<bool>("--json", () => true, "Output JSON (default)");
        var ensureOption = new Option<bool>("--ensure", () => true, "Ensure the daemon is running before returning info");
        var idleTimeoutOption = new Option<int?>("--idle-timeout", "Idle timeout in seconds to use when auto-starting the daemon");

        var infoCommand = new Command("info", "Return daemon connection info for this workspace");
        infoCommand.AddOption(jsonOption);
        infoCommand.AddOption(ensureOption);
        infoCommand.AddOption(idleTimeoutOption);
        infoCommand.SetHandler(async (bool json, bool ensure, int? idleSeconds) =>
        {
            WorkspaceInfo info;
            if (ensure)
            {
                info = await EnsureDaemonAsync(idleSeconds);
            }
            else
            {
                var pidManager = new SymbolPidFileManager();
                info = BuildWorkspaceInfo(pidManager.FindRunningServer(), pidManager);
            }

            if (!json)
            {
                WriteWorkspaceInfo(info, prefix: "Workspace info");
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(info, JsonSourceGenerationContext.Default.WorkspaceInfo));
            }
        }, jsonOption, ensureOption, idleTimeoutOption);

        workspaceCommand.AddCommand(infoCommand);

        return workspaceCommand;
    }

    private static Command CreateLogsCommand()
    {
        var logsCommand = new Command("logs", "Explore persisted build logs");

        var jsonOption = new Option<bool>("--json", description: "Output JSON instead of text");
        var countOption = new Option<int>("--count", () => DefaultLogListCount, "Number of entries to list");

        var listCommand = new Command("list", "List recent build logs");
        listCommand.AddOption(jsonOption);
        listCommand.AddOption(countOption);
        listCommand.SetHandler((bool json, int count) =>
        {
            var entries = LoadManifestEntries();
            if (entries.Count == 0)
            {
                Console.WriteLine("No logs have been recorded yet.");
                return;
            }

            var limited = entries.Take(Math.Max(1, count)).ToList();
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(limited.ToArray(), JsonSourceGenerationContext.Default.LogManifestEntryArray));
                return;
            }

            for (var i = 0; i < limited.Count; i++)
            {
                var entry = limited[i];
                var statusSymbol = entry.Success ? "✔" : "✘";
                Console.WriteLine($"@{{{i}}} {statusSymbol} {entry.Timestamp:u} errors={entry.ErrorCount} warnings={entry.WarningCount} mode={entry.Mode} commit={entry.GitCommit ?? "(none)"}");
            }
        }, jsonOption, countOption);

        var logRefArgument = new Argument<string>("ref", () => "@{0}", "Log reference (e.g., @{0}, @{2}, latest, or directory name)");
        var fileOption = new Option<string>("--file", () => "prompt", "File to show: prompt, prompt-verbose, errors, index");

        var showCommand = new Command("show", "Print a file from a specific log run");
        showCommand.AddArgument(logRefArgument);
        showCommand.AddOption(fileOption);
        showCommand.SetHandler((string reference, string fileKind) =>
        {
            if (!TryResolveLog(reference, out var entry, out _))
            {
                Console.Error.WriteLine($"No log found matching '{reference}'.");
                return;
            }

            var path = ResolveLogFilePath(entry, fileKind);
            if (path == null)
            {
                Console.Error.WriteLine($"Unsupported file option '{fileKind}'.");
                return;
            }

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File not found: {path}");
                return;
            }

            Console.WriteLine(File.ReadAllText(path));
        }, logRefArgument, fileOption);

        var pathCommand = new Command("path", "Print the absolute path to a file in a log run");
        pathCommand.AddArgument(logRefArgument);
        pathCommand.AddOption(fileOption);
        pathCommand.SetHandler((string reference, string fileKind) =>
        {
            if (!TryResolveLog(reference, out var entry, out _))
            {
                Console.Error.WriteLine($"No log found matching '{reference}'.");
                return;
            }

            var path = ResolveLogFilePath(entry, fileKind);
            if (path == null)
            {
                Console.Error.WriteLine($"Unsupported file option '{fileKind}'.");
                return;
            }

            Console.WriteLine(path);
        }, logRefArgument, fileOption);

        logsCommand.AddCommand(listCommand);
        logsCommand.AddCommand(showCommand);
        logsCommand.AddCommand(pathCommand);

        return logsCommand;
    }

    private static Command CreateSymbolsCommand()
    {
        var symbolsCommand = new Command("symbols", "Symbol utilities");

        var queryCommand = new Command("query", "Query symbol locations via the daemon");
        var fileOption = new Option<string>("--file") { IsRequired = true };
        var lineOption = new Option<int>("--line") { IsRequired = true };
        var columnOption = new Option<int>("--column", () => 0, "Column to inspect (0 queries entire line)");

        queryCommand.AddOption(fileOption);
        queryCommand.AddOption(lineOption);
        queryCommand.AddOption(columnOption);

        queryCommand.SetHandler(async (string file, int line, int column) =>
        {
            await EnsureDaemonAsync(null);
            var pidManager = new SymbolPidFileManager();
            var client = new SymbolIndexerClient(pidManager);
            var results = await client.QuerySymbolsAsync(file, line, column);

            if (results.Count == 0)
            {
                Console.WriteLine("No symbols found.");
                return;
            }

            foreach (var result in results)
            {
                if (result.Location != null)
                {
                    Console.WriteLine($"{result.Name} -> {result.Location.File}:{result.Location.Line},{result.Location.Column}");
                }
                else
                {
                    Console.WriteLine($"{result.Name} -> {result.Type}{FormatAssemblyInfo(result.AssemblyInfo)}");
                }
            }
        }, fileOption, lineOption, columnOption);

        symbolsCommand.AddCommand(queryCommand);

        return symbolsCommand;
    }

    private static async Task RunDaemonAsync(TimeSpan idleTimeout, bool quiet)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                var minimumLevel = quiet ? LogLevel.Warning : LogLevel.Information;
                logging.SetMinimumLevel(minimumLevel);
                if (!quiet)
                {
                    logging.AddSimpleConsole(options =>
                    {
                        options.SingleLine = true;
                        options.TimestampFormat = "HH:mm:ss ";
                    });
                }
            })
            .ConfigureServices(services =>
            {
                services.Configure<SymbolIndexerOptions>(options => options.IdleTimeout = idleTimeout);
                services.AddSingleton<ISymbolIndexer, UsingBasedSymbolIndexer>();
                services.AddSingleton<IPidFileManager, SymbolPidFileManager>();
                services.AddHostedService<SymbolIndexerService>();
            })
            .Build();

        await host.RunAsync();
    }

    private static async Task<WorkspaceInfo> EnsureDaemonAsync(int? idleSeconds)
    {
        var pidManager = new SymbolPidFileManager();
        var running = pidManager.FindRunningServer();
        if (running != null)
        {
            return BuildWorkspaceInfo(running, pidManager);
        }

        LaunchDaemonBackground(idleSeconds);

        var sw = Stopwatch.StartNew();
        SymbolPidFile? pidFile = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(DefaultStartupTimeoutSeconds))
        {
            await Task.Delay(200);
            pidFile = pidManager.FindRunningServer();
            if (pidFile != null)
            {
                break;
            }
        }

        if (pidFile == null)
        {
            throw new InvalidOperationException("Daemon failed to start within timeout.");
        }

        return BuildWorkspaceInfo(pidFile, pidManager);
    }

    private static async Task ShutdownDaemonAsync()
    {
        var pidManager = new SymbolPidFileManager();
        var pidFile = pidManager.FindRunningServer();
        if (pidFile == null)
        {
            Console.WriteLine("Daemon is not running.");
            return;
        }

        var client = new SymbolIndexerClient(pidManager);
        if (await client.ShutdownAsync())
        {
            Console.WriteLine($"Shutdown requested for PID {pidFile.ProcessId}.");
            return;
        }

        try
        {
            var process = Process.GetProcessById(pidFile.ProcessId);
            process.Kill(entireProcessTree: true);
            Console.WriteLine($"Forcefully terminated daemon (PID {pidFile.ProcessId}).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to terminate daemon: {ex.Message}");
        }
    }

    private static WorkspaceInfo BuildWorkspaceInfo(SymbolPidFile? pidFile, SymbolPidFileManager pidManager)
    {
        return new WorkspaceInfo
        {
            IsRunning = pidFile != null,
            Pid = pidFile?.ProcessId,
            PipeName = pidFile?.PipeName,
            ServerPath = pidFile?.ServerPath,
            PidFilePath = pidFile?.Path,
            WorkingDirectory = Directory.GetCurrentDirectory(),
            LocalPidDirectory = pidManager.GetPidFileDirectory(),
            TimestampUtc = DateTime.UtcNow
        };
    }

    private static void WriteWorkspaceInfo(WorkspaceInfo info, string prefix)
    {
        if (!info.IsRunning)
        {
            Console.WriteLine("Daemon is not running.");
            Console.WriteLine($"PID directory: {info.LocalPidDirectory}");
            return;
        }

        Console.WriteLine(prefix);
        Console.WriteLine($"PID: {info.Pid}");
        Console.WriteLine($"Pipe: {info.PipeName}");
        Console.WriteLine($"PID file: {info.PidFilePath}");
        Console.WriteLine($"Working directory: {info.WorkingDirectory}");
    }

    private static void LaunchDaemonBackground(int? idleSeconds)
    {
        var executablePath = ResolveSelfExecutablePath();
        var arguments = new StringBuilder("daemon run --quiet");
        if (idleSeconds.HasValue && idleSeconds.Value > 0)
        {
            arguments.Append(" --idle-timeout ");
            arguments.Append(idleSeconds.Value.ToString(CultureInfo.InvariantCulture));
        }

        var workingDirectory = Directory.GetCurrentDirectory();

        if (OperatingSystem.IsWindows())
        {
            var quotedExe = QuoteForCmd(executablePath);
            var cmdArgs = $"/c start \"dotnet-logs\" {quotedExe} {arguments}";
            var startInfo = new ProcessStartInfo("cmd.exe", cmdArgs)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("Failed to start daemon process.");
            }
        }
        else
        {
            var quotedExe = QuoteForBash(executablePath);
            var command = $"nohup {quotedExe} {arguments} >/dev/null 2>&1 &";
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = workingDirectory,
                Arguments = "-c \"" + command.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            startInfo.EnvironmentVariables["DOTNET_LOGS_DETACHED"] = "1";

            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("Failed to start daemon process.");
            }
        }
    }

    private static string ResolveSelfExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) && !Path.GetFileName(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var nearbyExecutable = Path.Combine(Path.GetDirectoryName(processPath) ?? string.Empty, GetExecutableFileName());
            if (File.Exists(nearbyExecutable))
            {
                return Path.GetFullPath(nearbyExecutable);
            }

            if (IsSelfExecutable(processPath))
            {
                return Path.GetFullPath(processPath);
            }
        }

        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, GetExecutableFileName());
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        throw new InvalidOperationException("Unable to locate dotnet-logs executable path.");
    }

    private static bool IsSelfExecutable(string path)
    {
        var fileName = Path.GetFileName(path);
        if (OperatingSystem.IsWindows())
        {
            return string.Equals(fileName, "dotnet-logs.exe", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(fileName, "dotnet-logs", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExecutableFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "dotnet-logs.exe";
        }

        return "dotnet-logs";
    }

    private static string QuoteForCmd(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string QuoteForBash(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    private static string FormatAssemblyInfo(string? assemblyInfo)
    {
        return string.IsNullOrEmpty(assemblyInfo) ? string.Empty : $" ({assemblyInfo})";
    }

    private static List<LogManifestEntry> LoadManifestEntries()
    {
        var manifestPath = Path.Combine("_logs", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize(json, JsonSourceGenerationContext.Default.LogManifest);
            return manifest?.Entries?.OrderByDescending(e => e.Timestamp).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool TryResolveLog(string reference, out LogManifestEntry entry, out int index)
    {
        var entries = LoadManifestEntries();
        entry = null!;
        index = -1;

        if (entries.Count == 0)
        {
            return false;
        }

        var normalized = reference.Trim();
        if (string.Equals(normalized, "latest", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(normalized))
        {
            entry = entries[0];
            index = 0;
            return true;
        }

        if (normalized.StartsWith("@{" , StringComparison.Ordinal) && normalized.EndsWith("}", StringComparison.Ordinal))
        {
            var slice = normalized[2..^1];
            if (int.TryParse(slice, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) && idx >= 0 && idx < entries.Count)
            {
                entry = entries[idx];
                index = idx;
                return true;
            }
        }

        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var indexValue) && indexValue >= 0 && indexValue < entries.Count)
        {
            entry = entries[indexValue];
            index = indexValue;
            return true;
        }

        var match = entries.FindIndex(e => string.Equals(e.Id, normalized, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Directory, normalized, StringComparison.OrdinalIgnoreCase));
        if (match >= 0)
        {
            entry = entries[match];
            index = match;
            return true;
        }

        return false;
    }

    private static string? ResolveLogFilePath(LogManifestEntry entry, string fileKind)
    {
        var directory = Path.Combine("_logs", entry.Directory);
        var fileName = fileKind switch
        {
            "prompt" => "dotnet-build-prompt.md",
            "prompt-verbose" => "dotnet-build-prompt-verbose.md",
            "errors" => "dotnet-build-errors.md",
            "index" => "index.md",
            _ => null
        };

        if (fileName == null)
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(directory, fileName));
    }
}

public record WorkspaceInfo
{
    public bool IsRunning { get; init; }
    public int? Pid { get; init; }
    public string? PipeName { get; init; }
    public string? ServerPath { get; init; }
    public string? PidFilePath { get; init; }
    public string WorkingDirectory { get; init; } = string.Empty;
    public string LocalPidDirectory { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; }
}

public record LogManifest
{
    public LogManifestEntry[] Entries { get; init; } = Array.Empty<LogManifestEntry>();
}

public record LogManifestEntry
{
    public string Id { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public double DurationSeconds { get; init; }
    public bool Success { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int ProjectCount { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Directory { get; init; } = string.Empty;
    public string PromptFile { get; init; } = string.Empty;
    public string? GitCommit { get; init; }
    public string? GitBranch { get; init; }
    public bool GitDirty { get; init; }
}
