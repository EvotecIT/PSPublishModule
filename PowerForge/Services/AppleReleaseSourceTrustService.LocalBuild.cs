namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    internal IReadOnlyDictionary<string, string> ReadApprovedLocalBuildPackageRevisions(
        string repositoryRoot,
        string projectPath)
    {
        lock (_validationGate)
        {
            ResetValidationState();
            try
            {
                var root = Path.GetFullPath(repositoryRoot);
                var configuredProjectPath = Path.GetFullPath(projectPath);
                EnsurePathWithinRepository(
                    root,
                    configuredProjectPath,
                    "Local Apple ProjectPath");

                var metadataPath = ResolveLocalBuildMetadataPath(configuredProjectPath);
                EnsureTrackedFile(
                    root,
                    metadataPath,
                    "Local Apple project metadata");
                var metadataPaths = new HashSet<string>(GetPathComparer())
                {
                    metadataPath
                };
                AddReferencedWorkspaceProjects(root, metadataPaths);
                AddReferencedXcodeProjects(
                    root,
                    metadataPaths,
                    Array.Empty<string>());

                var packageLocks = new HashSet<string>(GetPathComparer());
                var localPackageRoots = new HashSet<string>(GetPathComparer());
                foreach (var projectMetadata in metadataPaths.Where(path =>
                             path.EndsWith(
                                 "project.pbxproj",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    packageLocks.UnionWith(ResolveEffectivePackageLockPaths(
                        projectMetadata,
                        metadataPaths));
                    var projectDirectory = Path.GetDirectoryName(
                        Path.GetDirectoryName(projectMetadata)!)!;
                    var objects = ParsePbxObjects(File.ReadAllText(projectMetadata));
                    foreach (var localPackage in objects.Values.Where(value =>
                                 value.Isa.Equals(
                                     "XCLocalSwiftPackageReference",
                                     StringComparison.OrdinalIgnoreCase)))
                    {
                        var relativePath = ReadPbxScalar(
                            localPackage.Body,
                            "relativePath");
                        if (string.IsNullOrWhiteSpace(relativePath))
                        {
                            throw new InvalidOperationException(
                                "Local Swift package reference is missing relativePath.");
                        }
                        var packageRoot = ResolvePbxPath(
                            projectDirectory,
                            relativePath!,
                            "local Swift package");
                        AddLocalPackageGraphLocks(
                            root,
                            packageRoot,
                            packageLocks,
                            localPackageRoots);
                    }
                }
                var revisions = ReadApprovedTrackedPackageRevisions(root, packageLocks);
                ValidatePendingGitFilters();
                return revisions;
            }
            finally
            {
                ResetValidationState();
            }
        }
    }

    internal void ValidateLocalBuildInputContainment(
        string repositoryRoot,
        string projectPath,
        string scheme)
    {
        lock (_validationGate)
        {
            ResetValidationState();
            try
            {
                var root = Path.GetFullPath(repositoryRoot);
                var configuredProjectPath = Path.GetFullPath(projectPath);
                EnsurePathWithinRepository(
                    root,
                    configuredProjectPath,
                    "Local Apple ProjectPath");

                var metadataPath = ResolveLocalBuildMetadataPath(
                    configuredProjectPath);

                EnsureTrackedFile(
                    root,
                    metadataPath,
                    "Local Apple project metadata");
                var metadataPaths = new HashSet<string>(GetPathComparer())
                {
                    metadataPath
                };
                AddReferencedWorkspaceProjects(root, metadataPaths);
                AddReferencedXcodeProjects(
                    root,
                    metadataPaths,
                    Array.Empty<string>());

                EnsureTrackedSharedScheme(
                    root,
                    root,
                    new AppleAppConfiguration
                    {
                        Name = scheme,
                        ProjectPath = configuredProjectPath,
                        Scheme = scheme
                    },
                    metadataPaths);

                foreach (var projectMetadata in metadataPaths.Where(path =>
                             path.EndsWith(
                                 "project.pbxproj",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    ValidateWholeProjectGraph(
                        root,
                        projectMetadata,
                        metadataPaths,
                        Array.Empty<string>());
                }
                ValidatePendingGitFilters();
            }
            finally
            {
                ResetValidationState();
            }
        }
    }

    private static string ResolveLocalBuildMetadataPath(string configuredProjectPath)
    {
        if (File.Exists(configuredProjectPath))
            return configuredProjectPath;
        return configuredProjectPath.EndsWith(
            ".xcworkspace",
            StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(configuredProjectPath, "contents.xcworkspacedata")
            : Path.Combine(configuredProjectPath, "project.pbxproj");
    }

    private void AddLocalPackageGraphLocks(
        string repositoryRoot,
        string packageRoot,
        ISet<string> packageLocks,
        ISet<string> visitedPackageRoots)
    {
        packageRoot = Path.GetFullPath(packageRoot);
        EnsureDirectoryWithinRepository(
            repositoryRoot,
            packageRoot,
            "Xcode local Swift package");
        if (!visitedPackageRoots.Add(packageRoot))
            return;

        var packageLock = Path.Combine(packageRoot, "Package.resolved");
        if (File.Exists(packageLock))
            packageLocks.Add(packageLock);
        var manifests = Directory
            .EnumerateFiles(packageRoot, "Package*.swift", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Equals(
                               "Package.swift",
                               StringComparison.Ordinal) ||
                           System.Text.RegularExpressions.Regex.IsMatch(
                               Path.GetFileName(path),
                               "^Package@swift-[0-9]+(?:\\.[0-9]+)*\\.swift$",
                               System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (!manifests.Any(path => Path.GetFileName(path).Equals(
                "Package.swift",
                StringComparison.Ordinal)))
        {
            throw new FileNotFoundException(
                $"Local Swift package manifest was not found: {Path.Combine(packageRoot, "Package.swift")}");
        }

        foreach (var manifest in manifests)
        {
            var source = RemoveSwiftComments(File.ReadAllText(manifest));
            var syntax = MaskSwiftStringLiterals(source);
            foreach (var dependency in ParseDirectSwiftPackageDependencyCalls(
                         source,
                         syntax).Where(call => call.Arguments.ContainsKey("path")))
            {
                if (!TryReadLiteralSwiftString(
                        dependency.Arguments["path"],
                        out var nestedPath))
                {
                    throw new InvalidOperationException(
                        $"Local Swift package '{packageRoot}' uses a computed local dependency path.");
                }
                AddLocalPackageGraphLocks(
                    repositoryRoot,
                    ResolvePbxPath(
                        packageRoot,
                        nestedPath,
                        "nested local Swift package"),
                    packageLocks,
                    visitedPackageRoots);
            }
        }
    }

    private static HashSet<string> ResolvePbxReferences(
        IReadOnlyDictionary<string, PbxObject> objects,
        string isa,
        string key)
        => objects.Values
            .Where(value => value.Isa.Equals(
                isa,
                StringComparison.OrdinalIgnoreCase))
            .Select(value => ReadPbxScalar(value.Body, key))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
