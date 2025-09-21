using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownTableLogger.SymbolIndexer;

public class SymbolIndexerProcessClient
{
    private SymbolIndexerInfo? _cachedInfo;
    private DateTime _cacheExpiryUtc;
    private readonly object _lock = new();

    public async Task<List<SymbolResult>> QuerySymbolsAsync(string file, int line, int column = 0)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var info = await GetSymbolIndexerInfoAsync(forceRefresh: attempt > 0);
                if (info?.IsRunning != true || string.IsNullOrEmpty(info.PipeName))
                {
                    return new List<SymbolResult>();
                }

                using var client = new NamedPipeClientStream(".", info.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(1000);

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
            catch (IOException)
            {
                InvalidateCache();
            }
            catch (TimeoutException)
            {
                InvalidateCache();
            }
            catch (Exception ex)
            {
                MaybeLogDebug($"SymbolIndexer error: {ex.Message}");
                InvalidateCache();
            }
        }

        return new List<SymbolResult>();
    }

    private void InvalidateCache()
    {
        lock (_lock)
        {
            _cachedInfo = null;
            _cacheExpiryUtc = DateTime.MinValue;
        }
    }

    private async Task<SymbolIndexerInfo?> GetSymbolIndexerInfoAsync(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && _cachedInfo != null && DateTime.UtcNow < _cacheExpiryUtc)
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
                    FileName = "dotnet-logs",
                    Arguments = "workspace info --json",
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
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
                    if (info?.IsRunning == true && !string.IsNullOrEmpty(info.PipeName))
                    {
                        _cachedInfo = info;
                        _cacheExpiryUtc = DateTime.UtcNow.AddSeconds(30);
                    }
                    else
                    {
                        _cachedInfo = null;
                        _cacheExpiryUtc = DateTime.MinValue;
                    }
                }

                return info;
            }

            return null;
        }
        catch (Exception ex)
        {
            MaybeLogDebug($"Failed to discover SymbolIndexer: {ex.Message}");
            return null;
        }
    }

    private static void MaybeLogDebug(string message)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_LOGS_DEBUG") == "1")
        {
            Console.Error.WriteLine($"[dotnet-logs] {message}");
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
    public DateTime TimestampUtc { get; init; }
}
