using System.Diagnostics;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Orchestrator.Git;

public sealed class GitRepositoryInspector
{
    public RepositoryGitSnapshot Inspect(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        if (!Directory.Exists(repositoryRoot))
        {
            return new RepositoryGitSnapshot(false, null, null, 0, 0, 0, 0);
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo {
                FileName = "git",
                Arguments = "status --porcelain=2 --branch",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var _ = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return new RepositoryGitSnapshot(false, null, null, 0, 0, 0, 0);
            }

            return Parse(output);
        }
        catch
        {
            return new RepositoryGitSnapshot(false, null, null, 0, 0, 0, 0);
        }
    }

    private static RepositoryGitSnapshot Parse(string output)
    {
        string? branchName = null;
        string? upstreamBranch = null;
        var aheadCount = 0;
        var behindCount = 0;
        var trackedChangeCount = 0;
        var untrackedChangeCount = 0;

        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branchName = line["# branch.head ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstreamBranch = line["# branch.upstream ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                ParseAheadBehind(line["# branch.ab ".Length..], out aheadCount, out behindCount);
                continue;
            }

            if (line.StartsWith("? ", StringComparison.Ordinal))
            {
                untrackedChangeCount++;
                continue;
            }

            if (line.StartsWith("1 ", StringComparison.Ordinal)
                || line.StartsWith("2 ", StringComparison.Ordinal)
                || line.StartsWith("u ", StringComparison.Ordinal))
            {
                trackedChangeCount++;
            }
        }

        return new RepositoryGitSnapshot(
            IsGitRepository: true,
            BranchName: branchName,
            UpstreamBranch: upstreamBranch,
            AheadCount: aheadCount,
            BehindCount: behindCount,
            TrackedChangeCount: trackedChangeCount,
            UntrackedChangeCount: untrackedChangeCount);
    }

    private static void ParseAheadBehind(string value, out int aheadCount, out int behindCount)
    {
        aheadCount = 0;
        behindCount = 0;

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith('+') && int.TryParse(part[1..], out var ahead))
            {
                aheadCount = ahead;
            }

            if (part.StartsWith('-') && int.TryParse(part[1..], out var behind))
            {
                behindCount = behind;
            }
        }
    }
}

