using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SymbolIndexer;

public class SymbolServer : IDisposable
{
    private readonly string _pipeName;
    private readonly ISymbolIndexer _symbolIndexer;
    private readonly ILogger<SymbolServer> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Task> _connectionTasks = new();
    private readonly object _connectionLock = new();
    private int _permissionDeniedCount;
    private readonly Action? _onActivity;
    private readonly Action? _onShutdown;

    public SymbolServer(string pipeName, ISymbolIndexer symbolIndexer, ILogger<SymbolServer> logger, Action? onActivity = null, Action? onShutdown = null)
    {
        _pipeName = pipeName;
        _symbolIndexer = symbolIndexer;
        _logger = logger;
        _onActivity = onActivity;
        _onShutdown = onShutdown;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting symbol server on pipe: {PipeName}", _pipeName);

        while (!cancellationToken.IsCancellationRequested && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var pipeStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096,
                    4096);

                _logger.LogDebug("Waiting for client connection...");
                await pipeStream.WaitForConnectionAsync(cancellationToken);
                _logger.LogDebug("Client connected");
                _onActivity?.Invoke();

                var connectionTask = HandleConnectionAsync(pipeStream, cancellationToken);
                lock (_connectionLock)
                {
                    _connectionTasks.Add(connectionTask);
                }

                // Clean up completed tasks
                lock (_connectionLock)
                {
                    _connectionTasks.RemoveAll(t => t.IsCompleted);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (IsPermissionDenied(ex))
                {
                    var count = Interlocked.Increment(ref _permissionDeniedCount);
                    if (count == 1)
                    {
                        _logger.LogError(ex, "Permission denied creating pipe server. Named pipes may be disabled in this environment.");
                    }
                    else if (count % 10 == 0)
                    {
                        _logger.LogWarning("Permission denied creating pipe server. Retried {Count} times.", count);
                    }

                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                _logger.LogError(ex, "Error creating pipe server");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public void Stop()
    {
        _logger.LogInformation("Stopping symbol server");
        _cancellationTokenSource.Cancel();
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipeStream, CancellationToken cancellationToken)
    {
        try
        {
            using (pipeStream)
            {
                while (pipeStream.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var request = await SymbolProtocol.ReadRequestAsync(pipeStream, cancellationToken);
                        _logger.LogDebug("Received request: {Type} for {File}:{Line}:{Column}", 
                            request.Type, request.File, request.Line, request.Column);
                        _onActivity?.Invoke();

                        SymbolQueryResponse response;
                        
                        if (request.Type == "shutdown")
                        {
                            response = new SymbolQueryResponse([]);
                            await SymbolProtocol.WriteResponseAsync(pipeStream, response, cancellationToken);
                            Stop();
                            _onShutdown?.Invoke();
                            break;
                        }
                        else if (request.Type == "symbols")
                        {
                            var symbols = await _symbolIndexer.QuerySymbolsAsync(request.File, request.Line, request.Column);
                            response = new SymbolQueryResponse(symbols);
                        }
                        else
                        {
                            response = new SymbolQueryResponse([], $"Unknown request type: {request.Type}");
                        }

                        await SymbolProtocol.WriteResponseAsync(pipeStream, response, cancellationToken);
                        _onActivity?.Invoke();
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connection");
        }
    }

    public void Dispose()
    {
        try
        {
            _cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        try
        {
            _cancellationTokenSource.Dispose();
        }
        finally
        {
            lock (_connectionLock)
            {
                foreach (var task in _connectionTasks)
                {
                    task.Dispose();
                }
                _connectionTasks.Clear();
            }
        }
    }

    private static bool IsPermissionDenied(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return false;
        }

        if (ex is System.Net.Sockets.SocketException socketEx)
        {
            return socketEx.SocketErrorCode == System.Net.Sockets.SocketError.AccessDenied;
        }

        return ex.InnerException != null && IsPermissionDenied(ex.InnerException);
    }
}
