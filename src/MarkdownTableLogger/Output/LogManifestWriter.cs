using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MarkdownTableLogger.Output;

internal static class LogManifestWriter
{
    private static readonly object ManifestLock = new();

    public static void AppendEntry(string logsRoot, LogManifestEntry entry)
    {
        Directory.CreateDirectory(logsRoot);
        var manifestPath = Path.Combine(logsRoot, "manifest.json");

        lock (ManifestLock)
        {
            LogManifest manifest;
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    manifest = JsonSerializer.Deserialize(json, ManifestJsonContext.Default.LogManifest) ?? new LogManifest();
                }
                catch
                {
                    manifest = new LogManifest();
                }
            }
            else
            {
                manifest = new LogManifest();
            }

            manifest.Entries = manifest.Entries
                .Where(e => !string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            manifest.Entries.Insert(0, entry);
            if (manifest.Entries.Count > 200)
            {
                manifest.Entries = manifest.Entries.Take(200).ToList();
            }

            var updatedJson = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.LogManifest);
            File.WriteAllText(manifestPath, updatedJson);
        }
    }
}

internal record LogManifest
{
    public List<LogManifestEntry> Entries { get; set; } = new();
}

internal record LogManifestEntry
{
    public string Id { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public double DurationSeconds { get; init; }
    public bool Success { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int ProjectCount { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Directory { get; init; } = string.Empty;
    public string PromptFile { get; init; } = string.Empty;
    public string? GitCommit { get; init; }
    public string? GitBranch { get; init; }
    public bool GitDirty { get; init; }
}
