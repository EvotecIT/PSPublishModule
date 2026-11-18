namespace PowerForge;

internal sealed partial class ReleaseVersionCommitSession
{
    private const string PathListFilePrefix = "POWERFORGE_RELEASE_VERSION_FILES-";

    /// <summary>
    /// Commits exactly the blobs that passed classification on top of the recorded HEAD, without
    /// touching the working tree or other staged work, and moves the branch only if it is still there.
    /// </summary>
    private CommitOutcome CommitClassified(Snapshot snapshot, Classification classification, string message)
    {
        var root = snapshot.RepositoryRoot;
        var paths = classification.Stamped;
        var modes = ReadTreeEntries(root, snapshot.Head, paths);
        var indexFile = ResolveGitPath(_git, root, "powerforge-release-index-" + Guid.NewGuid().ToString("N"));
        if (indexFile is null)
            return new CommitOutcome { Problem = "the Git directory could not be resolved" };

        string tree;
        try
        {
            var scratch = new ReleaseGitRunner(CreateGitClient(indexFile));
            scratch.Run(root, "read-tree", snapshot.Head);
            foreach (var chunk in Chunk(paths, 50))
            {
                var arguments = new List<string> { "update-index", "--add" };
                foreach (var path in chunk)
                {
                    var mode = modes.TryGetValue(path, out var entry) ? entry.Mode : "100644";
                    arguments.Add("--cacheinfo");
                    arguments.Add(mode);
                    arguments.Add(classification.Blobs[path]);
                    arguments.Add(path);
                }

                scratch.Run(root, arguments.ToArray());
            }

            tree = scratch.Run(root, "write-tree").StdOut.Trim();
        }
        catch (Exception ex)
        {
            return new CommitOutcome { Problem = "git could not prepare the commit: " + FirstLine(ex.Message) };
        }
        finally
        {
            TryDelete(indexFile);
            TryDelete(indexFile + ".lock");
        }

        // commit-tree uses the configured identity and commit.gpgSign like an ordinary commit.
        var commit = _git.RunAllowFailure(root, "commit-tree", tree, "-p", snapshot.Head, "-m", message);
        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.StdOut))
            return new CommitOutcome { Problem = "git commit failed (" + ReleaseGitRunner.Describe(commit) + ")" };

        var sha = commit.StdOut.Trim();
        var update = _git.RunAllowFailure(
            root,
            "update-ref", "-m", "commit: " + message,
            "refs/heads/" + snapshot.Branch, sha, snapshot.Head);
        if (!update.Succeeded)
            return new CommitOutcome { Problem = "the branch moved before it could be updated (" + ReleaseGitRunner.Describe(update) + ")" };

        return new CommitOutcome { Sha = sha, IndexCommand = SyncIndex(snapshot, paths, modes) };
    }

    /// <summary>
    /// Points the index entries of committed files at the new commit, except where the maintainer
    /// staged something of their own. Returns a command when the index could not be updated.
    /// </summary>
    private string? SyncIndex(Snapshot snapshot, string[] paths, Dictionary<string, TreeEntry> headEntries)
    {
        var root = snapshot.RepositoryRoot;
        var unstaged = new List<string>();
        try
        {
            foreach (var chunk in Chunk(paths, 100))
            {
                var arguments = new List<string> { "ls-files", "-s", "-z", "--" };
                arguments.AddRange(chunk);
                foreach (var record in _git.Run(root, arguments.ToArray()).StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var tab = record.IndexOf('\t');
                    var fields = tab > 0 ? record.Substring(0, tab).Split(' ') : Array.Empty<string>();
                    if (fields.Length < 3)
                        continue;
                    var path = record.Substring(tab + 1);
                    if (headEntries.TryGetValue(path, out var head) && string.Equals(fields[1], head.Blob, StringComparison.OrdinalIgnoreCase))
                        unstaged.Add(path);
                }
            }

            foreach (var chunk in Chunk(unstaged, 100))
            {
                var arguments = new List<string> { "reset", "-q", "--" };
                arguments.AddRange(chunk);
                _git.Run(root, arguments.ToArray());
            }

            return null;
        }
        catch
        {
            return unstaged.Count == 0
                ? null
                : "git --literal-pathspecs reset -q -- " + string.Join(" ", unstaged.Select(Quote));
        }
    }

    private Dictionary<string, TreeEntry> ReadTreeEntries(string repositoryRoot, string commit, IReadOnlyList<string> paths)
    {
        var entries = new Dictionary<string, TreeEntry>(StringComparer.Ordinal);
        foreach (var chunk in Chunk(paths, 100))
        {
            var arguments = new List<string> { "ls-tree", "-z", commit, "--" };
            arguments.AddRange(chunk);
            foreach (var record in _git.Run(repositoryRoot, arguments.ToArray()).StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // "<mode> <type> <object>\t<path>"
                var tab = record.IndexOf('\t');
                var fields = tab > 0 ? record.Substring(0, tab).Split(' ') : Array.Empty<string>();
                if (fields.Length >= 3)
                    entries[record.Substring(tab + 1)] = new TreeEntry(fields[0], fields[2]);
            }
        }

        return entries;
    }

    private PushOutcome Push(Snapshot snapshot, string sha)
    {
        var branch = snapshot.Branch!;
        var remote = ReadConfig(snapshot.RepositoryRoot, $"branch.{branch}.remote");
        var mergeRef = ReadConfig(snapshot.RepositoryRoot, $"branch.{branch}.merge");
        if (string.IsNullOrWhiteSpace(remote) || remote == "." ||
            string.IsNullOrWhiteSpace(mergeRef) || !mergeRef!.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            // Never invent a destination: without a configured upstream the commit stays local.
            return new PushOutcome(
                false,
                $"branch '{branch}' has no upstream branch on a remote",
                $"git push --no-follow-tags --set-upstream <remote> {branch}",
                null);
        }

        var remoteBranch = mergeRef.Substring("refs/heads/".Length);
        var command = $"git push --no-follow-tags {remote} {sha}:{mergeRef}";
        // A plain push of this exact commit: never forced, never rebased, never preceded by a pull,
        // and without tags that push.followTags would otherwise add.
        var result = _git.RunAllowFailure(snapshot.RepositoryRoot, "push", "--quiet", "--no-follow-tags", remote!, sha + ":" + mergeRef);
        return result.Succeeded
            ? new PushOutcome(true, null, command, $"{remote}/{remoteBranch}")
            : new PushOutcome(false, "push rejected: " + ReleaseGitRunner.Describe(result), command, null);
    }

    private string BuildCommitCommand(Snapshot snapshot, string[] paths, string message)
    {
        const int inlineLimit = 5;
        var inline = $"git --literal-pathspecs commit -m {Quote(message)} -- {string.Join(" ", paths.Select(Quote))}";
        if (paths.Length <= inlineLimit)
            return inline;

        // One list per attempt, kept because the warning refers to it.
        var pathListFile = WritePathList(snapshot, paths);
        return pathListFile is null
            ? inline
            : $"git --literal-pathspecs commit -m {Quote(message)} --pathspec-from-file={Quote(pathListFile)} --pathspec-file-nul";
    }

    private string? WritePathList(Snapshot snapshot, string[] paths)
    {
        try
        {
            var file = ResolveGitPath(_git, snapshot.RepositoryRoot, PathListFilePrefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (file is null)
                return null;

            File.WriteAllText(file, string.Join("\0", paths) + "\0", new System.Text.UTF8Encoding(false));
            return file;
        }
        catch
        {
            return null;
        }
    }

    private string? ReadBranch(string repositoryRoot)
    {
        var result = _git.RunAllowFailure(repositoryRoot, "symbolic-ref", "--quiet", "--short", "HEAD");
        return result.Succeeded && !string.IsNullOrWhiteSpace(result.StdOut) ? result.StdOut.Trim() : null;
    }

    private string? ReadConfig(string repositoryRoot, string key)
    {
        var result = _git.RunAllowFailure(repositoryRoot, "config", "--get", key);
        return result.Succeeded && !string.IsNullOrWhiteSpace(result.StdOut) ? result.StdOut.Trim() : null;
    }

    private static string FirstLine(string value)
        => (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temporary index files live in the Git directory and are harmless if left behind.
        }
    }

    private readonly struct TreeEntry
    {
        internal TreeEntry(string mode, string blob)
        {
            Mode = mode;
            Blob = blob;
        }

        internal string Mode { get; }
        internal string Blob { get; }
    }
}
