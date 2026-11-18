using System.Security.Cryptography;

namespace PowerForge;

/// <summary>
/// Saves the version numbers a release wrote into tracked files to Git once the release succeeds.
/// </summary>
/// <remarks>
/// <para>
/// The session records the checkout before the release starts. When the release finishes it commits
/// only files whose changes are limited to version numbers, so generated build output and the
/// maintainer's own work are never included. Files the maintainer had already edited are skipped
/// with a warning.
/// </para>
/// <para>
/// This step never fails, blocks or delays a release: every problem becomes one warning line with
/// the command that finishes the step by hand, and the release result is left as it was.
/// </para>
/// <para>
/// Only the outermost release host commits. A session opened while another session is active in
/// this process, or in a parent process that started this one, does nothing.
/// </para>
/// </remarks>
internal sealed partial class ReleaseVersionCommitSession : IDisposable
{
    /// <summary>Set for child processes started while a release session owns the commit.</summary>
    internal const string OwnerEnvironmentVariable = "POWERFORGE_RELEASE_VERSION_COMMIT_OWNER";

    private const string PathListFileName = "POWERFORGE_RELEASE_VERSION_FILES";
    private static readonly AsyncLocal<bool> ActiveInContext = new();

    private readonly ILogger _logger;
    private readonly ReleaseGitRunner _git;
    private readonly ReleaseVersionCommitMode _mode;
    private readonly bool _ownsScope;
    private readonly bool _previousScope;
    private readonly Snapshot? _snapshot;
    private readonly string? _captureProblem;
    private bool _completed;
    private bool _disposed;

    private ReleaseVersionCommitSession(
        ILogger logger,
        ReleaseGitRunner git,
        ReleaseVersionCommitMode mode,
        bool ownsScope,
        bool previousScope,
        Snapshot? snapshot,
        string? captureProblem)
    {
        _logger = logger;
        _git = git;
        _mode = mode;
        _ownsScope = ownsScope;
        _previousScope = previousScope;
        _snapshot = snapshot;
        _captureProblem = captureProblem;
    }

    /// <summary>True while a release host in this process or a parent process owns the version commit.</summary>
    internal static bool IsOwnedByActiveRelease
        => ActiveInContext.Value ||
           string.Equals(Environment.GetEnvironmentVariable(OwnerEnvironmentVariable), "1", StringComparison.Ordinal);

    /// <summary>
    /// Environment for child processes, so a release host they run does not commit on its own.
    /// Returns <paramref name="environment"/> unchanged when no session is active.
    /// </summary>
    internal static Dictionary<string, string?>? AddOwnerEnvironment(Dictionary<string, string?>? environment)
    {
        if (!IsOwnedByActiveRelease)
            return environment;

        environment ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        environment[OwnerEnvironmentVariable] = "1";
        return environment;
    }

    /// <summary>
    /// Opens a session for a release host. Call it before anything writes version numbers.
    /// </summary>
    /// <param name="logger">Logger that receives the summary or warning line.</param>
    /// <param name="startDirectory">Any directory inside the repository being released.</param>
    /// <param name="mode">Configured commit mode.</param>
    /// <param name="publishing">True only for a real run that publishes; local builds and plans never commit.</param>
    /// <param name="git">Optional Git client (tests).</param>
    internal static ReleaseVersionCommitSession Begin(
        ILogger logger,
        string? startDirectory,
        ReleaseVersionCommitMode mode,
        bool publishing,
        GitClient? git = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        var runner = new ReleaseGitRunner(git ?? CreateGitClient());
        if (IsOwnedByActiveRelease)
            return new ReleaseVersionCommitSession(logger, runner, ReleaseVersionCommitMode.Off, ownsScope: false, previousScope: false, null, null);

        var previous = ActiveInContext.Value;
        ActiveInContext.Value = true;
        if (mode == ReleaseVersionCommitMode.Off || !publishing)
            return new ReleaseVersionCommitSession(logger, runner, ReleaseVersionCommitMode.Off, ownsScope: true, previous, null, null);

        try
        {
            var snapshot = Capture(runner, startDirectory);
            return new ReleaseVersionCommitSession(logger, runner, mode, ownsScope: true, previous, snapshot, null);
        }
        catch (Exception ex)
        {
            return new ReleaseVersionCommitSession(logger, runner, mode, ownsScope: true, previous, null, ex.Message);
        }
    }

    /// <summary>
    /// Commits (and optionally pushes) the stamped version files. Never throws.
    /// </summary>
    /// <param name="releaseSucceeded">True only when every publish step succeeded.</param>
    /// <param name="name">Release name used in the commit message.</param>
    /// <param name="version">Release version used in the commit message.</param>
    /// <returns>The outcome, or <c>null</c> when the step is off for this run.</returns>
    internal ReleaseVersionCommitResult? Complete(bool releaseSucceeded, string? name, string? version)
    {
        if (_completed || _mode == ReleaseVersionCommitMode.Off)
            return null;
        _completed = true;

        try
        {
            return CompleteCore(releaseSucceeded, name, version);
        }
        catch (Exception ex)
        {
            return Warn(new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Incomplete,
                Message = $"Version changes were not committed: {ex.Message}"
            });
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsScope)
            ActiveInContext.Value = _previousScope;
    }

    private ReleaseVersionCommitResult CompleteCore(bool releaseSucceeded, string? name, string? version)
    {
        if (_snapshot is null)
        {
            return Warn(new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Incomplete,
                Message = $"Version changes were not committed: {_captureProblem ?? "the Git state could not be read before the release"}."
            });
        }

        var snapshot = _snapshot;
        var classification = Classify(snapshot);
        var stamped = classification.Stamped;
        var skipped = classification.Skipped;
        var message = BuildMessage(name, version, snapshot.RepositoryRoot);

        if (!releaseSucceeded)
        {
            if (stamped.Length == 0)
                return Info(new ReleaseVersionCommitResult { Status = ReleaseVersionCommitStatus.NothingToCommit, Message = "The release did not finish; no version files changed." });

            return Warn(new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Incomplete,
                Files = stamped,
                SkippedFiles = skipped,
                Message = $"The release did not finish, so {Count(stamped.Length, "version file")} ({Describe(stamped)}) were left uncommitted. A successful rerun commits them."
            });
        }

        if (stamped.Length == 0)
        {
            var nothing = new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.NothingToCommit,
                SkippedFiles = skipped,
                Message = skipped.Length == 0
                    ? "No version changes to commit."
                    : $"No version changes committed. {SkippedSentence(skipped)}"
            };
            return skipped.Length == 0 ? Info(nothing) : Warn(nothing);
        }

        var commitCommand = BuildCommitCommand(snapshot, stamped, message);
        var currentHead = _git.RunAllowFailure(snapshot.RepositoryRoot, "rev-parse", "HEAD");
        var currentBranch = ReadBranch(snapshot.RepositoryRoot);
        if (snapshot.Branch is null || !currentHead.Succeeded ||
            !string.Equals(currentHead.StdOut.Trim(), snapshot.Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentBranch, snapshot.Branch, StringComparison.Ordinal))
        {
            var reason = snapshot.Branch is null
                ? "the checkout is not on a branch"
                : "HEAD moved during the release";
            return Warn(new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Incomplete,
                Files = stamped,
                SkippedFiles = skipped,
                FollowUpCommand = commitCommand,
                Message = $"Left {Count(stamped.Length, "version file")} uncommitted because {reason}. {SkippedSentence(skipped)}Commit them with: {commitCommand}"
            });
        }

        var commit = Commit(snapshot, stamped, message);
        if (!commit.Succeeded)
        {
            return Warn(new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Incomplete,
                Files = stamped,
                SkippedFiles = skipped,
                FollowUpCommand = commitCommand,
                Message = $"Left {Count(stamped.Length, "version file")} uncommitted because git commit failed ({ReleaseGitRunner.Describe(commit)}). {SkippedSentence(skipped)}Commit them with: {commitCommand}"
            });
        }

        var sha = _git.GetHeadSha(snapshot.RepositoryRoot);
        var shortSha = sha.Length > 9 ? sha.Substring(0, 9) : sha;
        var committed = $"Committed {Count(stamped.Length, "version file")} as {shortSha}";
        if (_mode != ReleaseVersionCommitMode.Push)
        {
            var local = new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Committed,
                CommitSha = sha,
                Files = stamped,
                SkippedFiles = skipped,
                Message = skipped.Length == 0 ? $"{committed}." : $"{committed}. {SkippedSentence(skipped)}".TrimEnd()
            };
            return skipped.Length == 0 ? Success(local) : Warn(local);
        }

        var push = Push(snapshot);
        if (!push.Succeeded)
        {
            return Warn(new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Incomplete,
                CommitSha = sha,
                Files = stamped,
                SkippedFiles = skipped,
                FollowUpCommand = push.Command,
                Message = $"{committed} but did not push it ({push.Problem}). {SkippedSentence(skipped)}Push it, or open a pull request from it, with: {push.Command}"
            });
        }

        var pushed = new ReleaseVersionCommitResult
        {
            Status = ReleaseVersionCommitStatus.Pushed,
            CommitSha = sha,
            Files = stamped,
            SkippedFiles = skipped,
            Message = $"{committed} and pushed to {push.Target}. {SkippedSentence(skipped)}".TrimEnd()
        };
        return skipped.Length == 0 ? Success(pushed) : Warn(pushed);
    }

    private ProcessRunResult Commit(Snapshot snapshot, string[] paths, string message)
    {
        var pathListFile = WritePathList(snapshot, paths);
        if (pathListFile is null)
        {
            var arguments = new List<string> { "commit", "--quiet", "-m", message, "--" };
            arguments.AddRange(paths);
            return _git.RunAllowFailure(snapshot.RepositoryRoot, arguments.ToArray());
        }

        // A commit with paths records exactly those paths from the working tree and leaves
        // everything else, staged or not, where the maintainer put it.
        return _git.RunAllowFailure(
            snapshot.RepositoryRoot,
            "commit", "--quiet", "-m", message, "--pathspec-from-file=" + pathListFile, "--pathspec-file-nul");
    }

    private PushOutcome Push(Snapshot snapshot)
    {
        var branch = snapshot.Branch!;
        var configuredRemote = ReadConfig(snapshot.RepositoryRoot, $"branch.{branch}.remote");
        var remote = string.IsNullOrWhiteSpace(configuredRemote) || configuredRemote == "."
            ? null
            : configuredRemote;
        if (remote is null)
        {
            var remotes = _git.RunAllowFailure(snapshot.RepositoryRoot, "remote");
            if (remotes.Succeeded &&
                remotes.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(static value => string.Equals(value.Trim(), "origin", StringComparison.Ordinal)))
            {
                remote = "origin";
            }
        }

        var mergeRef = ReadConfig(snapshot.RepositoryRoot, $"branch.{branch}.merge");
        var remoteBranch = !string.IsNullOrWhiteSpace(mergeRef) && mergeRef!.StartsWith("refs/heads/", StringComparison.Ordinal)
            ? mergeRef.Substring("refs/heads/".Length)
            : branch;
        if (remote is null)
        {
            return new PushOutcome(false, $"branch '{branch}' has no remote", $"git push <remote> HEAD:{remoteBranch}", null);
        }

        var command = $"git push {remote} HEAD:{remoteBranch}";
        // A plain push: never forced, never rebased, never preceded by a pull.
        var result = _git.RunAllowFailure(snapshot.RepositoryRoot, "push", "--quiet", remote, "HEAD:refs/heads/" + remoteBranch);
        return result.Succeeded
            ? new PushOutcome(true, null, command, $"{remote}/{remoteBranch}")
            : new PushOutcome(false, "push rejected: " + ReleaseGitRunner.Describe(result), command, null);
    }

    private string BuildCommitCommand(Snapshot snapshot, string[] paths, string message)
    {
        const int inlineLimit = 5;
        if (paths.Length <= inlineLimit)
            return $"git commit -m {Quote(message)} -- {string.Join(" ", paths.Select(Quote))}";

        var pathListFile = WritePathList(snapshot, paths);
        if (pathListFile is null)
            return $"git commit -m {Quote(message)} -- {string.Join(" ", paths.Select(Quote))}";

        return $"git commit -m {Quote(message)} --pathspec-from-file={Quote(pathListFile)} --pathspec-file-nul";
    }

    private string? WritePathList(Snapshot snapshot, string[] paths)
    {
        try
        {
            var gitPath = _git.RunAllowFailure(snapshot.RepositoryRoot, "rev-parse", "--git-path", PathListFileName);
            if (!gitPath.Succeeded || string.IsNullOrWhiteSpace(gitPath.StdOut))
                return null;

            var file = Path.GetFullPath(Path.Combine(snapshot.RepositoryRoot, gitPath.StdOut.Trim()));
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

    private ReleaseVersionCommitResult Info(ReleaseVersionCommitResult result)
    {
        _logger.Info(result.Message);
        return result;
    }

    private ReleaseVersionCommitResult Success(ReleaseVersionCommitResult result)
    {
        _logger.Success(result.Message);
        return result;
    }

    private ReleaseVersionCommitResult Warn(ReleaseVersionCommitResult result)
    {
        _logger.Warn(result.Message);
        return result;
    }

    private static string BuildMessage(string? name, string? version, string repositoryRoot)
    {
        var releaseName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : name!.Trim();
        return string.IsNullOrWhiteSpace(version)
            ? $"Release {releaseName}"
            : $"Release {releaseName} {version!.Trim()}";
    }

    private static string SkippedSentence(string[] skipped)
        => skipped.Length == 0
            ? string.Empty
            : $"Left {Describe(skipped)} uncommitted because {(skipped.Length == 1 ? "it" : "they")} already had local edits before the release. ";

    private static string Describe(string[] paths)
    {
        const int shown = 5;
        return paths.Length <= shown
            ? string.Join(", ", paths)
            : string.Join(", ", paths.Take(shown)) + $" and {paths.Length - shown} more";
    }

    private static string Count(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Quote(string value)
        => value.IndexOfAny(new[] { ' ', '"', '\'', '\t' }) < 0 ? value : "\"" + value.Replace("\"", "\\\"") + "\"";

    private static GitClient CreateGitClient()
        => new(
            defaultTimeout: TimeSpan.FromMinutes(2),
            environmentVariables: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                // Never wait for a credential prompt; a push that needs one becomes a warning.
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GCM_INTERACTIVE"] = "Never",
                ["GIT_LITERAL_PATHSPECS"] = "1",
                ["LC_ALL"] = "C"
            });

    private static string? HashFile(string repositoryRoot, string relativePath)
    {
        var path = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(stream));
    }

    private sealed class Snapshot
    {
        internal string RepositoryRoot { get; set; } = string.Empty;
        internal string Head { get; set; } = string.Empty;
        internal string? Branch { get; set; }
        internal Dictionary<string, DirtyFile> DirtyAtStart { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class DirtyFile
    {
        internal string? Hash { get; set; }
        internal bool VersionOnly { get; set; }
    }

    private sealed class Classification
    {
        internal string[] Stamped { get; set; } = Array.Empty<string>();
        internal string[] Skipped { get; set; } = Array.Empty<string>();
    }

    private sealed class PushOutcome
    {
        internal PushOutcome(bool succeeded, string? problem, string command, string? target)
        {
            Succeeded = succeeded;
            Problem = problem;
            Command = command;
            Target = target;
        }

        internal bool Succeeded { get; }
        internal string? Problem { get; }
        internal string Command { get; }
        internal string? Target { get; }
    }
}
