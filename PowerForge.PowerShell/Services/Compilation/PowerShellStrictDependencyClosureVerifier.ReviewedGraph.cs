namespace PowerForge;

internal static partial class PowerShellStrictDependencyClosureVerifier
{
    private static void VerifyReviewedDependencyGraph(
        PowerShellStrictDependencyClosureRequest request,
        IReadOnlyCollection<ManagedAssemblyInspection> assemblies,
        PowerShellCompilationDependencyClosure result)
    {
        var locked = request.DependencyGraph.Nodes
            .Where(static node => node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Deployment))
            .Where(static node => node.Kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or PowerShellCompilationDependencyNodeKind.BinaryModule)
            .Where(static node => node.Exists)
            .Where(static node => node.Disposition is PowerShellCompilationDependencyGraphDisposition.Referenced or
                PowerShellCompilationDependencyGraphDisposition.Bundled or
                PowerShellCompilationDependencyGraphDisposition.PrivateRestored)
            .Where(static node => Version.TryParse(node.Identity.Version, out _))
            .GroupBy(
                static node => PowerShellTargetRuntimeAssemblyCatalog.CreateStableKey(
                    node.Identity.Name,
                    Version.Parse(node.Identity.Version),
                    node.Identity.PublicKeyToken,
                    node.Identity.Culture,
                    node.Identity.Retargetable,
                    node.Identity.ContentType),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in assemblies)
        {
            if (IsCompilerOwnedPrimaryAssembly(request.Files, assembly.DisplayPath))
                continue;
            if (!locked.TryGetValue(assembly.Identity.StableKey, out var candidates))
            {
                var providerCandidates = (request.ProviderLock?.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
                    .SelectMany(static package => package.Assemblies ?? Array.Empty<PowerShellCompilationProviderAssembly>())
                    .Where(candidate =>
                        candidate.AssemblyName.Equals(assembly.Identity.Name, StringComparison.OrdinalIgnoreCase) &&
                        candidate.AssemblyVersion.Equals(assembly.Identity.Version.ToString(), StringComparison.Ordinal) &&
                        candidate.PublicKeyToken.Equals(assembly.Identity.PublicKeyToken, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (providerCandidates.Length == 0)
                    throw new InvalidOperationException(
                        $"Strict runtime-free delivered dependency '{assembly.Identity.DisplayName}' is absent from the reviewed dependency graph and provider lock.");
                if (!providerCandidates.Any(candidate => candidate.Sha256.Equals(assembly.ContentSha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        $"Strict runtime-free delivered provider dependency '{assembly.Identity.DisplayName}' does not match its reviewed provider-lock SHA-256.");
                result.DeliveredDependencies.Add(new PowerShellCompilationDeliveredDependency
                {
                    Identity = assembly.Identity.DisplayName,
                    DeliveredSha256 = assembly.ContentSha256,
                    ReviewedInputSha256 = providerCandidates.Select(static candidate => candidate.Sha256)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static hash => hash, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Derivation = "ExactProviderLock"
                });
                continue;
            }
            var contentHashes = candidates.Select(static node => node.Identity.Sha256)
                .Where(static hash => !string.IsNullOrWhiteSpace(hash))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (contentHashes.Length == 0)
                throw new InvalidOperationException(
                    $"Strict runtime-free delivered dependency '{assembly.Identity.DisplayName}' has no SHA-256 content identity in the reviewed dependency graph.");
            var exact = contentHashes.Contains(assembly.ContentSha256, StringComparer.OrdinalIgnoreCase);
            var sdkTransformed = !exact &&
                request.Optimization is PowerShellCompilationExecutableOptimization.Trimmed or PowerShellCompilationExecutableOptimization.NativeAot &&
                candidates.Any(static node => node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal));
            if (!exact && !sdkTransformed)
                throw new InvalidOperationException(
                    $"Strict runtime-free delivered dependency '{assembly.Identity.DisplayName}' does not match the SHA-256 content identity in the reviewed dependency graph.");
            if (sdkTransformed) result.TransformedManagedAssemblies++;
            result.DeliveredDependencies.Add(new PowerShellCompilationDeliveredDependency
            {
                Identity = assembly.Identity.DisplayName,
                DeliveredSha256 = assembly.ContentSha256,
                ReviewedInputSha256 = contentHashes.OrderBy(static hash => hash, StringComparer.OrdinalIgnoreCase).ToArray(),
                Derivation = sdkTransformed ? "SdkOptimization" : "Exact"
            });
        }

        foreach (var pair in locked.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        foreach (var node in pair.Value.OrderBy(static node => node.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(node.Identity.Sha256))
                throw new InvalidOperationException(
                    $"Strict runtime-free reviewed dependency '{node.Identity.Name}' has no SHA-256 content identity.");
            if (assemblies.Any(assembly =>
                    assembly.Identity.StableKey.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) &&
                    assembly.ContentSha256.Equals(node.Identity.Sha256, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal))
                continue;
            throw new InvalidOperationException(
                $"Strict runtime-free reviewed dependency '{node.Identity.Name}' was required for delivery but is absent from the delivered artifact closure.");
        }
    }

    private static bool IsResolvedRuntimeFacadeReference(
        AssemblyIdentity reference,
        IEnumerable<ManagedAssemblyInspection> assemblies,
        ISet<string> targetRuntimeAssemblies,
        IReadOnlyDictionary<string, Version> compatibleSignedRuntimeAssemblies)
    {
        if (string.IsNullOrWhiteSpace(reference.PublicKeyToken)) return false;
        var compatibleKey = PowerShellTargetRuntimeAssemblyCatalog.CreateCompatibleSignedKey(
            reference.Name,
            reference.PublicKeyToken,
            reference.Culture,
            reference.ContentType);
        if (compatibleSignedRuntimeAssemblies.TryGetValue(compatibleKey, out var targetVersion) &&
            targetVersion >= reference.Version)
            return true;
        if (reference.Version != new Version(0, 0, 0, 0)) return false;
        return assemblies.Any(candidate =>
            targetRuntimeAssemblies.Contains(candidate.Identity.StableKey) &&
            candidate.Identity.Name.Equals(reference.Name, StringComparison.OrdinalIgnoreCase) &&
            candidate.Identity.PublicKeyToken.Equals(reference.PublicKeyToken, StringComparison.OrdinalIgnoreCase) &&
            candidate.Identity.Culture.Equals(reference.Culture, StringComparison.OrdinalIgnoreCase) &&
            candidate.Identity.Retargetable == reference.Retargetable &&
            candidate.Identity.ContentType.Equals(reference.ContentType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCompilerOwnedPrimaryAssembly(
        IEnumerable<PowerShellCompilationArtifactFile> files,
        string displayPath)
    {
        foreach (var file in files.Where(static file => file.Role is "Primary" or "GeneratedAssembly" or "TypedAssembly"))
        {
            if (PowerShellCompilationPathSafety.PathEquals(file.Path, displayPath)) return true;
            var separator = displayPath.IndexOf('!');
            if (separator <= 0 || !PowerShellCompilationPathSafety.PathEquals(file.Path, displayPath.Substring(0, separator))) continue;
            var entryName = Path.GetFileNameWithoutExtension(displayPath.Substring(separator + 1));
            var primaryFileName = Path.GetFileName(file.Path);
            var primaryName = Path.GetFileNameWithoutExtension(file.Path);
            if (entryName.Equals(primaryFileName, StringComparison.OrdinalIgnoreCase) ||
                entryName.Equals(primaryName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
