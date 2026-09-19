using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static void ApplySharedReleaseVersion(
        DotNetPublishPlan plan,
        string? sharedReleaseVersion,
        string? sourceCommit,
        string? releaseConfigPath)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));
        var verifiedSourceCommit = VerifySharedReleaseSourceCommit(plan.ProjectRoot, sourceCommit, releaseConfigPath);
        ValidateNativeInstallerReleaseVersions(plan, sharedReleaseVersion);
        if (string.IsNullOrWhiteSpace(sharedReleaseVersion))
            return;

        foreach (var entry in BuildSharedReleaseVersionProperties(sharedReleaseVersion!, verifiedSourceCommit))
            plan.MsBuildProperties[entry.Key] = entry.Value;
    }

    internal static void ValidateNativeInstallerReleaseVersions(DotNetPublishPlan plan, string? sharedReleaseVersion)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        if (!string.IsNullOrWhiteSpace(sharedReleaseVersion))
        {
            string releaseVersion = sharedReleaseVersion!;
            foreach (KeyValuePair<string, DotNetPublishMsiVersionPlan> resolvedVersion in plan.MsiVersions ?? new Dictionary<string, DotNetPublishMsiVersionPlan>())
            {
                string[] keyParts = resolvedVersion.Key.Split('|');
                if (keyParts.Length != 5 ||
                    string.IsNullOrWhiteSpace(keyParts[0]) ||
                    string.IsNullOrWhiteSpace(keyParts[1]) ||
                    string.IsNullOrWhiteSpace(keyParts[4]))
                    throw new InvalidOperationException($"MSI version plan '{resolvedVersion.Key}' has an invalid key.");
                string installerId = keyParts[0];
                var installer = (plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
                    .FirstOrDefault(candidate => string.Equals(candidate.Id, installerId, StringComparison.OrdinalIgnoreCase));
                if (installer is null)
                    throw new InvalidOperationException($"MSI version plan '{resolvedVersion.Key}' has no matching installer.");
                if (installer.Versioning is not { Enabled: true, ApplyToPublish: true })
                    continue;
                if (string.Equals(resolvedVersion.Value.Version?.Trim(), releaseVersion.Trim(), StringComparison.Ordinal))
                    continue;
                throw new InvalidOperationException(
                    $"MSI installer '{resolvedVersion.Key}' version '{resolvedVersion.Value.Version}' does not match release version '{releaseVersion}'.");
            }
        }

        foreach (DotNetPublishInstallerPlan installer in plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
        {
            string? nativeVersion = installer.Kind switch
            {
                DotNetPublishInstallerKind.Debian => installer.Debian?.Version,
                DotNetPublishInstallerKind.MacApp => installer.MacApp?.Version,
                _ => null
            };
            if (nativeVersion is null)
                continue;

            string? effectiveReleaseVersion = sharedReleaseVersion;
            if (string.IsNullOrWhiteSpace(effectiveReleaseVersion))
            {
                DotNetPublishTargetPlan? target = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.Name,
                        installer.PrepareFromTarget,
                        StringComparison.OrdinalIgnoreCase));
                effectiveReleaseVersion = target?.Version;
                if (string.IsNullOrWhiteSpace(effectiveReleaseVersion))
                {
                    throw new InvalidOperationException(
                        $"Native installer '{installer.Id}' cannot be bound to a release version because target '{installer.PrepareFromTarget}' has no resolved version.");
                }
            }

            if (!string.Equals(nativeVersion.Trim(), effectiveReleaseVersion!.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Native installer '{installer.Id}' version '{nativeVersion}' does not match release version '{effectiveReleaseVersion}'.");
            }
        }
    }

    /// <summary>
    /// Binds an exact release commit to a clean checkout while admitting only the public-release wrapper's
    /// validated, non-code authorization config and module provenance inputs.
    /// </summary>
    internal static string? VerifySharedReleaseSourceCommit(
        string projectRoot,
        string? configuredCommit,
        string? releaseConfigPath = null)
    {
        var expectedCommit = configuredCommit?.Trim();
        if (string.IsNullOrWhiteSpace(expectedCommit) ||
            !Regex.IsMatch(expectedCommit!, "^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            throw new InvalidOperationException(
                "An exact GitHub.Commitish requires the DotNet publish project root to be an existing Git checkout.");
        }

        var root = Path.GetFullPath(projectRoot);
        var git = GitClient.CreateTrustedSystemClient(defaultTimeout: TimeSpan.FromMinutes(2));
        var result = git.RunRawAsync(root, ["rev-parse", "HEAD"], TimeSpan.FromMinutes(2))
            .GetAwaiter()
            .GetResult();
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Unable to bind GitHub.Commitish to the DotNet publish checkout. " +
                (string.IsNullOrWhiteSpace(result.StdErr) ? "git rev-parse HEAD failed." : result.StdErr.Trim()));
        }

        var observedCommit = result.StdOut.Trim();
        if (!Regex.IsMatch(observedCommit, "^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("The DotNet publish checkout did not report an exact 40-character Git commit SHA.");
        if (!string.Equals(expectedCommit, observedCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"GitHub.Commitish '{expectedCommit}' does not match the DotNet publish checkout HEAD '{observedCommit}'.");
        }

        var status = git.RunRawAsync(
                root,
                ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
                TimeSpan.FromMinutes(2))
            .GetAwaiter()
            .GetResult();
        if (!status.Succeeded)
        {
            throw new InvalidOperationException(
                "Unable to verify that the DotNet publish checkout is clean. " +
                (string.IsNullOrWhiteSpace(status.StdErr) ? "git status failed." : status.StdErr.Trim()));
        }

        var allowedGeneratedPaths = ResolveAuthorizedPublicReleaseGeneratedPaths(
            root,
            releaseConfigPath,
            expectedCommit!);
        var unexpectedChanges = ParseGitStatusPaths(status.StdOut)
            .Where(entry => !entry.IsUntracked || !allowedGeneratedPaths.Contains(entry.Path))
            .ToArray();
        if (unexpectedChanges.Length > 0)
        {
            throw new InvalidOperationException(
                "An exact GitHub.Commitish requires a clean DotNet publish checkout with no tracked or untracked changes.");
        }

        return observedCommit.ToLowerInvariant();
    }

    private static HashSet<string> ResolveAuthorizedPublicReleaseGeneratedPaths(
        string projectRoot,
        string? releaseConfigPath,
        string expectedCommit)
    {
        var allowed = new HashSet<string>(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(releaseConfigPath))
            return allowed;

        var configPath = Path.GetFullPath(releaseConfigPath);
        var configMatch = AuthorizedConfigurationFileName.Match(Path.GetFileName(configPath));
        if (PathIsWithin(configPath, projectRoot) || !configMatch.Success || !File.Exists(configPath))
        {
            return allowed;
        }

        EnsureOrdinaryFile(configPath, "Public release authorization config");
        if (!string.Equals(configMatch.Groups["commit"].Value, expectedCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The public release authorization config filename does not bind the expected commit.");

        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = config.RootElement;
        var configuredCommit = GetRequiredNestedString(root, "GitHub", "Commitish");
        if (!string.Equals(configuredCommit, expectedCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The public release authorization config does not bind the expected commit.");

        var provenancePath = Path.Combine(projectRoot, "Module", "PowerForge.ReleaseProvenance.json");
        var expectedModuleName = GetRequiredNestedString(root, "Module", "ModuleName");
        var expectedVersion = GetRequiredNestedString(root, "Module", "ModuleVersion");
        var expectedOwner = GetRequiredNestedString(root, "GitHub", "Owner");
        var expectedRepository = GetRequiredNestedString(root, "GitHub", "Repository");
        if (!string.Equals(configMatch.Groups["version"].Value, expectedVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("The public release authorization config filename does not bind the configured module version.");

        if (File.Exists(provenancePath))
        {
            EnsureOrdinaryGeneratedFile(projectRoot, provenancePath, "Public release module provenance");
            using var provenance = JsonDocument.Parse(File.ReadAllText(provenancePath));
            var provenanceRoot = provenance.RootElement;
            RequireExactString(provenanceRoot, "moduleName", expectedModuleName);
            RequireExactString(provenanceRoot, "version", expectedVersion);
            RequireExactString(provenanceRoot, "repository", $"https://github.com/{expectedOwner}/{expectedRepository}");
            RequireExactString(provenanceRoot, "commit", expectedCommit);
            if (!provenanceRoot.TryGetProperty("schemaVersion", out var schemaVersion) ||
                schemaVersion.ValueKind != JsonValueKind.Number ||
                schemaVersion.GetInt32() != 1 ||
                !provenanceRoot.TryGetProperty("sourceDirty", out var sourceDirty) ||
                sourceDirty.ValueKind != JsonValueKind.False)
            {
                throw new InvalidOperationException("The public release module provenance schema or source state is invalid.");
            }
            if (provenanceRoot.EnumerateObject().Count() != 6)
                throw new InvalidOperationException("The public release module provenance contains unexpected properties.");

            allowed.Add(ToGitPath(projectRoot, provenancePath));
        }

        var signedProvenancePath = Path.Combine(
            projectRoot,
            "Module",
            PowerForgeModuleSourceAttestationWriter.FileName);
        if (File.Exists(signedProvenancePath))
        {
            EnsureOrdinaryGeneratedFile(projectRoot, signedProvenancePath, "Signed public release module provenance");
            var signedProvenance = PowerForgeModuleSourceAttestationWriter.Read(File.ReadAllBytes(signedProvenancePath));
            if (!string.Equals(signedProvenance.ModuleName, expectedModuleName, StringComparison.Ordinal) ||
                !string.Equals(signedProvenance.Version, expectedVersion, StringComparison.Ordinal) ||
                !string.Equals(signedProvenance.SourceRevision, expectedCommit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The signed public release module provenance does not match the authorization config.");
            }

            allowed.Add(ToGitPath(projectRoot, signedProvenancePath));
        }

        return allowed;
    }

    private static (bool IsUntracked, string Path)[] ParseGitStatusPaths(string status)
    {
        if (string.IsNullOrEmpty(status))
            return Array.Empty<(bool, string)>();

        var entries = new List<(bool IsUntracked, string Path)>();
        var records = status.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (record.Length < 4)
                throw new InvalidOperationException("Git returned an invalid source status record.");
            var statusCode = record.Substring(0, 2);
            var path = record.Substring(3).Replace('\\', '/');
            entries.Add((string.Equals(statusCode, "??", StringComparison.Ordinal), path));
            if ((statusCode[0] == 'R' || statusCode[0] == 'C' || statusCode[1] == 'R' || statusCode[1] == 'C') &&
                index + 1 < records.Length)
            {
                entries.Add((false, records[++index].Replace('\\', '/')));
            }
        }
        return entries.ToArray();
    }

    private static void EnsureOrdinaryGeneratedFile(string projectRoot, string path, string name)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"{name} was not found: {path}");
        var current = Path.GetFullPath(path);
        var root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (true)
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{name} must not traverse a link or reparse point: {current}");
            if (PathEquals(current, root))
                break;
            current = Path.GetDirectoryName(current)
                      ?? throw new InvalidOperationException($"{name} escaped the release checkout.");
        }
    }

    private static void EnsureOrdinaryFile(string path, string name)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"{name} was not found: {path}");

        var current = Path.GetFullPath(path);
        while (true)
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{name} must not traverse a link or reparse point: {current}");

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || PathEquals(current, parent))
                break;
            current = parent;
        }
    }

    private static string GetRequiredNestedString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var nested) ||
            nested.ValueKind != JsonValueKind.Object ||
            !nested.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"The public release authorization config requires {objectName}.{propertyName}.");
        }
        return value.GetString()!.Trim();
    }

    private static void RequireExactString(JsonElement root, string propertyName, string expected)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The public release module provenance {propertyName} does not match the authorization config.");
        }
    }

    private static string ToGitPath(string root, string path)
        => GetRelativePathCompat(root, path).Replace('\\', '/');

    private static bool PathEquals(string? left, string right)
        => !string.IsNullOrWhiteSpace(left) &&
           string.Equals(
               Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
               Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
               RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                   ? StringComparison.OrdinalIgnoreCase
                   : StringComparison.Ordinal);
}
