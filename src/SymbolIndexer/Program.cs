using System;
using System.CommandLine;
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
        
        var fileOption = new Option<string>("--file", "File to query symbols from");
        var lineOption = new Option<int>("--line", "Line number to query");
        var columnOption = new Option<int?>("--column", "Column number to query (optional - searches entire line if omitted)");
        
        queryCommand.AddOption(fileOption);
        queryCommand.AddOption(lineOption);
        queryCommand.AddOption(columnOption);

        startCommand.SetHandler(StartDaemon);
        shutdownCommand.SetHandler(ShutdownDaemon);
        queryCommand.SetHandler(QuerySymbols, fileOption, lineOption, columnOption);

        rootCommand.AddCommand(startCommand);
        rootCommand.AddCommand(shutdownCommand);
        rootCommand.AddCommand(queryCommand);

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
}