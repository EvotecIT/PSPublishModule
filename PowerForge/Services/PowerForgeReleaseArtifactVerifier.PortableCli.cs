using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private PowerForgeReleaseArtifactEvidence VerifyPortableCli(
        PowerForgeReleaseArtifactVerificationRequest request)
    {
        string projectRoot = RequireDirectory(request.ProjectRoot, nameof(request.ProjectRoot));
        string artifactId = DotNetPublishReleaseArtifactVerifier.RequireText(request.ArtifactId, nameof(request.ArtifactId));
        string checksumsPath = ResolveRequestFile(projectRoot, request.ChecksumsPath, nameof(request.ChecksumsPath));
        string manifestPath = ResolveRequestFile(projectRoot, request.ManifestPath, nameof(request.ManifestPath));
        string configurationPath = ResolveRequestFile(projectRoot, request.ConfigurationPath, nameof(request.ConfigurationPath));
        string expectedRevision = RequireExpectedRevision(request.ExpectedSourceRevision);
        ExpectedPortable expected = ReadExpectedPortable(configurationPath, artifactId, request);

        VerifyChecksummedFile(projectRoot, checksumsPath, manifestPath, "PowerForge manifest");
        using JsonDocument manifest = ReadJson(manifestPath, "PowerForge manifest");
        if (manifest.RootElement.ValueKind != JsonValueKind.Array)
            throw Invalid("PowerForge manifest must contain a JSON array.");

        string target = string.IsNullOrWhiteSpace(request.Target) ? artifactId : request.Target!.Trim();
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
        if (ReadInt32(entry, "SignedFiles") < 1)
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

        string[] configuredSignaturePaths = request.SignaturePaths ?? Array.Empty<string>();
        string[] signaturePaths = configuredSignaturePaths.Length == 0
            ? new[] { manifestExecutable }
            : configuredSignaturePaths;
        if (signaturePaths.Any(string.IsNullOrWhiteSpace))
            throw Invalid("Portable signature paths cannot contain empty values.");

        var signatures = new List<VerifiedSignature>();
        foreach (string configuredPath in signaturePaths)
        {
            string signaturePath = ResolveManifestPath(projectRoot, configuredPath, expected.AllowOutsideProjectRoot);
            EnsurePathWithinDirectory(outputDirectory, signaturePath, "Portable signature path");
            string signatureDigest = VerifyChecksummedFile(projectRoot, checksumsPath, signaturePath, "portable signed file");
            if (IsZipArchive(artifactPath))
                VerifyArchiveContainsFile(artifactPath, outputDirectory, signaturePath, signatureDigest);
            signatures.Add(VerifySignature(signaturePath, expected.SignerThumbprint, expected.SignerSubjectName));
        }
        if (!signatures.Any(signature => PathsEqual(signature.PhysicalPath, executablePath)))
            throw Invalid("Portable release evidence must include the manifest executable signature.");

        VerifiedSignature signer = RequireOneSigner(signatures);
        string version = NormalizeVersion(_readPortableVersion(executablePath));
        ValidateExpectedVersion(request.ExpectedVersion, version);
        PowerForgeReleaseEvidenceFile[] evidence = BuildExternalEvidence(
            projectRoot,
            checksumsPath,
            manifestPath,
            expected.ConfigurationPaths,
            request.SbomPaths);

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
        return new ExpectedPortable(
            configuration,
            target,
            configured.InputPaths,
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
