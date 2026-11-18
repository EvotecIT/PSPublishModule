using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class ReleaseVersionCommitSession
{
    // Version-shaped tokens: 1.2, 1.2.3, 1.2.3.4, optional v prefix, prerelease and build metadata.
    private static readonly Regex VersionToken = new(
        @"(?<![0-9A-Za-z.])v?\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z][0-9A-Za-z.\-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.\-]*)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static Snapshot Capture(ReleaseGitRunner git, string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            throw new InvalidOperationException("the release has no repository directory to read");

        ProcessRunResult topLevel;
        try
        {
            topLevel = git.RunAllowFailure(startDirectory!, "rev-parse", "--show-toplevel");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"git could not be started ({ex.Message})");
        }

        if (!topLevel.Succeeded || string.IsNullOrWhiteSpace(topLevel.StdOut))
            throw new InvalidOperationException($"'{startDirectory}' is not inside a Git repository");

        var root = Path.GetFullPath(topLevel.StdOut.Trim());
        var head = git.RunAllowFailure(root, "rev-parse", "--verify", "HEAD");
        if (!head.Succeeded || string.IsNullOrWhiteSpace(head.StdOut))
            throw new InvalidOperationException("the repository has no commits yet");

        var headSha = head.StdOut.Trim();
        var branch = git.RunAllowFailure(root, "symbolic-ref", "--quiet", "--short", "HEAD");
        var dirty = new Dictionary<string, DirtyFile>(StringComparer.Ordinal);
        foreach (var change in ReadChanges(git, root, headSha))
        {
            dirty[change.Key] = new DirtyFile
            {
                Hash = HashFile(root, change.Key),
                VersionOnly = change.Value
            };
        }

        return new Snapshot
        {
            RepositoryRoot = root,
            Head = headSha,
            Branch = branch.Succeeded && !string.IsNullOrWhiteSpace(branch.StdOut) ? branch.StdOut.Trim() : null,
            DirtyAtStart = dirty
        };
    }

    private Classification Classify(Snapshot snapshot)
    {
        var stamped = new List<string>();
        var skipped = new List<string>();
        foreach (var change in ReadChanges(_git, snapshot.RepositoryRoot, snapshot.Head).OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var path = change.Key;
            var versionOnlyNow = change.Value;
            if (!snapshot.DirtyAtStart.TryGetValue(path, out var before))
            {
                // Clean before the release: a version-only change is stamping; anything
                // else is generated build output and stays out of the commit.
                if (versionOnlyNow)
                    stamped.Add(path);
                else
                    _logger.Verbose($"Not committing '{path}': the release changed more than version numbers in it.");
                continue;
            }

            if (before.VersionOnly)
            {
                // Version stamping left over from an earlier run (for example a release
                // that is being resumed) is still the release's own change.
                if (versionOnlyNow)
                    stamped.Add(path);
                continue;
            }

            var changedDuringRelease = !string.Equals(before.Hash, HashFile(snapshot.RepositoryRoot, path), StringComparison.Ordinal);
            if (changedDuringRelease)
                skipped.Add(path);
        }

        return new Classification { Stamped = stamped.ToArray(), Skipped = skipped.ToArray() };
    }

    /// <summary>
    /// Reads tracked changes between <paramref name="baseCommit"/> and the working tree.
    /// The value is true when the file's change is limited to version numbers.
    /// </summary>
    private static Dictionary<string, bool> ReadChanges(ReleaseGitRunner git, string repositoryRoot, string baseCommit)
    {
        var diff = git.Run(
            repositoryRoot,
            "-c", "core.quotePath=false",
            "diff", "-U0", "--no-color", "--no-ext-diff", "--no-textconv", "--no-renames",
            "--src-prefix=a/", "--dst-prefix=b/",
            baseCommit, "--");
        return ParseDiff(diff.StdOut);
    }

    internal static Dictionary<string, bool> ParseDiff(string diff)
    {
        var files = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? path = null;
        var versionOnly = false;
        var hunks = 0;
        var inHunk = false;
        var removed = new List<string>();
        var added = new List<string>();

        void CloseHunk()
        {
            if (!inHunk)
                return;
            hunks++;
            if (removed.Count != added.Count)
                versionOnly = false;
            else
            {
                for (var i = 0; i < removed.Count && versionOnly; i++)
                {
                    if (!string.Equals(NormalizeVersions(removed[i]), NormalizeVersions(added[i]), StringComparison.Ordinal))
                        versionOnly = false;
                }
            }

            removed.Clear();
            added.Clear();
            inHunk = false;
        }

        void CloseFile()
        {
            CloseHunk();
            if (path is not null)
                files[path] = versionOnly && hunks > 0;
            path = null;
            versionOnly = false;
            hunks = 0;
        }

        foreach (var rawLine in (diff ?? string.Empty).Split('\n'))
        {
            var line = rawLine.EndsWith("\r", StringComparison.Ordinal) ? rawLine.Substring(0, rawLine.Length - 1) : rawLine;
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                CloseFile();
                path = ParseHeaderPath(line.Substring("diff --git ".Length));
                versionOnly = path is not null;
                if (path is null)
                    path = line;
                continue;
            }

            if (path is null)
                continue;

            if (inHunk)
            {
                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    CloseHunk();
                    inHunk = true;
                }
                else if (line.StartsWith("-", StringComparison.Ordinal))
                    removed.Add(line.Substring(1));
                else if (line.StartsWith("+", StringComparison.Ordinal))
                    added.Add(line.Substring(1));
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                if (line.StartsWith("+++ b/", StringComparison.Ordinal))
                    path = line.Substring("+++ b/".Length);
                else
                    versionOnly = false;
            }
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                if (!line.StartsWith("--- a/", StringComparison.Ordinal))
                    versionOnly = false;
            }
            else if (line.StartsWith("Binary files ", StringComparison.Ordinal) ||
                     line.StartsWith("old mode ", StringComparison.Ordinal) ||
                     line.StartsWith("new mode ", StringComparison.Ordinal) ||
                     line.StartsWith("new file mode ", StringComparison.Ordinal) ||
                     line.StartsWith("deleted file mode ", StringComparison.Ordinal) ||
                     line.StartsWith("Submodule ", StringComparison.Ordinal))
            {
                versionOnly = false;
            }
        }

        CloseFile();
        return files;
    }

    private static string? ParseHeaderPath(string header)
    {
        // "a/<path> b/<path>" with identical halves because renames are disabled.
        if (header.Length < 5 || !header.StartsWith("a/", StringComparison.Ordinal))
            return null;
        if ((header.Length - 5) % 2 != 0)
            return null;
        var length = (header.Length - 5) / 2;
        var path = header.Substring(2, length);
        return string.Equals(header.Substring(2 + length), " b/" + path, StringComparison.Ordinal) ? path : null;
    }

    private static string NormalizeVersions(string line)
        => VersionToken.Replace(line, "<version>");
}
