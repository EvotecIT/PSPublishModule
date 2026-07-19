namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static string[] ValidateServerRecoveryManifest(PowerForgeServerRecoveryManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        if (manifest.SchemaVersion != 2)
            errors.Add("schemaVersion must be 2; regenerate or migrate the recovery manifest before using this engine revision.");
        if (string.IsNullOrWhiteSpace(manifest.Target?.SshAlias) && string.IsNullOrWhiteSpace(manifest.Target?.Host))
            errors.Add("target must declare sshAlias or host.");

        ValidateHostAndPackages(manifest, errors);
        ValidateOperationLocks(manifest, errors);
        ValidateSystemdActivation(manifest.Systemd, errors);
        ValidateNamedCommandText(manifest.Capture?.Commands, "capture.commands", errors);
        ValidateNamedCommandText(manifest.Deploy?.Commands, "deploy.commands", errors);
        ValidateNamedCommandText(manifest.Verify?.Commands, "verify.commands", errors);

        var plainFiles = manifest.Capture?.PlainFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        var encryptedFiles = manifest.Capture?.EncryptedFiles ?? Array.Empty<PowerForgeServerManagedFile>();
        var plainPaths = ValidateCaptureEntries(plainFiles, "capture.plainFiles", sensitive: false, errors);
        var encryptedPaths = ValidateCaptureEntries(encryptedFiles, "capture.encryptedFiles", sensitive: true, errors);
        ValidateLetsEncryptCapturePaths(plainPaths, encryptedPaths, errors);
        var retention = manifest.BackupTarget?.Retention;
        if (retention?.KeepDays is not null)
            errors.Add("backupTarget.retention.keepDays is not implemented; use keepLatestInTree for current-tree retention.");
        if (retention?.KeepLatestInTree is not null && retention.KeepLatest is not null)
            errors.Add("backupTarget.retention must not set both keepLatestInTree and its deprecated keepLatest alias.");
        ValidateBackupTarget(manifest.BackupTarget, plainFiles, encryptedFiles, errors);
        ValidateRepositories(manifest.Repositories, manifest.Capture?.Commands, errors);

        var managedPathIds = new HashSet<string>(StringComparer.Ordinal);
        var managedPaths = new HashSet<string>(StringComparer.Ordinal);
        var managedTargets = new List<(string Id, string Path, string Kind)>();
        var sourceManagedPaths = new List<(string Id, string Source, string Target)>();
        var repositoryRoots = (manifest.Repositories ?? Array.Empty<PowerForgeServerRepository>())
            .Select(static repository => repository.Path?.TrimEnd('/'))
            .Where(static path => !string.IsNullOrWhiteSpace(path) &&
                                  path.StartsWith("/", StringComparison.Ordinal) &&
                                  path.IndexOfAny(['*', '?', '[']) < 0)
            .Cast<string>()
            .ToArray();
        foreach (var path in manifest.Paths ?? Array.Empty<PowerForgeServerPath>())
        {
            if (string.IsNullOrWhiteSpace(path.Id) || !managedPathIds.Add(path.Id))
                errors.Add($"Managed path id '{path.Id}' is missing or duplicated.");
            var normalizedPath = NormalizeCapturePath(path.Path, $"paths[{path.Id}].path", errors);
            if (normalizedPath is not null)
            {
                if (!string.Equals(normalizedPath, "/", StringComparison.Ordinal) && HasTrailingPathSeparator(path.Path))
                    errors.Add($"Managed path '{path.Id}' target must not end with '/'.");
                if (normalizedPath.IndexOfAny(['*', '?', '[']) >= 0)
                    errors.Add($"Managed path '{path.Id}' must use an exact path.");
                if (!managedPaths.Add(normalizedPath))
                    errors.Add($"Managed path '{normalizedPath}' is duplicated.");
                managedTargets.Add((path.Id ?? normalizedPath, normalizedPath, path.Kind ?? string.Empty));
            }
            if (!IsValidUnixIdentity(path.Owner))
                errors.Add($"Managed path '{path.Id}' has an invalid owner.");
            if (!IsValidUnixIdentity(path.Group))
                errors.Add($"Managed path '{path.Id}' has an invalid group.");
            if (!IsValidMode(path.Mode))
                errors.Add($"Managed path '{path.Id}' has an invalid mode.");
            if (!string.IsNullOrWhiteSpace(path.Kind) &&
                !string.Equals(path.Kind, "directory", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(path.Kind, "file", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(path.Kind, "symlink", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Managed path '{path.Id}' has unsupported kind '{path.Kind}'.");

            if (!string.IsNullOrWhiteSpace(path.Source))
            {
                var normalizedSource = NormalizeCapturePath(path.Source, $"paths[{path.Id}].source", errors);
                if (normalizedSource is not null)
                {
                    if (HasTrailingPathSeparator(path.Source))
                        errors.Add($"Managed path '{path.Id}' source must not end with '/'.");
                    if (normalizedSource.IndexOfAny(['*', '?', '[']) >= 0)
                        errors.Add($"Managed path '{path.Id}' source must use an exact path.");
                    if (!string.Equals(path.Kind, "file", StringComparison.OrdinalIgnoreCase))
                        errors.Add($"Source-managed path '{path.Id}' must use kind 'file'.");
                    if (string.IsNullOrWhiteSpace(path.Owner) || string.IsNullOrWhiteSpace(path.Group) || string.IsNullOrWhiteSpace(path.Mode))
                        errors.Add($"Source-managed path '{path.Id}' must declare owner, group, and mode.");
                    if (normalizedPath is not null && string.Equals(normalizedSource, normalizedPath, StringComparison.Ordinal))
                        errors.Add($"Source-managed path '{path.Id}' source and target must differ.");
                    if (!repositoryRoots.Any(root => PathStrictlyContains(root, normalizedSource)))
                        errors.Add($"Source-managed path '{path.Id}' source must be inside a declared repository path.");
                    if (normalizedPath is not null && repositoryRoots.Any(root =>
                            PathContains(root, normalizedPath) || PathContains(normalizedPath, root)))
                        errors.Add($"Source-managed path '{path.Id}' target must not overlap declared repository paths.");
                    if (normalizedPath is not null)
                        sourceManagedPaths.Add((path.Id ?? normalizedPath, normalizedSource, normalizedPath));
                }
            }

            if (!string.IsNullOrWhiteSpace(path.Validation) &&
                !string.Equals(path.Validation, "sudoers", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Managed path '{path.Id}' has unsupported validation '{path.Validation}'.");
            if (string.Equals(normalizedPath, "/etc/sudoers", StringComparison.Ordinal))
                errors.Add($"Managed path '{path.Id}' must not replace /etc/sudoers.");
            else if (IsSudoersPolicyTarget(normalizedPath) &&
                     !string.Equals(path.Validation, "sudoers", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Managed path '{path.Id}' below /etc/sudoers.d must declare validation 'sudoers'.");
            if (string.Equals(path.Validation, "sudoers", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(path.Source))
                    errors.Add($"Sudoers-managed path '{path.Id}' must declare a source.");
                if (!string.Equals(path.Kind, "file", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(path.Owner, "root", StringComparison.Ordinal) ||
                    !string.Equals(path.Group, "root", StringComparison.Ordinal) ||
                    path.Mode is not ("440" or "0440") ||
                    !IsSafeSudoersTarget(normalizedPath))
                    errors.Add($"Sudoers-managed path '{path.Id}' must be a root-owned 0440 file with a dot-free name directly below /etc/sudoers.d.");
            }
        }

        ValidateApacheManagedFiles(
            manifest.Apache?.Sites,
            "apache.sites",
            "/etc/apache2/sites-available/",
            repositoryRoots,
            managedPaths,
            managedTargets,
            sourceManagedPaths,
            errors);
        ValidateApacheManagedFiles(
            manifest.Apache?.Conf,
            "apache.conf",
            "/etc/apache2/conf-available/",
            repositoryRoots,
            managedPaths,
            managedTargets,
            sourceManagedPaths,
            errors);
        ValidateRepositoryManagedFiles(
            (manifest.Systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>())
                .Concat(manifest.Systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
                .Select(static unit => new PowerForgeServerManagedFile { Source = unit.Source, Target = unit.Target }),
            "systemd.units",
            repositoryRoots,
            managedPaths,
            managedTargets,
            sourceManagedPaths,
            errors);
        ValidateManagedTargetHierarchy(managedTargets, errors);

        foreach (var plainPath in plainPaths)
        {
            foreach (var encryptedPath in encryptedPaths.Where(encryptedPath => CapturePathsMayOverlap(plainPath, encryptedPath)))
                errors.Add($"Plain capture path '{plainPath}' overlaps encrypted capture path '{encryptedPath}'.");
        }

        var secretIds = new HashSet<string>(StringComparer.Ordinal);
        var secretPaths = new List<(string Id, string Path)>();
        foreach (var secret in manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
        {
            if (string.IsNullOrWhiteSpace(secret.Id) || !secretIds.Add(secret.Id))
                errors.Add($"Secret id '{secret.Id}' is missing or duplicated.");
            if (!string.Equals(secret.Capture, "exclude", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(secret.Capture, "encrypted", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Secret '{secret.Id}' has unsupported capture mode '{secret.Capture}'.");
            if (!string.IsNullOrWhiteSpace(secret.RestoreMode) &&
                !string.Equals(secret.RestoreMode, "file", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(secret.RestoreMode, "directory", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Secret '{secret.Id}' has unsupported restore mode '{secret.RestoreMode}'.");
            if (!IsValidUnixIdentity(secret.Owner))
                errors.Add($"Secret '{secret.Id}' has an invalid restore owner.");
            if (!IsValidUnixIdentity(secret.Group))
                errors.Add($"Secret '{secret.Id}' has an invalid restore group.");
            if (!IsValidMode(secret.Mode))
                errors.Add($"Secret '{secret.Id}' has an invalid restore mode.");

            string? secretPath = null;
            if (!string.IsNullOrWhiteSpace(secret.Path))
                secretPath = NormalizeCapturePath(secret.Path, $"secrets[{secret.Id}].path", errors);
            if (secretPath is not null)
                secretPaths.Add((secret.Id, secretPath));

            var repositoryRoot = secretPath is null
                ? null
                : repositoryRoots
                    .Where(root => PathContains(root, secretPath))
                    .OrderByDescending(static root => root.Length)
                    .FirstOrDefault();
            if (repositoryRoot is not null)
            {
                if (string.Equals(repositoryRoot, secretPath, StringComparison.Ordinal))
                    errors.Add($"Secret '{secret.Id}' must not replace declared repository root '{repositoryRoot}'.");
                else if (!secret.RestoreAfterRepositories)
                    errors.Add($"Secret '{secret.Id}' at '{secretPath}' is inside repository '{repositoryRoot}' and must set restoreAfterRepositories=true.");
                if (!string.Equals(secret.Capture, "encrypted", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Repository-overlapping secret '{secret.Id}' must use capture 'encrypted'.");
                if (!string.Equals(secret.RestoreMode, "file", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Repository-overlapping secret '{secret.Id}' must use restoreMode 'file'.");
                if (string.IsNullOrWhiteSpace(secret.Owner) || string.IsNullOrWhiteSpace(secret.Group) || string.IsNullOrWhiteSpace(secret.Mode))
                    errors.Add($"Repository-overlapping secret '{secret.Id}' must declare owner, group, and mode for deterministic post-clone installation.");
            }
            else if (secret.RestoreAfterRepositories)
            {
                errors.Add($"Secret '{secret.Id}' sets restoreAfterRepositories=true but is not inside a declared repository path.");
            }

            if (string.Equals(secret.Capture, "exclude", StringComparison.OrdinalIgnoreCase))
            {
                if (secretPath is not null && plainPaths.Any(path => CapturePathsMayOverlap(secretPath, path)))
                    errors.Add($"Excluded secret '{secret.Id}' at '{secretPath}' overlaps capture.plainFiles and could be published without encryption.");
                if (secretPath is not null && encryptedPaths.Any(path => CapturePathsMayOverlap(secretPath, path)))
                    errors.Add($"Excluded secret '{secret.Id}' at '{secretPath}' overlaps capture.encryptedFiles and would not be excluded.");
                continue;
            }

            if (!string.Equals(secret.Capture, "encrypted", StringComparison.OrdinalIgnoreCase))
                continue;

            if (secretPath is null)
            {
                errors.Add($"Encrypted secret '{secret.Id}' must declare a path covered by capture.encryptedFiles.");
                continue;
            }

            if (!encryptedPaths.Any(path => PathContains(path, secretPath)))
                errors.Add($"Encrypted secret '{secret.Id}' at '{secretPath}' is not covered by capture.encryptedFiles.");
            if (secret.RestoreAfterRepositories && !encryptedPaths.Contains(secretPath, StringComparer.Ordinal))
                errors.Add($"Deferred repository secret '{secret.Id}' at '{secretPath}' must have one exact capture.encryptedFiles entry.");
            if (plainPaths.Any(path => CapturePathsMayOverlap(secretPath, path)))
                errors.Add($"Encrypted secret '{secret.Id}' at '{secretPath}' overlaps capture.plainFiles.");
        }

        var deferredSecretPaths = new HashSet<string>(
            (manifest.Secrets ?? Array.Empty<PowerForgeServerSecret>())
                .Where(static secret => secret.RestoreAfterRepositories &&
                                        string.Equals(secret.Capture, "encrypted", StringComparison.OrdinalIgnoreCase) &&
                                        !string.IsNullOrWhiteSpace(secret.Path))
                .Select(static secret => secret.Path!.TrimEnd('/')),
            StringComparer.Ordinal);
        foreach (var repositoryRoot in repositoryRoots)
        {
            foreach (var plainPath in plainPaths.Where(path => CapturePathsMayOverlap(repositoryRoot, path)))
                errors.Add($"Plain capture path '{plainPath}' overlaps repository '{repositoryRoot}'; repository content must be restored from its pinned source.");
            foreach (var encryptedPath in encryptedPaths.Where(path =>
                         CapturePathsMayOverlap(repositoryRoot, path) && !deferredSecretPaths.Contains(path)))
            {
                errors.Add($"Encrypted capture path '{encryptedPath}' overlaps repository '{repositoryRoot}' without an exact deferred secret contract.");
            }
        }

        for (var leftIndex = 0; leftIndex < encryptedPaths.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < encryptedPaths.Length; rightIndex++)
            {
                if (CapturePathsMayOverlap(encryptedPaths[leftIndex], encryptedPaths[rightIndex]))
                    errors.Add($"Encrypted capture paths '{encryptedPaths[leftIndex]}' and '{encryptedPaths[rightIndex]}' overlap.");
            }
        }

        foreach (var managed in sourceManagedPaths)
        {
            foreach (var encryptedPath in encryptedPaths.Where(path => CapturePathsMayOverlap(managed.Source, path)))
                errors.Add($"Source-managed path '{managed.Id}' source '{managed.Source}' overlaps encrypted capture path '{encryptedPath}'.");
            foreach (var encryptedPath in encryptedPaths.Where(path => CapturePathsMayOverlap(managed.Target, path)))
                errors.Add($"Source-managed path '{managed.Id}' target '{managed.Target}' overlaps encrypted capture path '{encryptedPath}'.");
            foreach (var secret in secretPaths.Where(secret => CapturePathsMayOverlap(managed.Source, secret.Path)))
                errors.Add($"Source-managed path '{managed.Id}' source '{managed.Source}' overlaps secret '{secret.Id}' at '{secret.Path}'.");
            foreach (var secret in secretPaths.Where(secret => CapturePathsMayOverlap(managed.Target, secret.Path)))
                errors.Add($"Source-managed path '{managed.Id}' target '{managed.Target}' overlaps secret '{secret.Id}' at '{secret.Path}'.");
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void ValidateRepositoryManagedFiles(
        IEnumerable<PowerForgeServerManagedFile> files,
        string section,
        IReadOnlyCollection<string> repositoryRoots,
        ISet<string> managedPaths,
        ICollection<(string Id, string Path, string Kind)> managedTargets,
        ICollection<(string Id, string Source, string Target)> sourceManagedPaths,
        ICollection<string> errors)
    {
        var index = 0;
        foreach (var file in files)
        {
            var id = $"{section}[{index}]";
            if (string.IsNullOrWhiteSpace(file.Source))
            {
                if (!string.IsNullOrWhiteSpace(file.Target))
                {
                    var observedTarget = NormalizeCapturePath(file.Target, $"{id}.target", errors);
                    if (observedTarget is not null && HasTrailingPathSeparator(file.Target))
                        errors.Add($"{id}.target must not end with '/'.");
                    if (observedTarget is not null && observedTarget.IndexOfAny(['*', '?', '[']) >= 0)
                        errors.Add($"{id}.target must use an exact path.");
                    if (IsSudoersPolicyTarget(observedTarget))
                        errors.Add($"{id}.target must not manage /etc/sudoers or files below /etc/sudoers.d.");
                    if (observedTarget is not null && !managedPaths.Add(observedTarget))
                        errors.Add($"Managed path '{observedTarget}' is duplicated.");
                    if (observedTarget is not null)
                        managedTargets.Add((id, observedTarget, "file"));
                }
                index++;
                continue;
            }
            var source = NormalizeCapturePath(file.Source, $"{id}.source", errors);
            var target = NormalizeCapturePath(file.Target, $"{id}.target", errors);
            if (source is not null && HasTrailingPathSeparator(file.Source))
                errors.Add($"{id}.source must not end with '/'.");
            if (target is not null && HasTrailingPathSeparator(file.Target))
                errors.Add($"{id}.target must not end with '/'.");
            if (source is not null && source.IndexOfAny(['*', '?', '[']) >= 0)
                errors.Add($"{id}.source must use an exact path.");
            if (target is not null && target.IndexOfAny(['*', '?', '[']) >= 0)
                errors.Add($"{id}.target must use an exact path.");
            if (IsSudoersPolicyTarget(target))
                errors.Add($"{id}.target must not manage /etc/sudoers or files below /etc/sudoers.d.");
            if (source is not null && target is not null)
            {
                if (string.Equals(source, target, StringComparison.Ordinal))
                    errors.Add($"{id} source and target must differ.");
                if (!repositoryRoots.Any(root => PathStrictlyContains(root, source)))
                    errors.Add($"{id}.source must be inside a declared repository path.");
                if (repositoryRoots.Any(root => PathContains(root, target) || PathContains(target, root)))
                    errors.Add($"{id}.target must not overlap declared repository paths.");
                if (!managedPaths.Add(target))
                    errors.Add($"Managed path '{target}' is duplicated.");
                managedTargets.Add((id, target, "file"));
                sourceManagedPaths.Add((id, source, target));
            }
            index++;
        }
    }

    private static void ValidateApacheManagedFiles(
        IEnumerable<PowerForgeServerApacheFile>? files,
        string section,
        string targetRoot,
        IReadOnlyCollection<string> repositoryRoots,
        ISet<string> managedPaths,
        ICollection<(string Id, string Path, string Kind)> managedTargets,
        ICollection<(string Id, string Source, string Target)> sourceManagedPaths,
        ICollection<string> errors)
    {
        var entries = (files ?? Array.Empty<PowerForgeServerApacheFile>()).ToArray();
        for (var index = 0; index < entries.Length; index++)
        {
            var target = entries[index].Target;
            if (string.IsNullOrWhiteSpace(target) ||
                !target.StartsWith(targetRoot, StringComparison.Ordinal) ||
                !target.EndsWith(".conf", StringComparison.Ordinal) ||
                target[targetRoot.Length..].Contains('/', StringComparison.Ordinal) ||
                System.Text.Encoding.UTF8.GetByteCount(Path.GetFileName(target)) > 255)
            {
                errors.Add($"{section}[{index}].target must be an exact .conf file with a filename of at most 255 bytes directly below '{targetRoot.TrimEnd('/')}'.");
            }
        }

        ValidateRepositoryManagedFiles(
            entries.Select(static file => new PowerForgeServerManagedFile
            {
                Source = file.Source,
                Target = file.Target,
                Required = file.Required
            }),
            section,
            repositoryRoots,
            managedPaths,
            managedTargets,
            sourceManagedPaths,
            errors);
    }

    private static void ValidateManagedTargetHierarchy(
        IReadOnlyCollection<(string Id, string Path, string Kind)> managedTargets,
        ICollection<string> errors)
    {
        foreach (var file in managedTargets.Where(static target =>
                     string.Equals(target.Kind, "file", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var descendant in managedTargets.Where(target => PathStrictlyContains(file.Path, target.Path)))
                errors.Add($"Managed file target '{file.Path}' must not contain managed target '{descendant.Path}'.");
        }
    }

    private static void ValidateHostAndPackages(
        PowerForgeServerRecoveryManifest manifest,
        ICollection<string> errors)
    {
        if (!IsSupportedUbuntuTarget(manifest.Target?.Os))
            errors.Add("target.os must be 'ubuntu' or an Ubuntu release such as 'ubuntu-24.04'.");

        var architecture = NormalizeLinuxArchitecture(manifest.Target?.Architecture);
        if (architecture is not null and not ("x86_64" or "aarch64"))
            errors.Add("target.architecture must be x64 or arm64.");

        var packages = manifest.Packages;
        var aptIndex = 0;
        foreach (var package in packages?.Apt ?? Array.Empty<string>())
        {
            if (!IsSafeAptPackageName(package))
                errors.Add($"packages.apt[{aptIndex}] must be a safe Debian package name.");
            aptIndex++;
        }

        var sdkIndex = 0;
        foreach (var version in packages?.DotnetSdks ?? Array.Empty<string>())
        {
            if (!TryNormalizeDotnetSdkVersion(version, out _))
                errors.Add($"packages.dotnetSdks[{sdkIndex}] must be a supported Ubuntu 24.04 LTS SDK band: 8/8.0 or 10/10.0.");
            sdkIndex++;
        }
        if (packages?.DotnetSdks?.Length > 0 &&
            !string.Equals(manifest.Target?.Os, "ubuntu-24.04", StringComparison.OrdinalIgnoreCase))
            errors.Add("packages.dotnetSdks currently requires target.os ubuntu-24.04.");

        var moduleIndex = 0;
        foreach (var module in (packages?.ApacheModules ?? Array.Empty<string>())
                 .Concat(manifest.Apache?.Modules ?? Array.Empty<string>()))
        {
            if (!IsSafeApacheModuleName(module))
                errors.Add($"Apache module at index {moduleIndex} contains unsupported characters.");
            moduleIndex++;
        }

        if (packages?.Powershell == true)
        {
            if (!string.Equals(manifest.Target?.Os, "ubuntu-24.04", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("packages.powershell currently requires target.os ubuntu-24.04.");
            }
            if (architecture != "x86_64")
                errors.Add("packages.powershell currently requires target.architecture x64.");
        }
    }

    private static bool IsSupportedUbuntuTarget(string? os)
    {
        if (string.IsNullOrWhiteSpace(os) || string.Equals(os, "ubuntu", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!os.StartsWith("ubuntu-", StringComparison.OrdinalIgnoreCase))
            return false;

        var version = os["ubuntu-".Length..];
        var parts = version.Split('.');
        return parts.Length == 2 &&
               parts.All(static part => part.Length == 2 && part.All(static character => character is >= '0' and <= '9'));
    }

    private static void ValidateRepositories(
        IEnumerable<PowerForgeServerRepository>? repositories,
        IEnumerable<PowerForgeServerNamedCommand>? captureCommands,
        ICollection<string> errors)
    {
        var captureCommandsById = (captureCommands ?? Array.Empty<PowerForgeServerNamedCommand>())
            .Where(static command => !string.IsNullOrWhiteSpace(command.Id))
            .GroupBy(static command => command.Id!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var repositoryPaths = new List<(int Index, string Path)>();
        var index = 0;
        foreach (var repository in repositories ?? Array.Empty<PowerForgeServerRepository>())
        {
            var field = $"repositories[{index}]";
            var repositoryPath = NormalizeCapturePath(repository.Path, $"{field}.path", errors);
            if (repositoryPath is not null && HasTrailingPathSeparator(repository.Path))
                errors.Add($"{field}.path must not end with '/'.");
            if (repositoryPath is not null)
                repositoryPaths.Add((index, repositoryPath));

            var prerequisites = new HashSet<string>(StringComparer.Ordinal);
            var prerequisiteIndex = 0;
            foreach (var path in repository.BootstrapRequiredFiles ?? Array.Empty<string>())
            {
                var normalized = NormalizeCapturePath(path, $"{field}.bootstrapRequiredFiles[{prerequisiteIndex}]", errors);
                if (normalized is not null && !prerequisites.Add(normalized))
                    errors.Add($"{field}.bootstrapRequiredFiles[{prerequisiteIndex}] duplicates prerequisite path '{normalized}'.");
                prerequisiteIndex++;
            }

            var hasIdentity = !string.IsNullOrWhiteSpace(repository.SshIdentityFile);
            var hasKnownHosts = !string.IsNullOrWhiteSpace(repository.SshKnownHostsFile);
            if (hasIdentity != hasKnownHosts)
            {
                errors.Add($"{field} must declare both sshIdentityFile and sshKnownHostsFile.");
                index++;
                continue;
            }

            if (hasIdentity)
            {
                var identity = NormalizeCapturePath(repository.SshIdentityFile, $"{field}.sshIdentityFile", errors);
                var knownHosts = NormalizeCapturePath(repository.SshKnownHostsFile, $"{field}.sshKnownHostsFile", errors);
                if (identity is not null && !prerequisites.Contains(identity))
                    errors.Add($"{field}.bootstrapRequiredFiles must include sshIdentityFile '{identity}'.");
                if (knownHosts is not null && !prerequisites.Contains(knownHosts))
                    errors.Add($"{field}.bootstrapRequiredFiles must include sshKnownHostsFile '{knownHosts}'.");
            }

            var refCaptureCommandIds = new List<(string Field, string Id)>();
            if (!string.IsNullOrWhiteSpace(repository.RefCaptureCommandId))
            {
                refCaptureCommandIds.Add(($"{field}.refCaptureCommandId", repository.RefCaptureCommandId));
            }

            if (repository.RefCaptureCommandIds is not null)
            {
                if (repository.RefCaptureCommandIds.Length is < 2 or > 16)
                    errors.Add($"{field}.refCaptureCommandIds must contain between 2 and 16 command identifiers.");
                if (!string.IsNullOrWhiteSpace(repository.RefCaptureCommandId))
                    errors.Add($"{field} must not declare both refCaptureCommandId and refCaptureCommandIds.");

                var seenRefCaptureCommandIds = new HashSet<string>(StringComparer.Ordinal);
                for (var commandIndex = 0; commandIndex < repository.RefCaptureCommandIds.Length; commandIndex++)
                {
                    var commandId = repository.RefCaptureCommandIds[commandIndex];
                    var commandField = $"{field}.refCaptureCommandIds[{commandIndex}]";
                    if (string.IsNullOrWhiteSpace(commandId) || !IsSafeIdentifier(commandId))
                    {
                        errors.Add($"{commandField} contains unsupported characters.");
                        continue;
                    }
                    if (!seenRefCaptureCommandIds.Add(commandId))
                    {
                        errors.Add($"{commandField} duplicates capture command '{commandId}'.");
                        continue;
                    }
                    refCaptureCommandIds.Add((commandField, commandId));
                }
            }

            foreach (var (commandField, commandId) in refCaptureCommandIds)
            {
                if (!IsSafeIdentifier(commandId))
                {
                    errors.Add($"{commandField} contains unsupported characters.");
                }
                else if (!captureCommandsById.TryGetValue(commandId, out var commands) || commands.Length != 1)
                {
                    errors.Add($"{commandField} must identify exactly one capture command.");
                }
                else if (!commands[0].Required || commands[0].Sensitive)
                {
                    errors.Add($"{commandField} must identify a required, non-sensitive capture command.");
                }
            }

            index++;
        }

        for (var leftIndex = 0; leftIndex < repositoryPaths.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < repositoryPaths.Count; rightIndex++)
            {
                var left = repositoryPaths[leftIndex];
                var right = repositoryPaths[rightIndex];
                if (PathContains(left.Path, right.Path) || PathContains(right.Path, left.Path))
                    errors.Add($"repositories[{left.Index}].path and repositories[{right.Index}].path must not be duplicate or nested.");
            }
        }
    }

    private static bool IsSafeIdentifier(string value)
        => value.Length <= 64 &&
           (IsAsciiLetterOrDigit(value[0]) || value[0] == '_') &&
           value.All(static character => IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static void ValidateBackupTarget(
        PowerForgeServerBackupTarget? backupTarget,
        IReadOnlyCollection<PowerForgeServerManagedFile> plainFiles,
        IReadOnlyCollection<PowerForgeServerManagedFile> encryptedFiles,
        ICollection<string> errors)
    {
        if (backupTarget is null)
            return;

        if (!string.Equals(backupTarget.Type, "github", StringComparison.OrdinalIgnoreCase))
            errors.Add("backupTarget.type must be github.");
        if (!IsRepositoryName(backupTarget.Repository))
            errors.Add("backupTarget.repository must be an owner/repository name.");
        if (!IsSafeRepositoryRelativeValue(backupTarget.Branch, allowSlash: true))
            errors.Add("backupTarget.branch contains unsupported characters.");
        if (!IsSafeRepositoryRelativeValue(backupTarget.Path, allowSlash: true))
            errors.Add("backupTarget.path must be a safe repository-relative path.");
        if (!string.Equals(backupTarget.Encryption, "age", StringComparison.OrdinalIgnoreCase))
            errors.Add("backupTarget.encryption must be age.");
        if (string.IsNullOrWhiteSpace(backupTarget.Recipient) && string.IsNullOrWhiteSpace(backupTarget.RecipientEnv))
            errors.Add("backupTarget must declare recipient or recipientEnv.");
        if (!string.IsNullOrWhiteSpace(backupTarget.RecipientEnv) && !IsEnvironmentVariableName(backupTarget.RecipientEnv))
            errors.Add("backupTarget.recipientEnv must be an environment variable name.");

        var retention = backupTarget.Retention;
        var keepLatest = retention?.KeepLatestInTree ?? retention?.KeepLatest;
        if (keepLatest is null)
            errors.Add("backupTarget.retention.keepLatestInTree is required.");
        else if (keepLatest is < 1 or > 365)
            errors.Add("backupTarget.retention.keepLatestInTree must be from 1 through 365.");

        if (plainFiles.Count == 0)
            errors.Add("backup capture requires at least one capture.plainFiles entry.");
        if (encryptedFiles.Count == 0)
            errors.Add("backup capture requires at least one capture.encryptedFiles entry.");
    }

    internal static string[] GetEncryptedRestorePaths(PowerForgeServerRecoveryManifest manifest)
    {
        var errors = new List<string>();
        var paths = ValidateCaptureEntries(
            manifest.Capture?.EncryptedFiles ?? Array.Empty<PowerForgeServerManagedFile>(),
            "capture.encryptedFiles",
            sensitive: true,
            errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("Server recovery manifest validation failed: " + string.Join(" ", errors));
        return paths;
    }

    private static string[] ValidateCaptureEntries(
        IEnumerable<PowerForgeServerManagedFile> entries,
        string section,
        bool sensitive,
        ICollection<string> errors)
    {
        var paths = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var entry in entries)
        {
            var path = NormalizeCapturePath(entry.Target, $"{section}[{index}].target", errors);
            if (path is not null)
            {
                paths.Add(path);
                if (!seenPaths.Add(path))
                    errors.Add($"{section}[{index}] duplicates capture path '{path}'.");
                if (path.IndexOfAny(['*', '?', '[']) >= 0)
                    errors.Add($"{section}[{index}] must use an exact path so capture and restore share one archive allowlist.");
            }
            if (entry.Sensitive is null)
            {
                if (sensitive)
                    errors.Add($"{section}[{index}] must explicitly set sensitive=true.");
            }
            else if (entry.Sensitive.Value != sensitive)
            {
                var expectation = sensitive ? "must" : "must not";
                errors.Add($"{section}[{index}] {expectation} set sensitive=true.");
            }
            index++;
        }
        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? NormalizeCapturePath(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
            return null;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Contains('\\'))
        {
            errors.Add($"{field} must be a canonical Unix path without surrounding whitespace or backslashes.");
            return null;
        }

        var normalized = value.TrimEnd('/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal) || normalized == "/")
        {
            errors.Add($"{field} must be an absolute path below '/'.");
            return null;
        }

        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
        {
            errors.Add($"{field} must not contain '.' or '..' segments.");
            return null;
        }

        if (normalized.Contains("//", StringComparison.Ordinal))
        {
            errors.Add($"{field} must not contain empty path segments.");
            return null;
        }

        if (normalized.Any(static character => !(IsAsciiLetterOrDigit(character) || character is '/' or '.' or '_' or '-' or '*' or '?' or '[' or ']')))
        {
            errors.Add($"{field} contains unsupported characters.");
            return null;
        }

        return normalized;
    }

    private static bool HasTrailingPathSeparator(string? value)
        => !string.IsNullOrEmpty(value) && value.EndsWith("/", StringComparison.Ordinal);

    private static bool CapturePathsMayOverlap(string left, string right)
    {
        var leftPrefix = GetCaptureStaticPrefix(left);
        var rightPrefix = GetCaptureStaticPrefix(right);
        return PathContains(leftPrefix, rightPrefix) || PathContains(rightPrefix, leftPrefix);
    }

    private static string GetCaptureStaticPrefix(string path)
    {
        var wildcardIndex = path.IndexOfAny(['*', '?', '[']);
        if (wildcardIndex < 0)
            return path;

        var slashIndex = path.LastIndexOf('/', wildcardIndex);
        return slashIndex <= 0 ? "/" : path[..slashIndex];
    }

    private static bool PathContains(string parent, string candidate)
        => candidate.Equals(parent, StringComparison.Ordinal) ||
           candidate.StartsWith(parent.EndsWith("/", StringComparison.Ordinal) ? parent : parent + "/", StringComparison.Ordinal);

    private static bool PathStrictlyContains(string parent, string candidate)
        => !candidate.Equals(parent, StringComparison.Ordinal) && PathContains(parent, candidate);

    private static bool IsSafeSudoersTarget(string? path)
    {
        const string prefix = "/etc/sudoers.d/";
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var name = path[prefix.Length..];
        return name.Length > 0 &&
               !name.Contains('/', StringComparison.Ordinal) &&
               name.All(static character => IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    private static bool IsSudoersPolicyTarget(string? path)
        => string.Equals(path, "/etc/sudoers", StringComparison.Ordinal) ||
           string.Equals(path, "/etc/sudoers.d", StringComparison.Ordinal) ||
           (path?.StartsWith("/etc/sudoers.d/", StringComparison.Ordinal) ?? false);

    private static bool IsValidUnixIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (IsNumericUnixIdentity(value))
        {
            return value.Length <= 10 &&
                   (value.Length == 1 || value[0] != '0') &&
                   uint.TryParse(value, out var id) &&
                   id != uint.MaxValue;
        }

        return value.Length <= 32 &&
               (IsAsciiLetterOrDigit(value[0]) || value[0] == '_') &&
               value.All(static character => IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    private static bool IsNumericUnixIdentity(string value)
        => value.Length > 0 && value.All(static character => character is >= '0' and <= '9');

    private static bool IsRootUnixIdentity(string value)
        => string.Equals(value, "root", StringComparison.Ordinal) ||
           string.Equals(value, "0", StringComparison.Ordinal);

    private static bool IsValidMode(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           (value.Length is 3 or 4 && value.All(static character => character is >= '0' and <= '7'));

    private static bool IsAsciiLetterOrDigit(char value)
        => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool IsRepositoryName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(static part =>
            part.Length > 0 && part.All(static character => IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-'));
    }

    private static bool IsSafeRepositoryRelativeValue(string? value, bool allowSlash)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.StartsWith("/", StringComparison.Ordinal) &&
           !value.Contains("..", StringComparison.Ordinal) &&
           value.All(character => IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-' || (allowSlash && character == '/'));

    private static bool IsEnvironmentVariableName(string value)
        => value.Length > 0 &&
           (IsAsciiLetterOrDigit(value[0]) || value[0] == '_') &&
           !char.IsDigit(value[0]) &&
           value.All(static character => IsAsciiLetterOrDigit(character) || character == '_');
}
