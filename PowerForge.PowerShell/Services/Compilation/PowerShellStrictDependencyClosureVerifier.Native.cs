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
            var reviewed = reviewedNative.FirstOrDefault(node => NativeNamesMatch(import, node.Identity.Name));
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
        var reviewed = request.DependencyGraph.Nodes
            .Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.NativeLibrary &&
                                  node.Exists &&
                                  node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Deployment) &&
                                  node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(node.Identity.Sha256))
            .ToArray();
        foreach (var native in nativeLibraries)
        {
            var match = reviewed.FirstOrDefault(node =>
                NativeNamesMatch(native.Name, node.Identity.Name) &&
                native.ContentSha256.Equals(node.Identity.Sha256, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                result.Limitations.Add(
                    $"Native dependency certification remains fail-closed because delivered native dependency '{native.Name}' does not match an exact SHA-256 content identity in the reviewed runtime-pack graph.");
                continue;
            }
            result.DeliveredNativeDependencies.Add(new PowerShellCompilationDeliveredNativeDependency
            {
                Name = native.Name,
                DeliveredSha256 = native.ContentSha256,
                ReviewedSource = match.Identity.Source
            });
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
        foreach (var assembly in assemblies)
        foreach (var import in assembly.NativeImports.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!reviewedManaged.Contains(assembly.Identity.StableKey))
                throw new InvalidOperationException(
                    $"Strict runtime-free managed dependency '{assembly.DisplayPath}' imports native library '{import}'. Native dependency certification remains fail-closed because only exact reviewed .NET runtime-pack callers may use the target native ABI.");
            var nativeSource = reviewedNative.FirstOrDefault(node => NativeNamesMatch(import, node.Identity.Name));
            if (nativeSource is not null)
            {
                result.ReviewedNativeImports.Add(assembly.Identity.Name + ":" + import + "->" + nativeSource.Identity.Source);
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

    private static bool NativeNamesMatch(string left, string right)
    {
        var leftName = Path.GetFileName(left);
        var rightName = Path.GetFileName(right);
        if (leftName.Equals(rightName, StringComparison.OrdinalIgnoreCase)) return true;
        return rightName.StartsWith(leftName + ".", StringComparison.OrdinalIgnoreCase) ||
               leftName.StartsWith(rightName + ".", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NativeLibraryInspection
    {
        internal NativeLibraryInspection(string name, string displayPath, string contentSha256)
        {
            Name = name;
            DisplayPath = displayPath;
            ContentSha256 = contentSha256;
        }

        internal string Name { get; }
        internal string DisplayPath { get; }
        internal string ContentSha256 { get; }
    }
}
