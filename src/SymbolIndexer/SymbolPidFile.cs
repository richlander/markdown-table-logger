using System;
using System.IO;
using System.Text;

namespace SymbolIndexer;

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
            using var reader = new StreamReader(path, Encoding.UTF8);
            
            if (!int.TryParse(reader.ReadLine(), out var processId))
            {
                return null;
            }

            if (reader.ReadLine() != ServerType)
            {
                return null;
            }

            var serverPath = reader.ReadLine();
            if (string.IsNullOrEmpty(serverPath))
            {
                return null;
            }

            var pipeName = reader.ReadLine();
            if (string.IsNullOrEmpty(pipeName))
            {
                return null;
            }

            return new SymbolPidFile(path, processId, serverPath, pipeName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Write()
    {
        using var writer = new StreamWriter(Path, false, Encoding.UTF8);
        writer.WriteLine(ProcessId);
        writer.WriteLine(ServerType);
        writer.WriteLine(ServerPath);
        writer.WriteLine(PipeName);
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (Exception)
        {
            // Ignore deletion errors
        }
    }
}