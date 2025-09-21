using System;
using System.Diagnostics;

namespace MarkdownTableLogger.Output;

internal static class GitMetadataCollector
{
    public static GitMetadata Collect()
    {
        var commit = TryReadGitValue("rev-parse HEAD");
        var branch = TryReadGitValue("branch --show-current");
        var status = TryReadGitValue("status --short");

        return new GitMetadata
        {
            Commit = commit,
            Branch = string.IsNullOrWhiteSpace(branch) ? null : branch,
            IsDirty = !string.IsNullOrWhiteSpace(status)
        };
    }

    private static string? TryReadGitValue(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            return process.StandardOutput.ReadToEnd().Trim();
        }
        catch
        {
            return null;
        }
    }
}

internal record GitMetadata
{
    public string? Commit { get; init; }
    public string? Branch { get; init; }
    public bool IsDirty { get; init; }
}
