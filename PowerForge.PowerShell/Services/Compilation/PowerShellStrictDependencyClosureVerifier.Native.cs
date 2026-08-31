namespace PowerForge;

/// <summary>Owns native-library and native-executable closure checks for Strict artifacts.</summary>
internal static partial class PowerShellStrictDependencyClosureVerifier
{
    private static bool IsNativeLibrary(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(".so.", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);
    }

    private static void VerifyNativeExecutableImports(
        PowerShellStrictDependencyClosureRequest request,
        PowerShellCompilationNativeExecutableEvidence executable,
        PowerShellCompilationDependencyClosure result)
    {
        var reviewedNative = request.DependencyGraph.Nodes
            .Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.NativeLibrary &&
                                  node.Exists &&
                                  node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(node.Identity.Sha256))
            .ToArray();
        foreach (var import in executable.ImportedLibraries)
        {
            var reviewed = reviewedNative.FirstOrDefault(node =>
                PowerShellNativeLibraryName.CanResolve(request.RuntimeIdentifier, import, node.Identity.Name));
            if (reviewed is not null)
            {
                result.ReviewedNativeImports.Add("NativeAOT:" + import + "->" + reviewed.Identity.Source);
                continue;
            }
            if (PowerShellTargetNativeAbiCatalog.Contains(request.RuntimeIdentifier, import))
            {
                result.TargetAbiNativeImports.Add("NativeAOT:" + import);
                continue;
            }
            throw new InvalidOperationException(
                $"NativeAOT executable imports '{import}', which is neither an exact reviewed runtime-pack asset nor part of the explicit '{request.RuntimeIdentifier}' target operating-system ABI.");
        }
    }

    private static void VerifyReviewedNativeDependencyGraph(
        PowerShellStrictDependencyClosureRequest request,
        IEnumerable<NativeLibraryInspection> nativeLibraries,
        PowerShellCompilationDependencyClosure result)
    {
        var deliveredLibraries = nativeLibraries.ToArray();
        var reviewed = request.DependencyGraph.Nodes
            .Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.NativeLibrary &&
                                  node.Exists &&
                                  node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Deployment) &&
                                  node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(node.Identity.Sha256))
            .ToArray();
        var reviewedProviderAssets = (request.ProviderLock?.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
            .SelectMany(package => (package.NativeAssets ?? Array.Empty<PowerShellCompilationProviderNativeAsset>())
                .Select(asset => new { package.PackageId, Asset = asset }))
            .ToArray();
        foreach (var native in deliveredLibraries)
        {
            var match = reviewed.FirstOrDefault(node =>
                PowerShellNativeLibraryName.FileNamesEqual(request.RuntimeIdentifier, native.Name, node.Identity.Name) &&
                native.ContentSha256.Equals(node.Identity.Sha256, StringComparison.OrdinalIgnoreCase));
            string? reviewedSource = null;
            if (match is not null)
            {
                reviewedSource = match.Identity.Source;
            }
            else
            {
                var providerMatch = reviewedProviderAssets.FirstOrDefault(candidate =>
                    PowerShellNativeLibraryName.FileNamesEqual(request.RuntimeIdentifier, native.Name, candidate.Asset.FileName) &&
                    native.ContentSha256.Equals(candidate.Asset.Sha256, StringComparison.OrdinalIgnoreCase));
                if (providerMatch is not null)
                    reviewedSource = "Provider:" + providerMatch.PackageId + ":" + providerMatch.Asset.Path;
            }
            if (reviewedSource is null)
            {
                result.Limitations.Add(
                    $"Native dependency certification remains fail-closed because delivered native dependency '{native.Name}' does not match an exact SHA-256 content identity in the reviewed runtime-pack or provider lock.");
                continue;
            }

            result.DeliveredNativeDependencies.Add(new PowerShellCompilationDeliveredNativeDependency
            {
                Name = native.Name,
                DeliveredSha256 = native.ContentSha256,
                ReviewedSource = reviewedSource
            });
            foreach (var import in native.ImportedLibraries)
            {
                var deliveredImport = deliveredLibraries.FirstOrDefault(candidate =>
                    PowerShellNativeLibraryName.CanResolve(request.RuntimeIdentifier, import, candidate.Name));
                if (deliveredImport is not null)
                {
                    result.ReviewedNativeImports.Add(native.Name + ":" + import + "->" + deliveredImport.Name);
                    continue;
                }
                if (PowerShellTargetNativeAbiCatalog.Contains(request.RuntimeIdentifier, import))
                {
                    result.TargetAbiNativeImports.Add(native.Name + ":" + import);
                    continue;
                }
                throw new InvalidOperationException(
                    $"Reviewed native dependency '{native.DisplayPath}' imports '{import}', which is neither another delivered reviewed native asset nor part of the explicit '{request.RuntimeIdentifier}' target operating-system ABI.");
            }
        }
    }

    private static void VerifyNativeReferenceClosure(
        PowerShellStrictDependencyClosureRequest request,
        IEnumerable<ManagedAssemblyInspection> assemblies,
        PowerShellCompilationDependencyClosure result)
    {
        var reviewedManaged = request.DependencyGraph.Nodes
            .Where(static node => node.Kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or PowerShellCompilationDependencyNodeKind.BinaryModule &&
                                  node.Exists &&
                                  node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal))
            .Where(static node => Version.TryParse(node.Identity.Version, out _))
            .Select(node => PowerShellTargetRuntimeAssemblyCatalog.CreateStableKey(
                node.Identity.Name,
                Version.Parse(node.Identity.Version),
                node.Identity.PublicKeyToken,
                node.Identity.Culture,
                node.Identity.Retargetable,
                node.Identity.ContentType))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewedNative = request.DependencyGraph.Nodes
            .Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.NativeLibrary &&
                                  node.Exists &&
                                  node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(node.Identity.Sha256))
            .ToArray();
        var reviewedProviderAssemblies = (request.ProviderLock?.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
            .SelectMany(static package => package.Assemblies ?? Array.Empty<PowerShellCompilationProviderAssembly>())
            .ToArray();
        var reviewedProviderNative = (request.ProviderLock?.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
            .SelectMany(static package => package.NativeAssets ?? Array.Empty<PowerShellCompilationProviderNativeAsset>())
            .ToArray();
        foreach (var assembly in assemblies)
        foreach (var import in assembly.NativeImports.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var providerCaller = reviewedProviderAssemblies.Any(candidate =>
                candidate.AssemblyName.Equals(assembly.Identity.Name, StringComparison.OrdinalIgnoreCase) &&
                candidate.AssemblyVersion.Equals(assembly.Identity.Version.ToString(), StringComparison.Ordinal) &&
                candidate.PublicKeyToken.Equals(assembly.Identity.PublicKeyToken, StringComparison.OrdinalIgnoreCase));
            if (!reviewedManaged.Contains(assembly.Identity.StableKey) && !providerCaller)
                throw new InvalidOperationException(
                    $"Strict runtime-free managed dependency '{assembly.DisplayPath}' imports native library '{import}'. Native dependency certification remains fail-closed because only exact reviewed .NET runtime-pack or provider-lock callers may use the target native ABI.");
            var nativeSource = reviewedNative.FirstOrDefault(node =>
                PowerShellNativeLibraryName.CanResolve(request.RuntimeIdentifier, import, node.Identity.Name));
            if (nativeSource is not null)
            {
                result.ReviewedNativeImports.Add(assembly.Identity.Name + ":" + import + "->" + nativeSource.Identity.Source);
                continue;
            }
            var providerNativeSource = reviewedProviderNative.FirstOrDefault(asset =>
                PowerShellNativeLibraryName.CanResolve(request.RuntimeIdentifier, import, asset.FileName));
            if (providerNativeSource is not null)
            {
                result.ReviewedNativeImports.Add(assembly.Identity.Name + ":" + import + "->Provider:" + providerNativeSource.Path);
                continue;
            }
            if (PowerShellTargetNativeAbiCatalog.Contains(request.RuntimeIdentifier, import))
            {
                result.TargetAbiNativeImports.Add(assembly.Identity.Name + ":" + import);
                continue;
            }
            throw new InvalidOperationException(
                $"Strict runtime-free managed dependency '{assembly.DisplayPath}' imports native library '{import}'. " +
                $"The import is neither an exact reviewed runtime-pack asset nor part of the explicit '{request.RuntimeIdentifier}' target operating-system ABI.");
        }
    }

    private sealed class NativeLibraryInspection
    {
        internal NativeLibraryInspection(
            string name,
            string displayPath,
            PowerShellCompilationNativeExecutableEvidence inspection)
        {
            Name = name;
            DisplayPath = displayPath;
            ContentSha256 = inspection.Sha256;
            ImportedLibraries = inspection.ImportedLibraries;
        }

        internal string Name { get; }
        internal string DisplayPath { get; }
        internal string ContentSha256 { get; }
        internal string[] ImportedLibraries { get; }
    }
}
