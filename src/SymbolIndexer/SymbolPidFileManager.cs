using System;
using System.IO;

namespace SymbolIndexer;

public class SymbolPidFileManager : IPidFileManager
{
    private const string PidFileDirectoryEnvironmentVariable = "DOTNET_BUILD_PIDFILE_DIRECTORY";

    public string GetPidFileDirectory()
    {
        var directory = Environment.GetEnvironmentVariable(PidFileDirectoryEnvironmentVariable);
        if (!string.IsNullOrEmpty(directory))
        {
            return directory;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".dotnet", "pids", "build");
    }

    public SymbolPidFile? FindRunningServer()
    {
        var directory = GetPidFileDirectory();
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var file in Directory.GetFiles(directory, $"{SymbolPidFile.FilePrefix}*"))
        {
            var pidFile = SymbolPidFile.Read(file);
            if (pidFile != null && IsProcessRunning(pidFile.ProcessId))
            {
                return pidFile;
            }
            
            // Clean up stale pid files
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        return null;
    }

    public SymbolPidFile CreatePidFile(int processId, string serverPath, string pipeName)
    {
        var directory = GetPidFileDirectory();
        Directory.CreateDirectory(directory);

        var fileName = $"{SymbolPidFile.FilePrefix}{processId}.pid";
        var filePath = Path.Combine(directory, fileName);

        var pidFile = new SymbolPidFile(filePath, processId, serverPath, pipeName);
        pidFile.Write();
        
        return pidFile;
    }

    public void CleanupPidFile(SymbolPidFile pidFile)
    {
        pidFile.Delete();
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}