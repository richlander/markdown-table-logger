using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SymbolIndexer;

public class SymbolIndexerService : BackgroundService
{
    private readonly ISymbolIndexer _symbolIndexer;
    private readonly IPidFileManager _pidFileManager;
    private readonly ILogger<SymbolIndexerService> _logger;
    private SymbolPidFile? _pidFile;
    private SymbolServer? _symbolServer;
    private FileSystemWatcher? _fileWatcher;

    public SymbolIndexerService(
        ISymbolIndexer symbolIndexer, 
        IPidFileManager pidFileManager,
        ILogger<SymbolIndexerService> logger)
    {
        _symbolIndexer = symbolIndexer;
        _pidFileManager = pidFileManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

            // Set up file system watcher
            SetupFileWatcher(projectPath);

            // Start the symbol server
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var serverLogger = loggerFactory.CreateLogger<SymbolServer>();
            _symbolServer = new SymbolServer(pipeName, _symbolIndexer, serverLogger);

            _logger.LogInformation("Starting symbol server on pipe: {PipeName}", pipeName);
            await _symbolServer.StartAsync(stoppingToken);
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

        _logger.LogDebug("File changed: {FilePath}", e.FullPath);
        
        // Debounce rapid changes
        await Task.Delay(100);
        await _symbolIndexer.UpdateFileAsync(e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("File deleted: {FilePath}", e.FullPath);
        // TODO: Remove symbols from deleted files from index
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        _logger.LogDebug("File renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
        // TODO: Handle file renames in index
        await _symbolIndexer.UpdateFileAsync(e.FullPath);
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