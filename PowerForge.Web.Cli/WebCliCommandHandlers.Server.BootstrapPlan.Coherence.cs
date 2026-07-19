namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static string BuildOperationLockInstallCommand(string path)
    {
        var (configPath, configLine) = GetOperationLockTmpfilesDefinition(path);
        return string.Join("; ",
            "test -d '/etc/tmpfiles.d' && test ! -L '/etc/tmpfiles.d' && test \"$(stat -c '%U:%G' -- '/etc/tmpfiles.d')\" = 'root:root' || { echo 'systemd tmpfiles configuration directory is unsafe' >&2; exit 3; }",
            "powerforge_tmpfiles_mode=$(stat -c '%a' -- '/etc/tmpfiles.d')",
            "(( (8#$powerforge_tmpfiles_mode & 0022) == 0 )) || { echo 'systemd tmpfiles configuration directory must not be group/world writable' >&2; exit 3; }",
            "powerforge_operation_lock_tmp_config=$(mktemp '/etc/tmpfiles.d/.powerforge-operation-lock.XXXXXX')",
            "trap 'rm -f -- \"$powerforge_operation_lock_tmp_config\"' EXIT",
            $"printf '%s\\n' {ShellQuote(configLine)} >\"$powerforge_operation_lock_tmp_config\"",
            "chown root:root \"$powerforge_operation_lock_tmp_config\"",
            "chmod 0644 \"$powerforge_operation_lock_tmp_config\"",
            $"mv -Tf -- \"$powerforge_operation_lock_tmp_config\" {ShellQuote(configPath)}",
            "trap - EXIT",
            $"systemd-tmpfiles --create {ShellQuote(configPath)}",
            BuildOperationLockPostcondition(path));
    }

    internal static (string Path, string Line) GetOperationLockTmpfilesDefinition(string path)
    {
        if (!IsValidOperationLockPath(path))
            throw new InvalidOperationException($"Operation lock contains unsupported characters or location: {path}");

        var lockName = path["/var/lock/".Length..^".lock".Length];
        return ($"/etc/tmpfiles.d/powerforge-operation-lock-{lockName}.conf", $"f {path} 0644 root root -");
    }

    internal static string BuildOperationLockPostcondition(string path)
    {
        var quoted = ShellQuote(path);
        return $"test -f {quoted} && test ! -L {quoted} && " +
               $"test \"$(stat -c '%U:%G %a' -- {quoted})\" = 'root:root 644' || " +
               $"{{ echo {ShellQuote($"Operation lock must be a root-owned mode-644 regular file: {path}")} >&2; exit 3; }}";
    }

    internal static string BuildBootstrapOperationLockAcquireCommand(IEnumerable<string> paths)
    {
        var locks = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var commands = new List<string>();
        for (var index = 0; index < locks.Length; index++)
        {
            var descriptor = $"powerforge_operation_lock_fd_{index + 1}";
            commands.Add(BuildOperationLockPostcondition(locks[index]));
            commands.Add($"exec {{{descriptor}}}>{ShellQuote(locks[index])}");
            commands.Add(
                $"flock -n \"${descriptor}\" || {{ echo {ShellQuote($"Another host operation holds recovery lock: {locks[index]}")} >&2; exit 3; }}");
        }
        return string.Join('\n', commands);
    }

    internal static string BuildBootstrapOperationLockReleaseCommand(IEnumerable<string> paths)
    {
        var lockCount = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return string.Join('\n', Enumerable.Range(1, lockCount)
            .Select(static index => $"exec {{powerforge_operation_lock_fd_{index}}}>&-"));
    }

    internal static string BuildDeferredSecretInstallCommand(
        PowerForgeServerSecret secret,
        string stagingRoot,
        string repositoryRoot)
    {
        var target = secret.Path?.TrimEnd('/') ?? string.Empty;
        var staged = stagingRoot.TrimEnd('/') + target;
        var relativeTarget = target[(repositoryRoot.TrimEnd('/').Length + 1)..];
        var owner = string.IsNullOrWhiteSpace(secret.Owner) ? "root" : secret.Owner;
        var group = string.IsNullOrWhiteSpace(secret.Group) ? "root" : secret.Group;
        var mode = string.IsNullOrWhiteSpace(secret.Mode) ? "0600" : secret.Mode;
        var normalizedMode = mode.TrimStart('0');
        if (normalizedMode.Length == 0)
            normalizedMode = "0";
        var ownerFormat = IsNumericUnixIdentity(owner) ? "%u" : "%U";
        var groupFormat = IsNumericUnixIdentity(group) ? "%g" : "%G";
        var quotedTarget = ShellQuote(target);
        var quotedStaged = ShellQuote(staged);
        var targetPostcondition = string.Join(" && ",
            $"test -f {quotedTarget}",
            $"test ! -L {quotedTarget}",
            $"test \"$(stat -c '{ownerFormat}' -- {quotedTarget})\" = {ShellQuote(owner)}",
            $"test \"$(stat -c '{groupFormat}' -- {quotedTarget})\" = {ShellQuote(group)}",
            $"test \"$(stat -c '%a' -- {quotedTarget})\" = {ShellQuote(normalizedMode)}");
        var invalidTarget = $"echo {ShellQuote($"Deferred repository secret target is missing or has unexpected metadata: {target}")} >&2; exit 3";
        var validateTarget = secret.RequiredDuringBootstrap == false
            ? $"if [ -e {quotedTarget} ] || [ -L {quotedTarget} ]; then {targetPostcondition} || {{ {invalidTarget}; }}; fi"
            : $"{targetPostcondition} || {{ {invalidTarget}; }}";
        return string.Join("; ",
            $"if git -C {ShellQuote(repositoryRoot)} ls-files --error-unmatch -- {ShellQuote(relativeTarget)} >/dev/null 2>&1; then echo {ShellQuote($"Deferred repository secret must not be tracked: {target}")} >&2; exit 3; fi",
            $"git -C {ShellQuote(repositoryRoot)} check-ignore -q -- {ShellQuote(relativeTarget)} || {{ echo {ShellQuote($"Deferred repository secret must be ignored for rerunnable recovery: {target}")} >&2; exit 3; }}",
            BuildRootControlledParentPreparationCommand(target, "deferred_secret"),
            $"if [ -e {quotedStaged} ] || [ -L {quotedStaged} ]; then " +
            $"test -f {quotedStaged} && test ! -L {quotedStaged} || {{ echo {ShellQuote($"Staged repository secret is unsafe: {secret.Id}")} >&2; exit 3; }}; " +
            $"{BuildExistingRegularFileTargetGuard(target)}; " +
            $"install -T -o {ShellQuote(owner)} -g {ShellQuote(group)} -m {ShellQuote(mode)} {quotedStaged} {quotedTarget}; " +
            $"rm -f -- {quotedStaged}; else\n{BuildExistingRegularFileTargetGuard(target)}; fi",
            validateTarget);
    }

    internal static string BuildApacheActivationCommand(PowerForgeServerApache apache)
    {
        var service = string.IsNullOrWhiteSpace(apache.Service) ? "apache2" : apache.Service;
        var validateCommand = string.IsNullOrWhiteSpace(apache.ValidateCommand)
            ? "apachectl configtest"
            : apache.ValidateCommand;
        var entries = new List<(string Name, bool Enabled, string Enable, string Disable, string ActivePath)>();
        entries.AddRange((apache.Sites ?? Array.Empty<PowerForgeServerApacheFile>())
            .Where(static file => file.Enabled is not null)
            .Select(static file =>
            {
                var name = Path.GetFileName(file.Target) ?? string.Empty;
                return (name, file.Enabled == true, "a2ensite", "a2dissite", $"/etc/apache2/sites-enabled/{name}");
            }));
        entries.AddRange((apache.Conf ?? Array.Empty<PowerForgeServerApacheFile>())
            .Where(static file => file.Enabled is not null)
            .Select(static file =>
            {
                var name = Path.GetFileName(file.Target) ?? string.Empty;
                return (name, file.Enabled == true, "a2enconf", "a2disconf", $"/etc/apache2/conf-enabled/{name}");
            }));

        var commands = new List<string>
        {
            "powerforge_apache_state=$(mktemp -d)",
            "chmod 0700 \"$powerforge_apache_state\"",
            "powerforge_restore_apache_activation() {",
            "  powerforge_apache_status=$?",
            "  trap - EXIT HUP INT TERM",
            "  set +e"
        };
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            commands.Add(
                $"  if [ -f \"$powerforge_apache_state/{index}.enabled\" ]; then {entry.Enable} {ShellQuote(entry.Name)} >/dev/null; else {entry.Disable} {ShellQuote(entry.Name)} >/dev/null; fi");
        }
        commands.Add($"  if {validateCommand}; then systemctl reload {ShellQuote(service)} || true; fi");
        commands.Add("  rm -rf -- \"$powerforge_apache_state\"");
        commands.Add("  exit \"$powerforge_apache_status\"");
        commands.Add("}");
        commands.Add("trap 'rm -rf -- \"$powerforge_apache_state\"' EXIT");
        commands.Add("trap 'exit 129' HUP");
        commands.Add("trap 'exit 130' INT");
        commands.Add("trap 'exit 143' TERM");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            commands.Add(
                $"if [ -e {ShellQuote(entry.ActivePath)} ] || [ -L {ShellQuote(entry.ActivePath)} ]; then : >\"$powerforge_apache_state/{index}.enabled\"; fi");
        }
        commands.Add("trap powerforge_restore_apache_activation EXIT");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            commands.Add($"{(entry.Enabled ? entry.Enable : entry.Disable)} {ShellQuote(entry.Name)}");
        }
        commands.Add(validateCommand);
        commands.Add($"systemctl reload {ShellQuote(service)}");
        commands.Add("trap - EXIT HUP INT TERM");
        commands.Add("rm -rf -- \"$powerforge_apache_state\"");
        return string.Join('\n', commands);
    }
}
