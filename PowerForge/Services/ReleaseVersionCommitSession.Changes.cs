using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class ReleaseVersionCommitSession
{
    private const string StampRecordFileName = "POWERFORGE_RELEASE_VERSION_STAMPS";

    // Version-shaped tokens: 1.2, 1.2.3, 1.2.3.4, optional v prefix, prerelease and build metadata.
    private static readonly Regex VersionToken = new(
        @"(?<![0-9A-Za-z.])v?\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z][0-9A-Za-z.\-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.\-]*)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static Snapshot Capture(ReleaseGitRunner git, IReadOnlyList<string?> sourceDirectories)
    {
        var roots = new List<string>();
        foreach (var directory in sourceDirectories ?? Array.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;

            ProcessRunResult topLevel;
            try
            {
                topLevel = git.RunAllowFailure(directory!, "rev-parse", "--show-toplevel");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"git could not be started ({ex.Message})");
            }

            if (!topLevel.Succeeded || string.IsNullOrWhiteSpace(topLevel.StdOut))
                throw new InvalidOperationException($"'{directory}' is not inside a Git repository");

            var root = Path.GetFullPath(topLevel.StdOut.Trim());
            if (!roots.Any(existing => string.Equals(existing, root, StringComparison.OrdinalIgnoreCase)))
                roots.Add(root);
        }

        if (roots.Count == 0)
            throw new InvalidOperationException("the release has no repository directory to read");
        if (roots.Count > 1)
            throw new InvalidOperationException($"the release stamps versions in more than one checkout ({string.Join(", ", roots)}), so commit them in each checkout yourself");

        var repositoryRoot = roots[0];
        var head = git.RunAllowFailure(repositoryRoot, "rev-parse", "--verify", "HEAD");
        if (!head.Succeeded || string.IsNullOrWhiteSpace(head.StdOut))
            throw new InvalidOperationException("the repository has no commits yet");

        var headSha = head.StdOut.Trim();
        var branch = git.RunAllowFailure(repositoryRoot, "symbolic-ref", "--quiet", "--short", "HEAD");
        var dirty = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var path in ReadChanges(git, repositoryRoot, headSha).Keys)
            dirty[path] = HashFile(repositoryRoot, path);

        return new Snapshot
        {
            RepositoryRoot = repositoryRoot,
            Head = headSha,
            Branch = branch.Succeeded && !string.IsNullOrWhiteSpace(branch.StdOut) ? branch.StdOut.Trim() : null,
            DirtyAtStart = dirty,
            RecordedStamps = ReadStampRecord(git, repositoryRoot, headSha)
        };
    }

    private Classification Classify(Snapshot snapshot)
    {
        // Store the current content first; the commit is built from exactly these blobs.
        var changedPaths = ReadChangedPaths(snapshot);
        var stored = StoreBlobs(snapshot.RepositoryRoot, changedPaths, write: true);
        var changes = ReadChanges(_git, snapshot.RepositoryRoot, snapshot.Head);

        var candidates = new List<string>();
        var skipped = new List<string>();
        foreach (var change in changes.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var path = change.Key;
            var versionOnlyNow = change.Value;
            if (!snapshot.DirtyAtStart.TryGetValue(path, out var startHash))
            {
                // Clean before the release: a version-only change is stamping; anything
                // else is generated build output and stays out of the commit.
                if (versionOnlyNow)
                    candidates.Add(path);
                else
                    Log(() => _logger.Verbose($"Not committing '{path}': the release changed more than version numbers in it."));
                continue;
            }

            if (startHash is not null &&
                snapshot.RecordedStamps.TryGetValue(path, out var recorded) &&
                string.Equals(recorded, startHash, StringComparison.Ordinal))
            {
                // Stamps an earlier, unfinished run of this release recorded as its own.
                if (versionOnlyNow)
                    candidates.Add(path);
                continue;
            }

            // The maintainer's own change, version edits included: never committed. Say so only
            // when the release also wrote to the file.
            if (!string.Equals(startHash, HashFile(snapshot.RepositoryRoot, path), StringComparison.Ordinal))
                skipped.Add(path);
        }

        // A file edited while it was being checked is left alone rather than committed.
        var stable = StoreBlobs(snapshot.RepositoryRoot, candidates, write: false);
        var stamped = new List<string>();
        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in candidates)
        {
            if (stored.TryGetValue(path, out var blob) &&
                stable.TryGetValue(path, out var again) &&
                string.Equals(blob, again, StringComparison.Ordinal))
            {
                stamped.Add(path);
                blobs[path] = blob;
            }
            else
            {
                skipped.Add(path);
            }
        }

        return new Classification
        {
            Stamped = stamped.ToArray(),
            Blobs = blobs,
            Skipped = skipped.Distinct(StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal).ToArray()
        };
    }

    private string[] ReadChangedPaths(Snapshot snapshot)
        => _git.Run(snapshot.RepositoryRoot, "-c", "core.quotePath=false", "diff", "--name-only", "-z", "--no-renames", snapshot.Head, "--")
            .StdOut
            .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

    private Dictionary<string, string> StoreBlobs(string repositoryRoot, IReadOnlyList<string> paths, bool write)
    {
        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        var existing = paths
            .Where(path => File.Exists(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
        foreach (var chunk in Chunk(existing, 100))
        {
            var arguments = new List<string> { "hash-object" };
            if (write)
                arguments.Add("-w");
            arguments.Add("--");
            arguments.AddRange(chunk);
            var ids = _git.Run(repositoryRoot, arguments.ToArray())
                .StdOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < chunk.Length && i < ids.Length; i++)
                blobs[chunk[i]] = ids[i].Trim();
        }

        return blobs;
    }

    private static IEnumerable<string[]> Chunk(IReadOnlyList<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToArray();
    }

    private static Dictionary<string, string> ReadStampRecord(ReleaseGitRunner git, string repositoryRoot, string head)
    {
        var stamps = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var file = ResolveGitPath(git, repositoryRoot, StampRecordFileName);
            if (file is null || !File.Exists(file))
                return stamps;

            var lines = File.ReadAllLines(file);
            if (lines.Length == 0 || !string.Equals(lines[0].Trim(), head, StringComparison.OrdinalIgnoreCase))
                return stamps;

            foreach (var line in lines.Skip(1))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0)
                    stamps[line.Substring(tab + 1)] = line.Substring(0, tab);
            }
        }
        catch
        {
            // An unreadable record only means earlier stamps are treated as the maintainer's.
        }

        return stamps;
    }

    private void WriteStampRecord(Snapshot snapshot, string[] stamped)
    {
        try
        {
            var file = ResolveGitPath(_git, snapshot.RepositoryRoot, StampRecordFileName);
            if (file is null)
                return;

            var lines = new List<string> { snapshot.Head };
            foreach (var path in stamped)
            {
                var hash = HashFile(snapshot.RepositoryRoot, path);
                if (hash is not null)
                    lines.Add(hash + "\t" + path);
            }

            File.WriteAllText(file, string.Join("\n", lines) + "\n", new System.Text.UTF8Encoding(false));
        }
        catch
        {
            // Without the record a later run leaves these files for the maintainer to commit.
        }
    }

    private void DeleteStampRecord(Snapshot snapshot)
    {
        try
        {
            var file = ResolveGitPath(_git, snapshot.RepositoryRoot, StampRecordFileName);
            if (file is not null && File.Exists(file))
                File.Delete(file);
        }
        catch
        {
            // A stale record only matches files whose content it recorded for this exact HEAD.
        }
    }

    private static string? ResolveGitPath(ReleaseGitRunner git, string repositoryRoot, string name)
    {
        var gitPath = git.RunAllowFailure(repositoryRoot, "rev-parse", "--git-path", name);
        if (!gitPath.Succeeded || string.IsNullOrWhiteSpace(gitPath.StdOut))
            return null;

        return Path.GetFullPath(Path.Combine(repositoryRoot, gitPath.StdOut.Trim()));
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
        var headerParsed = false;
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
            headerParsed = false;
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
                headerParsed = path is not null;
                versionOnly = headerParsed;
                path ??= line;
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
                if (!line.StartsWith("+++ b/", StringComparison.Ordinal))
                    versionOnly = false;
                else if (!headerParsed)
                    path = StripHeaderDelimiter(line.Substring("+++ b/".Length));
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

    // Git ends "---"/"+++" file names that contain spaces with a tab delimiter.
    private static string StripHeaderDelimiter(string value)
    {
        var tab = value.IndexOf('\t');
        return tab >= 0 ? value.Substring(0, tab) : value;
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
