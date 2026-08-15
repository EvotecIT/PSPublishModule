using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private PowerForgeReleaseArtifactEvidence VerifyPortableCli(
        PowerForgeReleaseArtifactVerificationRequest request)
    {
        string projectRoot = RequireDirectory(request.ProjectRoot, nameof(request.ProjectRoot));
        string artifactId = DotNetPublishReleaseArtifactVerifier.RequireText(request.ArtifactId, nameof(request.ArtifactId));
        string target = string.IsNullOrWhiteSpace(request.Target) ? artifactId : request.Target!.Trim();
        string? bundleId = string.IsNullOrWhiteSpace(request.BundleId) ? null : request.BundleId!.Trim();
        if (!string.Equals(target, artifactId, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Portable release artifact ID must match the selected publish target.");
        string checksumsPath = ResolveRequestFile(projectRoot, request.ChecksumsPath, nameof(request.ChecksumsPath));
        string manifestPath = ResolveRequestFile(projectRoot, request.ManifestPath, nameof(request.ManifestPath));
        string configurationPath = ResolveRequestFile(projectRoot, request.ConfigurationPath, nameof(request.ConfigurationPath));
        string expectedRevision = RequireExpectedRevision(request.ExpectedSourceRevision);
        ExpectedPortable expected = ReadExpectedPortable(configurationPath, artifactId, request);

        VerifyChecksummedFile(projectRoot, checksumsPath, manifestPath, "PowerForge manifest");
        using JsonDocument manifest = ReadJson(manifestPath, "PowerForge manifest", MaxManifestBytes);
        if (manifest.RootElement.ValueKind != JsonValueKind.Array)
            throw Invalid("PowerForge manifest must contain a JSON array.");

        JsonElement[] targetEntries = manifest.RootElement.EnumerateArray()
            .Where(entry => Is(entry, "Category", bundleId is null ? "Publish" : "Bundle") &&
                            Is(entry, "Target", target) &&
                            (bundleId is null || Is(entry, "BundleId", bundleId)))
            .ToArray();
        JsonElement[] entries = targetEntries;
        entries = FilterEntries(entries, "Runtime", request.Runtime);
        entries = FilterEntries(entries, "Framework", request.Framework);
        entries = FilterEntries(entries, "Style", request.Style);
        if (entries.Length != 1)
            throw Invalid(
                $"PowerForge manifest selectors must identify exactly one '{target}' portable " +
                $"{(bundleId is null ? "publish entry" : $"bundle '{bundleId}' entry")}; " +
                "specify RID, framework, and style for matrix builds.");

        JsonElement entry = entries[0];
        string manifestKind = ReadString(entry, "Kind");
        if (!string.IsNullOrWhiteSpace(manifestKind) &&
            !string.Equals(manifestKind, DotNetPublishTargetKind.Cli.ToString(), StringComparison.OrdinalIgnoreCase))
            throw Invalid($"PowerForge manifest target kind '{manifestKind}' is not a CLI release target.");
        int signedFileCount = ReadInt32(entry, "SignedFiles");
        if (signedFileCount < 1)
            throw Invalid("PowerForge manifest does not attest that the portable output was signed.");
        if (!TryGet(entry, "SourceDirty", out JsonElement sourceDirty) || sourceDirty.ValueKind != JsonValueKind.False)
            throw Invalid("PowerForge manifest must come from a clean source checkout.");

        string sourceRevision = DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
            ReadString(entry, "SourceRevision"),
            "source revision");
        ValidateRevision(sourceRevision, expectedRevision);
        ValidatePortableDimensions(entry, expected);

        string manifestArchive = ReadString(entry, "ZipPath");
        string manifestExecutable = ReadString(entry, "ExePath");
        string artifactSelection;
        if (string.IsNullOrWhiteSpace(request.ArtifactPath))
        {
            string selectedManifestPath = !string.IsNullOrWhiteSpace(manifestArchive)
                ? manifestArchive
                : manifestExecutable;
            artifactSelection = ResolvePortableManifestArtifactPath(
                projectRoot,
                checksumsPath,
                selectedManifestPath,
                expected.AllowOutsideProjectRoot,
                string.IsNullOrWhiteSpace(manifestArchive) ? entry : null);
        }
        else
        {
            artifactSelection = request.ArtifactPath!;
        }
        string artifactPath = string.IsNullOrWhiteSpace(request.ArtifactPath)
            ? ResolveManifestPath(projectRoot, artifactSelection, expected.AllowOutsideProjectRoot)
            : ResolveRequestFile(projectRoot, artifactSelection, nameof(request.ArtifactPath));
        bool artifactIsArchive = IsZipArchive(artifactPath);
        if (artifactIsArchive != expected.Zip)
        {
            throw Invalid(
                $"Requested portable artifact packaging does not match the configured " +
                $"{(expected.Bundle is null ? "publish target" : "bundle")} ZIP policy.");
        }
        if (artifactIsArchive)
        {
            if (!string.Equals(Path.GetFileName(manifestArchive), Path.GetFileName(artifactPath), StringComparison.OrdinalIgnoreCase))
                throw Invalid("Requested portable archive does not match the selected manifest entry.");
        }
        else if (!DirectArtifactNameMatchesManifestEntry(
                     entry,
                     manifestExecutable,
                     artifactPath,
                     bundleId,
                     targetEntries.Length > 1))
        {
            throw Invalid("A direct portable artifact must have the release-asset identity of the manifest executable.");
        }
        string artifactDigest = VerifyChecksummedFile(projectRoot, checksumsPath, artifactPath, "portable artifact");
        VerifiedSignature[] signatures;
        string signedProductVersion;
        string executableIdentity;
        string? inventoryVersion = null;
        PowerForgeReleaseEvidenceFile[] directInventoryEvidence = Array.Empty<PowerForgeReleaseEvidenceFile>();
        if (artifactIsArchive)
        {
            PortableArchiveVerification archive = VerifyPortableArchiveInventory(
                artifactPath,
                expected.SignerThumbprint,
                expected.SignerSubjectName,
                expected.Sign.Provider == DotNetPublishSigningProvider.AzureArtifactSigning);
            if (!string.Equals(archive.Inventory.ArtifactId, artifactId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(archive.Inventory.Target, target, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(archive.Inventory.BundleId, bundleId, StringComparison.OrdinalIgnoreCase) ||
                (bundleId is not null && archive.Inventory.SchemaVersion < 3))
                throw Invalid("Publisher-signed portable payload identity does not match the requested artifact target.");
            if (!string.Equals(archive.Inventory.Runtime, ReadString(entry, "Runtime"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(archive.Inventory.Framework, ReadString(entry, "Framework"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(archive.Inventory.Style, ReadString(entry, "Style"), StringComparison.OrdinalIgnoreCase))
                throw Invalid("Publisher-signed portable payload dimensions do not match the selected manifest entry.");
            ValidateRevision(
                DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
                    archive.Inventory.SourceRevision,
                    "portable payload inventory source revision"),
                expectedRevision);
            if (archive.Inventory.SignedFilePaths.Length != signedFileCount)
                throw Invalid("PowerForge manifest signed-file count does not match the publisher-signed payload inventory.");
            ValidatePortableConfigurationPolicy(archive.Inventory, expected);
            ValidateConfiguredPortableSignatureCoverage(archive.Inventory, expected.Sign);
            string manifestExecutableForValidation = (request.SignaturePaths ?? Array.Empty<string>()).Any() && bundleId is null
                ? ResolvePortableManifestArtifactPath(
                    projectRoot,
                    checksumsPath,
                    manifestExecutable,
                    expected.AllowOutsideProjectRoot,
                    entry)
                : manifestExecutable;
            ValidateRequestedPortableSignaturePaths(
                request.SignaturePaths,
                projectRoot,
                manifestExecutableForValidation,
                expected.AllowOutsideProjectRoot,
                archive.Inventory.SignedFilePaths);
            signatures = archive.Signatures;
            signedProductVersion = archive.SignedProductVersion;
            executableIdentity = archive.ExecutableIdentity;
            inventoryVersion = archive.Inventory.Version;
            if (!string.Equals(archive.Inventory.ExecutableIdentity, executableIdentity, StringComparison.OrdinalIgnoreCase))
                throw Invalid("Signed executable identity does not match the publisher-signed payload inventory.");
        }
        else
        {
            ValidateRequestedDirectPortableSignaturePaths(
                request.SignaturePaths,
                projectRoot,
                artifactPath,
                expected.AllowOutsideProjectRoot);
            VerifiedSignature directSigner = VerifySignature(
                artifactPath,
                expected.SignerThumbprint,
                expected.SignerSubjectName);
            signatures = new[] { directSigner };
            signedProductVersion = _readPortableVersion(artifactPath);
            executableIdentity = _readPortableIdentity(artifactPath);
            PortableDirectVerification direct = VerifyPortableDirectInventory(
                projectRoot,
                checksumsPath,
                artifactPath,
                artifactDigest,
                directSigner);
            PowerForgePortablePayloadInventory inventory = direct.Inventory;
            directInventoryEvidence = direct.Evidence;
            if (!string.Equals(inventory.ArtifactId, artifactId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(inventory.Target, target, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(inventory.BundleId, bundleId, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("Publisher-signed direct portable identity does not match the requested artifact target.");
            }
            if (!string.Equals(inventory.Runtime, ReadString(entry, "Runtime"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(inventory.Framework, ReadString(entry, "Framework"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(inventory.Style, ReadString(entry, "Style"), StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid("Publisher-signed direct portable dimensions do not match the selected manifest entry.");
            }
            ValidateRevision(
                DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
                    inventory.SourceRevision,
                    "direct portable inventory source revision"),
                expectedRevision);
            if (!string.Equals(inventory.ExecutableIdentity, executableIdentity, StringComparison.OrdinalIgnoreCase))
                throw Invalid("Signed executable identity does not match the publisher-signed direct portable inventory.");
            ValidatePortableConfigurationPolicy(inventory, expected);
            inventoryVersion = inventory.Version;
        }

        if (expected.ExecutableIdentities.Length > 0 &&
            !DotNetPublishPipelineRunner.PortableExecutableIdentityMatches(
                executableIdentity,
                expected.ExecutableIdentities))
        {
            throw Invalid(
                "Signed executable product or assembly identity does not match the configured publish project identity.");
        }
        VerifiedSignature signer = artifactIsArchive
            && expected.Sign.Provider == DotNetPublishSigningProvider.AzureArtifactSigning
                ? RequireOnePublisherSubject(signatures)
                : RequireOneSigner(signatures);
        string aggregateSignerThumbprint = signatures
            .Select(signature => signature.Thumbprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count() == 1
                ? signer.Thumbprint
                : string.Empty;
        ValidatePortableSourceBinding(signedProductVersion, expectedRevision);
        string version = NormalizePortableVersion(signedProductVersion);
        ValidateExpectedPortableVersion(request.ExpectedVersion, version);
        if (inventoryVersion is not null)
        {
            if (!string.Equals(NormalizePortableVersion(inventoryVersion), version, StringComparison.OrdinalIgnoreCase))
                throw Invalid("Publisher-signed payload inventory version does not match the signed executable.");
        }
        PowerForgeReleaseEvidenceFile[] evidence = BuildExternalEvidence(
            projectRoot,
            checksumsPath,
            manifestPath,
            expected.ConfigurationPaths,
            request.SbomPaths,
            artifactId,
            version,
            artifactDigest,
            signatures)
            .Concat(directInventoryEvidence)
            .ToArray();

        return new PowerForgeReleaseArtifactEvidence
        {
            ArtifactKind = request.Kind,
            ArtifactId = artifactId,
            ArtifactPath = artifactPath,
            FileName = Path.GetFileName(artifactPath),
            Sha256 = artifactDigest,
            Version = version,
            SourceRevision = sourceRevision.ToLowerInvariant(),
            SignerSubject = signer.Subject,
            SignerThumbprint = aggregateSignerThumbprint,
            SignatureStatus = "valid",
            SignaturePaths = signatures.Select(signature => signature.DisplayPath).ToArray(),
            Signatures = signatures.Select(signature => new PowerForgeReleaseSignatureEvidence
            {
                Path = signature.DisplayPath,
                Subject = signature.Subject,
                Thumbprint = signature.Thumbprint,
                Ownership = "publisher"
            }).ToArray(),
            EvidenceFiles = evidence
        };
    }

    private static bool DirectArtifactNameMatchesManifestEntry(
        JsonElement entry,
        string manifestExecutable,
        string artifactPath,
        string? bundleId,
        bool requireMatrixIdentity)
    {
        string requestedName = Path.GetFileName(artifactPath);
        string originalName = Path.GetFileName(
            manifestExecutable.Replace('/', Path.DirectorySeparatorChar));
        string matrixName = DotNetPublishReleaseAssetNaming.CreateDirectMatrixAssetName(
            ReadString(entry, "Target"),
            ReadString(entry, "Framework"),
            ReadString(entry, "Runtime"),
            ReadString(entry, "Style"),
            bundleId is null ? DotNetPublishArtefactCategory.Publish : DotNetPublishArtefactCategory.Bundle,
            bundleId,
            manifestExecutable);
        if (string.Equals(matrixName, requestedName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.Equals(originalName, requestedName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!requireMatrixIdentity)
            return true;

        return PortablePathEndsWith(
            artifactPath,
            BuildPortableManifestExecutableRecoverySuffix(entry, manifestExecutable));
    }

    private static string ResolvePortableManifestArtifactPath(
        string projectRoot,
        string checksumsPath,
        string manifestValue,
        bool allowOutsideProjectRoot,
        JsonElement? directEntry = null)
    {
        string normalized = DotNetPublishReleaseArtifactVerifier.RequireText(
                manifestValue,
                "manifest artifact path")
            .Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(normalized))
        {
            string currentPath = ResolveManifestPath(projectRoot, normalized, allowOutsideProjectRoot);
            if (File.Exists(currentPath))
                return currentPath;
        }
        else
        {
            if (allowOutsideProjectRoot && File.Exists(normalized))
                return ResolveManifestPath(projectRoot, normalized, allowOutsideProjectRoot: true);

            try
            {
                string currentPath = ResolveManifestPath(projectRoot, normalized, allowOutsideProjectRoot: false);
                if (File.Exists(currentPath))
                    return currentPath;
            }
            catch (InvalidDataException)
            {
                // A rooted path from a different build checkout is recovered from the checksum catalog below.
            }
        }

        string fileName = Path.GetFileName(normalized);
        string? matrixAssetName = directEntry.HasValue
            ? DotNetPublishReleaseAssetNaming.CreateDirectMatrixAssetName(
                ReadString(directEntry.Value, "Target"),
                ReadString(directEntry.Value, "Framework"),
                ReadString(directEntry.Value, "Runtime"),
                ReadString(directEntry.Value, "Style"),
                DotNetPublishArtefactCategory.Publish,
                bundleId: null,
                manifestValue)
            : null;
        string? requiredRecoverySuffix = directEntry.HasValue
            ? TryBuildPortableManifestExecutableRecoverySuffix(directEntry.Value, manifestValue)
            : null;
        string[] candidateNames = new[] { fileName, matrixAssetName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => name!)
            .ToArray();
        string[] candidates = candidateNames
            .SelectMany(name => DotNetPublishReleaseArtifactVerifier.FindChecksumPathsByFileName(checksumsPath, name))
            .Select(path => ResolveManifestPath(projectRoot, path, allowOutsideProjectRoot))
            .Where(File.Exists)
            .Where(path => directEntry is null ||
                           string.Equals(Path.GetFileName(path), matrixAssetName, StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrWhiteSpace(requiredRecoverySuffix) &&
                            PortablePathEndsWith(path, requiredRecoverySuffix!)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length != 1)
        {
            string identityRequirement = string.IsNullOrWhiteSpace(requiredRecoverySuffix)
                ? string.Empty
                : " that preserves the selected runtime, framework, and style path identity";
            throw Invalid(
                $"Relocated PowerForge manifest artifact '{fileName}' must resolve to exactly one checksummed file{identityRequirement} in the current repository.");
        }

        return candidates[0];
    }

    private static string BuildPortableManifestExecutableRecoverySuffix(JsonElement entry, string manifestExecutable)
    {
        return TryBuildPortableManifestExecutableRecoverySuffix(entry, manifestExecutable)
               ?? throw Invalid(
                   "A relocated direct portable executable must preserve its runtime, framework, and style path identity.");
    }

    private static string? TryBuildPortableManifestExecutableRecoverySuffix(JsonElement entry, string manifestExecutable)
    {
        string normalized = DotNetPublishReleaseArtifactVerifier.RequireText(
                manifestExecutable,
                "manifest executable path")
            .Replace('\\', '/');
        string[] segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string[] dimensions =
        {
            ReadString(entry, "Runtime"),
            ReadString(entry, "Framework"),
            ReadString(entry, "Style")
        };
        int[] dimensionIndexes = dimensions
            .Select(dimension => Array.FindLastIndex(
                segments,
                segment => string.Equals(segment, dimension, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (dimensionIndexes.Any(index => index < 0))
            return null;

        return string.Join("/", segments.Skip(dimensionIndexes.Min()));
    }

    private static bool PortablePathEndsWith(string path, string suffix)
    {
        string normalizedPath = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
        string normalizedSuffix = suffix.Replace('\\', '/').Trim('/');
        return normalizedPath.EndsWith("/" + normalizedSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static ExpectedPortable ReadExpectedPortable(
        string configurationPath,
        string artifactId,
        PowerForgeReleaseArtifactVerificationRequest request)
    {
        string? trustedSignerThumbprint = string.IsNullOrWhiteSpace(request.SignThumbprint)
            ? null
            : DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(request.SignThumbprint);
        string? trustedSignerSubject = trustedSignerThumbprint is not null || string.IsNullOrWhiteSpace(request.SignSubjectName)
            ? null
            : DotNetPublishReleaseArtifactVerifier.RequireCompleteCertificateSubject(
                request.SignSubjectName!,
                nameof(request.SignSubjectName));
        if (trustedSignerThumbprint is null && trustedSignerSubject is null)
        {
            throw Invalid(
                "Portable release verification requires an out-of-band publisher thumbprint or exact subject name; " +
                "release configuration cannot establish publisher trust.");
        }

        DotNetPublishConfiguredSpec configured =
            DotNetPublishReleaseArtifactVerifier.ReadConfiguredPublishSpecWithInputs(configurationPath);
        DotNetPublishSpec configuration = configured.Configuration;
        if (!string.IsNullOrWhiteSpace(request.Profile))
            configuration.Profile = request.Profile!.Trim();
        configuration = DotNetPublishPipelineRunner.ResolveProfile(configuration);
        string targetName = string.IsNullOrWhiteSpace(request.Target) ? artifactId : request.Target!.Trim();
        DotNetPublishTarget[] targets = (configuration.Targets ?? Array.Empty<DotNetPublishTarget>())
            .Where(target => string.Equals(target.Name, targetName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (targets.Length != 1 || targets[0].Publish is null)
            throw Invalid($"PowerForge configuration must define exactly one publish target '{targetName}'.");

        DotNetPublishTarget target = targets[0];
        if (target.Kind != DotNetPublishTargetKind.Unknown && target.Kind != DotNetPublishTargetKind.Cli)
            throw Invalid($"PowerForge configuration target kind '{target.Kind}' is not a CLI release target.");
        DotNetPublishBundle? bundle = null;
        if (!string.IsNullOrWhiteSpace(request.BundleId))
        {
            DotNetPublishBundle[] bundles = (configuration.Bundles ?? Array.Empty<DotNetPublishBundle>())
                .Where(candidate =>
                    string.Equals(candidate.Id, request.BundleId!.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.PrepareFromTarget, targetName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (bundles.Length != 1)
            {
                throw Invalid(
                    $"PowerForge configuration must define exactly one bundle '{request.BundleId!.Trim()}' " +
                    $"prepared from publish target '{targetName}'.");
            }
            bundle = bundles[0];
        }
        DotNetPublishSignOptions? sign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
            configuration.SigningProfiles,
            string.IsNullOrWhiteSpace(request.SignProfile) ? target.Publish.SignProfile : request.SignProfile,
            string.IsNullOrWhiteSpace(request.SignProfile) ? target.Publish.Sign : null,
            target.Publish.SignOverrides,
            $"Target '{targetName}'");
        if (!string.IsNullOrWhiteSpace(request.SignThumbprint) || !string.IsNullOrWhiteSpace(request.SignSubjectName))
        {
            sign = DotNetPublishSigningProfileResolver.CloneSignOptions(sign) ?? new DotNetPublishSignOptions();
            sign.Enabled = true;
            if (!string.IsNullOrWhiteSpace(request.SignThumbprint))
                sign.Thumbprint = request.SignThumbprint!.Trim();
            if (!string.IsNullOrWhiteSpace(request.SignSubjectName))
            {
                sign.SubjectName = request.SignSubjectName!.Trim();
                if (string.IsNullOrWhiteSpace(request.SignThumbprint))
                    sign.Thumbprint = null;
            }
        }
        if (request.EnableSigning.HasValue)
        {
            sign = DotNetPublishSigningProfileResolver.CloneSignOptions(sign) ?? new DotNetPublishSignOptions();
            sign.Enabled = request.EnableSigning.Value;
        }
        if (sign is null || !sign.Enabled)
            throw Invalid("PowerForge portable signing must be enabled for a release artifact.");
        string configurationPolicySha256 =
            DotNetPublishPipelineRunner.ComputePortableConfigurationPolicySha256(
                target.Name,
                target.Kind,
                bundle?.Id,
                bundle?.Zip ?? target.Publish.Zip,
                sign);

        string[] executableIdentities = ResolveConfiguredPortableExecutableIdentities(
            configuration,
            target,
            configured.InputPaths);
        return new ExpectedPortable(
            configuration,
            target,
            bundle,
            configured.InputPaths,
            executableIdentities,
            sign,
            trustedSignerThumbprint,
            trustedSignerSubject,
            configurationPolicySha256,
            configuration.DotNet.AllowOutputOutsideProjectRoot);
    }

    private static string[] ResolveConfiguredPortableExecutableIdentities(
        DotNetPublishSpec configuration,
        DotNetPublishTarget target,
        IReadOnlyList<string> configurationPaths)
    {
        string? configuredIdentity = target.Publish?.ExecutableIdentity;
        string? projectPath = target.ProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath) && !string.IsNullOrWhiteSpace(target.ProjectId))
        {
            projectPath = (configuration.Projects ?? Array.Empty<DotNetPublishProject>())
                .Where(project => string.Equals(project.Id, target.ProjectId, StringComparison.OrdinalIgnoreCase))
                .Select(project => project.Path)
                .SingleOrDefault();
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            if (!string.IsNullOrWhiteSpace(configuredIdentity))
                return new[] { configuredIdentity!.Trim() };
            throw Invalid(
                "Portable release verification requires ProjectPath, ProjectId, or Publish.ExecutableIdentity " +
                "to bind the signed executable to its configured project identity.");
        }

        string configurationPath = configurationPaths.Last();
        string configurationDirectory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))
            ?? Directory.GetCurrentDirectory();
        string projectRoot = string.IsNullOrWhiteSpace(configuration.DotNet.ProjectRoot)
            ? configurationDirectory
            : Path.GetFullPath(Path.IsPathRooted(configuration.DotNet.ProjectRoot)
                ? configuration.DotNet.ProjectRoot!
                : Path.Combine(configurationDirectory, configuration.DotNet.ProjectRoot!));
        string resolvedProjectPath = Path.GetFullPath(Path.IsPathRooted(projectPath)
            ? projectPath!
            : Path.Combine(projectRoot, projectPath!));
        if (!File.Exists(resolvedProjectPath) && string.IsNullOrWhiteSpace(configuredIdentity))
            return Array.Empty<string>();

        return DotNetPublishPipelineRunner.ResolvePortableExecutableIdentities(
            resolvedProjectPath,
            configuredIdentity);
    }

    private static void ValidatePortableDimensions(JsonElement entry, ExpectedPortable expected)
    {
        DotNetPublishTarget target = expected.Target;
        DotNetPublishPublishOptions publish = target.Publish!;
        DotNetPublishSpec configuration = expected.Configuration;
        string framework = ReadString(entry, "Framework");
        string runtime = ReadString(entry, "Runtime");
        string style = ReadString(entry, "Style");
        string[] frameworks = NormalizeConfiguredStrings(publish.Frameworks);
        if (frameworks.Length == 0 && !string.IsNullOrWhiteSpace(publish.Framework))
            frameworks = new[] { publish.Framework.Trim() };
        if (frameworks.Length == 0)
            frameworks = NormalizeConfiguredStrings(configuration.Matrix?.Frameworks);

        string[] runtimes = NormalizeConfiguredStrings(publish.Runtimes);
        if (runtimes.Length == 0)
            runtimes = NormalizeConfiguredStrings(configuration.Matrix?.Runtimes);
        if (runtimes.Length == 0)
            runtimes = NormalizeConfiguredStrings(configuration.DotNet.Runtimes);

        DotNetPublishStyle[] styles = (publish.Styles ?? Array.Empty<DotNetPublishStyle>()).Distinct().ToArray();
        if (styles.Length == 0)
            styles = (configuration.Matrix?.Styles ?? Array.Empty<DotNetPublishStyle>()).Distinct().ToArray();
        if (styles.Length == 0)
            styles = new[] { publish.Style };
        if ((frameworks.Length > 0 && !frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase)) ||
            (runtimes.Length > 0 && !runtimes.Contains(runtime, StringComparer.OrdinalIgnoreCase)) ||
            !styles.Any(value => string.Equals(value.ToString(), style, StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid("PowerForge manifest portable dimensions do not match the configured publish target.");
        }

        DotNetPublishMatrixRule[] include = configuration.Matrix?.Include ?? Array.Empty<DotNetPublishMatrixRule>();
        DotNetPublishMatrixRule[] exclude = configuration.Matrix?.Exclude ?? Array.Empty<DotNetPublishMatrixRule>();
        if ((include.Length > 0 && !include.Any(rule => RuleMatches(target.Name, runtime, framework, style, rule))) ||
            exclude.Any(rule => RuleMatches(target.Name, runtime, framework, style, rule)))
            throw Invalid("PowerForge manifest portable dimensions are excluded by the configured publish matrix.");

        DotNetPublishBundle? bundle = expected.Bundle;
        if (bundle is null)
            return;
        string[] bundleFrameworks = NormalizeConfiguredStrings(bundle.Frameworks);
        string[] bundleRuntimes = NormalizeConfiguredStrings(bundle.Runtimes);
        DotNetPublishStyle[] bundleStyles = (bundle.Styles ?? Array.Empty<DotNetPublishStyle>())
            .Distinct()
            .ToArray();
        if ((bundleFrameworks.Length > 0 && !bundleFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase)) ||
            (bundleRuntimes.Length > 0 && !bundleRuntimes.Contains(runtime, StringComparer.OrdinalIgnoreCase)) ||
            (bundleStyles.Length > 0 && !bundleStyles.Any(value =>
                string.Equals(value.ToString(), style, StringComparison.OrdinalIgnoreCase))))
        {
            throw Invalid("PowerForge manifest portable dimensions do not match the configured bundle selectors.");
        }
    }

    private static void ValidateConfiguredPortableSignatureCoverage(
        PowerForgePortablePayloadInventory inventory,
        DotNetPublishSignOptions sign)
    {
        var signedPaths = new HashSet<string>(
            (inventory.SignedFilePaths ?? Array.Empty<string>()).Select(NormalizeArchivePath),
            StringComparer.Ordinal);
        string[] unsignedPortableBinaries = (inventory.Entries ?? Array.Empty<PowerForgePortablePayloadEntry>())
            .Select(entry => NormalizeArchivePath(entry.Path))
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                           (sign.IncludeDlls && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            .Where(path => !signedPaths.Contains(path))
            .ToArray();
        if (unsignedPortableBinaries.Length > 0)
        {
            throw Invalid(
                "The publisher-signed payload inventory does not include every required executable" +
                (sign.IncludeDlls ? " or DLL" : string.Empty) +
                " entry in its signed-file selection.");
        }
    }

    private static void ValidatePortableConfigurationPolicy(
        PowerForgePortablePayloadInventory inventory,
        ExpectedPortable expected)
    {
        if (!string.Equals(
                inventory.ConfigurationPolicySha256,
                expected.ConfigurationPolicySha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(
                "Publisher-signed portable payload configuration policy does not match the supplied release configuration.");
        }
    }

    private static string[] EnumeratePortableSigningFiles(string outputDirectory, DotNetPublishSignOptions sign)
    {
        var paths = new List<string>();
        paths.AddRange(Directory.EnumerateFiles(outputDirectory, "*.exe", SearchOption.AllDirectories));
        if (sign.IncludeDlls)
            paths.AddRange(Directory.EnumerateFiles(outputDirectory, "*.dll", SearchOption.AllDirectories));
        return paths.Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolvePortableSignaturePaths(
        string projectRoot,
        IEnumerable<string>? values,
        bool allowOutsideProjectRoot,
        string label)
    {
        string[] configured = (values ?? Array.Empty<string>()).ToArray();
        if (configured.Any(string.IsNullOrWhiteSpace))
            throw Invalid($"Portable {label}s cannot contain empty values.");
        string[] paths = configured
            .Select(path => ResolveManifestPath(projectRoot, path, allowOutsideProjectRoot))
            .ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw Invalid($"Portable {label}s must be unique.");
        return paths;
    }

    private static bool SamePhysicalPathSet(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right) =>
        left.Count == right.Count && left.All(path => right.Any(candidate => PathsEqual(path, candidate)));

    private static void ValidateRequestedPortableSignaturePaths(
        IEnumerable<string>? requestedValues,
        string projectRoot,
        string manifestExecutable,
        bool allowOutsideProjectRoot,
        IReadOnlyCollection<string> inventoryValues)
    {
        string[] requested = (requestedValues ?? Array.Empty<string>()).ToArray();
        if (requested.Length == 0)
            return;

        string executablePath = ResolveManifestPath(projectRoot, manifestExecutable, allowOutsideProjectRoot);
        string outputDirectory = Path.GetDirectoryName(executablePath)
            ?? throw Invalid("PowerForge manifest executable directory could not be resolved.");
        string[] requestedRelative = ResolvePortableSignaturePaths(
                projectRoot,
                requested,
                allowOutsideProjectRoot,
                "signature path")
            .Select(path =>
            {
                EnsurePathWithinDirectory(outputDirectory, path, "Portable signature path");
                return DotNetPublishReleaseArtifactVerifier.GetRelativePath(outputDirectory, path).Replace('\\', '/');
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] inventoryRelative = inventoryValues
            .Select(NormalizeArchivePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedRelative.Length != inventoryRelative.Length ||
            !requestedRelative.SequenceEqual(inventoryRelative, StringComparer.OrdinalIgnoreCase))
        {
            throw Invalid(
                "Requested portable signature paths do not match the publisher-signed payload inventory.");
        }
    }

    private static void ValidateRequestedDirectPortableSignaturePaths(
        IEnumerable<string>? requestedValues,
        string projectRoot,
        string artifactPath,
        bool allowOutsideProjectRoot)
    {
        string[] requested = (requestedValues ?? Array.Empty<string>()).ToArray();
        if (requested.Length == 0)
            return;

        string[] paths = ResolvePortableSignaturePaths(
            projectRoot,
            requested,
            allowOutsideProjectRoot,
            "signature path");
        if (paths.Length != 1 || !PathsEqual(paths[0], artifactPath))
        {
            throw Invalid(
                "Requested portable signature paths must identify exactly the verified direct executable.");
        }
    }

    private static string[] NormalizeConfiguredStrings(IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool RuleMatches(
        string target,
        string runtime,
        string framework,
        string style,
        DotNetPublishMatrixRule? rule)
    {
        if (rule is null)
            return false;
        string[] targets = NormalizeConfiguredStrings(rule.Targets);
        return (targets.Length == 0 || targets.Any(pattern => DotNetPublishPipelineRunner.WildcardMatch(target, pattern))) &&
               (string.IsNullOrWhiteSpace(rule.Runtime) || DotNetPublishPipelineRunner.WildcardMatch(runtime, rule.Runtime!.Trim())) &&
               (string.IsNullOrWhiteSpace(rule.Framework) || DotNetPublishPipelineRunner.WildcardMatch(framework, rule.Framework!.Trim())) &&
               (string.IsNullOrWhiteSpace(rule.Style) || DotNetPublishPipelineRunner.WildcardMatch(style, rule.Style!.Trim()));
    }
}
