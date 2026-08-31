using System.IO.Compression;
using System.Reflection.PortableExecutable;
using NuGet.Packaging;

namespace PowerForge;

public sealed partial class PowerShellCompilationProviderPackageReader
{
    /// <summary>Creates exact native-asset evidence without loading or executing the asset.</summary>
    public static PowerShellCompilationProviderNativeAsset InspectNativeAsset(
        string assetPath,
        string packageRelativePath,
        string runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) throw new ArgumentException("A native-asset path is required.", nameof(assetPath));
        if (!File.Exists(assetPath)) throw new FileNotFoundException("Provider native asset was not found.", assetPath);
        var normalizedPackagePath = NormalizePath(packageRelativePath);
        if (normalizedPackagePath.Length == 0 || normalizedPackagePath.Contains("../", StringComparison.Ordinal))
            throw new ArgumentException("A safe package-relative native-asset path is required.", nameof(packageRelativePath));
        var normalizedRid = (runtimeIdentifier ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedRid.Length == 0 || !System.Text.RegularExpressions.Regex.IsMatch(normalizedRid, "^[a-z0-9]+(?:[.-][a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException("A canonical runtime identifier is required for a provider native asset.", nameof(runtimeIdentifier));
        var fileName = Path.GetFileName(normalizedPackagePath);
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A provider native asset requires a file name.", nameof(packageRelativePath));
        var bytes = File.ReadAllBytes(assetPath);
        if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (reader.HasMetadata) throw new InvalidOperationException($"Provider native asset '{normalizedPackagePath}' contains managed metadata and must be declared as an assembly.");
        }
        var inspection = PowerShellNativeExecutableInspector.Inspect(bytes, normalizedRid, normalizedPackagePath);
        return new PowerShellCompilationProviderNativeAsset
        {
            Path = normalizedPackagePath,
            Sha256 = Hash(bytes),
            RuntimeIdentifier = normalizedRid,
            FileName = fileName,
            Format = inspection.Format,
            Architecture = inspection.Architecture,
            ImportedLibraries = inspection.ImportedLibraries
        };
    }

    private static PowerShellCompilationProviderNativeAsset[] ValidateNativeAssets(
        PackageArchiveReader packageReader,
        IReadOnlyCollection<string> files,
        PowerShellCompilationProviderPackageManifest manifest,
        string packagePath,
        string? runtimeIdentifier)
    {
        var selectedRid = (runtimeIdentifier ?? string.Empty).Trim().ToLowerInvariant();
        var result = new List<PowerShellCompilationProviderNativeAsset>();
        foreach (var declared in (manifest.NativeAssets ?? Array.Empty<PowerShellCompilationProviderNativeAsset>())
                     .OrderBy(static asset => asset.Path, StringComparer.Ordinal))
        {
            var path = NormalizePath(declared.Path);
            if (path.Length == 0 || path.StartsWith("/", StringComparison.Ordinal) || path.Contains("../", StringComparison.Ordinal))
                throw new InvalidOperationException($"Provider package '{packagePath}' contains unsafe native-asset path '{declared.Path}'.");
            if (!files.Contains(path, StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider package '{packagePath}' is missing declared native asset '{path}'.");
            if (!Path.GetFileName(path).Equals(declared.FileName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Provider native asset '{path}' file name does not match its manifest.");
            byte[] bytes;
            using (var source = packageReader.GetStream(path))
            using (var memory = new MemoryStream())
            {
                source.CopyTo(memory);
                bytes = memory.ToArray();
            }
            var sha256 = Hash(bytes);
            if (!string.Equals(sha256, declared.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Provider native asset '{path}' SHA-256 does not match its manifest.");
            if (declared.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
                if (reader.HasMetadata)
                    throw new InvalidOperationException($"Provider native asset '{path}' contains managed metadata.");
            }
            var inspection = PowerShellNativeExecutableInspector.Inspect(bytes, declared.RuntimeIdentifier, path);
            if (!inspection.Format.Equals(declared.Format, StringComparison.Ordinal) ||
                !inspection.Architecture.Equals(declared.Architecture, StringComparison.Ordinal) ||
                !inspection.ImportedLibraries.SequenceEqual(declared.ImportedLibraries ?? Array.Empty<string>(), StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider native asset '{path}' inspected format, architecture, or import closure does not match its manifest.");
            var validated = new PowerShellCompilationProviderNativeAsset
            {
                Path = path,
                Sha256 = sha256,
                RuntimeIdentifier = declared.RuntimeIdentifier,
                FileName = declared.FileName,
                Format = inspection.Format,
                Architecture = inspection.Architecture,
                ImportedLibraries = inspection.ImportedLibraries
            };
            if (declared.RuntimeIdentifier.Equals(selectedRid, StringComparison.Ordinal)) result.Add(validated);
        }
        EnsureNativeImportClosure(manifest.NativeAssets ?? Array.Empty<PowerShellCompilationProviderNativeAsset>(), packagePath);
        return result.ToArray();
    }

    internal static void EnsureNativeImportClosure(
        IReadOnlyCollection<PowerShellCompilationProviderNativeAsset> nativeAssets,
        string packagePath)
    {
        foreach (var asset in nativeAssets)
        foreach (var import in asset.ImportedLibraries ?? Array.Empty<string>())
        {
            var delivered = nativeAssets.Any(candidate =>
                candidate.RuntimeIdentifier.Equals(asset.RuntimeIdentifier, StringComparison.Ordinal) &&
                PowerShellNativeLibraryName.CanResolve(asset.RuntimeIdentifier, import, candidate.FileName));
            if (delivered || PowerShellTargetNativeAbiCatalog.Contains(asset.RuntimeIdentifier, import)) continue;
            throw new InvalidOperationException(
                $"Provider package '{packagePath}' native asset '{asset.Path}' imports '{import}', which is neither another exact asset for '{asset.RuntimeIdentifier}' nor part of that target operating-system ABI.");
        }
    }
}
