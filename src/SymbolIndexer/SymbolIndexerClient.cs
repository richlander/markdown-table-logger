using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SymbolIndexer;

public class SymbolIndexerClient
{
    private readonly IPidFileManager _pidFileManager;
    private readonly ILogger<SymbolIndexerClient>? _logger;

    public SymbolIndexerClient(IPidFileManager pidFileManager, ILogger<SymbolIndexerClient>? logger = null)
    {
        _pidFileManager = pidFileManager;
        _logger = logger;
    }

    public async Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column)
    {
        var pidFile = _pidFileManager.FindRunningServer();
        if (pidFile == null)
        {
            Console.Error.WriteLine("Error: No symbol indexer daemon is running.");
            Console.Error.WriteLine("Start the daemon with: dotnet run --project src/SymbolIndexer -- start");
            _logger?.LogWarning("No running symbol indexer found");
            return [];
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pidFile.PipeName, PipeDirection.InOut);
            await client.ConnectAsync(5000); // 5 second timeout

            var request = new SymbolQueryRequest(
                Version: SymbolProtocol.ProtocolVersion,
                Type: "symbols", 
                File: file,
                Line: line,
                Column: column);

            await SymbolProtocol.WriteRequestAsync(client, request, CancellationToken.None);
            var response = await SymbolProtocol.ReadResponseAsync(client, CancellationToken.None);
            return response.Symbols;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error querying symbols from server");
            return [];
        }
    }

    public async Task ShutdownAsync()
    {
        var pidFile = _pidFileManager.FindRunningServer();
        if (pidFile == null)
        {
            Console.WriteLine("No running symbol indexer found");
            return;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pidFile.PipeName, PipeDirection.InOut);
            await client.ConnectAsync(5000); // 5 second timeout

            var request = new SymbolQueryRequest(
                Version: SymbolProtocol.ProtocolVersion,
                Type: "shutdown",
                File: "",
                Line: 0,
                Column: 0);

            await SymbolProtocol.WriteRequestAsync(client, request, CancellationToken.None);

            Console.WriteLine($"Successfully shut down symbol indexer (PID: {pidFile.ProcessId})");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error shutting down server");
            Console.WriteLine($"Error shutting down server: {ex.Message}");
        }
    }

}