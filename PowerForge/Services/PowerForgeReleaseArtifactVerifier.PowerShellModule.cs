using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private PowerForgeReleaseArtifactEvidence VerifyPowerShellModule(
        PowerForgeReleaseArtifactVerificationRequest request)
    {
        string projectRoot = RequireDirectory(request.ProjectRoot, nameof(request.ProjectRoot));
        string moduleName = DotNetPublishReleaseArtifactVerifier.RequireText(request.ArtifactId, nameof(request.ArtifactId));
        string? selectedTarget = request.Target?.Trim();
        if (!string.IsNullOrWhiteSpace(selectedTarget) &&
            !string.Equals(selectedTarget, moduleName, StringComparison.OrdinalIgnoreCase))
            throw Invalid("PowerShell module artifact ID must match the selected target when one is provided.");
        string checksumsPath = ResolveRequestFile(projectRoot, request.ChecksumsPath, nameof(request.ChecksumsPath));
        string artifactPath = ResolveRequestFile(projectRoot, request.ArtifactPath, nameof(request.ArtifactPath));
        string signingEvidencePath = ResolveRequestFile(
            projectRoot,
            request.SigningEvidencePath,
            nameof(request.SigningEvidencePath));
        string expectedRevision = RequireExpectedRevision(request.ExpectedSourceRevision);
        if (!IsZipArchive(artifactPath))
            throw Invalid("Packed PowerShell module artifacts must be ZIP-compatible archives.");

        string? expectedThumbprint = string.IsNullOrWhiteSpace(request.SignThumbprint)
            ? null
            : DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(request.SignThumbprint);
        string? expectedSubject = expectedThumbprint is not null || string.IsNullOrWhiteSpace(request.SignSubjectName)
            ? null
            : request.SignSubjectName!.Trim();
        if (expectedThumbprint is null && expectedSubject is null)
            throw Invalid("Packed module verification requires an expected signer thumbprint or subject name.");

        string artifactDigest = VerifyChecksummedFile(projectRoot, checksumsPath, artifactPath, "PowerShell module artifact");
        VerifyChecksummedFile(projectRoot, checksumsPath, signingEvidencePath, "module signing evidence");
        PowerForgeModuleSigningEvidence signingEvidence = ReadSigningEvidence(signingEvidencePath);
        if (signingEvidence.SchemaVersion != 2)
            throw Invalid("Module signing evidence schema version is not supported.");
        if (signingEvidence.SourceDirty is not false)
            throw Invalid("Module signing evidence must attest a clean source checkout.");
        if (!string.Equals(signingEvidence.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Module signing evidence identifies '{signingEvidence.ModuleName}', expected '{moduleName}'.");

        using ZipArchive archive = ZipFile.OpenRead(artifactPath);
        Dictionary<string, ZipArchiveEntry> entries = ValidateArchiveEntries(archive);
        string[] manifestEntries = entries.Keys.Where(entry =>
                string.Equals(Path.GetFileName(entry), moduleName + ".psd1", StringComparison.Ordinal))
            .ToArray();
        if (manifestEntries.Length != 1)
            throw Invalid($"Packed module artifact must contain exactly one '{moduleName}.psd1' manifest.");
        string manifestPath = manifestEntries[0];
        if (!string.Equals(NormalizeArchivePath(signingEvidence.ManifestPath), manifestPath, StringComparison.Ordinal))
            throw Invalid("Module signing evidence does not identify the packed module manifest.");

        string manifestText = DecodeModuleManifest(ReadEntryBytes(entries[manifestPath]));
        if (!ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText, "ModuleVersion", out string? manifestVersion) ||
            string.IsNullOrWhiteSpace(manifestVersion))
            throw Invalid("Packed module manifest does not declare ModuleVersion.");
        string[] prereleaseValues = ModuleManifestValueReader.ReadPsDataStringOrArrayFromText(manifestText, "Prerelease");
        if (prereleaseValues.Length > 1)
            throw Invalid("Packed module manifest declares more than one prerelease label.");
        string prerelease = prereleaseValues.SingleOrDefault() ?? string.Empty;
        string version = NormalizeModuleVersion(manifestVersion!, prerelease);
        ValidateExpectedModuleVersion(request.ExpectedVersion, version);
        if (!ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText, "RootModule", out string? rootModule) ||
            string.IsNullOrWhiteSpace(rootModule))
            throw Invalid("Packed module manifest must declare a RootModule entrypoint.");
        string rootModulePath = ResolveArchiveRelativePath(manifestPath, rootModule!);
        if (!entries.ContainsKey(rootModulePath))
            throw Invalid($"Packed module RootModule '{rootModulePath}' was not found in the archive.");

        string sourceRevisionFromEvidence = DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
            signingEvidence.SourceRevision,
            "module signing evidence source revision");
        ValidateRevision(sourceRevisionFromEvidence, expectedRevision);
        if (!string.Equals(NormalizeModuleVersionText(signingEvidence.Version), version, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Module signing evidence version does not match the packed module manifest.");
        string[] signaturePaths = NormalizeSigningEvidencePaths(signingEvidence.SignableFiles);
        string signedProvenancePath = ResolveArchiveRelativePath(
            manifestPath,
            PowerForgeModuleSourceAttestationWriter.FileName);
        if (!signaturePaths.Contains(manifestPath, StringComparer.Ordinal) ||
            !signaturePaths.Contains(rootModulePath, StringComparer.Ordinal) ||
            !signaturePaths.Contains(signedProvenancePath, StringComparer.Ordinal))
            throw Invalid("Module signing evidence must cover the manifest, RootModule, and signed source attestation.");
        string[] requestedSignaturePaths = (request.SignaturePaths ?? Array.Empty<string>())
            .Select(NormalizeArchivePath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedSignaturePaths.Length > 0 &&
            (!requestedSignaturePaths.All(path => signaturePaths.Contains(path, StringComparer.Ordinal)) ||
             requestedSignaturePaths.Length != signaturePaths.Length))
            throw Invalid("Requested module signature paths do not match the complete trusted signing evidence.");
        Dictionary<string, PowerForgeModulePreservedSignature> thirdPartySignatures =
            NormalizePreservedThirdPartySignatures(signingEvidence.PreservedThirdPartySignatures, signaturePaths);
        if (thirdPartySignatures.ContainsKey(manifestPath) ||
            thirdPartySignatures.ContainsKey(rootModulePath) ||
            thirdPartySignatures.ContainsKey(signedProvenancePath))
            throw Invalid("The module manifest, RootModule, and source attestation must be owned by the configured release publisher.");

        string provenancePath = ResolveArchiveRelativePath(
            manifestPath,
            PublishedRegistryProvenanceValidator.ModuleProvenanceFileName);
        if (!entries.TryGetValue(provenancePath, out ZipArchiveEntry? provenanceEntry))
            throw Invalid($"Primary packed module must contain {PublishedRegistryProvenanceValidator.ModuleProvenanceFileName} beside its manifest.");
        if (!entries.TryGetValue(signedProvenancePath, out ZipArchiveEntry? signedProvenanceEntry))
            throw Invalid($"Primary packed module must contain {PowerForgeModuleSourceAttestationWriter.FileName} beside its manifest.");

        byte[] provenanceBytes = ReadEntryBytes(provenanceEntry);
        using JsonDocument provenance = JsonDocument.Parse(provenanceBytes);
        string actualModuleName = RequireJsonText(provenance.RootElement, "moduleName", "module provenance");
        if (!string.Equals(actualModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Packed module provenance identifies '{actualModuleName}', expected '{moduleName}'.");
        string provenanceVersion = NormalizeModuleVersionText(RequireJsonText(provenance.RootElement, "version", "module provenance"));
        if (!string.Equals(provenanceVersion, version, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Packed module provenance version does not match the module manifest.");
        string sourceRevision = DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
            RequireJsonText(provenance.RootElement, "commit", "module provenance"),
            "module provenance commit");
        ValidateRevision(sourceRevision, expectedRevision);
        if (!string.Equals(sourceRevision, sourceRevisionFromEvidence, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Module signing evidence source revision does not match embedded provenance.");
        if (!TryGet(provenance.RootElement, "sourceDirty", out JsonElement provenanceDirty) ||
            provenanceDirty.ValueKind != JsonValueKind.False)
            throw Invalid("Packed module provenance must attest a clean source checkout.");
        byte[] signedProvenanceBytes = ReadEntryBytes(signedProvenanceEntry);
        PowerForgeModuleSourceAttestation signedProvenance =
            PowerForgeModuleSourceAttestationWriter.Read(signedProvenanceBytes);
        if (!string.Equals(signedProvenance.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Signed module source attestation does not identify the primary module.");
        if (!string.Equals(NormalizeModuleVersionText(signedProvenance.Version), version, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Signed module source attestation version does not match the primary module manifest.");
        ValidateRevision(signedProvenance.SourceRevision, expectedRevision);
        if (!string.Equals(signedProvenance.SourceRevision, sourceRevision, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Signed module source attestation does not match embedded module provenance.");

        var signatures = new List<VerifiedSignature>();
        var signatureEvidence = new List<PowerForgeReleaseSignatureEvidence>();
        string tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge.ReleaseArtifact", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            foreach (string configuredPath in signaturePaths)
            {
                string entryPath = NormalizeArchivePath(configuredPath);
                if (!entries.TryGetValue(entryPath, out ZipArchiveEntry? signatureEntry) || signatureEntry.Length == 0)
                    throw Invalid($"Signed module entry '{entryPath}' was not found exactly once in the archive.");
                string extractedPath = Path.Combine(
                    tempRoot,
                    signatures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Path.GetFileName(entryPath));
                using (Stream input = signatureEntry.Open())
                using (FileStream output = File.Create(extractedPath))
                    input.CopyTo(output);
                bool isThirdParty = thirdPartySignatures.TryGetValue(entryPath, out PowerForgeModulePreservedSignature? preserved);
                VerifiedSignature signature = VerifySignature(
                    extractedPath,
                    isThirdParty ? null : expectedThumbprint,
                    isThirdParty ? null : expectedSubject);
                if (isThirdParty &&
                    (!string.Equals(signature.Subject, preserved!.Subject, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(signature.Thumbprint, preserved.Thumbprint, StringComparison.OrdinalIgnoreCase)))
                    throw Invalid($"Preserved third-party signer identity does not match '{entryPath}'.");
                var representedSignature = new VerifiedSignature(
                    signature.PhysicalPath,
                    artifactPath + "!" + entryPath,
                    signature.Subject,
                    signature.Thumbprint);
                signatures.Add(representedSignature);
                signatureEvidence.Add(new PowerForgeReleaseSignatureEvidence
                {
                    Path = representedSignature.DisplayPath,
                    Subject = signature.Subject,
                    Thumbprint = signature.Thumbprint,
                    Ownership = isThirdParty ? "third-party" : "publisher"
                });
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }

        VerifiedSignature signer = RequireOneSigner(signatures.Where((_, index) =>
            signatureEvidence[index].Ownership == "publisher").ToArray());
        var evidence = BuildExternalEvidence(
            projectRoot,
            checksumsPath,
            provenancePath: null,
            configurationPaths: null,
            sbomPaths: request.SbomPaths,
            artifactId: moduleName,
            artifactVersion: version,
            artifactDigest: artifactDigest).ToList();
        evidence.Add(new PowerForgeReleaseEvidenceFile
        {
            Role = "signing-policy",
            Path = signingEvidencePath,
            Sha256 = DotNetPublishReleaseArtifactVerifier.ComputeSha256(signingEvidencePath)
        });
        evidence.Add(new PowerForgeReleaseEvidenceFile
        {
            Role = "provenance",
            Path = artifactPath + "!" + NormalizeArchivePath(provenanceEntry.FullName),
            Sha256 = ComputeSha256(provenanceBytes)
        });
        evidence.Add(new PowerForgeReleaseEvidenceFile
        {
            Role = "signed-provenance",
            Path = artifactPath + "!" + signedProvenancePath,
            Sha256 = ComputeSha256(signedProvenanceBytes)
        });

        return new PowerForgeReleaseArtifactEvidence
        {
            ArtifactKind = request.Kind,
            ArtifactId = moduleName,
            ArtifactPath = artifactPath,
            FileName = Path.GetFileName(artifactPath),
            Sha256 = artifactDigest,
            Version = version,
            SourceRevision = sourceRevision.ToLowerInvariant(),
            SignerSubject = signer.Subject,
            SignerThumbprint = signer.Thumbprint,
            SignatureStatus = "valid",
            SignaturePaths = signatures.Select(signature => signature.DisplayPath).ToArray(),
            Signatures = signatureEvidence.ToArray(),
            EvidenceFiles = evidence.ToArray()
        };
    }

    private static PowerForgeModuleSigningEvidence ReadSigningEvidence(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PowerForgeModuleSigningEvidence>(
                       File.ReadAllText(path),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw Invalid("Module signing evidence could not be deserialized.");
        }
        catch (JsonException exception)
        {
            throw Invalid($"Module signing evidence is not valid JSON: {exception.Message}");
        }
    }

    private static string[] NormalizeSigningEvidencePaths(IEnumerable<string>? values)
    {
        string[] paths = (values ?? Array.Empty<string>()).Select(NormalizeArchivePath).ToArray();
        if (paths.Length == 0)
            throw Invalid("Module signing evidence must enumerate at least one signable file.");
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw Invalid("Module signing evidence contains duplicate or case-conflicting file paths.");
        return paths;
    }

    private static Dictionary<string, PowerForgeModulePreservedSignature> NormalizePreservedThirdPartySignatures(
        IEnumerable<PowerForgeModulePreservedSignature>? values,
        IReadOnlyCollection<string> signaturePaths)
    {
        var result = new Dictionary<string, PowerForgeModulePreservedSignature>(StringComparer.Ordinal);
        var duplicateGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PowerForgeModulePreservedSignature signature in values ?? Array.Empty<PowerForgeModulePreservedSignature>())
        {
            if (signature is null)
                throw Invalid("Preserved third-party signing evidence cannot contain null entries.");
            string path = NormalizeArchivePath(signature.Path);
            if (!signaturePaths.Contains(path, StringComparer.Ordinal))
                throw Invalid($"Preserved third-party signer identity refers to unverified file '{path}'.");
            if (string.IsNullOrWhiteSpace(signature.Subject))
                throw Invalid($"Preserved third-party signer identity is missing a subject for '{path}'.");
            string thumbprint = DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(signature.Thumbprint);
            if (!duplicateGuard.Add(path))
                throw Invalid($"Preserved third-party signer identity contains duplicate file '{path}'.");
            result.Add(path, new PowerForgeModulePreservedSignature
            {
                Path = path,
                Subject = signature.Subject.Trim(),
                Thumbprint = thumbprint
            });
        }
        return result;
    }

    private static string ResolveArchiveRelativePath(string manifestPath, string relativePath)
    {
        string manifestDirectory = Path.GetDirectoryName(manifestPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        string combined = string.IsNullOrWhiteSpace(manifestDirectory)
            ? relativePath
            : manifestDirectory.Replace(Path.DirectorySeparatorChar, '/') + "/" + relativePath;
        return NormalizeArchivePath(combined);
    }

    private static string DecodeModuleManifest(byte[] bytes)
    {
        if (bytes.Length >= 4 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00) ||
             (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)))
            throw Invalid("Packed module manifest uses unsupported UTF-32 encoding.");

        Encoding encoding;
        int preambleLength;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(false, true);
            preambleLength = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, false, true);
            preambleLength = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, false, true);
            preambleLength = 2;
        }
        else
        {
            encoding = new UTF8Encoding(false, true);
            preambleLength = 0;
        }

        try
        {
            return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid($"Packed module manifest encoding is malformed: {exception.Message}");
        }
    }
}
