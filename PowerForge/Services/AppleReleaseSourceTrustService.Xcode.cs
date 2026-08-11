using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static readonly HashSet<string> ExternalXcodeSourceTrees = new(StringComparer.OrdinalIgnoreCase)
    {
        "BUILT_PRODUCTS_DIR", "SDKROOT", "DEVELOPER_DIR"
    };

    private static readonly HashSet<string> FileValuedBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "INFOPLIST_FILE",
        "INFOPLIST_PREFIX_HEADER",
        "CODE_SIGN_ENTITLEMENTS",
        "SWIFT_OBJC_BRIDGING_HEADER",
        "GCC_PREFIX_HEADER",
        "CLANG_PREFIX_HEADER",
        "MODULEMAP_FILE",
        "DEVELOPMENT_ASSET_PATHS"
    };

    private static readonly HashSet<string> SearchPathBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "HEADER_SEARCH_PATHS",
        "USER_HEADER_SEARCH_PATHS",
        "SYSTEM_HEADER_SEARCH_PATHS",
        "FRAMEWORK_SEARCH_PATHS",
        "LIBRARY_SEARCH_PATHS",
        "SWIFT_INCLUDE_PATHS"
    };

    private static readonly HashSet<string> FlagBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "OTHER_CFLAGS",
        "OTHER_CPLUSPLUSFLAGS",
        "OTHER_LDFLAGS",
        "OTHER_SWIFT_FLAGS",
        "INFOPLIST_OTHER_PREPROCESSOR_FLAGS"
    };

    private static readonly HashSet<string> DefinitionBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "GCC_PREPROCESSOR_DEFINITIONS",
        "GCC_PREPROCESSOR_DEFINITIONS_NOT_USED_IN_PRECOMPS",
        "INFOPLIST_PREPROCESSOR_DEFINITIONS",
        "SWIFT_ACTIVE_COMPILATION_CONDITIONS"
    };

    private static readonly HashSet<string> SourceSelectionBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "EXCLUDED_SOURCE_FILE_NAMES",
        "INCLUDED_SOURCE_FILE_NAMES",
        "EXCLUDED_RECURSIVE_SEARCH_PATH_SUBDIRECTORIES",
        "INCLUDED_RECURSIVE_SEARCH_PATH_SUBDIRECTORIES"
    };

    private static readonly HashSet<string> ExecutableBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACTOOL", "AR", "AS", "BITCODE_STRIP", "CC", "CHMOD", "CHOWN", "CODE_SIGN", "CODESIGN_ALLOCATE",
        "COPYSTRINGS", "COREML_COMPILER", "CPLUSPLUS", "DITTO", "DSYMUTIL", "IBTOOL", "INSTALL_NAME_TOOL",
        "INTENTS_COMPILER", "LD", "LDPLUSPLUS", "LEX", "LIBTOOL", "LIPO", "MAPC", "MIG", "MOMC",
        "MTL_COMPILER", "NM", "OTOOL", "PLUTIL", "PRODUCT_PACKAGING_UTILITY", "RANLIB", "RESMERGER", "REZ",
        "SEGEDIT", "STRIP", "SWIFT_DRIVER_SWIFT_EXEC", "SWIFT_EXEC", "TAPI", "TOUCH", "UNZIP", "YACC"
    };

    private static readonly HashSet<string> SdkSelectionBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "SDKROOT"
    };

    private void ValidateXcodeBuildGraph(
        string repositoryRoot,
        string projectRoot,
        IReadOnlyCollection<AppleAppConfiguration> apps,
        IReadOnlyCollection<string> metadataPaths,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        foreach (var app in apps.Where(static value => value.Enabled && !string.IsNullOrWhiteSpace(value.ProjectPath)))
            EnsureTrackedSharedScheme(repositoryRoot, projectRoot, app, metadataPaths);

        foreach (var metadataPath in metadataPaths.Where(path =>
                     path.EndsWith("project.pbxproj", StringComparison.OrdinalIgnoreCase) && File.Exists(path)))
        {
            ValidateProjectGraph(repositoryRoot, metadataPath, metadataPaths, generatedOutputPaths);
        }
    }

    private void AddReferencedXcodeProjects(
        string repositoryRoot,
        HashSet<string> metadataPaths,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        var pending = new Queue<string>(metadataPaths.Where(path =>
            path.EndsWith("project.pbxproj", StringComparison.OrdinalIgnoreCase)));
        var inspected = new HashSet<string>(GetPathComparer());
        while (pending.Count > 0)
        {
            var metadataPath = Path.GetFullPath(pending.Dequeue());
            if (!inspected.Add(metadataPath))
                continue;

            var projectDirectory = Path.GetDirectoryName(Path.GetDirectoryName(metadataPath)!)!;
            var objects = ParsePbxObjects(File.ReadAllText(metadataPath));
            var parents = BuildPbxParentMap(objects);
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in objects.Values.Where(static value =>
                         value.Isa.Equals("PBXFileReference", StringComparison.OrdinalIgnoreCase) &&
                         (value.Path ?? string.Empty).EndsWith(".xcodeproj", StringComparison.OrdinalIgnoreCase)))
            {
                var projectPath = ResolvePbxObjectPath(
                    projectDirectory,
                    item.Id,
                    objects,
                    parents,
                    cache,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (projectPath is null)
                {
                    throw new InvalidOperationException(
                        $"Referenced Xcode subproject uses an external source tree and cannot be attested: {metadataPath}");
                }

                EnsurePathWithinRepository(repositoryRoot, projectPath, "Xcode referenced subproject");
                EnsureNoGeneratedOutputOverlap(projectPath, generatedOutputPaths, "Xcode referenced subproject");
                EnsureNoLinkedTraversal(repositoryRoot, projectPath, "Xcode referenced subproject");
                var referencedMetadata = Path.Combine(projectPath, "project.pbxproj");
                EnsureTrackedFile(repositoryRoot, referencedMetadata, "Xcode referenced subproject metadata");
                if (metadataPaths.Add(referencedMetadata))
                    pending.Enqueue(referencedMetadata);
            }
        }
    }

    private void EnsureTrackedSharedScheme(
        string repositoryRoot,
        string projectRoot,
        AppleAppConfiguration app,
        IReadOnlyCollection<string> metadataPaths)
    {
        if (string.IsNullOrWhiteSpace(app.Scheme))
            throw new InvalidOperationException($"Apple app '{app.Name}' requires a shared Xcode scheme for an exact-source checkpoint.");

        var scheme = app.Scheme!.Trim();
        if (!Path.GetFileName(scheme).Equals(scheme, StringComparison.Ordinal) ||
            scheme.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
            throw new InvalidOperationException($"Apple app '{app.Name}' scheme must be a simple shared scheme name: {scheme}");

        var configuredContainer = ResolvePath(projectRoot, app.ProjectPath!);
        var containers = new HashSet<string>(GetPathComparer()) { configuredContainer };
        if (configuredContainer.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var metadataPath in metadataPaths)
            {
                if (metadataPath.EndsWith("project.pbxproj", StringComparison.OrdinalIgnoreCase))
                    containers.Add(Path.GetDirectoryName(metadataPath)!);
            }
        }

        var candidates = containers
            .Select(container => Path.Combine(container, "xcshareddata", "xcschemes", scheme + ".xcscheme"))
            .Where(File.Exists)
            .Distinct(GetPathComparer())
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"Apple app '{app.Name}' scheme '{scheme}' must exist as tracked shared Xcode metadata. " +
                "User schemes under xcuserdata are not exact-source release inputs.");
        }
        if (candidates.Length > 1)
        {
            throw new InvalidOperationException(
                $"Apple app '{app.Name}' scheme '{scheme}' is ambiguous across {candidates.Length} shared Xcode containers.");
        }

        EnsureTrackedFile(repositoryRoot, candidates[0], $"Apple app '{app.Name}' shared scheme");
        ValidateScheme(repositoryRoot, candidates[0], metadataPaths);
    }

    private void ValidateScheme(
        string repositoryRoot,
        string schemePath,
        IReadOnlyCollection<string> metadataPaths)
    {
        var document = XDocument.Load(schemePath, LoadOptions.None);
        if (document.Descendants().Any(element =>
                element.Name.LocalName.Equals("ExecutionAction", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Shared Xcode scheme actions are not accepted for exact-source checkpoints because their runtime inputs cannot be proven: {schemePath}");
        }

        var schemeContainer = FindXcodeContainer(schemePath)
            ?? throw new InvalidOperationException($"Shared Xcode scheme is not inside an Xcode project or workspace: {schemePath}");
        var containerRoot = Path.GetDirectoryName(schemeContainer)!;
        var knownMetadata = new HashSet<string>(metadataPaths.Select(Path.GetFullPath), GetPathComparer());
        foreach (var reference in document.Descendants()
                     .Select(element => element.Attribute("ReferencedContainer")?.Value)
                     .Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var referencedContainer = ResolveSchemeContainer(reference!, containerRoot);
            EnsurePathWithinRepository(repositoryRoot, referencedContainer, "Xcode scheme referenced container");
            if (!Directory.Exists(referencedContainer))
                throw new DirectoryNotFoundException($"Xcode scheme referenced container was not found: {referencedContainer}");
            EnsureNoLinkedTraversal(repositoryRoot, referencedContainer, "Xcode scheme referenced container");

            var metadataPath = referencedContainer.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(referencedContainer, "contents.xcworkspacedata")
                : referencedContainer.EndsWith(".xcodeproj", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(referencedContainer, "project.pbxproj")
                    : throw new InvalidOperationException(
                        $"Xcode scheme referenced container is not a project or workspace: {referencedContainer}");
            EnsureTrackedFile(repositoryRoot, metadataPath, "Xcode scheme referenced container metadata");
            if (!knownMetadata.Contains(Path.GetFullPath(metadataPath)))
            {
                throw new InvalidOperationException(
                    $"Xcode scheme references a container outside the validated project/workspace graph: {referencedContainer}");
            }
        }
    }

    private static string? FindXcodeContainer(string schemePath)
    {
        var current = Path.GetDirectoryName(schemePath);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (current.EndsWith(".xcodeproj", StringComparison.OrdinalIgnoreCase) ||
                current.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase))
                return current;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    private static string ResolveSchemeContainer(string reference, string containerRoot)
    {
        var separator = reference.IndexOf(':');
        var kind = separator < 0 ? "container" : reference.Substring(0, separator);
        var value = separator < 0 ? reference : reference.Substring(separator + 1);
        return kind.ToLowerInvariant() switch
        {
            "container" or "group" => ResolvePath(containerRoot, value),
            "absolute" => throw new InvalidOperationException(
                $"Absolute Xcode scheme references are not accepted for exact-source snapshot builds: {reference}"),
            _ => throw new InvalidOperationException($"Unsupported Xcode scheme container kind '{kind}'.")
        };
    }

    private void ValidateProjectGraph(
        string repositoryRoot,
        string metadataPath,
        IReadOnlyCollection<string> metadataPaths,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetDirectoryName(metadataPath)!)!;
        var packageLockPaths = ResolveEffectivePackageLockPaths(metadataPath, metadataPaths);
        var objects = ParsePbxObjects(File.ReadAllText(metadataPath));
        var parents = BuildPbxParentMap(objects);
        var buildFileReferences = objects.Values
            .Where(static value => value.Isa.Equals("PBXBuildFile", StringComparison.OrdinalIgnoreCase))
            .Select(value => ReadPbxScalar(value.Body, "fileRef"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nativeTargetProductReferences = objects.Values
            .Where(static value => value.Isa.Equals("PBXNativeTarget", StringComparison.OrdinalIgnoreCase))
            .Select(value => ReadPbxScalar(value.Body, "productReference"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shippingSources = ResolveShippingSourceOwnership(objects, metadataPath);
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var validatedLocalPackageRoots = new HashSet<string>(GetPathComparer());

        foreach (var buildConfiguration in objects.Values.Where(static value =>
                     value.Isa.Equals("XCBuildConfiguration", StringComparison.OrdinalIgnoreCase)))
        {
            ValidateBuildConfiguration(
                repositoryRoot,
                projectDirectory,
                buildConfiguration,
                objects,
                parents,
                cache,
                metadataPath,
                generatedOutputPaths);
        }

        foreach (var item in objects.Values)
        {
            if (item.Isa.Equals("PBXShellScriptBuildPhase", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PBX shell-script build phases are not accepted for exact-source checkpoints because arbitrary runtime inputs cannot be proven: {metadataPath}");
            }

            if (item.Isa.Equals("PBXBuildRule", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PBX custom build rules are not accepted for exact-source checkpoints because their runtime inputs cannot be proven: {metadataPath}");
            }

            if (item.Isa.Equals("PBXLegacyTarget", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PBX legacy targets are not accepted for exact-source checkpoints because their external build tool cannot be proven: {metadataPath}");
            }

            if (item.Isa.Equals("PBXBuildFile", StringComparison.OrdinalIgnoreCase))
            {
                ValidateBuildFileSettings(
                    repositoryRoot,
                    projectDirectory,
                    item,
                    generatedOutputPaths,
                    metadataPath);
                continue;
            }

            if (item.Isa.Equals("XCLocalSwiftPackageReference", StringComparison.OrdinalIgnoreCase))
            {
                ValidateLocalPackageReference(
                    repositoryRoot,
                    projectDirectory,
                    packageLockPaths,
                    item,
                    validatedLocalPackageRoots);
                continue;
            }

            if (item.Isa.Equals("PBXFileSystemSynchronizedBuildFileExceptionSet", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PBX file-system synchronized build-file exception sets are not accepted for exact-source checkpoints because their per-file compiler overrides cannot be proven: {metadataPath}");
            }

            if (item.Isa.Equals("XCRemoteSwiftPackageReference", StringComparison.OrdinalIgnoreCase))
            {
                ValidateRemotePackageReference(repositoryRoot, packageLockPaths, item);
                continue;
            }

            if (item.Isa.Equals("XCBuildConfiguration", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsPathBearingPbxObject(item.Isa))
                continue;

            if (Path.IsPathRooted(item.Path ?? string.Empty))
            {
                throw new InvalidOperationException(
                    $"Absolute Xcode project inputs are not accepted for exact-source snapshot builds: {item.Path} ({metadataPath})");
            }

            var candidate = ResolvePbxObjectPath(projectDirectory, item.Id, objects, parents, cache, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (candidate is null)
            {
                ValidateExternalXcodeBuildInput(item, metadataPath, buildFileReferences, nativeTargetProductReferences);
                continue;
            }
            ValidateResolvedProjectInput(
                repositoryRoot,
                candidate,
                item,
                metadataPath,
                generatedOutputPaths,
                buildFileReferences,
                shippingSources);
        }
    }

    private void ValidateBuildFileSettings(
        string repositoryRoot,
        string projectDirectory,
        PbxObject item,
        IReadOnlyCollection<string> generatedOutputPaths,
        string metadataPath)
    {
        var settings = ReadPbxDictionary(item.Body, "settings");
        if (settings is null)
            return;

        foreach (var assignment in ReadPbxAssignments(settings))
        {
            if (assignment.Key.Equals("ATTRIBUTES", StringComparison.OrdinalIgnoreCase))
                continue;
            if (assignment.Key.Equals("COMPILER_FLAGS", StringComparison.OrdinalIgnoreCase))
            {
                ValidateBuildFlagInputPaths(
                    repositoryRoot,
                    projectDirectory,
                    assignment.Value,
                    "COMPILER_FLAGS",
                    generatedOutputPaths,
                    $"PBXBuildFile '{item.Id}' in {metadataPath}",
                    new HashSet<string>(GetPathComparer()));
                continue;
            }

            throw new InvalidOperationException(
                $"PBXBuildFile '{item.Id}' uses unsupported per-file setting '{assignment.Key}', whose build behavior cannot be proven: {metadataPath}");
        }
    }

    private void ValidateBuildConfiguration(
        string repositoryRoot,
        string projectDirectory,
        PbxObject item,
        IReadOnlyDictionary<string, PbxObject> objects,
        IReadOnlyDictionary<string, string> parents,
        IDictionary<string, string?> cache,
        string metadataPath,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        var baseConfigurationReference = ReadPbxScalar(item.Body, "baseConfigurationReference")?
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(baseConfigurationReference))
        {
            var baseConfigurationPath = ResolvePbxObjectPath(
                projectDirectory,
                baseConfigurationReference!,
                objects,
                parents,
                cache,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (baseConfigurationPath is null)
                throw new InvalidOperationException($"Xcode base configuration uses an external source tree: {metadataPath}");
            EnsureNoGeneratedOutputOverlap(baseConfigurationPath, generatedOutputPaths, "Xcode base configuration");
            EnsureTrackedFile(repositoryRoot, baseConfigurationPath, "Xcode base configuration");
            EnsureTrackedXcconfigGraph(
                repositoryRoot,
                projectDirectory,
                baseConfigurationPath,
                generatedOutputPaths,
                new HashSet<string>(GetPathComparer()));
        }

        var buildSettings = ReadPbxDictionary(item.Body, "buildSettings");
        if (buildSettings is null)
            return;
        ValidateBuildSettingAssignments(
            repositoryRoot,
            projectDirectory,
            ReadPbxAssignments(buildSettings),
            generatedOutputPaths,
            "PBX build settings");
    }

    private void EnsureTrackedXcconfigGraph(
        string repositoryRoot,
        string projectDirectory,
        string configPath,
        IReadOnlyCollection<string> generatedOutputPaths,
        ISet<string> visited)
    {
        var fullPath = Path.GetFullPath(configPath);
        if (!visited.Add(fullPath))
            return;
        var contents = File.ReadAllText(fullPath);
        ValidateBuildSettingAssignments(
            repositoryRoot,
            projectDirectory,
            ReadXcconfigAssignments(contents),
            generatedOutputPaths,
            $"xcconfig '{fullPath}'");
        foreach (Match include in Regex.Matches(
                     contents,
                     "(?m)^[ \\t]*#include(?<optional>\\?)?[ \\t]+[\\\"<](?<path>[^\\\">]+)[\\\">]",
                     RegexOptions.CultureInvariant))
        {
            var value = include.Groups["path"].Value.Trim();
            var includedPath = ResolvePbxPath(
                Path.GetDirectoryName(fullPath)!,
                value,
                "xcconfig include");
            EnsurePathWithinRepository(repositoryRoot, includedPath, "Xcode xcconfig include");
            EnsureNoGeneratedOutputOverlap(includedPath, generatedOutputPaths, "Xcode xcconfig include");
            if (!File.Exists(includedPath))
            {
                if (include.Groups["optional"].Success)
                    continue;
                throw new FileNotFoundException(
                    $"Xcode xcconfig include cannot be proven at the exact source commit: {includedPath}",
                    includedPath);
            }
            EnsureTrackedFile(repositoryRoot, includedPath, "Xcode xcconfig include");
            EnsureTrackedXcconfigGraph(repositoryRoot, projectDirectory, includedPath, generatedOutputPaths, visited);
        }
    }

    private static void ValidateExternalXcodeBuildInput(
        PbxObject item,
        string metadataPath,
        ISet<string> buildFileReferences,
        ISet<string> nativeTargetProductReferences)
    {
        if (!buildFileReferences.Contains(item.Id))
            return;

        var sourceTree = item.SourceTree ?? string.Empty;
        var path = item.Path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Contains("$(", StringComparison.Ordinal) ||
            path.Contains("${", StringComparison.Ordinal) ||
            path.Split('/', '\\').Any(static segment => segment == ".."))
        {
            throw new InvalidOperationException(
                $"Xcode build input '{path}' uses external source tree '{sourceTree}' and cannot be proven at the exact source commit: {metadataPath}");
        }

        if (sourceTree.Equals("BUILT_PRODUCTS_DIR", StringComparison.OrdinalIgnoreCase))
        {
            if (nativeTargetProductReferences.Contains(item.Id))
                return;
            throw new InvalidOperationException(
                $"Xcode build input '{path}' uses BUILT_PRODUCTS_DIR without a validated PBXNativeTarget product owner: {metadataPath}");
        }

        var normalized = path.Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        var approvedSystemArtifact = extension.Equals(".framework", StringComparison.OrdinalIgnoreCase) ||
                                     extension.Equals(".tbd", StringComparison.OrdinalIgnoreCase) ||
                                     extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase) ||
                                     extension.Equals(".a", StringComparison.OrdinalIgnoreCase);
        var approvedRoot = sourceTree.Equals("SDKROOT", StringComparison.OrdinalIgnoreCase)
            ? normalized.StartsWith("System/Library/", StringComparison.Ordinal) ||
              normalized.StartsWith("usr/lib/", StringComparison.Ordinal)
            : sourceTree.Equals("DEVELOPER_DIR", StringComparison.OrdinalIgnoreCase) &&
              (normalized.StartsWith("Platforms/", StringComparison.Ordinal) ||
               normalized.StartsWith("Toolchains/", StringComparison.Ordinal) ||
               normalized.StartsWith("Library/", StringComparison.Ordinal));
        if (approvedSystemArtifact && approvedRoot)
            return;

        throw new InvalidOperationException(
            $"Xcode build input '{path}' from external source tree '{sourceTree}' is not a validated SDK, toolchain, or owned target product: {metadataPath}");
    }

    private void ValidateRemotePackageReference(
        string repositoryRoot,
        IReadOnlyCollection<string> packageLockPaths,
        PbxObject item)
    {
        var repositoryUrl = ReadPbxScalar(item.Body, "repositoryURL")?.Trim();
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            throw new InvalidOperationException("Remote Swift package reference is missing repositoryURL.");

        var locks = FindTrackedPackageLocks(packageLockPaths, repositoryUrl!);
        if (locks.Length == 0)
        {
            throw new InvalidOperationException(
                $"Remote Swift package '{repositoryUrl}' must be bound by a tracked Package.resolved lock so preflight and exact archive materialization consume the same approved graph.");
        }
        foreach (var packageLock in locks)
            EnsureTrackedFile(repositoryRoot, packageLock, "Swift package resolution lock");
        var resolvedRevision = ResolvePackageRevision(packageLockPaths, repositoryUrl!);
        ValidateRemotePackageSource(repositoryUrl!, resolvedRevision, packageLockPaths);
    }

    private void ValidateResolvedProjectInput(
        string repositoryRoot,
        string candidate,
        PbxObject item,
        string metadataPath,
        IReadOnlyCollection<string> generatedOutputPaths,
        ISet<string> buildFileReferences,
        ShippingSourceOwnership shippingSources)
    {
        EnsurePathWithinRepository(repositoryRoot, candidate, $"Xcode {item.Isa} input");
        var directoryFileReferenceIsBuilt =
            item.Isa.Equals("PBXFileReference", StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(candidate) &&
            buildFileReferences.Contains(item.Id);
        var concreteFileReference = !item.Isa.Equals("PBXFileReference", StringComparison.OrdinalIgnoreCase) ||
                                    File.Exists(candidate) ||
                                    directoryFileReferenceIsBuilt;
        if (concreteFileReference &&
            !item.Isa.Equals("PBXGroup", StringComparison.OrdinalIgnoreCase) &&
            !item.Isa.Equals("PBXVariantGroup", StringComparison.OrdinalIgnoreCase))
        {
            EnsureNoGeneratedOutputOverlap(candidate, generatedOutputPaths, $"Xcode {item.Isa} input");
        }
        var isShippingSource = shippingSources.FileReferences.Contains(item.Id) ||
                               shippingSources.SynchronizedRoots.Contains(item.Id);
        if (File.Exists(candidate))
        {
            EnsureTrackedFile(
                repositoryRoot,
                candidate,
                $"Xcode {item.Isa} input",
                validateSwiftDeterminism: false);
            if (isShippingSource && Path.GetExtension(candidate).Equals(".swift", StringComparison.OrdinalIgnoreCase))
                ValidateSourceLevelIncludes(repositoryRoot, candidate, validateSwiftDeterminism: true);
        }
        else if (Directory.Exists(candidate))
        {
            if (directoryFileReferenceIsBuilt ||
                item.Isa.Equals("XCVersionGroup", StringComparison.OrdinalIgnoreCase) ||
                item.Isa.Equals("PBXFileSystemSynchronizedRootGroup", StringComparison.OrdinalIgnoreCase))
            {
                EnsureTrackedDirectoryTree(
                    repositoryRoot,
                    candidate,
                    $"Xcode {item.Isa} input",
                    validateSwiftDeterminism: isShippingSource);
            }
            else
            {
                EnsureNoLinkedTraversal(repositoryRoot, candidate, $"Xcode {item.Isa} input");
            }
        }
        else if (buildFileReferences.Contains(item.Id) ||
                 Path.IsPathRooted(item.Path ?? string.Empty) ||
                 (item.Path ?? string.Empty).Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new FileNotFoundException(
                $"Xcode project references a missing explicit path that cannot be proven: {candidate} ({metadataPath})",
                candidate);
        }
    }

    private void EnsureTrackedDirectoryTree(
        string repositoryRoot,
        string path,
        string name,
        bool validateSwiftDeterminism = false)
    {
        EnsureDirectoryWithinRepository(repositoryRoot, path, name);
        var relativeRoot = FrameworkCompatibility.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        var indexEntries = RunGit(repositoryRoot, "ls-files", "-v", "-z", "--", relativeRoot)
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        var hiddenEntry = indexEntries.FirstOrDefault(HasHiddenGitIndexState);
        if (hiddenEntry is not null)
        {
            throw new InvalidOperationException(
                $"{name} contains a skip-worktree or assume-unchanged Git index entry and cannot be attested: {hiddenEntry.Substring(2)}");
        }
        var tracked = indexEntries
            .Where(static entry => entry.Length > 2 && entry[1] == ' ')
            .Select(entry => Path.GetFullPath(Path.Combine(repositoryRoot, entry.Substring(2))))
            .ToHashSet(GetPathComparer());
        var headBlobs = ReadHeadTreeBlobIds(repositoryRoot, relativeRoot);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{name} must not contain a symbolic link or reparse point: {entry}");
            if (File.Exists(entry) && !tracked.Contains(Path.GetFullPath(entry)))
            {
                throw new InvalidOperationException(
                    $"{name} must be tracked at the exact source commit: " +
                    FrameworkCompatibility.GetRelativePath(repositoryRoot, entry).Replace('\\', '/'));
            }
            if (File.Exists(entry))
            {
                var fullPath = Path.GetFullPath(entry);
                if (!headBlobs.TryGetValue(fullPath, out var expectedBlob) ||
                    !expectedBlob.Equals(ComputeRawGitBlobId(repositoryRoot, fullPath), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{name} differs from the exact source commit: " +
                        FrameworkCompatibility.GetRelativePath(repositoryRoot, entry).Replace('\\', '/'));
                }
                ValidateSourceLevelIncludes(repositoryRoot, fullPath, validateSwiftDeterminism, expectedBlob);
            }
        }
    }

    private static string ResolveBuildSettingPath(string projectDirectory, string value, string key)
    {
        var expanded = value.Trim();
        if (Path.IsPathRooted(expanded))
        {
            throw new InvalidOperationException(
                $"Xcode build setting {key} must resolve inside the repository for exact-source snapshot builds; absolute paths are not accepted: {value}");
        }
        foreach (var variable in new[] { "$(SRCROOT)", "$(PROJECT_DIR)", "$(SOURCE_ROOT)", "${SRCROOT}", "${PROJECT_DIR}", "${SOURCE_ROOT}" })
            expanded = expanded.Replace(variable, projectDirectory);
        expanded = expanded.Replace("$(inherited)", string.Empty).Replace("$(INHERITED)", string.Empty).Trim();
        if (expanded.Contains("$(", StringComparison.Ordinal) || expanded.Contains("${", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Variable-based Xcode build setting {key} cannot be proven for an exact-source checkpoint: {value}");
        }
        return ResolvePath(projectDirectory, expanded);
    }

    private static string[] SplitBuildSettingPaths(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("(", StringComparison.Ordinal) && normalized.EndsWith(")", StringComparison.Ordinal))
            normalized = normalized.Substring(1, normalized.Length - 2);
        return Regex.Matches(normalized, "\"(?<quoted>(?:\\\\.|[^\"])*)\"|(?<bare>[^,\\s]+)", RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Groups["quoted"].Success
                ? UnescapePbxString(match.Groups["quoted"].Value)
                : match.Groups["bare"].Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value) &&
                                   !value.Equals("$(inherited)", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static bool IsPathBearingPbxObject(string isa)
        => isa.Equals("PBXGroup", StringComparison.OrdinalIgnoreCase) ||
           isa.Equals("PBXVariantGroup", StringComparison.OrdinalIgnoreCase) ||
           isa.Equals("XCVersionGroup", StringComparison.OrdinalIgnoreCase) ||
           isa.Equals("PBXFileReference", StringComparison.OrdinalIgnoreCase) ||
           isa.Equals("PBXFileSystemSynchronizedRootGroup", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> BuildPbxParentMap(IReadOnlyDictionary<string, PbxObject> objects)
    {
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in objects.Values.Where(item =>
                     item.Isa.Equals("PBXGroup", StringComparison.OrdinalIgnoreCase) ||
                     item.Isa.Equals("PBXVariantGroup", StringComparison.OrdinalIgnoreCase) ||
                     item.Isa.Equals("XCVersionGroup", StringComparison.OrdinalIgnoreCase) ||
                     item.Isa.Equals("PBXFileSystemSynchronizedRootGroup", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var child in ReadPbxReferences(group.Body, "children"))
            {
                if (parents.TryGetValue(child, out var existing) && !existing.Equals(group.Id, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Xcode object '{child}' has ambiguous PBX group ancestry.");
                parents[child] = group.Id;
            }
        }
        return parents;
    }

    private static string? ResolvePbxObjectPath(
        string projectDirectory,
        string objectId,
        IReadOnlyDictionary<string, PbxObject> objects,
        IReadOnlyDictionary<string, string> parents,
        IDictionary<string, string?> cache,
        ISet<string> resolving)
    {
        if (cache.TryGetValue(objectId, out var cached))
            return cached;
        if (!objects.TryGetValue(objectId, out var item))
            throw new InvalidOperationException($"Xcode project references unknown PBX object '{objectId}'.");
        if (!resolving.Add(objectId))
            throw new InvalidOperationException($"Xcode PBX group ancestry contains a cycle at '{objectId}'.");

        var sourceTree = string.IsNullOrWhiteSpace(item.SourceTree) ? "<group>" : item.SourceTree!;
        if (ExternalXcodeSourceTrees.Contains(sourceTree))
        {
            cache[objectId] = null;
            resolving.Remove(objectId);
            return null;
        }

        string basePath;
        if (sourceTree.Equals("SOURCE_ROOT", StringComparison.OrdinalIgnoreCase))
        {
            basePath = projectDirectory;
        }
        else if (sourceTree.Equals("<absolute>", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(item.Path) || !Path.IsPathRooted(item.Path))
                throw new InvalidOperationException($"Absolute Xcode input '{item.Id}' does not contain an absolute path.");
            basePath = Path.GetPathRoot(item.Path!)!;
        }
        else if (sourceTree.Equals("<group>", StringComparison.OrdinalIgnoreCase))
        {
            basePath = parents.TryGetValue(objectId, out var parentId)
                ? ResolvePbxObjectPath(projectDirectory, parentId, objects, parents, cache, resolving) ?? projectDirectory
                : projectDirectory;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported Xcode sourceTree '{sourceTree}' in exact-source project metadata.");
        }

        var resolved = string.IsNullOrWhiteSpace(item.Path)
            ? Path.GetFullPath(basePath)
            : ResolvePbxPath(basePath, item.Path!, item.Isa);
        cache[objectId] = resolved;
        resolving.Remove(objectId);
        return resolved;
    }

    private static string ResolvePbxPath(string basePath, string value, string context)
    {
        if (value.Contains("$(", StringComparison.Ordinal) || value.Contains("${", StringComparison.Ordinal))
            throw new InvalidOperationException($"Variable-based Xcode {context} path cannot be proven for an exact-source checkpoint: {value}");
        return ResolvePath(basePath, value);
    }

    private static string[] ResolveObjectAwareSynchronizedRoots(IEnumerable<string> metadataPaths)
    {
        var roots = new HashSet<string>(GetPathComparer());
        foreach (var metadataPath in metadataPaths.Where(path =>
                     path.EndsWith("project.pbxproj", StringComparison.OrdinalIgnoreCase) && File.Exists(path)))
        {
            var projectDirectory = Path.GetDirectoryName(Path.GetDirectoryName(metadataPath)!)!;
            var objects = ParsePbxObjects(File.ReadAllText(metadataPath));
            var parents = BuildPbxParentMap(objects);
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in objects.Values.Where(value =>
                         value.Isa.Equals("PBXFileSystemSynchronizedRootGroup", StringComparison.OrdinalIgnoreCase)))
            {
                var path = ResolvePbxObjectPath(
                    projectDirectory,
                    item.Id,
                    objects,
                    parents,
                    cache,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (path is not null)
                    roots.Add(path);
            }
        }
        return roots.ToArray();
    }

}
