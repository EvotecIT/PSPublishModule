using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void ValidateLocalPackageReference(
        string repositoryRoot,
        string projectDirectory,
        IReadOnlyCollection<string> packageLockPaths,
        PbxObject item,
        ISet<string> validatedPackageRoots)
    {
        var relativePath = ReadPbxScalar(item.Body, "relativePath");
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("Local Swift package reference is missing relativePath.");
        var packageRoot = ResolvePbxPath(projectDirectory, relativePath!, "local Swift package");
        EnsureDirectoryWithinRepository(repositoryRoot, packageRoot, "Xcode local Swift package");
        ValidateLocalPackageRoot(repositoryRoot, packageRoot, packageLockPaths, validatedPackageRoots);
    }

    private void ValidateLocalPackageRoot(
        string repositoryRoot,
        string packageRoot,
        IReadOnlyCollection<string> packageLockPaths,
        ISet<string> validatedPackageRoots)
    {
        packageRoot = Path.GetFullPath(packageRoot);
        EnsureDirectoryWithinRepository(repositoryRoot, packageRoot, "Xcode local Swift package");
        if (!validatedPackageRoots.Add(packageRoot))
            return;

        var manifestPaths = Directory.EnumerateFiles(packageRoot, "Package*.swift", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Equals("Package.swift", StringComparison.Ordinal) ||
                           Regex.IsMatch(
                               Path.GetFileName(path),
                               "^Package@swift-[0-9]+(?:\\.[0-9]+)*\\.swift$",
                               RegexOptions.CultureInvariant))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (!manifestPaths.Any(path => Path.GetFileName(path).Equals("Package.swift", StringComparison.Ordinal)))
            throw new FileNotFoundException($"Local Swift package manifest was not found: {Path.Combine(packageRoot, "Package.swift")}");
        foreach (var manifestPath in manifestPaths)
            EnsureTrackedFile(repositoryRoot, manifestPath, "Xcode local Swift package manifest");
        foreach (var conventionalInput in new[]
                 {
                     Path.Combine(packageRoot, "Package.resolved"),
                     Path.Combine(packageRoot, "Sources"),
                     Path.Combine(packageRoot, "Plugins")
                 })
        {
            if (File.Exists(conventionalInput))
                EnsureTrackedFile(repositoryRoot, conventionalInput, "Xcode local Swift package input");
            else if (Directory.Exists(conventionalInput))
                EnsureTrackedDirectoryTree(repositoryRoot, conventionalInput, "Xcode local Swift package input");
        }

        foreach (var manifestPath in manifestPaths)
            ValidateLocalPackageManifest(
                repositoryRoot,
                packageRoot,
                packageLockPaths,
                validatedPackageRoots,
                manifestPath);
    }

    private void ValidateLocalPackageManifest(
        string repositoryRoot,
        string packageRoot,
        IReadOnlyCollection<string> packageLockPaths,
        ISet<string> validatedPackageRoots,
        string manifestPath)
    {
        var manifestWithoutComments = RemoveSwiftComments(File.ReadAllText(manifestPath));
        EnsureNoExecutableSwiftStringInterpolation(packageRoot, manifestWithoutComments);
        var manifestSyntax = MaskSwiftStringLiterals(manifestWithoutComments);
        ValidateLocalPackageExecutableSafety(packageRoot, manifestSyntax);
        ValidateDirectSwiftPackageDependencyFactories(packageRoot, manifestSyntax);
        var dependencyCalls = ParseDirectSwiftPackageDependencyCalls(manifestWithoutComments, manifestSyntax);
        ValidateRemotePackageDependencies(repositoryRoot, packageRoot, packageLockPaths, dependencyCalls);
        ValidateNestedLocalPackageDependencies(
            repositoryRoot,
            packageRoot,
            packageLockPaths,
            validatedPackageRoots,
            dependencyCalls);
        ValidateLiteralSwiftPackagePaths(repositoryRoot, packageRoot, manifestWithoutComments);
    }

    private static void ValidateLocalPackageExecutableSafety(string packageRoot, string manifestSyntax)
    {
        if (ContainsSwiftIdentifier(manifestSyntax, "unsafeFlags"))
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' uses unsafeFlags, whose compiler and linker inputs cannot be proven at the exact source commit. " +
                "Replace unsafe flags with tracked package settings before creating an Apple checkpoint.");
        if (ContainsSwiftIdentifier(manifestSyntax, "systemLibrary"))
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' declares a systemLibrary target, whose pkg-config and host library inputs cannot be proven at the exact source commit. " +
                "Replace the system library dependency with tracked package sources before creating an Apple checkpoint.");
        if (ContainsSwiftIdentifier(manifestSyntax, "plugin") || ContainsSwiftMemberReference(manifestSyntax, "macro"))
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' declares or invokes a SwiftPM plugin or macro, whose executable runtime inputs cannot be proven at the exact source commit. " +
                "Replace build-tool plugins and macros with tracked deterministic build inputs before creating an Apple checkpoint.");
    }

    private void ValidateRemotePackageDependencies(
        string repositoryRoot,
        string packageRoot,
        IReadOnlyCollection<string> packageLockPaths,
        IEnumerable<SwiftPackageDependencyCall> dependencyCalls)
    {
        foreach (var dependency in dependencyCalls.Where(static call =>
                     call.Arguments.ContainsKey("url") || call.Arguments.ContainsKey("id")))
        {
            var identityArgument = dependency.Arguments.TryGetValue("url", out var url)
                ? url
                : dependency.Arguments["id"];
            if (!TryReadLiteralSwiftString(identityArgument, out var identity))
                throw new InvalidOperationException(
                    $"Local Swift package '{packageRoot}' declares a dynamic external dependency that cannot be bound to exact source. " +
                    "Use a literal package URL or registry identity and commit its Package.resolved lock.");

            var hasExactRevision = dependency.Arguments.TryGetValue("revision", out var revision) &&
                                   TryReadLiteralSwiftString(revision, out var revisionValue) &&
                                   Regex.IsMatch(revisionValue, "^[A-Fa-f0-9]{40}$", RegexOptions.CultureInvariant);
            if (!dependency.Arguments.ContainsKey("id") && hasExactRevision)
                continue;
            var locks = FindTrackedPackageLocks(
                packageLockPaths
                    .Concat(new[] { Path.Combine(packageRoot, "Package.resolved") })
                    .Distinct(GetPathComparer())
                    .ToArray(),
                identity);
            if (locks.Length == 0)
                throw new InvalidOperationException(
                    $"Local Swift package '{packageRoot}' declares external dependency '{identity}' without an exact 40-character revision. " +
                    "Commit a Package.resolved lock containing that dependency before creating an exact-source Apple checkpoint.");
            foreach (var packageLock in locks)
                EnsureTrackedFile(repositoryRoot, packageLock, "Xcode local Swift package resolution lock");
        }
    }

    private void ValidateNestedLocalPackageDependencies(
        string repositoryRoot,
        string packageRoot,
        IReadOnlyCollection<string> packageLockPaths,
        ISet<string> validatedPackageRoots,
        IEnumerable<SwiftPackageDependencyCall> dependencyCalls)
    {
        foreach (var dependency in dependencyCalls.Where(static call => call.Arguments.ContainsKey("path")))
        {
            if (!TryReadLiteralSwiftString(dependency.Arguments["path"], out var nestedPath))
                throw new InvalidOperationException(
                    $"Local Swift package '{packageRoot}' uses a computed, interpolated, or escaped package dependency path that cannot be bound to exact source. " +
                    "Use a simple literal path inside the tracked repository.");
            var nestedPackageRoot = ResolvePbxPath(packageRoot, nestedPath, "nested local Swift package");
            ValidateLocalPackageRoot(repositoryRoot, nestedPackageRoot, packageLockPaths, validatedPackageRoots);
        }
    }

    private void ValidateLiteralSwiftPackagePaths(string repositoryRoot, string packageRoot, string manifest)
    {
        var pathArguments = Regex.Matches(manifest, "(?:\\bpath\\b|`path`)\\s*:", RegexOptions.CultureInvariant);
        var literalPathArguments = Regex.Matches(
            manifest,
            "(?:\\bpath\\b|`path`)\\s*:\\s*\"(?<path>[^\"\\\\\\r\\n]+)\"\\s*(?=[,)])",
            RegexOptions.CultureInvariant);
        if (pathArguments.Count != literalPathArguments.Count)
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' uses a computed, interpolated, or escaped path argument that cannot be bound to exact source. " +
                "Use a simple literal path inside the tracked repository.");
        foreach (Match match in literalPathArguments)
        {
            var explicitPath = ResolvePbxPath(packageRoot, match.Groups["path"].Value, "Swift package manifest input");
            EnsurePathWithinRepository(repositoryRoot, explicitPath, "Swift package manifest input");
            if (File.Exists(explicitPath))
                EnsureTrackedFile(repositoryRoot, explicitPath, "Swift package manifest input");
            else if (Directory.Exists(explicitPath))
                EnsureTrackedDirectoryTree(repositoryRoot, explicitPath, "Swift package manifest input");
            else
                throw new FileNotFoundException($"Swift package manifest input was not found: {explicitPath}", explicitPath);
        }
    }
}
