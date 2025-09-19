using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SymbolIndexer;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Symbol indexer daemon for dotnet projects");

        var startCommand = new Command("start", "Start the symbol indexer daemon");
        var shutdownCommand = new Command("shutdown", "Shutdown the symbol indexer daemon");
        var queryCommand = new Command("query", "Query symbol locations");
        var statusCommand = new Command("status", "Show status of the symbol indexer daemon");
        var discoverCommand = new Command("discover", "Discover running daemon connection info");

        var fileOption = new Option<string>("--file", "File to query symbols from");
        var lineOption = new Option<int>("--line", "Line number to query");
        var columnOption = new Option<int?>("--column", "Column number to query (optional - searches entire line if omitted)");
        var followOption = new Option<bool>(new[] { "--follow", "-f" }, "Follow log output");
        var jsonOption = new Option<bool>("--json", "Output results as JSON");

        queryCommand.AddOption(fileOption);
        queryCommand.AddOption(lineOption);
        queryCommand.AddOption(columnOption);
        statusCommand.AddOption(followOption);
        discoverCommand.AddOption(jsonOption);

        startCommand.SetHandler(StartDaemon);
        shutdownCommand.SetHandler(ShutdownDaemon);
        queryCommand.SetHandler(QuerySymbols, fileOption, lineOption, columnOption);
        statusCommand.SetHandler(ShowStatus, followOption);
        discoverCommand.SetHandler(DiscoverDaemon, jsonOption);

        rootCommand.AddCommand(startCommand);
        rootCommand.AddCommand(shutdownCommand);
        rootCommand.AddCommand(queryCommand);
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(discoverCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static async Task StartDaemon()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHostedService<SymbolIndexerService>();
                services.AddSingleton<ISymbolIndexer, RoslynSymbolIndexer>();
                services.AddSingleton<IPidFileManager, SymbolPidFileManager>();
            })
            .Build();

        await host.RunAsync();
    }

    static async Task ShutdownDaemon()
    {
        var pidManager = new SymbolPidFileManager();
        var client = new SymbolIndexerClient(pidManager);
        await client.ShutdownAsync();
    }

    static async Task QuerySymbols(string file, int line, int? column)
    {
        var pidManager = new SymbolPidFileManager();
        var client = new SymbolIndexerClient(pidManager);
        var results = await client.QuerySymbolsAsync(file, line, column ?? 0);
        
        if (results.Count == 0)
        {
            Console.WriteLine($"No symbols found at {file}:{line}" + (column.HasValue ? $":{column}" : ""));
            return;
        }

        foreach (var symbol in results)
        {
            if (symbol.Location != null)
            {
                Console.WriteLine($"- {symbol.Name} - {symbol.Location.File}:{symbol.Location.Line},{symbol.Location.Column}");
            }
            else
            {
                Console.WriteLine($"- {symbol.Name} - not found in codebase");
            }
        }
    }

    static Task ShowStatus(bool follow)
    {
        var pidManager = new SymbolPidFileManager();
        var pidFile = pidManager.FindRunningServer();

        if (pidFile == null)
        {
            Console.WriteLine("SymbolIndexer is not running");
            Console.WriteLine($"Local PID directory: {pidManager.GetPidFileDirectory()}");
            return Task.CompletedTask;
        }

        Console.WriteLine("SymbolIndexer is running");
        Console.WriteLine($"PID: {pidFile.ProcessId}");
        Console.WriteLine($"Pipe: {pidFile.PipeName}");
        Console.WriteLine($"Server: {pidFile.ServerPath}");
        Console.WriteLine($"PID File: {pidFile.Path}");
        Console.WriteLine($"Working Directory: {Directory.GetCurrentDirectory()}");

        if (follow)
        {
            Console.WriteLine("\nNote: --follow option is not yet implemented.");
            Console.WriteLine("Use 'ps' or 'top' to monitor the process manually.");
        }

        return Task.CompletedTask;
    }

    static Task DiscoverDaemon(bool json)
    {
        var pidManager = new SymbolPidFileManager();
        var pidFile = pidManager.FindRunningServer();

        if (json)
        {
            var discoveryInfo = new DiscoveryInfo
            {
                IsRunning = pidFile != null,
                Pid = pidFile?.ProcessId,
                PipeName = pidFile?.PipeName,
                ServerPath = pidFile?.ServerPath,
                PidFilePath = pidFile?.Path,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                LocalPidDirectory = pidManager.GetPidFileDirectory()
            };

            var jsonOutput = JsonSerializer.Serialize(discoveryInfo, JsonSourceGenerationContext.Default.DiscoveryInfo);
            Console.WriteLine(jsonOutput);
        }
        else
        {
            if (pidFile == null)
            {
                Console.WriteLine("No running SymbolIndexer found");
                Console.WriteLine($"Local PID directory: {pidManager.GetPidFileDirectory()}");
            }
            else
            {
                Console.WriteLine($"PID: {pidFile.ProcessId}");
                Console.WriteLine($"Pipe: {pidFile.PipeName}");
                Console.WriteLine($"Server: {pidFile.ServerPath}");
                Console.WriteLine($"PID File: {pidFile.Path}");
            }
        }

        return Task.CompletedTask;
    }
}

public record DiscoveryInfo
{
    public bool IsRunning { get; init; }
    public int? Pid { get; init; }
    public string? PipeName { get; init; }
    public string? ServerPath { get; init; }
    public string? PidFilePath { get; init; }
    public string WorkingDirectory { get; init; } = "";
    public string LocalPidDirectory { get; init; } = "";
}