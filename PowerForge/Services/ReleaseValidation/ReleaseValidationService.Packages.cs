using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using System.IO.Compression;

namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private sealed class PackageInspection
    {
        internal string Path { get; set; } = string.Empty;
        internal string Id { get; set; } = string.Empty;
        internal string Version { get; set; } = string.Empty;
        internal string[] Files { get; set; } = Array.Empty<string>();
        internal PackageDependencyGroup[] Dependencies { get; set; } = Array.Empty<PackageDependencyGroup>();
    }

    private static PackageInspection InspectPackage(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemPathSafety.RequireRegularFile(path);
        using var input = new ArchiveMetadataReadStream(File.OpenRead(path), cancellationToken);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        // The IO budget and cancellation apply while ZipArchive builds Entries, not
        // only after its complete central-directory inventory has been allocated.
        if (archive.Entries.Count > PowerForgeReleaseArtifactVerifier.MaxArchiveEntries)
            throw new InvalidDataException($"Package archive exceeds the {PowerForgeReleaseArtifactVerifier.MaxArchiveEntries} entry limit.");
        using var reader = new PackageArchiveReader(archive);
        using var nuspec = reader.GetNuspec();
        using var metadata = new MemoryStream();
        PowerForgeReleaseArtifactVerifier.CopyBounded(nuspec, metadata,
            PowerForgeReleaseArtifactVerifier.MaxArchiveMetadataBytes, "Package nuspec", cancellationToken);
        metadata.Position = 0;
        var manifest = new NuspecReader(metadata);
        var id = manifest.GetId();
        if (!PackageIdValidator.IsValidPackageId(id))
            throw new InvalidDataException($"Package ID '{id}' is invalid.");
        var package = new PackageInspection
        {
            Path = path, Id = id, Version = manifest.GetVersion().ToNormalizedString(),
            Files = reader.GetFiles().ToArray(), Dependencies = manifest.GetDependencyGroups().ToArray()
        };
        cancellationToken.ThrowIfCancellationRequested();
        return package;
    }

    private static IEnumerable<PackageInspection> InspectPrimaryPackages(string root, CancellationToken cancellationToken)
        => InspectPackages(root, symbols: false, cancellationToken);

    // Flat-folder feeds need NuGet's canonical identity filename, not a caller's renamed artifact filename.
    private static string PackageFeedPath(string feed, PackageInspection package)
        => Within(feed, new PackagePathResolver(feed).GetPackageFileName(
            new NuGet.Packaging.Core.PackageIdentity(package.Id, NuGetVersion.Parse(package.Version))));

    private static IEnumerable<PackageInspection> InspectPackages(string root, bool symbols, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemPathSafety.RejectReparsePoints(root, root, "Package directory");
        foreach (var path in Directory.EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbols ? path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase) :
                path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            {
                FileSystemPathSafety.RejectReparsePoints(path, root, "Package input");
                yield return InspectPackage(path, cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<Dictionary<string, PackageInspection>> ValidatePackagesAsync(PackageSetValidation spec,
        Dictionary<string, string> variables, ReleaseValidationReport report, CancellationToken cancellationToken)
    {
        if (spec.Items.Length == 0) throw new InvalidOperationException("Package validation requires package contracts.");
        var root = Resolve(spec.Path, variables);
        variables["PackageRoot"] = root;
        var actual = InspectPrimaryPackages(root, cancellationToken).ToArray();
        var symbolPackages = spec.Items.Any(contract => contract.SymbolEntries.Length > 0 || contract.ForbiddenSymbolEntries.Length > 0)
            ? InspectPackages(root, symbols: true, cancellationToken).ToArray() : Array.Empty<PackageInspection>();
        var selected = new Dictionary<string, PackageInspection>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in spec.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(contract.Id) || selected.ContainsKey(contract.Id))
                throw new InvalidOperationException($"Package identity '{contract.Id}' is empty or duplicated in the contract.");
            var matches = actual.Where(p => string.Equals(p.Id, contract.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"Expected exactly one '{contract.Id}' package; found {matches.Length}.");
            var package = matches[0];
            selected.Add(package.Id, package);
            if (spec.SameVersion && string.IsNullOrWhiteSpace(report.Version)) report.Version = package.Version;
            if (spec.SameVersion && NuGetVersion.Parse(report.Version) != NuGetVersion.Parse(package.Version))
                throw new InvalidOperationException($"{package.Id} version {package.Version} does not match {report.Version}.");
            variables["Version"] = report.Version;
            CheckEntries(package.Id, package.Files, contract.RequiredEntries, contract.ForbiddenEntries);
            ValidateDependencies(package, contract);
            if (contract.SymbolEntries.Length > 0 || contract.ForbiddenSymbolEntries.Length > 0)
            {
                var symbols = FindSymbolPackage(package, symbolPackages, cancellationToken);
                CheckEntries(package.Id + " symbols", symbols.Files, contract.SymbolEntries, contract.ForbiddenSymbolEntries);
            }
            if (spec.VerifySignatures || spec.RequireAuthorSignature || spec.AuthorCertificateFingerprints.Length > 0)
            {
                var verification = await new DotNetNuGetClient(_processRunner).VerifyPackageAsync(package.Path,
                    spec.RequireAuthorSignature, spec.AuthorCertificateFingerprints, cancellationToken).ConfigureAwait(false);
                if (!verification.Succeeded)
                    throw new InvalidOperationException($"NuGet signature verification failed for {package.Id}.\n{verification.StdErr}\n{verification.StdOut}");
            }
            report.Checks.Add($"Package {package.Id} {package.Version}");
        }
        if (spec.ExactSet && actual.Length != selected.Count)
            throw new InvalidOperationException("The package directory contains undeclared packages: " +
                string.Join(", ", actual.Where(p => !selected.ContainsKey(p.Id)).Select(p => p.Id)));
        return selected;
    }

    private static PackageInspection FindSymbolPackage(PackageInspection package, PackageInspection[] symbols, CancellationToken cancellationToken)
    {
        var version = NuGetVersion.Parse(package.Version);
        PackageInspection? match = null;
        foreach (var candidate in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(candidate.Id, package.Id, StringComparison.OrdinalIgnoreCase) ||
                NuGetVersion.Parse(candidate.Version) != version) continue;
            if (match is not null) throw new InvalidOperationException($"Symbol package identity '{package.Id}/{package.Version}' is ambiguous.");
            match = candidate;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return match ?? throw new InvalidOperationException($"Symbol package identity '{package.Id}/{package.Version}' is missing.");
    }

    private static void CheckEntries(string name, string[] files, string[] required, string[] forbidden)
    {
        foreach (var pattern in required)
            if (!files.Any(file => Matches(file, pattern))) throw new InvalidOperationException($"{name} is missing '{pattern}'.");
        foreach (var pattern in forbidden)
            if (files.Any(file => Matches(file, pattern))) throw new InvalidOperationException($"{name} must not contain '{pattern}'.");
    }

    private static void ValidateDependencies(PackageInspection package, PackageArtifactContract contract)
    {
        if (contract.DependencyFrameworks.Length > 0)
        {
            var expected = contract.DependencyFrameworks.Select(NuGetFramework.Parse).Select(f => f.GetShortFolderName()).OrderBy(f => f).ToArray();
            var actual = package.Dependencies.Select(g => g.TargetFramework.GetShortFolderName()).OrderBy(f => f).ToArray();
            if (!expected.SequenceEqual(actual)) throw new InvalidOperationException($"{package.Id} dependency frameworks differ from the contract.");
        }
        if ((contract.RequiredDependencies.Length > 0 || contract.RuntimeOnlyDependencies.Length > 0) && package.Dependencies.Length == 0)
            throw new InvalidOperationException($"{package.Id} has no required dependency groups.");
        foreach (var group in package.Dependencies)
        {
            var dependencies = group.Packages.ToArray();
            foreach (var id in contract.RequiredDependencies)
                if (!dependencies.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"{package.Id}/{group.TargetFramework}: missing dependency '{id}'.");
            foreach (var id in contract.ForbiddenDependencies)
                if (dependencies.Any(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"{package.Id}/{group.TargetFramework}: forbidden dependency '{id}'.");
            foreach (var id in contract.RuntimeOnlyDependencies)
            {
                var dependency = dependencies.SingleOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"{package.Id}/{group.TargetFramework}: missing runtime-only dependency '{id}'.");
                var included = dependency.Include ?? Array.Empty<string>();
                var excluded = dependency.Exclude ?? Array.Empty<string>();
                if (!excluded.Contains("Compile", StringComparer.OrdinalIgnoreCase) && !excluded.Contains("All", StringComparer.OrdinalIgnoreCase) &&
                    (included.Count == 0 || included.Contains("Compile", StringComparer.OrdinalIgnoreCase) || included.Contains("All", StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"{package.Id}/{group.TargetFramework}: '{id}' exposes compile assets.");
            }
        }
    }
}
