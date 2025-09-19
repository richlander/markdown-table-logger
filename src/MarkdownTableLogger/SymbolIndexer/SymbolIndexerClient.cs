using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownTableLogger.SymbolIndexer;

public class SymbolIndexerClient
{
    private readonly SymbolPidFileManager _pidFileManager;

    public SymbolIndexerClient(SymbolPidFileManager pidFileManager)
    {
        _pidFileManager = pidFileManager;
    }

    public async Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column = 0)
    {
        var pidFile = _pidFileManager.FindRunningServer();
        if (pidFile == null)
        {
            // Symbol indexer not running - return empty results silently
            return new List<SymbolResult>();
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pidFile.PipeName, PipeDirection.InOut);
            await client.ConnectAsync(1000); // 1 second timeout - don't block logger

            var request = new SymbolQueryRequest(
                Version: SymbolProtocol.ProtocolVersion,
                Type: "symbols", 
                File: file,
                Line: line,
                Column: column);

            await SymbolProtocol.WriteRequestAsync(client, request, CancellationToken.None);
            var response = await SymbolProtocol.ReadResponseAsync(client, CancellationToken.None);
            
            return response.Symbols ?? new List<SymbolResult>();
        }
        catch (Exception ex)
        {
            // Temporary debugging: log the error instead of swallowing it
            Console.Error.WriteLine($"[DEBUG] SymbolIndexer error: {ex.Message}");
            return new List<SymbolResult>();
        }
    }
}