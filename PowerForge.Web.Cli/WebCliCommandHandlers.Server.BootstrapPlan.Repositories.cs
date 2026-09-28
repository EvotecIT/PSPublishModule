using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static string BuildRepositorySshTrustGuard(PowerForgeServerRepository repository)
    {
        var identity = ShellQuote(repository.SshIdentityFile!);
        var knownHosts = ShellQuote(repository.SshKnownHostsFile!);
        var message = ShellQuote($"Repository SSH files must be nonempty, regular, root-owned, and not writable by other users: {repository.Role}");
        return string.Join("; ",
            $"test -s {identity} && test -f {identity} && test ! -L {identity} || {{ echo {message} >&2; exit 3; }}",
            $"powerforge_assert_root_controlled_path {identity}",
            $"test -s {knownHosts} && test -f {knownHosts} && test ! -L {knownHosts} || {{ echo {message} >&2; exit 3; }}",
            $"powerforge_assert_root_controlled_path {knownHosts}");
    }

    private static string BuildRepositoryBootstrapCommand(PowerForgeServerRepository repository)
    {
        var path = ShellQuote(repository.Path!);
        var gitDirectory = ShellQuote(repository.Path!.TrimEnd('/') + "/.git");
        var branchArgument = string.IsNullOrWhiteSpace(repository.Branch)
            ? string.Empty
            : $" --branch {ShellQuote(repository.Branch)}";
        var gitPrefix = BuildRepositoryGitPrefix(repository);
        var cleanMessage = ShellQuote($"Repository must be clean before installing managed files: {repository.Path}");
        var cleanCheck = $"powerforge_repository_status=$(git --no-optional-locks -C {path} status --porcelain --untracked-files=normal); " +
                         $"test -z \"$powerforge_repository_status\" || {{ echo {cleanMessage} >&2; exit 3; }}";
        var remoteMessage = ShellQuote($"Repository origin differs from the declared URL: {repository.Path}");
        var checkRemote = $"test \"$(git -C {path} remote get-url origin)\" = {ShellQuote(repository.Url!)} || {{ echo {remoteMessage} >&2; exit 3; }}";
        var existing = $"powerforge_assert_root_controlled_path {path}; {cleanCheck}; {checkRemote}; " +
                       $"{gitPrefix}git -C {path} fetch --all --tags --prune";
        if (string.IsNullOrWhiteSpace(repository.Ref) && !string.IsNullOrWhiteSpace(repository.Branch))
        {
            var branch = ShellQuote(repository.Branch);
            var remoteBranch = ShellQuote("refs/remotes/origin/" + repository.Branch);
            var branchMessage = ShellQuote($"Repository must be on its declared branch and not ahead or divergent: {repository.Path}");
            existing = $"powerforge_assert_root_controlled_path {path}; {cleanCheck}; {checkRemote}; " +
                       $"{gitPrefix}git -C {path} fetch --no-tags origin {ShellQuote("refs/heads/" + repository.Branch + ":refs/remotes/origin/" + repository.Branch)}";
            existing += $"; test \"$(git -C {path} symbolic-ref --quiet --short HEAD)\" = {branch} && " +
                        $"git -C {path} merge-base --is-ancestor HEAD {remoteBranch} || {{ echo {branchMessage} >&2; exit 3; }}; " +
                        $"{BuildRepositoryIgnoredCollisionGuard(path, remoteBranch)}; " +
                        $"git -C {path} merge --ff-only {remoteBranch}";
        }
        else if (!string.IsNullOrWhiteSpace(repository.Ref))
        {
            existing += $"; {BuildRepositoryIgnoredCollisionGuard(path, ShellQuote(repository.Ref + "^{tree}"))}";
        }

        var clone = $"{BuildRepositoryCloneTargetSafetyCommand(repository.Path!)}; " +
                    $"{gitPrefix}git clone{branchArgument} {ShellQuote(repository.Url!)} {path}";
        var pinRef = string.IsNullOrWhiteSpace(repository.Ref)
            ? string.Empty
            : $"; git -C {path} checkout --detach {ShellQuote(repository.Ref)}";
        return $"if [ -d {gitDirectory} ]; then {existing}; else {clone}; fi; " +
               $"powerforge_assert_root_controlled_path {path}{pinRef}; {cleanCheck}";
    }

    private static string BuildRepositoryIgnoredCollisionGuard(string path, string revision)
    {
        var message = ShellQuote("Ignored repository content collides with the incoming revision; refusing to overwrite it");
        return $"powerforge_repository_path={path}; " +
               $"powerforge_incoming_tree=$(git -C {path} rev-parse --verify {revision}) || exit 3; " +
               $"git -C {path} ls-files -z --others --ignored --exclude-standard | " +
               "while IFS= read -r -d '' powerforge_ignored_path; do " +
               "powerforge_ignored_probe=$powerforge_ignored_path; " +
               "while :; do " +
               $"if git -C {path} cat-file -e \"$powerforge_incoming_tree:$powerforge_ignored_probe\" 2>/dev/null; then " +
               $"powerforge_incoming_type=$(git -C {path} cat-file -t \"$powerforge_incoming_tree:$powerforge_ignored_probe\") || exit 3; " +
               "if [ \"$powerforge_incoming_type\" != tree ] || " +
               "{ [ \"$powerforge_ignored_probe\" = \"$powerforge_ignored_path\" ] && " +
               "{ [ ! -d \"$powerforge_repository_path/$powerforge_ignored_path\" ] || " +
               "[ -L \"$powerforge_repository_path/$powerforge_ignored_path\" ]; }; }; then " +
               $"echo {message} >&2; exit 3; fi; fi; " +
               "case \"$powerforge_ignored_probe\" in */*) powerforge_ignored_probe=${powerforge_ignored_probe%/*} ;; *) break ;; esac; " +
               "done; done";
    }
}
