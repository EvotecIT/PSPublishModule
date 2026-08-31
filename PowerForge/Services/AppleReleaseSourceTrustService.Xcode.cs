using System.Text.RegularExpressions;

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
        "MODULEMAP_PRIVATE_FILE",
        "DEVELOPMENT_ASSET_PATHS",
        "EXPORTED_SYMBOLS_FILE",
        "UNEXPORTED_SYMBOLS_FILE",
        "ORDER_FILE"
    };

    private static readonly HashSet<string> SearchPathBuildSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "HEADER_SEARCH_PATHS",
        "USER_HEADER_SEARCH_PATHS",
        "SYSTEM_HEADER_SEARCH_PATHS",
        "MTL_HEADER_SEARCH_PATHS",
        "FRAMEWORK_SEARCH_PATHS",
        "IBC_PLUGIN_SEARCH_PATHS",
        "SYSTEM_FRAMEWORK_SEARCH_PATHS",
        "LIBRARY_SEARCH_PATHS",
        "SWIFT_INCLUDE_PATHS",
        "SWIFT_SYSTEM_INCLUDE_PATHS"
    };

    private static bool IsFlagBuildSetting(string key)
        => key.EndsWith("FLAGS", StringComparison.OrdinalIgnoreCase);

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
        {
            EnsureTrackedSharedScheme(
                repositoryRoot,
                projectRoot,
                app,
                metadataPaths);
        }

        foreach (var metadataPath in metadataPaths.Where(path =>
                     path.EndsWith("project.pbxproj", StringComparison.OrdinalIgnoreCase) && File.Exists(path)))
        {
            ValidateWholeProjectGraph(
                repositoryRoot,
                metadataPath,
                metadataPaths,
                generatedOutputPaths);
        }
    }

    private void ValidateWholeProjectGraph(
        string repositoryRoot,
        string metadataPath,
        IReadOnlyCollection<string> metadataPaths,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        try
        {
            ValidateProjectGraph(
                repositoryRoot,
                metadataPath,
                metadataPaths,
                generatedOutputPaths);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or FileNotFoundException)
        {
            throw new InvalidOperationException(
                "Exact-source Apple builds conservatively attest the complete referenced Xcode project because " +
                "xcodebuild does not expose an authoritative pre-build selected-target input graph. " +
                "Fix the reported project input; if it belongs only to an unrelated target, move that target or input " +
                "into a separate project before retrying. " +
                exception.Message,
                exception);
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
        var buildFileReferences = ResolvePbxReferences(
            objects,
            "PBXBuildFile",
            "fileRef");
        var nativeTargetProductReferences = ResolvePbxReferences(
            objects,
            "PBXNativeTarget",
            "productReference");
        var shippingSources = ResolveShippingSourceOwnership(
            repositoryRoot,
            projectDirectory,
            objects,
            metadataPath,
            generatedOutputPaths);
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

        foreach (var buildFile in objects.Values.Where(static value =>
                     value.Isa.Equals("PBXBuildFile", StringComparison.OrdinalIgnoreCase)))
        {
            ValidateBuildFileSettings(
                repositoryRoot,
                projectDirectory,
                buildFile,
                generatedOutputPaths,
                metadataPath);
        }

        foreach (var item in objects.Values)
        {
            EnsureExecutionMetadataAccepted(
                item.Isa,
                metadataPath,
                _validationScope);

            if (item.Isa.Equals("PBXBuildFile", StringComparison.OrdinalIgnoreCase))
            {
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
                ValidateRemotePackageReference(
                    repositoryRoot,
                    packageLockPaths,
                    item);
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

    internal static void EnsureExecutionMetadataAccepted(
        string isa,
        string metadataPath,
        AppleReleaseSourceTrustValidationScope validationScope)
    {
        if (validationScope == AppleReleaseSourceTrustValidationScope.SourceInspection)
            return;

        if (isa.Equals("PBXShellScriptBuildPhase", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PBX shell-script build phases are not accepted for exact-source checkpoints because arbitrary runtime inputs cannot be proven: {metadataPath}");
        }

        if (isa.Equals("PBXBuildRule", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PBX custom build rules are not accepted for exact-source checkpoints because their runtime inputs cannot be proven: {metadataPath}");
        }

        if (isa.Equals("PBXLegacyTarget", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PBX legacy targets are not accepted for exact-source checkpoints because their external build tool cannot be proven: {metadataPath}");
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
        ValidateRemotePackageIdentity(repositoryUrl!, resolvedRevision);
        ValidateRemotePackageSource(
            repositoryUrl!,
            resolvedRevision,
            packageLockPaths);
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
        var isShippingSource = shippingSources.FileReferences.ContainsKey(item.Id) ||
                               shippingSources.SynchronizedRoots.Contains(item.Id);
        if (File.Exists(candidate))
        {
            var effectiveSourceExtension = isShippingSource
                ? shippingSources.ResolveEffectiveExtension(item.Id, candidate, item, metadataPath)
                : null;
            EnsureTrackedFile(
                repositoryRoot,
                candidate,
                $"Xcode {item.Isa} input",
                validateSwiftDeterminism: effectiveSourceExtension?.Equals(".swift", StringComparison.OrdinalIgnoreCase) == true,
                effectiveSourceExtension: effectiveSourceExtension,
                assemblerWorkingDirectory: Path.GetDirectoryName(Path.GetDirectoryName(metadataPath)!)!);
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
                    validateSwiftDeterminism: isShippingSource,
                    assemblerWorkingDirectory: Path.GetDirectoryName(Path.GetDirectoryName(metadataPath)!)!);
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
        bool validateSwiftDeterminism = false,
        string? assemblerWorkingDirectory = null)
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
        var entries = EnumerateTreeWithoutLinks(path, name);
        var impliedDirectories = new HashSet<string>(GetPathComparer());
        foreach (var trackedPath in tracked.Where(File.Exists))
        {
            var directory = Path.GetDirectoryName(trackedPath);
            while (!string.IsNullOrWhiteSpace(directory) &&
                   IsPathAtOrWithin(directory!, path))
            {
                if (!impliedDirectories.Add(Path.GetFullPath(directory!)) ||
                    GetPathComparer().Equals(
                        Path.GetFullPath(directory!),
                        Path.GetFullPath(path)))
                {
                    break;
                }
                directory = Path.GetDirectoryName(directory);
            }
        }
        var untrackedDirectory = new[] { Path.GetFullPath(path) }
            .Concat(entries.Where(Directory.Exists).Select(Path.GetFullPath))
            .FirstOrDefault(directory => !impliedDirectories.Contains(directory));
        if (untrackedDirectory is not null)
        {
            throw new InvalidOperationException(
                $"{name} contains a directory that is not represented by tracked source at the exact commit: " +
                FrameworkCompatibility.GetRelativePath(repositoryRoot, untrackedDirectory).Replace('\\', '/'));
        }
        var trackedFiles = entries
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToArray();
        EnsureNoCustomGitFilters(
            repositoryRoot,
            trackedFiles
                .Select(file => FrameworkCompatibility.GetRelativePath(repositoryRoot, file).Replace('\\', '/'))
                .ToArray(),
            name);
        foreach (var entry in entries)
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
                var relativePath = FrameworkCompatibility.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
                var worktreeBlob = ComputeRawGitBlobId(repositoryRoot, fullPath);
                if (!headBlobs.TryGetValue(fullPath, out var expectedBlob))
                {
                    throw new InvalidOperationException(
                        $"{name} differs from the exact source commit: " +
                        relativePath);
                }
                if (!expectedBlob.Equals(worktreeBlob, StringComparison.OrdinalIgnoreCase) &&
                    !expectedBlob.Equals(
                        ComputePathAwareGitBlobId(repositoryRoot, fullPath, relativePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{name} differs from the exact source commit: " +
                        relativePath);
                }
                _validatedTrackedFileBlobs[fullPath] = worktreeBlob;
                ValidateSourceLevelIncludes(
                    repositoryRoot,
                    fullPath,
                    validateSwiftDeterminism,
                    worktreeBlob,
                    assemblerWorkingDirectory: assemblerWorkingDirectory);
            }
        }
    }

    internal static string[] EnumerateTreeWithoutLinks(string root, string name)
    {
        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"{name} must not contain a symbolic link or reparse point: {entry}");

                entries.Add(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
            }
        }

        return entries.ToArray();
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
