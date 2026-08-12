namespace PowerForge;

/// <summary>
/// Reusable typed client for git repository operations.
/// </summary>
public sealed class GitClient
{
    private readonly IProcessRunner _processRunner;
    private readonly string _gitExecutable;
    private readonly TimeSpan _defaultTimeout;
    private readonly IReadOnlyDictionary<string, string?>? _environmentVariables;
    private readonly bool _inheritEnvironment;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitClient"/> class.
    /// </summary>
    /// <param name="processRunner">Optional process runner implementation.</param>
    /// <param name="gitExecutable">Optional git executable name or path.</param>
    /// <param name="defaultTimeout">Optional default timeout.</param>
    /// <param name="environmentVariables">Optional environment supplied to Git.</param>
    /// <param name="inheritEnvironment">When false, Git starts from only the supplied environment.</param>
    public GitClient(
        IProcessRunner? processRunner = null,
        string gitExecutable = "git",
        TimeSpan? defaultTimeout = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        bool inheritEnvironment = true)
    {
        _processRunner = processRunner ?? new ProcessRunner();
        _gitExecutable = string.IsNullOrWhiteSpace(gitExecutable) ? "git" : gitExecutable;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(10);
        _environmentVariables = environmentVariables;
        _inheritEnvironment = inheritEnvironment;
    }

    /// <summary>
    /// Creates a Git client pinned to the operating-system installation with an isolated,
    /// non-interactive environment suitable for exact-source release attestation.
    /// </summary>
    /// <param name="processRunner">Optional process runner implementation.</param>
    /// <param name="defaultTimeout">Optional default timeout.</param>
    /// <returns>A trusted-system Git client.</returns>
    internal static GitClient CreateTrustedSystemClient(
        IProcessRunner? processRunner = null,
        TimeSpan? defaultTimeout = null)
    {
        var executable = ResolveTrustedSystemExecutable();
        var nullDevice = Path.DirectorySeparatorChar == '\\' ? "NUL" : "/dev/null";
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = nullDevice,
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never",
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_CONFIG_COUNT"] = "2",
            ["GIT_CONFIG_KEY_0"] = "core.hooksPath",
            ["GIT_CONFIG_VALUE_0"] = nullDevice,
            ["GIT_CONFIG_KEY_1"] = "core.fsmonitor",
            ["GIT_CONFIG_VALUE_1"] = "false",
            ["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["TMPDIR"] = Path.GetTempPath(),
            ["TMP"] = Path.GetTempPath(),
            ["TEMP"] = Path.GetTempPath(),
            ["LC_ALL"] = "C",
            ["LANG"] = "C"
        };
        if (Path.DirectorySeparatorChar == '\\')
            environment["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sshAuthSocket = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
        if (!string.IsNullOrWhiteSpace(sshAuthSocket))
            environment["SSH_AUTH_SOCK"] = sshAuthSocket;
        return new GitClient(processRunner, executable, defaultTimeout, environment, inheritEnvironment: false);
    }

    /// <summary>
    /// Gets a parsed repository status snapshot.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed repository status snapshot.</returns>
    public async Task<GitStatusSnapshot> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            new GitCommandRequest(repositoryRoot, GitCommandKind.StatusPorcelainBranch),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
            return new GitStatusSnapshot(false, null, null, 0, 0, 0, 0, result);

        return ParseStatus(result);
    }

    /// <summary>
    /// Executes <c>git status --short --branch</c>.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public Task<GitCommandResult> RunStatusShortBranchAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        => RunAsync(new GitCommandRequest(repositoryRoot, GitCommandKind.StatusShortBranch), cancellationToken);

    /// <summary>
    /// Executes <c>git status --short</c>.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public Task<GitCommandResult> RunStatusShortAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        => RunAsync(new GitCommandRequest(repositoryRoot, GitCommandKind.StatusShort), cancellationToken);

    /// <summary>
    /// Executes <c>git rev-parse --show-toplevel</c>.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public Task<GitCommandResult> ShowTopLevelAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        => RunAsync(new GitCommandRequest(repositoryRoot, GitCommandKind.ShowTopLevel), cancellationToken);

    /// <summary>
    /// Executes <c>git pull --rebase</c>.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public Task<GitCommandResult> PullRebaseAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        => RunAsync(new GitCommandRequest(repositoryRoot, GitCommandKind.PullRebase), cancellationToken);

    /// <summary>
    /// Executes <c>git switch -c &lt;branch&gt;</c>.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="branchName">Branch name to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public Task<GitCommandResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default)
        => RunAsync(new GitCommandRequest(repositoryRoot, GitCommandKind.CreateBranch, branchName: branchName), cancellationToken);

    /// <summary>
    /// Executes <c>git push --set-upstream &lt;remote&gt; &lt;branch&gt;</c>.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="branchName">Branch name to publish.</param>
    /// <param name="remoteName">Remote name to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public Task<GitCommandResult> SetUpstreamAsync(
        string repositoryRoot,
        string branchName,
        string remoteName = "origin",
        CancellationToken cancellationToken = default)
        => RunAsync(
            new GitCommandRequest(repositoryRoot, GitCommandKind.SetUpstream, branchName: branchName, remoteName: remoteName),
            cancellationToken);

    /// <summary>
    /// Executes <c>git remote get-url &lt;remote&gt;</c>.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="remoteName">Remote name to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public Task<GitCommandResult> GetRemoteUrlAsync(
        string repositoryRoot,
        string remoteName = "origin",
        CancellationToken cancellationToken = default)
        => RunAsync(
            new GitCommandRequest(repositoryRoot, GitCommandKind.GetRemoteUrl, remoteName: remoteName),
            cancellationToken);

    /// <summary>
    /// Executes an arbitrary git command with raw string arguments.
    /// Use this for commands not covered by the typed API.
    /// </summary>
    /// <param name="repositoryRoot">Repository working directory.</param>
    /// <param name="arguments">Raw git arguments.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process execution result.</returns>
    public async Task<ProcessRunResult> RunRawAsync(
        string repositoryRoot,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("Working directory is required.", nameof(repositoryRoot));

        return await _processRunner.RunAsync(
            new ProcessRunRequest(
                _gitExecutable,
                repositoryRoot,
                arguments,
                timeout ?? _defaultTimeout,
                _environmentVariables,
                captureOutput: true,
                captureError: true,
                inheritEnvironment: _inheritEnvironment),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a typed git command.
    /// </summary>
    /// <param name="request">Typed git command request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed git command result.</returns>
    public async Task<GitCommandResult> RunAsync(GitCommandRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            throw new ArgumentException("Working directory is required.", nameof(request));

        var (arguments, displayCommand) = BuildArguments(request);
        var result = await _processRunner.RunAsync(
            new ProcessRunRequest(
                _gitExecutable,
                request.WorkingDirectory,
                arguments,
                request.Timeout ?? _defaultTimeout,
                _environmentVariables,
                captureOutput: true,
                captureError: true,
                inheritEnvironment: _inheritEnvironment),
            cancellationToken).ConfigureAwait(false);

        return new GitCommandResult(
            request.CommandKind,
            request.WorkingDirectory,
            displayCommand,
            result.ExitCode,
            result.StdOut,
            result.StdErr,
            result.Executable,
            result.Duration,
            result.TimedOut);
    }

    private static string ResolveTrustedSystemExecutable()
    {
        string[] candidates;
        if (Path.DirectorySeparatorChar == '\\')
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            candidates = new[]
            {
                Path.Combine(programFiles, "Git", "cmd", "git.exe"),
                Path.Combine(programFiles, "Git", "bin", "git.exe"),
                Path.Combine(programFilesX86, "Git", "cmd", "git.exe"),
                Path.Combine(programFilesX86, "Git", "bin", "git.exe")
            };
        }
        else
        {
            candidates = new[] { "/usr/bin/git" };
        }

        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            throw new FileNotFoundException(
                "Trusted exact-source Git was not found in the operating-system installation. " +
                "Install Git in its standard system location before creating an Apple release checkpoint.");
        }
        return executable;
    }

    private static GitStatusSnapshot ParseStatus(GitCommandResult result)
    {
        string? branchName = null;
        string? upstreamBranch = null;
        var aheadCount = 0;
        var behindCount = 0;
        var trackedChangeCount = 0;
        var untrackedChangeCount = 0;

        foreach (var line in result.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branchName = line.Substring("# branch.head ".Length).Trim();
                continue;
            }

            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstreamBranch = line.Substring("# branch.upstream ".Length).Trim();
                continue;
            }

            if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                ParseAheadBehind(line.Substring("# branch.ab ".Length), out aheadCount, out behindCount);
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

        return new GitStatusSnapshot(true, branchName, upstreamBranch, aheadCount, behindCount, trackedChangeCount, untrackedChangeCount, result);
    }

    private static void ParseAheadBehind(string value, out int aheadCount, out int behindCount)
    {
        aheadCount = 0;
        behindCount = 0;

        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("+", StringComparison.Ordinal) && int.TryParse(part.Substring(1), out var ahead))
                aheadCount = ahead;

            if (part.StartsWith("-", StringComparison.Ordinal) && int.TryParse(part.Substring(1), out var behind))
                behindCount = behind;
        }
    }

    private static (IReadOnlyList<string> Arguments, string DisplayCommand) BuildArguments(GitCommandRequest request)
    {
        switch (request.CommandKind)
        {
            case GitCommandKind.StatusPorcelainBranch:
                return (["status", "--porcelain=2", "--branch"], "git status --porcelain=2 --branch");
            case GitCommandKind.StatusShortBranch:
                return (["status", "--short", "--branch"], "git status --short --branch");
            case GitCommandKind.StatusShort:
                return (["status", "--short"], "git status --short");
            case GitCommandKind.ShowTopLevel:
                return (["rev-parse", "--show-toplevel"], "git rev-parse --show-toplevel");
            case GitCommandKind.PullRebase:
                return (["pull", "--rebase"], "git pull --rebase");
            case GitCommandKind.CreateBranch:
                if (string.IsNullOrWhiteSpace(request.BranchName))
                    throw new ArgumentException("BranchName is required for CreateBranch.", nameof(request));
                return (["switch", "-c", request.BranchName!], $"git switch -c {request.BranchName}");
            case GitCommandKind.SetUpstream:
                if (string.IsNullOrWhiteSpace(request.BranchName))
                    throw new ArgumentException("BranchName is required for SetUpstream.", nameof(request));
                var remoteName = string.IsNullOrWhiteSpace(request.RemoteName) ? "origin" : request.RemoteName!;
                return (
                    ["push", "--set-upstream", remoteName, request.BranchName!],
                    $"git push --set-upstream {remoteName} {request.BranchName}");
            case GitCommandKind.GetRemoteUrl:
                var remoteToInspect = string.IsNullOrWhiteSpace(request.RemoteName) ? "origin" : request.RemoteName!;
                return (
                    ["remote", "get-url", remoteToInspect],
                    $"git remote get-url {remoteToInspect}");
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.CommandKind, "Unsupported git command kind.");
        }
    }
}
