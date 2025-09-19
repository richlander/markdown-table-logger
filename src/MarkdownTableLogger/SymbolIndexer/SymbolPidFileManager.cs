using System;
using System.Diagnostics;
using System.IO;

namespace MarkdownTableLogger.SymbolIndexer;

public class SymbolPidFile
{
    public const string ServerType = "sym";
    public const string FilePrefix = "sym-";

    public string Path { get; }
    public int ProcessId { get; }
    public string ServerPath { get; }
    public string PipeName { get; }

    public SymbolPidFile(string path, int processId, string serverPath, string pipeName)
    {
        Path = path;
        ProcessId = processId;
        ServerPath = serverPath;
        PipeName = pipeName;
    }

    public static SymbolPidFile? Read(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            
            if (!int.TryParse(reader.ReadLine(), out var processId))
                return null;

            if (reader.ReadLine() != ServerType)
                return null;

            var serverPath = reader.ReadLine();
            if (string.IsNullOrEmpty(serverPath))
                return null;

            var pipeName = reader.ReadLine();
            if (string.IsNullOrEmpty(pipeName))
                return null;

            return new SymbolPidFile(path, processId, serverPath, pipeName);
        }
        catch
        {
            return null;
        }
    }
}

public class SymbolPidFileManager
{
    private const string PidFileDirectoryEnvironmentVariable = "DOTNET_BUILD_PIDFILE_DIRECTORY";

    public string GetPidFileDirectory()
    {
        var directory = Environment.GetEnvironmentVariable(PidFileDirectoryEnvironmentVariable);
        if (!string.IsNullOrEmpty(directory))
            return directory;

        var currentDir = Directory.GetCurrentDirectory();
        return Path.Combine(currentDir, ".dotnet", "pids", "build");
    }

    public SymbolPidFile? FindRunningServer()
    {
        var directory = GetPidFileDirectory();
        if (!Directory.Exists(directory))
            return null;

        foreach (var file in Directory.GetFiles(directory, $"{SymbolPidFile.FilePrefix}*"))
        {
            var pidFile = SymbolPidFile.Read(file);
            if (pidFile != null && IsProcessRunning(pidFile.ProcessId))
                return pidFile;
        }

        return null;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}