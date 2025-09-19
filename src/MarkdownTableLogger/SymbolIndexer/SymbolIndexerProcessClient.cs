using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownTableLogger.SymbolIndexer;

public class SymbolIndexerProcessClient
{
    private SymbolIndexerInfo? _cachedInfo;
    private readonly object _lock = new();

    public async Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column = 0)
    {
        try
        {
            var info = await GetSymbolIndexerInfoAsync();
            if (info?.IsRunning != true || info.PipeName == null)
            {
                return new List<SymbolResult>();
            }

            // Use direct pipe communication for performance
            using var client = new NamedPipeClientStream(".", info.PipeName, PipeDirection.InOut);
            await client.ConnectAsync(1000); // 1 second timeout

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
            Console.Error.WriteLine($"[DEBUG] SymbolIndexer error: {ex.Message}");
            return new List<SymbolResult>();
        }
    }

    private async Task<SymbolIndexerInfo?> GetSymbolIndexerInfoAsync()
    {
        lock (_lock)
        {
            if (_cachedInfo != null)
            {
                return _cachedInfo;
            }
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "SymbolIndexer",
                    Arguments = "discover --json",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                var info = JsonSerializer.Deserialize(output, JsonSourceGenerationContext.Default.SymbolIndexerInfo);

                lock (_lock)
                {
                    _cachedInfo = info;
                }

                return info;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DEBUG] Failed to discover SymbolIndexer: {ex.Message}");
            return null;
        }
    }
}

public record SymbolIndexerInfo
{
    public bool IsRunning { get; init; }
    public int? Pid { get; init; }
    public string? PipeName { get; init; }
    public string? ServerPath { get; init; }
    public string? PidFilePath { get; init; }
    public string WorkingDirectory { get; init; } = "";
    public string LocalPidDirectory { get; init; } = "";
}