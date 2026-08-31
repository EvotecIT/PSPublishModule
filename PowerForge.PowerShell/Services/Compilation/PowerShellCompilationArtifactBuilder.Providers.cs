using System.IO.Compression;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static PowerShellCompilationProviderResolution ResolveProviderPackages(PowerShellCompilationBuildSpec spec)
    {
        var references = spec.ProviderPackages ?? Array.Empty<PowerShellCompilationProviderPackageReference>();
        if (references.Length == 0)
        {
            if (spec.ExpectedProviderLock is { Packages.Length: > 0 })
                throw new InvalidOperationException("A reviewed provider lock was supplied, but no provider packages were selected.");
            return new PowerShellCompilationProviderPackageReader().Resolve(
                Array.Empty<PowerShellCompilationProviderPackageReference>(),
                semanticProfileId: spec.SemanticProfileId);
        }
        if (spec.ExpectedProviderLock is null && !spec.AllowUnreviewedProviderResolution)
            throw new InvalidOperationException(
                "PowerShell compilation requires a separately reviewed provider lock. Supply ExpectedProviderLock from non-executing provider resolution, or explicitly set AllowUnreviewedProviderResolution for a development build.");
        if (spec.ExpectedProviderLock is not null && spec.AllowUnreviewedProviderResolution)
            throw new ArgumentException("ExpectedProviderLock and AllowUnreviewedProviderResolution are mutually exclusive.", nameof(spec));

        var resolution = new PowerShellCompilationProviderPackageReader().Resolve(
            references,
            spec.ProviderTrustPolicy ?? new PowerShellCompilationProviderTrustPolicy(),
            spec.SemanticProfileId,
            spec.RuntimeIdentifier);
        var nonRedistributable = resolution.Lock.Packages.FirstOrDefault(static package => !package.Redistributable);
        if (nonRedistributable is not null)
            throw new InvalidOperationException(
                $"Provider package '{nonRedistributable.PackageId}' is not approved for redistribution and cannot be delivered in a generated artifact.");
        if (spec.ExpectedProviderLock is not null)
            PowerShellCompilationProviderPackageReader.EnsureMatches(spec.ExpectedProviderLock, resolution.Lock);
        return resolution;
    }

    private static ProviderRuntimeAssembly[] PrepareProviderRuntimeAssemblies(
        string workspace,
        PowerShellCompilationProviderResolution resolution)
    {
        if (resolution.RuntimeAssemblies.Length == 0) return Array.Empty<ProviderRuntimeAssembly>();
        var root = Path.Combine(workspace, "provider-runtime");
        Directory.CreateDirectory(root);
        var result = new List<ProviderRuntimeAssembly>();
        foreach (var resolved in resolution.RuntimeAssemblies)
        {
            var target = Path.Combine(root, resolved.Assembly.AssemblyName + ".dll");
            using (var package = ZipFile.OpenRead(resolved.PackagePath))
            {
                var archivePath = resolved.Assembly.Path.Replace('\\', '/');
                var entries = package.Entries.Where(entry => entry.FullName.Replace('\\', '/').Equals(archivePath, StringComparison.Ordinal)).ToArray();
                if (entries.Length != 1)
                    throw new InvalidOperationException($"Provider package '{resolved.PackagePath}' no longer contains exactly one locked assembly '{archivePath}'.");
                using var source = entries[0].Open();
                using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
            }
            var inspected = PowerShellCompilationProviderPackageReader.InspectAssembly(target, resolved.Assembly.Path);
            if (!inspected.Sha256.Equals(resolved.Assembly.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !inspected.AssemblyName.Equals(resolved.Assembly.AssemblyName, StringComparison.Ordinal) ||
                !inspected.AssemblyVersion.Equals(resolved.Assembly.AssemblyVersion, StringComparison.Ordinal) ||
                !inspected.PublicKeyToken.Equals(resolved.Assembly.PublicKeyToken, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Extracted provider assembly '{resolved.Assembly.Path}' no longer matches its locked identity.");
            result.Add(new ProviderRuntimeAssembly(target, resolved.Assembly));
        }
        return result.ToArray();
    }

    private static ProviderRuntimeNativeAsset[] PrepareProviderRuntimeNativeAssets(
        string workspace,
        PowerShellCompilationProviderResolution resolution)
    {
        if (resolution.RuntimeNativeAssets.Length == 0) return Array.Empty<ProviderRuntimeNativeAsset>();
        var root = Path.Combine(workspace, "provider-native-runtime");
        Directory.CreateDirectory(root);
        var result = new List<ProviderRuntimeNativeAsset>();
        foreach (var resolved in resolution.RuntimeNativeAssets)
        {
            var target = Path.Combine(root, resolved.Asset.FileName);
            using (var package = ZipFile.OpenRead(resolved.PackagePath))
            {
                var archivePath = resolved.Asset.Path.Replace('\\', '/');
                var entries = package.Entries.Where(entry => entry.FullName.Replace('\\', '/').Equals(archivePath, StringComparison.Ordinal)).ToArray();
                if (entries.Length != 1)
                    throw new InvalidOperationException($"Provider package '{resolved.PackagePath}' no longer contains exactly one locked native asset '{archivePath}'.");
                using var source = entries[0].Open();
                using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
            }
            if (!ComputeSha256(target).Equals(resolved.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Extracted provider native asset '{resolved.Asset.Path}' no longer matches its locked SHA-256.");
            result.Add(new ProviderRuntimeNativeAsset(target, resolved.Asset));
        }
        return result.ToArray();
    }

    private static string CreateProviderProjectReferences(
        IEnumerable<ProviderRuntimeAssembly> assemblies,
        IEnumerable<ProviderRuntimeNativeAsset> nativeAssets)
        => string.Join(Environment.NewLine,
            assemblies.Select(assembly =>
                    $"<Reference Include=\"{EscapeXml(assembly.Evidence.AssemblyName)}\"><HintPath>{EscapeXml(assembly.Path)}</HintPath><Private>true</Private></Reference>")
                .Concat(nativeAssets.Select(asset =>
                    $"<None Include=\"{EscapeXml(asset.Path)}\"><Link>{EscapeXml(asset.Evidence.FileName)}</Link><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory><CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory><ExcludeFromSingleFile>true</ExcludeFromSingleFile></None>")));

    private static PowerShellCompilationArtifactFile[] CopyProviderRuntimeAssemblies(
        PowerShellCompilationBuildSpec spec,
        string primaryPath,
        IEnumerable<ProviderRuntimeAssembly> assemblies)
    {
        if (spec.Kind == PowerShellCompilationArtifactKind.Executable && spec.SingleFile)
            return Array.Empty<PowerShellCompilationArtifactFile>();
        var targetDirectory = Path.GetDirectoryName(primaryPath)
            ?? throw new InvalidOperationException("Generated artifact has no output directory.");
        var result = new List<PowerShellCompilationArtifactFile>();
        foreach (var assembly in assemblies)
        {
            var target = Path.Combine(targetDirectory, assembly.Evidence.AssemblyName + ".dll");
            if (!File.Exists(target)) File.Copy(assembly.Path, target, overwrite: false);
            if (!ComputeSha256(target).Equals(assembly.Evidence.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Published provider runtime assembly '{target}' does not match its locked SHA-256.");
            result.Add(CreateArtifactFile(target, "CompilerProviderRuntime"));
        }
        return result.ToArray();
    }

    private static PowerShellCompilationArtifactFile[] CopyProviderRuntimeNativeAssets(
        string primaryPath,
        IEnumerable<ProviderRuntimeNativeAsset> nativeAssets)
    {
        var targetDirectory = Path.GetDirectoryName(primaryPath)
            ?? throw new InvalidOperationException("Generated artifact has no output directory.");
        var result = new List<PowerShellCompilationArtifactFile>();
        foreach (var asset in nativeAssets)
        {
            var target = Path.Combine(targetDirectory, asset.Evidence.FileName);
            if (!File.Exists(target)) File.Copy(asset.Path, target, overwrite: false);
            if (!ComputeSha256(target).Equals(asset.Evidence.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Published provider native asset '{target}' does not match its locked SHA-256.");
            result.Add(CreateArtifactFile(target, "CompilerProviderNativeRuntime"));
        }
        return result.ToArray();
    }

    private sealed class ProviderRuntimeAssembly
    {
        internal ProviderRuntimeAssembly(string path, PowerShellCompilationProviderAssembly evidence)
        {
            Path = path;
            Evidence = evidence;
        }

        internal string Path { get; }
        internal PowerShellCompilationProviderAssembly Evidence { get; }
    }

    private sealed class ProviderRuntimeNativeAsset
    {
        internal ProviderRuntimeNativeAsset(string path, PowerShellCompilationProviderNativeAsset evidence)
        {
            Path = path;
            Evidence = evidence;
        }

        internal string Path { get; }
        internal PowerShellCompilationProviderNativeAsset Evidence { get; }
    }
}
