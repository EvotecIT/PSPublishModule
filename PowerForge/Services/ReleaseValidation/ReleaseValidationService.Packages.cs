using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;

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
        using var reader = new PackageArchiveReader(path);
        var identity = reader.GetIdentity();
        var package = new PackageInspection
        {
            Path = path, Id = identity.Id, Version = identity.Version.ToNormalizedString(),
            Files = reader.GetFiles().ToArray(), Dependencies = reader.GetPackageDependencies().ToArray()
        };
        cancellationToken.ThrowIfCancellationRequested();
        return package;
    }

    private static IEnumerable<PackageInspection> InspectPrimaryPackages(string root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemPathSafety.RejectReparsePoints(root, root, "Package directory");
        foreach (var path in Directory.EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
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
            if (string.IsNullOrWhiteSpace(report.Version)) report.Version = package.Version;
            if (spec.SameVersion && NuGetVersion.Parse(report.Version) != NuGetVersion.Parse(package.Version))
                throw new InvalidOperationException($"{package.Id} version {package.Version} does not match {report.Version}.");
            variables["Version"] = report.Version;
            CheckEntries(package.Id, package.Files, contract.RequiredEntries, contract.ForbiddenEntries);
            ValidateDependencies(package, contract);
            if (contract.SymbolEntries.Length > 0 || contract.ForbiddenSymbolEntries.Length > 0)
            {
                var symbols = InspectPackage(FindSymbolPackage(package.Path, cancellationToken), cancellationToken);
                if (!string.Equals(symbols.Id, package.Id, StringComparison.OrdinalIgnoreCase) ||
                    NuGetVersion.Parse(symbols.Version) != NuGetVersion.Parse(package.Version))
                    throw new InvalidOperationException($"Symbol package identity does not match '{package.Id}'.");
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

    private static string FindSymbolPackage(string packagePath, CancellationToken cancellationToken)
    {
        var root = Path.GetDirectoryName(packagePath)!;
        var expected = Path.GetFileNameWithoutExtension(packagePath) + ".snupkg";
        string? match = null;
        foreach (var path in Directory.EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetFileName(path), expected, StringComparison.OrdinalIgnoreCase)) continue;
            if (match is not null) throw new InvalidOperationException($"Symbol package '{expected}' is ambiguous.");
            FileSystemPathSafety.RejectReparsePoints(path, root, "Symbol package input");
            match = path;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return match ?? throw new InvalidOperationException($"Symbol package '{expected}' is missing.");
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
