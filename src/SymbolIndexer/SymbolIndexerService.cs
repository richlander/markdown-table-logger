using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SymbolIndexer;

public class SymbolIndexerService : BackgroundService
{
    private readonly ISymbolIndexer _symbolIndexer;
    private readonly IPidFileManager _pidFileManager;
    private readonly ILogger<SymbolIndexerService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly SymbolIndexerOptions _options;
    private readonly object _activityLock = new();
    private SymbolPidFile? _pidFile;
    private SymbolServer? _symbolServer;
    private FileSystemWatcher? _fileWatcher;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public SymbolIndexerService(
        ISymbolIndexer symbolIndexer, 
        IPidFileManager pidFileManager,
        ILogger<SymbolIndexerService> logger,
        ILoggerFactory loggerFactory,
        IHostApplicationLifetime lifetime,
        IOptions<SymbolIndexerOptions> options)
    {
        _symbolIndexer = symbolIndexer;
        _pidFileManager = pidFileManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _lifetime = lifetime;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task? monitorTask = null;
        try
        {
            // Check if another instance is already running
            var existingServer = _pidFileManager.FindRunningServer();
            if (existingServer != null)
            {
                _logger.LogWarning("Symbol indexer is already running (PID: {ProcessId})", existingServer.ProcessId);
                return;
            }

            // Generate very short pipe name for macOS Unix domain socket compatibility 
            var pipeName = $"s{Guid.NewGuid():N}"[..8]; // Very short to avoid 104-char Unix socket path limit
            var processId = Environment.ProcessId;
            var serverPath = Environment.ProcessPath ?? "dotnet-symbol-indexer";

            _pidFile = _pidFileManager.CreatePidFile(processId, serverPath, pipeName);
            _logger.LogInformation("Created PID file: {PidFile}", _pidFile.Path);

            // Get current directory as project path
            var projectPath = Directory.GetCurrentDirectory();
            _logger.LogInformation("Indexing project: {ProjectPath}", projectPath);

            // Build initial index
            await _symbolIndexer.RebuildIndexAsync(projectPath);
            MarkActivity();

            // Set up file system watcher
            SetupFileWatcher(projectPath);

            // Start the symbol server
            var serverLogger = _loggerFactory.CreateLogger<SymbolServer>();
            _symbolServer = new SymbolServer(pipeName, _symbolIndexer, serverLogger, MarkActivity, OnShutdownRequested);

            _logger.LogInformation("Starting symbol server on pipe: {PipeName}", pipeName);
            monitorTask = MonitorIdleAsync(stoppingToken);
            await _symbolServer.StartAsync(stoppingToken);
            if (monitorTask != null)
            {
                await monitorTask;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in symbol indexer service");
        }
        finally
        {
            Cleanup();
        }
    }

    private void SetupFileWatcher(string projectPath)
    {
        _fileWatcher = new FileSystemWatcher(projectPath)
        {
            Filter = "*.cs",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Created += OnFileChanged;
        _fileWatcher.Deleted += OnFileDeleted;
        _fileWatcher.Renamed += OnFileRenamed;

        _fileWatcher.EnableRaisingEvents = true;
        _logger.LogInformation("File system watcher started for {ProjectPath}", projectPath);
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains("bin") || e.FullPath.Contains("obj"))
            return;

        MarkActivity();
        _logger.LogDebug("File changed: {FilePath}", e.FullPath);
        
        // Debounce rapid changes
        await Task.Delay(100);
        await _symbolIndexer.UpdateFileAsync(e.FullPath);
    }

    private async void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        MarkActivity();
        _logger.LogDebug("File deleted: {FilePath}", e.FullPath);

        if (e.FullPath.Contains("bin") || e.FullPath.Contains("obj"))
            return;

        await _symbolIndexer.RemoveFileAsync(e.FullPath);
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        MarkActivity();
        _logger.LogDebug("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);

        if (e.FullPath.Contains("bin") || e.FullPath.Contains("obj"))
            return;

        // Remove old file from index and add new file
        await _symbolIndexer.RemoveFileAsync(e.OldFullPath);
        await _symbolIndexer.UpdateFileAsync(e.FullPath);
    }

    private void OnShutdownRequested()
    {
        _logger.LogInformation("Shutdown requested via RPC");
        _lifetime.StopApplication();
    }

    private void MarkActivity()
    {
        lock (_activityLock)
        {
            _lastActivityUtc = DateTime.UtcNow;
        }
    }

    private DateTime GetLastActivity()
    {
        lock (_activityLock)
        {
            return _lastActivityUtc;
        }
    }

    private async Task MonitorIdleAsync(CancellationToken stoppingToken)
    {
        var idleTimeout = _options.IdleTimeout;
        if (idleTimeout <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                var idleDuration = DateTime.UtcNow - GetLastActivity();
                if (idleDuration >= idleTimeout)
                {
                    _logger.LogInformation("Idle timeout of {IdleTimeout} reached (idle for {IdleDuration}), shutting down", idleTimeout, idleDuration);
                    _symbolServer?.Stop();
                    _lifetime.StopApplication();
                    break;
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Host stopping; nothing to do
        }
    }

    private void Cleanup()
    {
        _logger.LogInformation("Cleaning up symbol indexer service");

        _fileWatcher?.Dispose();
        _symbolServer?.Dispose();
        
        if (_pidFile != null)
        {
            _pidFileManager.CleanupPidFile(_pidFile);
        }
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }
}
