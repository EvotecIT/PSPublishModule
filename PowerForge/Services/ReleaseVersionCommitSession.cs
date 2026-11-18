using System.Security.Cryptography;

namespace PowerForge;

/// <summary>
/// Saves the version numbers a release wrote into tracked files to Git once the release succeeds.
/// </summary>
/// <remarks>
/// <para>
/// The session records the checkout before the release starts. When the release finishes it commits
/// the exact content it checked: only files whose changes are limited to version numbers, so generated
/// build output and the maintainer's own work are never included. Version edits that were already in
/// the checkout are only treated as the release's own when a failed run of the release recorded them.
/// Files the maintainer had already edited are skipped with a warning.
/// </para>
/// <para>
/// This step never fails or blocks a release and never changes its result. Problems become one warning
/// line (with the command that finishes the step whenever one exists) that a warning preference cannot
/// turn into a failure. The Git commands run in line with the release, so a slow commit or push adds
/// its own time, up to the Git runner's timeout.
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

    private static readonly AsyncLocal<bool> ActiveInContext = new();

    private readonly ILogger _logger;
    private readonly ReleaseGitRunner _git;
    private readonly ReleaseVersionCommitMode _mode;
    private readonly bool _ownsScope;
    private readonly bool _previousScope;
    private readonly Snapshot? _snapshot;
    private readonly string? _captureProblem;
    private bool _publishing;
    private bool _completed;
    private bool _disposed;

    private ReleaseVersionCommitSession(
        ILogger logger,
        ReleaseGitRunner git,
        ReleaseVersionCommitMode mode,
        bool publishing,
        bool ownsScope,
        bool previousScope,
        Snapshot? snapshot,
        string? captureProblem)
    {
        _logger = logger;
        _git = git;
        _mode = mode;
        _publishing = publishing;
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
    internal static ReleaseVersionCommitSession Begin(
        ILogger logger,
        string? startDirectory,
        ReleaseVersionCommitMode mode,
        bool publishing,
        GitClient? git = null)
        => Begin(logger, new[] { startDirectory }, mode, publishing, git);

    /// <summary>
    /// Opens a session for a release host. Call it before anything writes version numbers.
    /// </summary>
    /// <param name="logger">Logger that receives the summary or warning line.</param>
    /// <param name="sourceDirectories">Directories of every lane that can stamp versions; they must share one checkout.</param>
    /// <param name="mode">Configured commit mode.</param>
    /// <param name="publishing">
    /// False for plans, validation and local builds, which never commit. A host that only knows its
    /// publish lanes later can refine this with <see cref="SetPublishing"/> before completing.
    /// </param>
    /// <param name="git">Optional Git client (tests).</param>
    internal static ReleaseVersionCommitSession Begin(
        ILogger logger,
        IReadOnlyList<string?> sourceDirectories,
        ReleaseVersionCommitMode mode,
        bool publishing,
        GitClient? git = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        var runner = new ReleaseGitRunner(git ?? CreateGitClient());
        if (IsOwnedByActiveRelease)
            return new ReleaseVersionCommitSession(logger, runner, ReleaseVersionCommitMode.Off, false, ownsScope: false, previousScope: false, null, null);

        var previous = ActiveInContext.Value;
        ActiveInContext.Value = true;
        if (mode == ReleaseVersionCommitMode.Off || !publishing)
            return new ReleaseVersionCommitSession(logger, runner, ReleaseVersionCommitMode.Off, false, ownsScope: true, previous, null, null);

        try
        {
            var snapshot = Capture(runner, sourceDirectories);
            return new ReleaseVersionCommitSession(logger, runner, mode, true, ownsScope: true, previous, snapshot, null);
        }
        catch (Exception ex)
        {
            return new ReleaseVersionCommitSession(logger, runner, mode, true, ownsScope: true, previous, null, ex.Message);
        }
    }

    /// <summary>Records whether the lanes this run selected publish anything.</summary>
    internal void SetPublishing(bool publishing) => _publishing = _publishing && publishing;

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
        if (!_publishing)
        {
            Log(() => _logger.Verbose("Version changes are not committed because this run did not publish."));
            return null;
        }

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

            WriteStampRecord(snapshot, stamped);
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
            DeleteStampRecord(snapshot);
            var nothing = new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.NothingToCommit,
                SkippedFiles = skipped,
                Message = skipped.Length == 0
                    ? "No version changes to commit."
                    : $"No version changes committed. {SkippedSentence(skipped)}".TrimEnd()
            };
            return skipped.Length == 0 ? Info(nothing) : Warn(nothing);
        }

        var currentHead = _git.RunAllowFailure(snapshot.RepositoryRoot, "rev-parse", "HEAD");
        var currentBranch = ReadBranch(snapshot.RepositoryRoot);
        if (snapshot.Branch is null || !currentHead.Succeeded ||
            !string.Equals(currentHead.StdOut.Trim(), snapshot.Head, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentBranch, snapshot.Branch, StringComparison.Ordinal))
        {
            var reason = snapshot.Branch is null
                ? "the checkout is not on a branch"
                : "HEAD moved during the release";
            return LeaveUncommitted(snapshot, stamped, skipped, message, reason);
        }

        var commit = CommitClassified(snapshot, classification, message);
        if (commit.Sha is null)
            return LeaveUncommitted(snapshot, stamped, skipped, message, commit.Problem ?? "git could not create the commit");

        DeleteStampRecord(snapshot);
        var sha = commit.Sha;
        var shortSha = sha.Length > 9 ? sha.Substring(0, 9) : sha;
        var committed = $"Committed {Count(stamped.Length, "version file")} as {shortSha}";
        var indexNote = commit.IndexCommand is null
            ? string.Empty
            : $"Refresh the index for them with: {commit.IndexCommand} ";
        if (_mode != ReleaseVersionCommitMode.Push)
        {
            var local = new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Committed,
                CommitSha = sha,
                Files = stamped,
                SkippedFiles = skipped,
                FollowUpCommand = commit.IndexCommand,
                Message = $"{committed}. {SkippedSentence(skipped)}{indexNote}".TrimEnd()
            };
            return skipped.Length == 0 && commit.IndexCommand is null ? Success(local) : Warn(local);
        }

        PushOutcome push;
        try
        {
            push = Push(snapshot, sha);
        }
        catch (Exception ex)
        {
            push = new PushOutcome(
                false,
                $"the push outcome could not be confirmed: {ex.Message}",
                $"git status -sb && git push --no-follow-tags <remote> {sha}:refs/heads/{snapshot.Branch}",
                null);
        }

        if (!push.Succeeded)
        {
            return Warn(new ReleaseVersionCommitResult
            {
                Status = ReleaseVersionCommitStatus.Incomplete,
                CommitSha = sha,
                Files = stamped,
                SkippedFiles = skipped,
                FollowUpCommand = push.Command,
                Message = $"{committed} but did not push it ({push.Problem}). {SkippedSentence(skipped)}{indexNote}Push it, or open a pull request from it, with: {push.Command}"
            });
        }

        var pushed = new ReleaseVersionCommitResult
        {
            Status = ReleaseVersionCommitStatus.Pushed,
            CommitSha = sha,
            Files = stamped,
            SkippedFiles = skipped,
            FollowUpCommand = commit.IndexCommand,
            Message = $"{committed} and pushed to {push.Target}. {SkippedSentence(skipped)}{indexNote}".TrimEnd()
        };
        return skipped.Length == 0 && commit.IndexCommand is null ? Success(pushed) : Warn(pushed);
    }

    private ReleaseVersionCommitResult LeaveUncommitted(
        Snapshot snapshot,
        string[] stamped,
        string[] skipped,
        string message,
        string reason)
    {
        // The next successful run of this release may commit these, so record them as its own.
        WriteStampRecord(snapshot, stamped);
        var command = BuildCommitCommand(snapshot, stamped, message);
        return Warn(new ReleaseVersionCommitResult
        {
            Status = ReleaseVersionCommitStatus.Incomplete,
            Files = stamped,
            SkippedFiles = skipped,
            FollowUpCommand = command,
            Message = $"Left {Count(stamped.Length, "version file")} uncommitted because {reason}. {SkippedSentence(skipped)}Commit them with: {command}"
        });
    }

    private void Log(Action write)
    {
        try
        {
            write();
        }
        catch
        {
            // Reporting this optional step must never affect the release.
        }
    }

    private ReleaseVersionCommitResult Info(ReleaseVersionCommitResult result)
    {
        Log(() => _logger.Info(result.Message));
        return result;
    }

    private ReleaseVersionCommitResult Success(ReleaseVersionCommitResult result)
    {
        Log(() => _logger.Success(result.Message));
        return result;
    }

    private ReleaseVersionCommitResult Warn(ReleaseVersionCommitResult result)
    {
        // The release has already published; its warning preference must not turn this into a failure.
        Log(() =>
        {
            if (_logger is IAdvisoryWarningLogger advisory)
                advisory.WarnAdvisory(result.Message);
            else
                _logger.Warn(result.Message);
        });
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
        => value.IndexOfAny(new[] { ' ', '"', '\'', '\t', '[', ']', '*', '?' }) < 0 ? value : "\"" + value.Replace("\"", "\\\"") + "\"";

    private static GitClient CreateGitClient(string? indexFile = null)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            // Never wait for a credential prompt; a push that needs one becomes a warning.
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
            ["GIT_LITERAL_PATHSPECS"] = "1",
            ["LC_ALL"] = "C"
        };
        if (indexFile is not null)
            environment["GIT_INDEX_FILE"] = indexFile;
        return new GitClient(defaultTimeout: TimeSpan.FromMinutes(2), environmentVariables: environment);
    }

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
        internal Dictionary<string, string?> DirtyAtStart { get; set; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> RecordedStamps { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class Classification
    {
        internal string[] Stamped { get; set; } = Array.Empty<string>();
        internal Dictionary<string, string> Blobs { get; set; } = new(StringComparer.Ordinal);
        internal string[] Skipped { get; set; } = Array.Empty<string>();
    }

    private sealed class CommitOutcome
    {
        internal string? Sha { get; set; }
        internal string? Problem { get; set; }
        internal string? IndexCommand { get; set; }
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
