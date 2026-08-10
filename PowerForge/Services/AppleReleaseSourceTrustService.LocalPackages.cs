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
        ValidateLiteralSwiftPackagePaths(repositoryRoot, packageRoot, manifestWithoutComments, manifestSyntax);
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
        ValidateDeclarativeSwiftPackageManifest(packageRoot, manifestSyntax);
    }

    private static void ValidateDeclarativeSwiftPackageManifest(string packageRoot, string manifestSyntax)
    {
        foreach (Match import in Regex.Matches(
                     manifestSyntax,
                     "(?m)^\\s*(?:@[A-Za-z_][A-Za-z0-9_]*(?:\\([^\\r\\n]*\\))?\\s+)*" +
                     "import\\s+(?:(?:class|struct|enum|protocol|typealias|func|var|let)\\s+)?" +
                     "(?<module>[A-Za-z_][A-Za-z0-9_]*)(?:\\.[^\\r\\n]+)?\\s*$",
                     RegexOptions.CultureInvariant))
        {
            var module = import.Groups["module"].Value;
            if (module.Equals("PackageDescription", StringComparison.Ordinal))
                continue;
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' imports '{module}', which permits host-dependent manifest execution. " +
                "Use a declarative PackageDescription-only manifest before creating an exact-source Apple checkpoint.");
        }

        var hostStateIdentifiers = new[]
        {
            "ProcessInfo", "NSProcessInfo", "FileManager", "CommandLine", "UserDefaults",
            "Date", "Calendar", "TimeZone", "Locale", "Bundle", "Process", "UUID",
            "Context", "System", "Clock", "ContinuousClock", "SuspendingClock", "DispatchTime",
            "getenv", "readLine", "arc4random", "SecRandomCopyBytes", "CFAbsoluteTimeGetCurrent"
        };
        var hostState = hostStateIdentifiers.FirstOrDefault(identifier =>
            ContainsSwiftIdentifier(manifestSyntax, identifier));
        if (hostState is not null)
        {
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' reads host state through '{hostState}', so its executed manifest cannot be bound to the exact source commit. " +
                "Use literal PackageDescription declarations before creating an exact-source Apple checkpoint.");
        }

        var executableControlFlow = Regex.Match(
            manifestSyntax,
            "(?<![A-Za-z0-9_])(?<keyword>if|else|switch|case|for|while|repeat|guard|do|try|catch|throw|defer|return)(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);
        if (executableControlFlow.Success)
        {
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' uses executable manifest control flow '{executableControlFlow.Groups["keyword"].Value}', which cannot be proven independent of host state. " +
                "Use declarative PackageDescription declarations before creating an exact-source Apple checkpoint.");
        }
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

    private void ValidateLiteralSwiftPackagePaths(
        string repositoryRoot,
        string packageRoot,
        string manifest,
        string manifestSyntax)
    {
        var pathBearingFactories = new HashSet<string>(StringComparer.Ordinal)
        {
            "package", "target", "executableTarget", "testTarget", "binaryTarget",
            "systemLibrary", "plugin", "macro"
        };
        foreach (Match reference in Regex.Matches(
                     manifestSyntax,
                     "\\.\\s*(?<name>[A-Za-z_][A-Za-z0-9_]*|`[A-Za-z_][A-Za-z0-9_]*`)\\s*\\(",
                     RegexOptions.CultureInvariant))
        {
            var factory = reference.Groups["name"].Value.Trim('`');
            if (!pathBearingFactories.Contains(factory))
                continue;
            var openingParenthesis = reference.Index + reference.Length - 1;
            var closingParenthesis = FindMatchingSwiftDelimiter(manifestSyntax, openingParenthesis, '(', ')');
            var arguments = ParseTopLevelSwiftArguments(
                manifest.Substring(openingParenthesis + 1, closingParenthesis - openingParenthesis - 1),
                manifestSyntax.Substring(openingParenthesis + 1, closingParenthesis - openingParenthesis - 1));
            if (factory.Equals("binaryTarget", StringComparison.Ordinal) && !arguments.ContainsKey("path"))
            {
                if (!arguments.TryGetValue("url", out var urlArgument) ||
                    !arguments.TryGetValue("checksum", out var checksumArgument) ||
                    !TryReadLiteralSwiftString(urlArgument, out _) ||
                    !TryReadLiteralSwiftString(checksumArgument, out var checksum) ||
                    !Regex.IsMatch(checksum, "^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
                {
                    throw new InvalidOperationException(
                        $"Local Swift package '{packageRoot}' declares a remote binary target without a literal URL and literal 64-character SHA-256 checksum. " +
                        "Bind every remote binary target to immutable, integrity-checked bytes before creating an exact-source Apple checkpoint.");
                }
            }
            if (!arguments.TryGetValue("path", out var pathArgument))
                continue;
            if (!TryReadLiteralSwiftString(pathArgument, out var literalPath))
                throw new InvalidOperationException(
                    $"Local Swift package '{packageRoot}' uses a computed, interpolated, or escaped path argument that cannot be bound to exact source. " +
                    "Use a simple literal path inside the tracked repository.");

            var explicitPath = ResolvePbxPath(packageRoot, literalPath, "Swift package manifest input");
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
