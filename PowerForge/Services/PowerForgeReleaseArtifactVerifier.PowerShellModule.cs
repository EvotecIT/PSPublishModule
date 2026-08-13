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
                string.Equals(Path.GetFileName(entry), moduleName + ".psd1", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (manifestEntries.Length != 1)
            throw Invalid($"Packed module artifact must contain exactly one '{moduleName}.psd1' manifest.");
        string manifestPath = manifestEntries[0];
        if (!string.Equals(NormalizeArchivePath(signingEvidence.ManifestPath), manifestPath, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Module signing evidence does not identify the packed module manifest.");

        string manifestText = Encoding.UTF8.GetString(ReadEntryBytes(entries[manifestPath]));
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
        if (!signaturePaths.Contains(manifestPath, StringComparer.OrdinalIgnoreCase) ||
            !signaturePaths.Contains(rootModulePath, StringComparer.OrdinalIgnoreCase))
            throw Invalid("Module signing evidence must cover the module manifest and RootModule entrypoint.");
        string[] uncoveredSignableFiles = entries.Keys
            .Where(IsModuleSignableFile)
            .Where(path => !signaturePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (uncoveredSignableFiles.Length > 0)
            throw Invalid(
                $"Module signing evidence omits signable module file '{uncoveredSignableFiles[0]}'.");
        string[] requestedSignaturePaths = (request.SignaturePaths ?? Array.Empty<string>())
            .Select(NormalizeArchivePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedSignaturePaths.Length > 0 &&
            (!requestedSignaturePaths.All(path => signaturePaths.Contains(path, StringComparer.OrdinalIgnoreCase)) ||
             requestedSignaturePaths.Length != signaturePaths.Length))
            throw Invalid("Requested module signature paths do not match the complete trusted signing evidence.");
        Dictionary<string, PowerForgeModulePreservedSignature> thirdPartySignatures =
            NormalizePreservedThirdPartySignatures(signingEvidence.PreservedThirdPartySignatures, signaturePaths);
        if (thirdPartySignatures.ContainsKey(manifestPath) || thirdPartySignatures.ContainsKey(rootModulePath))
            throw Invalid("The module manifest and RootModule must be owned by the configured release publisher.");

        ZipArchiveEntry[] provenanceEntries = entries.Values.Where(entry =>
            string.Equals(Path.GetFileName(entry.FullName.Replace('\\', '/')),
                PublishedRegistryProvenanceValidator.ModuleProvenanceFileName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (provenanceEntries.Length != 1)
            throw Invalid(
                $"Packed module artifact must contain exactly one {PublishedRegistryProvenanceValidator.ModuleProvenanceFileName}.");

        ZipArchiveEntry provenanceEntry = provenanceEntries[0];
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
        string[] paths = (values ?? Array.Empty<string>())
            .Select(NormalizeArchivePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            throw Invalid("Module signing evidence must enumerate at least one signable file.");
        return paths;
    }

    private static Dictionary<string, PowerForgeModulePreservedSignature> NormalizePreservedThirdPartySignatures(
        IEnumerable<PowerForgeModulePreservedSignature>? values,
        IReadOnlyCollection<string> signaturePaths)
    {
        var result = new Dictionary<string, PowerForgeModulePreservedSignature>(StringComparer.OrdinalIgnoreCase);
        foreach (PowerForgeModulePreservedSignature signature in values ?? Array.Empty<PowerForgeModulePreservedSignature>())
        {
            if (signature is null)
                throw Invalid("Preserved third-party signing evidence cannot contain null entries.");
            string path = NormalizeArchivePath(signature.Path);
            if (!signaturePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                throw Invalid($"Preserved third-party signer identity refers to unverified file '{path}'.");
            if (string.IsNullOrWhiteSpace(signature.Subject))
                throw Invalid($"Preserved third-party signer identity is missing a subject for '{path}'.");
            string thumbprint = DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(signature.Thumbprint);
            if (result.ContainsKey(path))
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

    private static bool IsModuleSignableFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cdxml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cat", StringComparison.OrdinalIgnoreCase);
    }
}
