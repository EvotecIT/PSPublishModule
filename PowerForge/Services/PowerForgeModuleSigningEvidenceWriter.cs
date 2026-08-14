using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Creates the immutable signing evidence consumed when a packed PowerShell module is verified for release.
/// </summary>
public static class PowerForgeModuleSigningEvidenceWriter
{
    /// <summary>
    /// Creates signing evidence from the exact files verified by the shared module-signing pipeline.
    /// </summary>
    /// <param name="moduleRoot">Root directory whose relative paths exactly mirror the final packed archive layout.</param>
    /// <param name="moduleName">Module name.</param>
    /// <param name="version">Module version.</param>
    /// <param name="sourceRevision">Full Git source revision used by the build.</param>
    /// <param name="sourceDirty">Whether the source checkout contained tracked or untracked changes.</param>
    /// <param name="manifestPath">Path to the module manifest under <paramref name="moduleRoot"/>.</param>
    /// <param name="signingResult">Successful result returned by the shared module-signing pipeline.</param>
    /// <returns>Normalized evidence suitable for serialization beside a packed module.</returns>
    public static PowerForgeModuleSigningEvidence Create(
        string moduleRoot,
        string moduleName,
        string version,
        string sourceRevision,
        bool sourceDirty,
        string manifestPath,
        ModuleSigningResult signingResult)
    {
        string root = RequireDirectory(moduleRoot, nameof(moduleRoot));
        string name = RequireText(moduleName, nameof(moduleName));
        string normalizedVersion = RequireVersion(version);
        string revision = DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(sourceRevision, nameof(sourceRevision));
        if (sourceDirty)
            throw new InvalidOperationException("Release signing evidence cannot be created from a dirty source checkout.");
        if (signingResult is null)
            throw new ArgumentNullException(nameof(signingResult));
        if (!signingResult.Success || signingResult.TotalAfterExclude < 1)
            throw new InvalidOperationException("Module signing must complete successfully before release evidence can be created.");
        StringComparer pathComparer = FrameworkCompatibility.GetPathStringComparison(root) == StringComparison.Ordinal
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

        string manifest = ResolveFileUnderRoot(root, manifestPath, nameof(manifestPath));
        NormalizedSigningInventory inventory = NormalizeSigningInventory(root, signingResult, pathComparer);
        string[] verifiedFiles = inventory.VerifiedFiles;
        PowerForgeModulePreservedSignature[] preservedThirdPartySignatures = inventory.PreservedThirdPartySignatures;
        if (!verifiedFiles.Contains(manifest, pathComparer))
            throw new InvalidOperationException("Module signing evidence must include the module manifest.");
        if (preservedThirdPartySignatures.Any(signature =>
                pathComparer.Equals(ResolveFileUnderRoot(root, signature.Path, "preserved third-party signing path"), manifest)))
            throw new InvalidOperationException("The module manifest must be owned by the configured release publisher.");
        string? rootModule = ModuleManifestValueReader.ReadTopLevelString(manifest, "RootModule");
        if (!string.IsNullOrWhiteSpace(rootModule))
        {
            string rootModulePath = ResolveModuleEntrypoint(root, manifest, rootModule!);
            if (!verifiedFiles.Contains(rootModulePath, pathComparer))
                throw new InvalidOperationException("Module signing evidence must include the RootModule entrypoint.");
            if (preservedThirdPartySignatures.Any(signature =>
                    pathComparer.Equals(ResolveFileUnderRoot(root, signature.Path, "preserved third-party signing path"), rootModulePath)))
                throw new InvalidOperationException("The RootModule entrypoint must be owned by the configured release publisher.");
        }
        string manifestDirectory = Path.GetDirectoryName(manifest) ?? root;
        foreach (string relativePath in ModuleManifestLoadedContent.ReadRelativePaths(manifest))
        {
            string loadedPath = ResolveFileUnderRoot(root, Path.Combine(manifestDirectory, relativePath), "manifest-loaded content");
            if (!verifiedFiles.Contains(loadedPath, pathComparer))
                throw new InvalidOperationException($"Module signing evidence must include manifest-loaded content '{relativePath}'.");
            if (preservedThirdPartySignatures.Any(signature =>
                    pathComparer.Equals(ResolveFileUnderRoot(root, signature.Path, "preserved third-party signing path"), loadedPath)))
                throw new InvalidOperationException($"Manifest-loaded content '{relativePath}' must be owned by the configured release publisher.");
        }
        string sourceAttestationPath = ResolveFileUnderRoot(
            root,
            Path.Combine(Path.GetDirectoryName(manifest) ?? root, PowerForgeModuleSourceAttestationWriter.FileName),
            "signed source attestation");
        if (!verifiedFiles.Contains(sourceAttestationPath, pathComparer))
            throw new InvalidOperationException("Module signing evidence must include the signed source attestation.");
        if (preservedThirdPartySignatures.Any(signature =>
                pathComparer.Equals(
                    ResolveFileUnderRoot(root, signature.Path, "preserved third-party signing path"),
                    sourceAttestationPath)))
            throw new InvalidOperationException("The source attestation must be owned by the configured release publisher.");
        PowerForgeModuleSourceAttestation sourceAttestation =
            PowerForgeModuleSourceAttestationWriter.Read(File.ReadAllBytes(sourceAttestationPath));
        if (!string.Equals(sourceAttestation.ModuleName, name, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceAttestation.Version, normalizedVersion, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceAttestation.SourceRevision, revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Signed module source attestation does not match the signing evidence identity.");
        string[] signableFiles = verifiedFiles.Select(path => NormalizeRelativePath(root, path)).ToArray();
        string signingInventorySha256 = ComputeSigningInventorySha256(signableFiles, preservedThirdPartySignatures);
        if (!string.Equals(sourceAttestation.SigningInventorySha256, signingInventorySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Signed module source attestation does not bind the complete final signing inventory.");

        return new PowerForgeModuleSigningEvidence
        {
            SchemaVersion = 3,
            ModuleName = name,
            Version = normalizedVersion,
            SourceRevision = revision.ToLowerInvariant(),
            SourceDirty = false,
            ManifestPath = NormalizeRelativePath(root, manifest),
            SignableFiles = signableFiles,
            SigningInventorySha256 = signingInventorySha256,
            PreservedThirdPartySignatures = preservedThirdPartySignatures
        };
    }

    /// <summary>
    /// Writes signing evidence created from the exact files verified by the shared module-signing pipeline.
    /// </summary>
    /// <param name="outputPath">Destination JSON sidecar path.</param>
    /// <param name="moduleRoot">Root directory whose relative paths exactly mirror the final packed archive layout.</param>
    /// <param name="moduleName">Module name.</param>
    /// <param name="version">Module version.</param>
    /// <param name="sourceRevision">Full Git source revision used by the build.</param>
    /// <param name="sourceDirty">Whether the source checkout contained tracked or untracked changes.</param>
    /// <param name="manifestPath">Path to the module manifest under <paramref name="moduleRoot"/>.</param>
    /// <param name="signingResult">Successful result returned by the shared module-signing pipeline.</param>
    /// <returns>The normalized full path of the written sidecar.</returns>
    public static string Write(
        string outputPath,
        string moduleRoot,
        string moduleName,
        string version,
        string sourceRevision,
        bool sourceDirty,
        string manifestPath,
        ModuleSigningResult signingResult)
    {
        string destination = Path.GetFullPath(RequireText(outputPath, nameof(outputPath)));
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        PowerForgeModuleSigningEvidence evidence = Create(
            moduleRoot,
            moduleName,
            version,
            sourceRevision,
            sourceDirty,
            manifestPath,
            signingResult);
        File.WriteAllText(destination, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        return destination;
    }

    /// <summary>
    /// Writes signing evidence using the signed source attestation beside the primary module manifest.
    /// </summary>
    /// <param name="outputPath">Destination JSON sidecar path.</param>
    /// <param name="moduleRoot">Root directory whose relative paths exactly mirror the final packed archive layout.</param>
    /// <param name="moduleName">Expected primary module name.</param>
    /// <param name="version">Expected module version.</param>
    /// <param name="manifestPath">Path to the primary module manifest under <paramref name="moduleRoot"/>.</param>
    /// <param name="signingResult">Successful result returned by the shared module-signing pipeline.</param>
    /// <returns>The normalized full path of the written sidecar.</returns>
    public static string WriteFromSignedSourceAttestation(
        string outputPath,
        string moduleRoot,
        string moduleName,
        string version,
        string manifestPath,
        ModuleSigningResult signingResult)
    {
        string manifest = ResolveFileUnderRoot(RequireDirectory(moduleRoot, nameof(moduleRoot)), manifestPath, nameof(manifestPath));
        string attestationPath = Path.Combine(
            Path.GetDirectoryName(manifest) ?? throw new InvalidOperationException("Module manifest directory could not be resolved."),
            PowerForgeModuleSourceAttestationWriter.FileName);
        PowerForgeModuleSourceAttestation attestation = PowerForgeModuleSourceAttestationWriter.Read(File.ReadAllBytes(attestationPath));
        return Write(
            outputPath,
            moduleRoot,
            moduleName,
            version,
            attestation.SourceRevision,
            sourceDirty: false,
            manifest,
            signingResult);
    }

    internal static string ComputeSigningInventorySha256(
        string moduleRoot,
        ModuleSigningResult signingResult)
    {
        string root = RequireDirectory(moduleRoot, nameof(moduleRoot));
        StringComparer pathComparer = FrameworkCompatibility.GetPathStringComparison(root) == StringComparison.Ordinal
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        NormalizedSigningInventory inventory = NormalizeSigningInventory(root, signingResult, pathComparer);
        return ComputeSigningInventorySha256(
            inventory.VerifiedFiles.Select(path => NormalizeRelativePath(root, path)),
            inventory.PreservedThirdPartySignatures);
    }

    internal static string ComputeSigningInventorySha256(
        IEnumerable<string> signableFiles,
        IEnumerable<PowerForgeModulePreservedSignature>? preservedThirdPartySignatures)
    {
        string[] paths = signableFiles
            .Select(path => RequireText(path, "signable file path").Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, PowerForgeModulePreservedSignature> preserved =
            (preservedThirdPartySignatures ?? Array.Empty<PowerForgeModulePreservedSignature>())
            .ToDictionary(signature => signature.Path.Replace('\\', '/'), StringComparer.Ordinal);
        var canonical = new StringBuilder();
        foreach (string path in paths)
        {
            bool thirdParty = preserved.TryGetValue(path, out PowerForgeModulePreservedSignature? signature);
            AppendCanonical(canonical, path);
            AppendCanonical(canonical, thirdParty ? "third-party" : "publisher");
            AppendCanonical(canonical, thirdParty ? signature!.Subject.Trim() : string.Empty);
            AppendCanonical(canonical, thirdParty
                ? DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(signature!.Thumbprint)
                : string.Empty);
            canonical.Append('\n');
        }
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static void AppendCanonical(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static NormalizedSigningInventory NormalizeSigningInventory(
        string root,
        ModuleSigningResult signingResult,
        StringComparer pathComparer)
    {
        if (signingResult is null)
            throw new ArgumentNullException(nameof(signingResult));
        if (!signingResult.Success || signingResult.TotalAfterExclude < 1)
            throw new InvalidOperationException("Module signing must complete successfully before release evidence can be created.");
        string[] verifiedFiles = (signingResult.VerifiedFilePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveFileUnderRoot(root, path, "verified signing path"))
            .Distinct(pathComparer)
            .OrderBy(path => path, pathComparer)
            .ToArray();
        if (verifiedFiles.Length != signingResult.TotalAfterExclude)
            throw new InvalidOperationException(
                "Module signing evidence requires one exact verified file path for every file selected by the signing pipeline.");
        PowerForgeModulePreservedSignature[] preservedThirdPartySignatures =
            (signingResult.PreservedThirdPartySignatures ?? Array.Empty<ModuleSigningPreservedSignature>())
            .Select(signature => CreatePreservedSignature(root, signature))
            .OrderBy(signature => signature.Path, pathComparer)
            .ToArray();
        if (preservedThirdPartySignatures.Select(signature => signature.Path).Distinct(pathComparer).Count() !=
            preservedThirdPartySignatures.Length)
            throw new InvalidOperationException("Preserved third-party signing evidence contains duplicate file paths.");
        if (preservedThirdPartySignatures.Length != signingResult.AlreadySignedOther)
            throw new InvalidOperationException(
                "Preserved third-party signer identities must cover every valid third-party signature reported by the signing pipeline.");
        if (preservedThirdPartySignatures.Any(signature =>
                !verifiedFiles.Contains(ResolveFileUnderRoot(root, signature.Path, "preserved third-party signing path"), pathComparer)))
            throw new InvalidOperationException("Preserved third-party files must be part of the verified signing set.");
        return new NormalizedSigningInventory(verifiedFiles, preservedThirdPartySignatures);
    }

    private static string RequireDirectory(string path, string parameterName)
    {
        string fullPath = Path.GetFullPath(RequireText(path, parameterName));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"{parameterName} was not found: {fullPath}");
        return fullPath;
    }

    private static string ResolveFileUnderRoot(string root, string path, string label)
    {
        string candidate = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        string relative = FrameworkCompatibility.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} must stay under the staged module root.");
        if (!File.Exists(candidate))
            throw new FileNotFoundException($"{label} was not found.", candidate);
        return ResolveCanonicalExistingPath(root, relative, label);
    }

    private static string ResolveModuleEntrypoint(string root, string manifestPath, string rootModule)
    {
        if (Path.IsPathRooted(rootModule) || rootModule.StartsWith("\\", StringComparison.Ordinal) ||
            rootModule.StartsWith("/", StringComparison.Ordinal))
            throw new InvalidOperationException("RootModule entrypoint must be relative to the primary module manifest.");
        string manifestDirectory = Path.GetDirectoryName(manifestPath) ?? root;
        string candidate = Path.GetFullPath(Path.Combine(manifestDirectory, rootModule));
        string relativeToManifest = FrameworkCompatibility.GetRelativePath(manifestDirectory, candidate);
        if (Path.IsPathRooted(relativeToManifest) || relativeToManifest == ".." ||
            relativeToManifest.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("RootModule entrypoint must stay under the primary module manifest directory.");
        return ResolveFileUnderRoot(root, candidate, "RootModule entrypoint");
    }

    private static string ResolveCanonicalExistingPath(string root, string relativePath, string label)
    {
        string current = root;
        string[] segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            string[] matches = Directory.EnumerateFileSystemEntries(current)
                .Where(path => string.Equals(Path.GetFileName(path), segment, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"{label} has ambiguous filesystem casing.");
            current = matches[0];
        }
        return Path.GetFullPath(current);
    }

    private static string NormalizeRelativePath(string root, string path) =>
        FrameworkCompatibility.GetRelativePath(root, path).Replace('\\', '/');

    private static PowerForgeModulePreservedSignature CreatePreservedSignature(
        string root,
        ModuleSigningPreservedSignature signature)
    {
        if (signature is null)
            throw new InvalidOperationException("Preserved third-party signing evidence cannot contain null entries.");
        string path = ResolveFileUnderRoot(root, signature.FilePath, "preserved third-party signing path");
        string subject = RequireText(signature.Subject, "preserved third-party signer subject");
        string thumbprint = DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(signature.Thumbprint);
        return new PowerForgeModulePreservedSignature
        {
            Path = NormalizeRelativePath(root, path),
            Subject = subject,
            Thumbprint = thumbprint
        };
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        return value.Trim();
    }

    private static string RequireVersion(string version)
    {
        string value = RequireText(version, nameof(version));
        int separator = value.IndexOf('-');
        string numeric = separator < 0 ? value : value.Substring(0, separator);
        string prerelease = separator < 0 ? string.Empty : value.Substring(separator + 1);
        if (!Version.TryParse(numeric, out Version? parsed) ||
            (separator >= 0 && (prerelease.Length == 0 || prerelease.Any(character =>
                !(char.IsLetterOrDigit(character) || character == '.' || character == '-')))))
            throw new ArgumentException("version must be a valid module version, optionally including a prerelease label.", nameof(version));
        string normalized = parsed.Revision == 0
            ? new Version(parsed.Major, parsed.Minor, parsed.Build).ToString()
            : parsed.ToString();
        return prerelease.Length == 0 ? normalized : normalized + "-" + prerelease;
    }

    private sealed class NormalizedSigningInventory
    {
        internal NormalizedSigningInventory(
            string[] verifiedFiles,
            PowerForgeModulePreservedSignature[] preservedThirdPartySignatures)
        {
            VerifiedFiles = verifiedFiles;
            PreservedThirdPartySignatures = preservedThirdPartySignatures;
        }

        internal string[] VerifiedFiles { get; }

        internal PowerForgeModulePreservedSignature[] PreservedThirdPartySignatures { get; }
    }
}
