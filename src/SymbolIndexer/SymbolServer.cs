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

    public SymbolServer(string pipeName, ISymbolIndexer symbolIndexer, ILogger<SymbolServer> logger)
    {
        _pipeName = pipeName;
        _symbolIndexer = symbolIndexer;
        _logger = logger;
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

                var connectionTask = HandleConnectionAsync(pipeStream, cancellationToken);
                _connectionTasks.Add(connectionTask);

                // Clean up completed tasks
                _connectionTasks.RemoveAll(t => t.IsCompleted);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
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

                        SymbolQueryResponse response;
                        
                        if (request.Type == "shutdown")
                        {
                            response = new SymbolQueryResponse([]);
                            await SymbolProtocol.WriteResponseAsync(pipeStream, response, cancellationToken);
                            Stop();
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
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}