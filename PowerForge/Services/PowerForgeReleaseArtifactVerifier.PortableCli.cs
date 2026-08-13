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
        if (!string.Equals(target, artifactId, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Portable release artifact ID must match the selected publish target.");
        string checksumsPath = ResolveRequestFile(projectRoot, request.ChecksumsPath, nameof(request.ChecksumsPath));
        string manifestPath = ResolveRequestFile(projectRoot, request.ManifestPath, nameof(request.ManifestPath));
        string configurationPath = ResolveRequestFile(projectRoot, request.ConfigurationPath, nameof(request.ConfigurationPath));
        string expectedRevision = RequireExpectedRevision(request.ExpectedSourceRevision);
        ExpectedPortable expected = ReadExpectedPortable(configurationPath, artifactId, request);

        VerifyChecksummedFile(projectRoot, checksumsPath, manifestPath, "PowerForge manifest");
        using JsonDocument manifest = ReadJson(manifestPath, "PowerForge manifest");
        if (manifest.RootElement.ValueKind != JsonValueKind.Array)
            throw Invalid("PowerForge manifest must contain a JSON array.");

        JsonElement[] entries = manifest.RootElement.EnumerateArray()
            .Where(entry => Is(entry, "Category", "Publish") && Is(entry, "Target", target))
            .ToArray();
        entries = FilterEntries(entries, "Runtime", request.Runtime);
        entries = FilterEntries(entries, "Framework", request.Framework);
        entries = FilterEntries(entries, "Style", request.Style);
        if (entries.Length != 1)
            throw Invalid(
                $"PowerForge manifest selectors must identify exactly one '{target}' portable publish entry; " +
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

        string outputDirectory = ResolveManifestPath(projectRoot, ReadString(entry, "OutputDir"), expected.AllowOutsideProjectRoot);
        string manifestArchive = ReadString(entry, "ZipPath");
        string manifestExecutable = ReadString(entry, "ExePath");
        string artifactPath = ResolveRequestFile(projectRoot, request.ArtifactPath, nameof(request.ArtifactPath));
        RequireManifestPathMatch(projectRoot, artifactPath, manifestArchive, manifestExecutable, expected.AllowOutsideProjectRoot);
        string artifactDigest = VerifyChecksummedFile(projectRoot, checksumsPath, artifactPath, "portable artifact");
        string executablePath = ResolveManifestPath(projectRoot, manifestExecutable, expected.AllowOutsideProjectRoot);

        string[] expectedSignaturePaths = EnumeratePortableSigningFiles(outputDirectory, expected.Sign);
        if (expectedSignaturePaths.Length != signedFileCount)
            throw Invalid("PowerForge manifest signed-file count does not match the configured portable signing selection.");
        string[] manifestSignaturePaths = ResolvePortableSignaturePaths(
            projectRoot,
            ReadStringArray(entry, "SignedFilePaths"),
            expected.AllowOutsideProjectRoot,
            "manifest signed-file path");
        if (manifestSignaturePaths.Length > 0 && !SamePhysicalPathSet(manifestSignaturePaths, expectedSignaturePaths))
            throw Invalid("PowerForge manifest signed-file paths do not match the configured portable signing selection.");
        string[] requestedSignaturePaths = ResolvePortableSignaturePaths(
            projectRoot,
            request.SignaturePaths,
            expected.AllowOutsideProjectRoot,
            "requested signature path");
        string[] signaturePaths = manifestSignaturePaths.Length > 0
            ? manifestSignaturePaths
            : expectedSignaturePaths;
        if (requestedSignaturePaths.Length > 0 && !SamePhysicalPathSet(requestedSignaturePaths, signaturePaths))
            throw Invalid("Requested portable signature paths do not match the complete trusted signing selection.");

        bool artifactIsArchive = IsZipArchive(artifactPath);
        if (!artifactIsArchive &&
            (signaturePaths.Length != 1 || !PathsEqual(signaturePaths[0], executablePath)))
            throw Invalid("A direct portable executable artifact must be the only file in the trusted signing selection; use the ZIP artifact for multi-file outputs.");
        if (artifactIsArchive)
            VerifyPortableArchiveInventory(projectRoot, checksumsPath, artifactPath, outputDirectory);

        var signatures = new List<VerifiedSignature>();
        foreach (string signaturePath in signaturePaths)
        {
            EnsurePathWithinDirectory(outputDirectory, signaturePath, "Portable signature path");
            VerifyChecksummedFile(projectRoot, checksumsPath, signaturePath, "portable signed file");
            signatures.Add(VerifySignature(signaturePath, expected.SignerThumbprint, expected.SignerSubjectName));
        }
        if (!signatures.Any(signature => PathsEqual(signature.PhysicalPath, executablePath)))
            throw Invalid("Portable release evidence must include the manifest executable signature.");

        VerifiedSignature signer = RequireOneSigner(signatures);
        string signedProductVersion = _readPortableVersion(executablePath);
        ValidatePortableSourceBinding(signedProductVersion, expectedRevision);
        string version = NormalizePortableVersion(signedProductVersion);
        ValidateExpectedPortableVersion(request.ExpectedVersion, version);
        PowerForgeReleaseEvidenceFile[] evidence = BuildExternalEvidence(
            projectRoot,
            checksumsPath,
            manifestPath,
            expected.ConfigurationPaths,
            request.SbomPaths,
            artifactId,
            version,
            artifactDigest);

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
            SignerThumbprint = signer.Thumbprint,
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

    private static ExpectedPortable ReadExpectedPortable(
        string configurationPath,
        string artifactId,
        PowerForgeReleaseArtifactVerificationRequest request)
    {
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

        string? signerThumbprint = string.IsNullOrWhiteSpace(sign.Thumbprint)
            ? null
            : DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(sign.Thumbprint);
        string? signerSubject = signerThumbprint is not null || string.IsNullOrWhiteSpace(sign.SubjectName)
            ? null
            : sign.SubjectName!.Trim();
        if (signerThumbprint is null && signerSubject is null)
        {
            throw Invalid(
                "Portable release verification requires a configured or requested publisher thumbprint or exact subject name.");
        }
        return new ExpectedPortable(
            configuration,
            target,
            configured.InputPaths,
            sign,
            signerThumbprint,
            signerSubject,
            configuration.DotNet.AllowOutputOutsideProjectRoot);
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
