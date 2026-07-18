using System.Text;
using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerBootstrapPlan(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.bootstrap-plan");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var manifest = loaded.Manifest;
        var manifestPath = loaded.ManifestPath!;
        var outPathArg = ResolveServerPlanOutputDirectory(subArgs);
        var outputRoot = ResolveBootstrapPlanOutputPath(outPathArg, manifest);
        Directory.CreateDirectory(outputRoot);

        var warnings = new List<string>();
        var steps = BuildBootstrapPlanSteps(manifest, warnings);
        var markdownPath = Path.Combine(outputRoot, "bootstrap-plan.md");
        var scriptPath = Path.Combine(outputRoot, "bootstrap-plan.sh");
        WriteBootstrapPlanMarkdown(markdownPath, manifest, steps, warnings);
        WriteBootstrapPlanScript(scriptPath, steps);

        var result = new PowerForgeServerBootstrapPlanResult
        {
            ManifestPath = manifestPath,
            OutputPath = outputRoot,
            MarkdownPath = markdownPath,
            ScriptPath = scriptPath,
            Steps = steps.ToArray(),
            Warnings = warnings.ToArray()
        };

        File.WriteAllText(
            Path.Combine(outputRoot, "bootstrap-plan.json"),
            JsonSerializer.Serialize(result, WebCliJson.Context.PowerForgeServerBootstrapPlanResult));

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.bootstrap-plan",
                Success = true,
                ExitCode = 0,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.PowerForgeServerBootstrapPlanResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return 0;
        }

        logger.Success("Server bootstrap plan generated.");
        logger.Info($"Output: {outputRoot}");
        logger.Info($"Markdown: {markdownPath}");
        logger.Info($"Script draft: {scriptPath}");
        logger.Info($"Steps: {steps.Count}");
        foreach (var warning in warnings)
            logger.Warn(warning);

        return 0;
    }

    private static string ResolveBootstrapPlanOutputPath(string? outPathArg, PowerForgeServerRecoveryManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(outPathArg))
            return Path.GetFullPath(outPathArg);

        var name = SanitizeFileName(string.IsNullOrWhiteSpace(manifest.Name) ? "server" : manifest.Name);
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "_server-state", name, "bootstrap-plan"));
    }

    internal static List<PowerForgeServerBootstrapPlanStep> BuildBootstrapPlanSteps(
        PowerForgeServerRecoveryManifest manifest,
        ICollection<string> warnings)
    {
        var steps = new List<PowerForgeServerBootstrapPlanStep>();
        var plannedCommands = new HashSet<string>(StringComparer.Ordinal);
        var order = 1;

        AddStep(steps, ref order, "preflight", "Confirm supported host",
            BuildServerHostPreflightCommand(manifest.Target),
            plannedCommands: plannedCommands);

        if (RequiresRootControlledPathGuard(manifest))
        {
            AddStep(steps, ref order, "security", "Define root-controlled path guard",
                BuildRootControlledPathGuardFunction(),
                plannedCommands: plannedCommands);
        }

        if (manifest.Packages?.Apt?.Length > 0)
        {
            AddStep(steps, ref order, "packages", "Install apt prerequisites",
                "apt-get update && apt-get install -y " + string.Join(' ', manifest.Packages.Apt.Select(ShellQuote)),
                plannedCommands: plannedCommands);
        }

        var dotnetPackages = GetDeclaredDotnetSdkPackageNames(manifest.Packages?.DotnetSdks);
        if (dotnetPackages.Length > 0)
        {
            AddStep(steps, ref order, "runtimes", "Install .NET SDK prerequisites",
                "apt-get update && apt-get install -y " + string.Join(' ', dotnetPackages.Select(ShellQuote)),
                plannedCommands: plannedCommands);
        }

        if (manifest.Packages?.Powershell == true)
        {
            AddStep(steps, ref order, "runtimes", "Configure Microsoft package repository",
                BuildMicrosoftPackageRepositoryInstallCommand(),
                plannedCommands: plannedCommands);
            AddStep(steps, ref order, "runtimes", "Install PowerShell prerequisite",
                BuildPowerShellInstallCommand(), plannedCommands: plannedCommands);
        }

        foreach (var account in manifest.Accounts ?? Array.Empty<PowerForgeServerAccount>())
        {
            if (string.IsNullOrWhiteSpace(account.Name)) continue;
            var useradd = new List<string> { "useradd" };
            if (account.System) useradd.Add("--system");
            useradd.Add("--user-group");
            useradd.Add(account.CreateHome ? "--create-home" : "--no-create-home");
            if (!string.IsNullOrWhiteSpace(account.Home))
            {
                useradd.Add("--home-dir");
                useradd.Add(ShellQuote(account.Home));
            }
            if (!string.IsNullOrWhiteSpace(account.Shell))
            {
                useradd.Add("--shell");
                useradd.Add(ShellQuote(account.Shell));
            }
            useradd.Add(ShellQuote(account.Name));
            AddStep(steps, ref order, "accounts", $"Create account {account.Name}",
                $"id -u {ShellQuote(account.Name)} >/dev/null 2>&1 || {string.Join(' ', useradd)}",
                plannedCommands: plannedCommands);
        }

        foreach (var path in manifest.Paths ?? Array.Empty<PowerForgeServerPath>())
        {
            if (string.IsNullOrWhiteSpace(path.Path)) continue;
            var owner = string.IsNullOrWhiteSpace(path.Owner) ? "root" : path.Owner;
            var group = string.IsNullOrWhiteSpace(path.Group) ? "root" : path.Group;
            var mode = string.IsNullOrWhiteSpace(path.Mode) ? "755" : path.Mode;
            if (path.Kind?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true)
            {
                AddStep(steps, ref order, "filesystem", $"Create directory {path.Path}",
                    BuildManagedDirectoryInstallCommand(path.Path, owner, group, mode),
                    plannedCommands: plannedCommands);
            }
        }

        var operationLocks = manifest.OperationLocks ?? Array.Empty<string>();
        foreach (var operationLock in operationLocks)
        {
            AddStep(steps, ref order, "locking", $"Prepare shared operation lock {operationLock}",
                BuildOperationLockInstallCommand(operationLock), plannedCommands: plannedCommands);
        }
        if (operationLocks.Length > 0)
        {
            AddStep(steps, ref order, "locking", "Acquire shared operation locks for bootstrap",
                BuildBootstrapOperationLockAcquireCommand(operationLocks), plannedCommands: plannedCommands);
        }

        foreach (var repository in manifest.Repositories ?? Array.Empty<PowerForgeServerRepository>())
        {
            if (string.IsNullOrWhiteSpace(repository.Path)) continue;
            var prerequisites = repository.BootstrapRequiredFiles?
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            if (prerequisites.Length > 0)
            {
                var checks = string.Join(" && ", prerequisites.Select(path => $"test -s {ShellQuote(path)}"));
                var message = ShellQuote($"Restore bootstrap prerequisite files for repository {repository.Role} before continuing.");
                AddStep(steps, ref order, "repositories", $"Verify {repository.Role} repository prerequisites",
                    $"{checks} || {{ echo {message} >&2; exit 3; }}",
                    plannedCommands: plannedCommands);
            }
            if (!string.IsNullOrWhiteSpace(repository.RefCaptureCommandId) && string.IsNullOrWhiteSpace(repository.Ref))
            {
                var message = ShellQuote($"Use the captured recovery manifest containing repository ref from command {repository.RefCaptureCommandId}.");
                AddStep(steps, ref order, "repositories", $"Verify {repository.Role} captured revision",
                    $"echo {message} >&2; exit 3",
                    plannedCommands: plannedCommands);
            }
            if (string.IsNullOrWhiteSpace(repository.Url))
            {
                warnings.Add($"Repository URL is missing for role '{repository.Role}'. Bootstrap script leaves a manual clone step.");
                AddStep(steps, ref order, "repositories", $"Clone {repository.Role} repository", $"# TODO: git clone <{repository.Role}-repo-url> {ShellQuote(repository.Path)}", manual: true, plannedCommands: plannedCommands);
            }
            else
            {
                var branchArg = string.IsNullOrWhiteSpace(repository.Branch) ? string.Empty : $" --branch {ShellQuote(repository.Branch)}";
                var gitDirectory = repository.Path.TrimEnd('/') + "/.git";
                var gitPrefix = BuildRepositoryGitPrefix(repository);
                var prepareCloneTarget = BuildRepositoryCloneTargetSafetyCommand(repository.Path);
                var pinRef = string.IsNullOrWhiteSpace(repository.Ref)
                    ? string.Empty
                    : $"; git -C {ShellQuote(repository.Path)} checkout --detach {ShellQuote(repository.Ref)}";
                var cleanCheck =
                    $"; powerforge_repository_status=$(git --no-optional-locks -C {ShellQuote(repository.Path)} status --porcelain --untracked-files=normal); " +
                    $"test -z \"$powerforge_repository_status\" || {{ echo {ShellQuote($"Repository must be clean before installing managed files: {repository.Path}")} >&2; exit 3; }}";
                AddStep(steps, ref order, "repositories", $"Clone or update {repository.Role} repository",
                    $"if [ -d {ShellQuote(gitDirectory)} ]; then powerforge_assert_root_controlled_path {ShellQuote(repository.Path)}; {gitPrefix}git -C {ShellQuote(repository.Path)} fetch --all --tags --prune; else {prepareCloneTarget}; {gitPrefix}git clone{branchArg} {ShellQuote(repository.Url)} {ShellQuote(repository.Path)}; fi; powerforge_assert_root_controlled_path {ShellQuote(repository.Path)}{pinRef}{cleanCheck}",
                    plannedCommands: plannedCommands);
            }
        }

        var deferredSecrets = (manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
            .Where(static secret => secret.RestoreAfterRepositories && !string.IsNullOrWhiteSpace(secret.Path))
            .ToArray();
        var secretStagingRoot = BuildRestoreSecretsStagingRoot(manifest.Name);
        foreach (var secret in deferredSecrets)
        {
            var repositoryRoot = (manifest.Repositories ?? Array.Empty<PowerForgeServerRepository>())
                .Select(static repository => repository.Path?.TrimEnd('/'))
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Where(root => PathStrictlyContains(root, secret.Path!))
                .OrderByDescending(static root => root.Length)
                .First();
            AddStep(steps, ref order, "secrets", $"Install staged secret {secret.Id}",
                BuildDeferredSecretInstallCommand(secret, secretStagingRoot, repositoryRoot),
                sensitive: true,
                plannedCommands: plannedCommands);
        }
        if (deferredSecrets.Length > 0)
        {
            AddStep(steps, ref order, "secrets", "Remove empty secret staging directory",
                $"test -d {ShellQuote(secretStagingRoot)} && test ! -L {ShellQuote(secretStagingRoot)} && " +
                $"test \"$(stat -c '%U:%G %a' -- {ShellQuote(secretStagingRoot)})\" = 'root:root 700' && " +
                $"find {ShellQuote(secretStagingRoot)} -depth -mindepth 1 -type d -empty -delete && " +
                $"test -z \"$(find {ShellQuote(secretStagingRoot)} -mindepth 1 -print -quit)\" && rmdir -- {ShellQuote(secretStagingRoot)}",
                sensitive: true,
                plannedCommands: plannedCommands);
        }

        foreach (var path in manifest.Paths ?? Array.Empty<PowerForgeServerPath>())
        {
            if (string.IsNullOrWhiteSpace(path.Source) || string.IsNullOrWhiteSpace(path.Path)) continue;
            var repository = FindManagedSourceRepository(manifest.Repositories, path.Source)
                             ?? throw new InvalidOperationException($"Managed source '{path.Source}' is not below a declared repository root.");
            var repositoryRoot = repository.Path!.TrimEnd('/');
            AddStep(steps, ref order, "managed-files", $"Install managed file {path.Path}",
                BuildManagedFileInstallCommand(path, repositoryRoot, repository.Ref), plannedCommands: plannedCommands);
        }

        foreach (var command in manifest.Bootstrap?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>())
        {
            if (string.IsNullOrWhiteSpace(command.Command)) continue;
            AddStep(steps, ref order, "bootstrap", command.Id ?? "bootstrap command", command.Command, command.Sensitive, plannedCommands: plannedCommands);
        }

        var apacheModules = manifest.Packages?.ApacheModules ?? manifest.Apache?.Modules ?? Array.Empty<string>();
        if (apacheModules.Length > 0)
            AddStep(steps, ref order, "apache", "Enable Apache modules", "a2enmod " + string.Join(' ', apacheModules.Select(ShellQuote)), plannedCommands: plannedCommands);

        foreach (var file in (manifest.Apache?.Sites ?? Array.Empty<PowerForgeServerApacheFile>())
                 .Concat(manifest.Apache?.Conf ?? Array.Empty<PowerForgeServerApacheFile>()))
        {
            if (string.IsNullOrWhiteSpace(file.Source) || string.IsNullOrWhiteSpace(file.Target)) continue;
            var repository = FindManagedSourceRepository(manifest.Repositories, file.Source)
                             ?? throw new InvalidOperationException($"Managed source '{file.Source}' is not below a declared repository root.");
            var repositoryRoot = repository.Path!.TrimEnd('/');
            AddStep(steps, ref order, "apache", $"Install Apache file {file.Target}",
                BuildRepositoryManagedFileInstallCommand(file.Source, file.Target, repositoryRoot, "0644", repositoryRef: repository.Ref),
                plannedCommands: plannedCommands);
        }

        var apacheActivationFiles = (manifest.Apache?.Sites ?? Array.Empty<PowerForgeServerApacheFile>())
            .Concat(manifest.Apache?.Conf ?? Array.Empty<PowerForgeServerApacheFile>())
            .Where(static file => file.Enabled is not null)
            .ToArray();
        if (apacheActivationFiles.Length > 0)
        {
            AddStep(steps, ref order, "apache", "Activate Apache configuration transactionally",
                BuildApacheActivationCommand(manifest.Apache!), plannedCommands: plannedCommands);
        }

        foreach (var unit in (manifest.Systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
                 .Concat(manifest.Systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>()))
        {
            if (!string.IsNullOrWhiteSpace(unit.Source) && !string.IsNullOrWhiteSpace(unit.Target))
            {
                var repository = FindManagedSourceRepository(manifest.Repositories, unit.Source)
                                 ?? throw new InvalidOperationException($"Managed source '{unit.Source}' is not below a declared repository root.");
                var repositoryRoot = repository.Path!.TrimEnd('/');
                AddStep(steps, ref order, "systemd", $"Install systemd unit {unit.Name}",
                    BuildRepositoryManagedFileInstallCommand(unit.Source, unit.Target, repositoryRoot, "0644", repositoryRef: repository.Ref),
                    plannedCommands: plannedCommands);
            }
        }

        if (manifest.Systemd is not null)
        {
            AddStep(steps, ref order, "systemd", "Reload systemd units", "systemctl daemon-reload", plannedCommands: plannedCommands);

            foreach (var unit in (manifest.Systemd.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
                     .Concat(manifest.Systemd.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
                     .Where(static unit => unit.Enabled && !string.IsNullOrWhiteSpace(unit.Name)))
            {
                AddStep(steps, ref order, "systemd", $"Enable {unit.Name}", $"systemctl enable {ShellQuote(unit.Name!)}", plannedCommands: plannedCommands);
            }
        }

        if (manifest.Firewall is not null)
        {
            foreach (var port in manifest.Firewall.SshPorts ?? Array.Empty<int>())
                AddStep(steps, ref order, "firewall", $"Allow SSH port {port}", $"ufw allow {port}/tcp", plannedCommands: plannedCommands);

            if (!string.IsNullOrWhiteSpace(manifest.Firewall.SyncCommand))
                AddStep(steps, ref order, "firewall", "Apply origin firewall sync", manifest.Firewall.SyncCommand, plannedCommands: plannedCommands);

            AddStep(steps, ref order, "firewall", "Enable UFW", "ufw --force enable", plannedCommands: plannedCommands);
        }

        foreach (var secret in manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
        {
            if (secret.RequiredDuringBootstrap == false)
                continue;

            var message = ShellQuote($"Restore encrypted secret {secret.Id} before continuing.");
            string guard;
            if (!string.IsNullOrWhiteSpace(secret.Path))
            {
                guard = secret.RestoreMode?.Equals("directory", StringComparison.OrdinalIgnoreCase) == true
                    ? $"test -d {ShellQuote(secret.Path)} && find {ShellQuote(secret.Path)} -mindepth 1 -maxdepth 1 -print -quit | grep -q . || {{ echo {message} >&2; exit 3; }}"
                    : $"test -s {ShellQuote(secret.Path)} || {{ echo {message} >&2; exit 3; }}";
            }
            else if (!string.IsNullOrWhiteSpace(secret.Env))
            {
                guard = $"test -n \"$(printenv {ShellQuote(secret.Env)} 2>/dev/null)\" || {{ echo {message} >&2; exit 3; }}";
            }
            else
            {
                guard = $"echo {message} >&2; exit 3";
            }
            AddStep(steps, ref order, "secrets", $"Confirm restored secret {secret.Id}", guard, plannedCommands: plannedCommands);
        }

        foreach (var command in manifest.Deploy?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>())
        {
            if (string.IsNullOrWhiteSpace(command.Command)) continue;
            var shell = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? command.Command
                : $"cd {ShellQuote(command.WorkingDirectory)} && {command.Command}";
            AddStep(steps, ref order, "deploy", command.Id ?? "deploy command", shell, command.Sensitive, plannedCommands: plannedCommands);
        }

        AddStep(steps, ref order, "verify", "Run PowerForge server verify", "# Run from an operator workstation: powerforge-web server verify --manifest <manifest> --fail-on-failure", manual: true, plannedCommands: plannedCommands);
        return steps;
    }

    internal static string BuildManagedFileInstallCommand(
        PowerForgeServerPath path,
        string repositoryRoot,
        string? repositoryRef = null)
    {
        var owner = string.IsNullOrWhiteSpace(path.Owner) ? "root" : path.Owner;
        var group = string.IsNullOrWhiteSpace(path.Group) ? "root" : path.Group;
        var mode = string.IsNullOrWhiteSpace(path.Mode) ? "0644" : path.Mode;
        if (!string.Equals(path.Validation, "sudoers", StringComparison.OrdinalIgnoreCase))
            return BuildRepositoryManagedFileInstallCommand(
                path.Source ?? string.Empty,
                path.Path ?? string.Empty,
                repositoryRoot,
                mode,
                owner,
                group,
                repositoryRef);

        var source = ShellQuote(path.Source ?? string.Empty);
        var target = ShellQuote(path.Path ?? string.Empty);
        var sourceSafety = BuildManagedSourceSafetyCommand(
            path.Source ?? string.Empty,
            repositoryRoot,
            useSudo: false,
            requireRootControl: true,
            repositoryRef: repositoryRef);
        var targetSafety = BuildRootControlledTargetParentSafetyCommand(path.Path ?? string.Empty);
        var install = $"install -T -o {ShellQuote(owner)} -g {ShellQuote(group)} -m {ShellQuote(mode)} {source}";

        var temporaryTemplate = ShellQuote((path.Path ?? string.Empty) + ".powerforge.XXXXXX");
        return string.Join('\n',
            sourceSafety,
            targetSafety,
            $"powerforge_sudoers_temp=$(mktemp {temporaryTemplate})",
            "powerforge_sudoers_backup=\"${powerforge_sudoers_temp}.previous\"",
            "powerforge_sudoers_had_previous=0",
            "powerforge_sudoers_replaced=0",
            "powerforge_sudoers_restore() {",
            "  powerforge_sudoers_status=$?",
            "  trap - EXIT HUP INT TERM",
            "  if [ \"$powerforge_sudoers_replaced\" = 1 ]; then",
            $"    if [ \"$powerforge_sudoers_had_previous\" = 1 ] && [ -e \"$powerforge_sudoers_backup\" ]; then mv -fT -- \"$powerforge_sudoers_backup\" {target}; else rm -f -- {target}; fi",
            "    visudo -c || powerforge_sudoers_status=1",
            "  fi",
            "  rm -f -- \"$powerforge_sudoers_temp\" \"$powerforge_sudoers_backup\" || true",
            "  exit \"$powerforge_sudoers_status\"",
            "}",
            "trap powerforge_sudoers_restore EXIT",
            "trap 'exit 129' HUP",
            "trap 'exit 130' INT",
            "trap 'exit 143' TERM",
            $"{install} \"$powerforge_sudoers_temp\"",
            "visudo -cf \"$powerforge_sudoers_temp\"",
            BuildExistingRegularFileTargetGuard(path.Path ?? string.Empty),
            $"if [ -e {target} ]; then cp -a -- {target} \"$powerforge_sudoers_backup\"; powerforge_sudoers_had_previous=1; fi",
            "powerforge_sudoers_replaced=1",
            $"mv -fT -- \"$powerforge_sudoers_temp\" {target}",
            "visudo -c",
            "powerforge_sudoers_replaced=0",
            "rm -f -- \"$powerforge_sudoers_backup\"",
            "trap - EXIT HUP INT TERM");
    }

    internal static string BuildRepositoryManagedFileInstallCommand(
        string source,
        string target,
        string repositoryRoot,
        string mode,
        string owner = "root",
        string group = "root",
        string? repositoryRef = null)
    {
        var install = IsRootUnixIdentity(owner)
            ? $"install -T -o {ShellQuote(owner)} -g {ShellQuote(group)} -m {ShellQuote(mode)} {ShellQuote(source)} {ShellQuote(target)}"
            : $"runuser -u {ShellQuote(owner)} -g {ShellQuote(group)} -- install -T -m {ShellQuote(mode)} {ShellQuote(source)} {ShellQuote(target)}";
        var commands = new List<string>
        {
            BuildManagedSourceSafetyCommand(source, repositoryRoot, useSudo: false, requireRootControl: true, repositoryRef: repositoryRef)
        };
        if (IsRootUnixIdentity(owner))
            commands.Add(BuildRootControlledTargetSafetyCommand(target));
        commands.Add(install);
        return string.Join('\n', commands);
    }

    internal static string BuildManagedDirectoryInstallCommand(
        string target,
        string owner,
        string group,
        string mode)
    {
        var quotedTarget = ShellQuote(target);
        var existingTargetGuard =
            $"if [ -e {quotedTarget} ] || [ -L {quotedTarget} ]; then test -d {quotedTarget} && test ! -L {quotedTarget} || " +
            $"{{ echo {ShellQuote($"Managed directory target must not be a symlink or non-directory: {target}")} >&2; exit 3; }}; fi";
        var normalizedMode = mode.TrimStart('0');
        if (normalizedMode.Length == 0)
            normalizedMode = "0";
        var ownerFormat = IsNumericUnixIdentity(owner) ? "%u" : "%U";
        var groupFormat = IsNumericUnixIdentity(group) ? "%g" : "%G";
        var postcondition = string.Join(" && ",
            $"test -d {quotedTarget}",
            $"test ! -L {quotedTarget}",
            $"test \"$(stat -c '{ownerFormat}' -- {quotedTarget})\" = {ShellQuote(owner)}",
            $"test \"$(stat -c '{groupFormat}' -- {quotedTarget})\" = {ShellQuote(group)}",
            $"test \"$(stat -c '%a' -- {quotedTarget})\" = {ShellQuote(normalizedMode)}");
        if (!IsRootUnixIdentity(owner))
        {
            return string.Join('\n',
                existingTargetGuard,
                $"if runuser -u {ShellQuote(owner)} -g {ShellQuote(group)} -- install -d -m {ShellQuote(mode)} {quotedTarget} && {postcondition}; then :; else",
                BuildRootControlledParentPreparationCommand(target, "directory"),
                existingTargetGuard,
                $"install -d -o {ShellQuote(owner)} -g {ShellQuote(group)} -m {ShellQuote(mode)} {quotedTarget}",
                "fi",
                postcondition);
        }

        return string.Join('\n',
            BuildRootControlledParentPreparationCommand(target, "directory"),
            existingTargetGuard,
            $"install -d -o {ShellQuote(owner)} -g {ShellQuote(group)} -m {ShellQuote(mode)} {quotedTarget}",
            postcondition);
    }

    internal static string BuildManagedSourceSafetyCommand(
        string source,
        string repositoryRoot,
        bool useSudo,
        bool requireRootControl = false,
        string? repositoryRef = null)
    {
        var prefix = useSudo ? "sudo -n " : string.Empty;
        var quotedSource = ShellQuote(source);
        var quotedRepository = ShellQuote(repositoryRoot);
        var repositoryRelativePath = source[(repositoryRoot.TrimEnd('/').Length + 1)..];
        var revision = string.IsNullOrWhiteSpace(repositoryRef) ? "HEAD" : repositoryRef;
        var sourceObject = ShellQuote($"{revision}:{repositoryRelativePath}");
        var revisionArgument = ShellQuote(revision);
        var relativePathArgument = ShellQuote(repositoryRelativePath);
        var checks = new List<string>
        {
            $"{prefix}test -f {quotedSource}",
            $"{prefix}test ! -L {quotedSource}",
            $"powerforge_managed_source_real=$({prefix}realpath -e -- {quotedSource})",
            $"powerforge_managed_repository_real=$({prefix}realpath -e -- {quotedRepository})",
            "case \"$powerforge_managed_source_real\" in \"$powerforge_managed_repository_real\"/*) ;; *) echo 'Managed source resolves outside its declared repository.' >&2; exit 3 ;; esac",
            $"powerforge_managed_source_entry=$({prefix}git -c \"safe.directory=$powerforge_managed_repository_real\" -C \"$powerforge_managed_repository_real\" ls-tree {revisionArgument} -- {relativePathArgument})",
            "powerforge_managed_source_mode=${powerforge_managed_source_entry%% *}",
            "case \"$powerforge_managed_source_mode\" in 100644|100755) ;; *) false ;; esac",
            $"powerforge_managed_source_type=$({prefix}git -c \"safe.directory=$powerforge_managed_repository_real\" -C \"$powerforge_managed_repository_real\" cat-file -t {sourceObject})",
            "test \"$powerforge_managed_source_type\" = blob",
            $"{prefix}git -c \"safe.directory=$powerforge_managed_repository_real\" -C \"$powerforge_managed_repository_real\" cat-file -p {sourceObject} | {prefix}cmp -s -- {quotedSource} -"
        };
        if (requireRootControl)
        {
            checks.Add($"test \"$powerforge_managed_repository_real\" = {quotedRepository}");
            checks.Add("powerforge_assert_root_controlled_path \"$powerforge_managed_source_real\"");
        }
        var failure = ShellQuote($"Managed source must be a safe tracked file in the declared repository revision: {source}");
        return $"{string.Join(" && ", checks)} || {{ echo {failure} >&2; exit 3; }}";
    }

    internal static string BuildRootControlledPathGuardFunction(bool useSudo = false)
    {
        var prefix = useSudo ? "sudo -n " : string.Empty;
        return string.Join('\n',
            "powerforge_assert_root_controlled_path() {",
            "  local powerforge_path=\"$1\"",
            "  while :; do",
            $"    {prefix}test -e \"$powerforge_path\" || {{ echo \"Required path does not exist: $powerforge_path\" >&2; return 1; }}",
            $"    {prefix}test ! -L \"$powerforge_path\" || {{ echo \"Root-controlled path must not be a symlink: $powerforge_path\" >&2; return 1; }}",
            $"    test \"$({prefix}stat -c '%u' -- \"$powerforge_path\")\" = 0 || {{ echo \"Root-controlled path is not owned by root: $powerforge_path\" >&2; return 1; }}",
            $"    test -z \"$({prefix}find \"$powerforge_path\" -maxdepth 0 -perm /022 -print -quit)\" || {{ echo \"Root-controlled path is group- or world-writable: $powerforge_path\" >&2; return 1; }}",
            "    test \"$powerforge_path\" = / && break",
            "    powerforge_path=$(dirname -- \"$powerforge_path\")",
            "  done",
            "}");
    }

    private static string BuildRootControlledTargetParentSafetyCommand(string target)
        => $"powerforge_assert_root_controlled_path \"$(dirname -- {ShellQuote(target)})\"";

    private static string BuildRootControlledTargetSafetyCommand(string target)
        => string.Join('\n',
            BuildRootControlledTargetParentSafetyCommand(target),
            BuildExistingRegularFileTargetGuard(target));

    private static string BuildExistingRegularFileTargetGuard(string target)
    {
        var quotedTarget = ShellQuote(target);
        var message = ShellQuote($"Managed root target must be a regular non-symlink file when it already exists: {target}");
        return $"if [ -e {quotedTarget} ] || [ -L {quotedTarget} ]; then test -f {quotedTarget} && test ! -L {quotedTarget} || {{ echo {message} >&2; exit 3; }}; fi";
    }

    private static string BuildRepositoryCloneTargetSafetyCommand(string repositoryPath)
    {
        var quotedRepository = ShellQuote(repositoryPath);
        var message = ShellQuote($"Fresh clone target must not already exist or be a symlink: {repositoryPath}");
        return string.Join('\n',
            BuildRootControlledParentPreparationCommand(repositoryPath, "repository"),
            $"test ! -e {quotedRepository} && test ! -L {quotedRepository} || {{ echo {message} >&2; exit 3; }}");
    }

    private static string BuildRootControlledParentPreparationCommand(
        string target,
        string variablePrefix)
    {
        var parent = GetUnixParentDirectory(target);
        var quotedParent = ShellQuote(parent);
        var variable = $"powerforge_{variablePrefix}_ancestor";
        var commands = new List<string>
        {
            $"{variable}={quotedParent}",
            $"while [ ! -e \"${variable}\" ] && [ ! -L \"${variable}\" ]; do {variable}=$(dirname -- \"${variable}\"); done",
            $"powerforge_assert_root_controlled_path \"${variable}\""
        };
        commands.Add($"mkdir -p -- {quotedParent}");
        commands.Add($"powerforge_assert_root_controlled_path {quotedParent}");
        return string.Join('\n', commands);
    }

    private static bool RequiresRootControlledPathGuard(PowerForgeServerRecoveryManifest manifest)
        => manifest.Repositories?.Any(static repository => !string.IsNullOrWhiteSpace(repository.Path)) == true ||
           manifest.Paths?.Any(static path => !string.IsNullOrWhiteSpace(path.Source)) == true ||
           manifest.Paths?.Any(static path => string.Equals(path.Kind, "directory", StringComparison.OrdinalIgnoreCase)) == true ||
           (manifest.Apache?.Sites ?? Array.Empty<PowerForgeServerApacheFile>())
                .Concat(manifest.Apache?.Conf ?? Array.Empty<PowerForgeServerApacheFile>())
               .Any(static file => !string.IsNullOrWhiteSpace(file.Source)) ||
           (manifest.Systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
               .Concat(manifest.Systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
               .Any(static unit => !string.IsNullOrWhiteSpace(unit.Source));

    private static string GetUnixParentDirectory(string path)
    {
        var normalized = path.TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        return separator <= 0 ? "/" : normalized[..separator];
    }

    internal static PowerForgeServerRepository? FindManagedSourceRepository(
        IEnumerable<PowerForgeServerRepository>? repositories,
        string source)
        => (repositories ?? Array.Empty<PowerForgeServerRepository>())
            .Where(static repository => !string.IsNullOrWhiteSpace(repository.Path))
            .Where(repository => PathStrictlyContains(repository.Path!.TrimEnd('/'), source))
            .OrderByDescending(static repository => repository.Path!.Length)
            .FirstOrDefault();

    internal static string? FindManagedSourceRepositoryRoot(
        IEnumerable<PowerForgeServerRepository>? repositories,
        string source)
        => FindManagedSourceRepository(repositories, source)?.Path?.TrimEnd('/');

    private static string BuildRepositoryGitPrefix(PowerForgeServerRepository repository)
    {
        if (string.IsNullOrWhiteSpace(repository.SshIdentityFile) ||
            string.IsNullOrWhiteSpace(repository.SshKnownHostsFile))
            return string.Empty;

        var sshCommand = $"ssh -i {ShellQuote(repository.SshIdentityFile)} -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile={ShellQuote(repository.SshKnownHostsFile)}";
        return $"GIT_SSH_COMMAND={ShellQuote(sshCommand)} ";
    }

    private static void AddStep(
        ICollection<PowerForgeServerBootstrapPlanStep> steps,
        ref int order,
        string category,
        string title,
        string? command,
        bool sensitive = false,
        bool manual = false,
        ISet<string>? plannedCommands = null)
    {
        if (!string.IsNullOrWhiteSpace(command) &&
            plannedCommands is not null &&
            !command.TrimStart().StartsWith("#", StringComparison.Ordinal) &&
            !plannedCommands.Add(command))
        {
            return;
        }

        steps.Add(new PowerForgeServerBootstrapPlanStep
        {
            Order = order++,
            Category = category,
            Title = title,
            Command = command,
            Sensitive = sensitive,
            Manual = manual
        });
    }

    private static void WriteBootstrapPlanMarkdown(
        string path,
        PowerForgeServerRecoveryManifest manifest,
        IReadOnlyList<PowerForgeServerBootstrapPlanStep> steps,
        IReadOnlyList<string> warnings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# PowerForge Server Bootstrap Plan");
        builder.AppendLine();
        builder.AppendLine($"Manifest: `{manifest.Name}`");
        builder.AppendLine($"Target: `{manifest.Target?.SshAlias ?? manifest.Target?.Host ?? "new host"}`");
        builder.AppendLine();
        builder.AppendLine("This is a review artifact. It is safe to commit the plan, but not generated secret bundles.");
        builder.AppendLine();

        foreach (var group in steps.GroupBy(static step => step.Category))
        {
            builder.AppendLine($"## {group.Key}");
            builder.AppendLine();
            foreach (var step in group)
            {
                builder.AppendLine($"{step.Order}. {step.Title}");
                if (step.Manual) builder.AppendLine("   - Manual review/action required.");
                if (step.Sensitive) builder.AppendLine("   - Sensitive step; do not paste secret values into Git.");
                if (!string.IsNullOrWhiteSpace(step.Command))
                {
                    builder.AppendLine();
                    builder.AppendLine("```bash");
                    builder.AppendLine(step.Command);
                    builder.AppendLine("```");
                }
                builder.AppendLine();
            }
        }

        if (warnings.Count > 0)
        {
            builder.AppendLine("## Warnings");
            builder.AppendLine();
            foreach (var warning in warnings)
                builder.AppendLine($"- {warning}");
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static void WriteBootstrapPlanScript(
        string path,
        IReadOnlyList<PowerForgeServerBootstrapPlanStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/usr/bin/env bash");
        builder.AppendLine("set -Eeuo pipefail");
        builder.AppendLine("umask 022");
        builder.AppendLine();
        builder.AppendLine("# Generated by powerforge-web server bootstrap-plan.");
        builder.AppendLine("# Review before running. Manual/TODO steps intentionally remain comments.");
        builder.AppendLine();

        foreach (var step in steps)
        {
            builder.AppendLine($"# {step.Order}. [{step.Category}] {step.Title}");
            if (string.IsNullOrWhiteSpace(step.Command))
            {
                builder.AppendLine();
                continue;
            }

            if (step.Manual || step.Sensitive || step.Command.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                foreach (var line in step.Command.Split(new[] { '\r', '\n' }, StringSplitOptions.None))
                    builder.AppendLine("# " + line);
                if (step.Manual)
                {
                    builder.AppendLine($"echo {ShellQuote($"Manual bootstrap step required: {step.Title}")} >&2");
                    builder.AppendLine("exit 3");
                }
            }
            else
            {
                builder.AppendLine(step.Command);
            }

            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
