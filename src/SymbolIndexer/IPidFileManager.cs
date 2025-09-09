namespace SymbolIndexer;

public interface IPidFileManager
{
    string GetPidFileDirectory();
    SymbolPidFile? FindRunningServer();
    SymbolPidFile CreatePidFile(int processId, string serverPath, string pipeName);
    void CleanupPidFile(SymbolPidFile pidFile);
}