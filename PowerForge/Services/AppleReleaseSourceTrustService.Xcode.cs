using System.Text.RegularExpressions;
using System.Text.Json;
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
        "OTHER_SWIFT_FLAGS"
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
            "absolute" => Path.GetFullPath(value),
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
        var packageLockRoots = ResolvePackageLockSearchRoots(metadataPath, metadataPaths);
        var objects = ParsePbxObjects(File.ReadAllText(metadataPath));
        var parents = BuildPbxParentMap(objects);
        var buildFileReferences = objects.Values
            .Where(static value => value.Isa.Equals("PBXBuildFile", StringComparison.OrdinalIgnoreCase))
            .Select(value => ReadPbxScalar(value.Body, "fileRef"))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

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

            if (item.Isa.Equals("XCLocalSwiftPackageReference", StringComparison.OrdinalIgnoreCase))
            {
                ValidateLocalPackageReference(repositoryRoot, projectDirectory, packageLockRoots, item);
                continue;
            }

            if (item.Isa.Equals("XCRemoteSwiftPackageReference", StringComparison.OrdinalIgnoreCase))
            {
                ValidateRemotePackageReference(repositoryRoot, packageLockRoots, item);
                continue;
            }

            if (item.Isa.Equals("XCBuildConfiguration", StringComparison.OrdinalIgnoreCase))
            {
                ValidateBuildConfiguration(
                    repositoryRoot,
                    projectDirectory,
                    item,
                    objects,
                    parents,
                    cache,
                    metadataPath,
                    generatedOutputPaths);
                continue;
            }

            if (!IsPathBearingPbxObject(item.Isa))
                continue;

            var candidate = ResolvePbxObjectPath(projectDirectory, item.Id, objects, parents, cache, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (candidate is null)
                continue;
            ValidateResolvedProjectInput(
                repositoryRoot,
                candidate,
                item,
                metadataPath,
                generatedOutputPaths,
                buildFileReferences);
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
                var directive = include.Groups["optional"].Success ? "optional " : string.Empty;
                throw new FileNotFoundException(
                    $"Xcode {directive}xcconfig include cannot be proven at the exact source commit: {includedPath}",
                    includedPath);
            }
            EnsureTrackedFile(repositoryRoot, includedPath, "Xcode xcconfig include");
            EnsureTrackedXcconfigGraph(repositoryRoot, projectDirectory, includedPath, generatedOutputPaths, visited);
        }
    }

    private void ValidateLocalPackageReference(
        string repositoryRoot,
        string projectDirectory,
        IReadOnlyCollection<string> packageLockRoots,
        PbxObject item)
    {
        var relativePath = ReadPbxScalar(item.Body, "relativePath");
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("Local Swift package reference is missing relativePath.");
        var packageRoot = ResolvePbxPath(projectDirectory, relativePath!, "local Swift package");
        EnsureDirectoryWithinRepository(repositoryRoot, packageRoot, "Xcode local Swift package");
        var manifestPath = Path.Combine(packageRoot, "Package.swift");
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

        var manifest = File.ReadAllText(manifestPath);
        if (Regex.IsMatch(
                manifest,
                "\\.unsafeFlags\\s*\\(",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' uses unsafeFlags, whose compiler and linker inputs cannot be proven at the exact source commit. " +
                "Replace unsafe flags with tracked package settings before creating an Apple checkpoint.");
        }
        var externalDependencies = Regex.Matches(
                manifest,
                "\\.package\\s*\\((?<body>.*?)\\)",
                RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(static match => match.Groups["body"].Value)
            .Where(static body => Regex.IsMatch(body, "\\b(?:url|id)\\s*:", RegexOptions.CultureInvariant))
            .ToArray();
        foreach (var dependency in externalDependencies.Where(static body =>
                     Regex.IsMatch(body, "\\bid\\s*:", RegexOptions.CultureInvariant) ||
                     !Regex.IsMatch(
                         body,
                         "\\brevision\\s*:\\s*\"[A-Fa-f0-9]{40}\"",
                         RegexOptions.CultureInvariant)))
        {
            var identityMatch = Regex.Match(
                dependency,
                "\\b(?:url|id)\\s*:\\s*\"(?<identity>[^\"]+)\"",
                RegexOptions.CultureInvariant);
            if (!identityMatch.Success)
            {
                throw new InvalidOperationException(
                    $"Local Swift package '{packageRoot}' declares a dynamic external dependency that cannot be bound to exact source. " +
                    "Use a literal package URL or registry identity and commit its Package.resolved lock.");
            }

            var identity = identityMatch.Groups["identity"].Value;
            var lockRoots = packageLockRoots
                .Concat(new[] { packageRoot })
                .Distinct(GetPathComparer())
                .ToArray();
            var locks = FindTrackedPackageLocks(repositoryRoot, lockRoots, identity);
            if (locks.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Local Swift package '{packageRoot}' declares external dependency '{identity}' without an exact 40-character revision. " +
                    "Commit a Package.resolved lock containing that dependency before creating an exact-source Apple checkpoint.");
            }
            foreach (var packageLock in locks)
                EnsureTrackedFile(repositoryRoot, packageLock, "Xcode local Swift package resolution lock");
        }
        var manifestWithoutComments = RemoveSwiftComments(manifest);
        var pathArguments = Regex.Matches(
            manifestWithoutComments,
            "\\bpath\\s*:",
            RegexOptions.CultureInvariant);
        var literalPathArguments = Regex.Matches(
            manifestWithoutComments,
            "\\bpath\\s*:\\s*\"(?<path>[^\"\\\\\\r\\n]+)\"\\s*(?=[,)])",
            RegexOptions.CultureInvariant);
        if (pathArguments.Count != literalPathArguments.Count)
        {
            throw new InvalidOperationException(
                $"Local Swift package '{packageRoot}' uses a computed, interpolated, or escaped path argument that cannot be bound to exact source. " +
                "Use a simple literal path inside the tracked repository.");
        }
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

    private static string RemoveSwiftComments(string source)
    {
        var result = source.ToCharArray();
        var inString = false;
        var escaped = false;
        for (var index = 0; index < result.Length; index++)
        {
            var current = result[index];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == '"')
                    inString = false;
                continue;
            }
            if (current == '"')
            {
                inString = true;
                continue;
            }
            if (current != '/' || index + 1 >= result.Length)
                continue;
            if (result[index + 1] == '/')
            {
                while (index < result.Length && result[index] != '\r' && result[index] != '\n')
                    result[index++] = ' ';
                index--;
            }
            else if (result[index + 1] == '*')
            {
                result[index++] = ' ';
                result[index] = ' ';
                while (++index < result.Length)
                {
                    if (result[index] == '*' && index + 1 < result.Length && result[index + 1] == '/')
                    {
                        result[index] = ' ';
                        result[++index] = ' ';
                        break;
                    }
                    if (result[index] != '\r' && result[index] != '\n')
                        result[index] = ' ';
                }
            }
        }
        return new string(result);
    }

    private void ValidateRemotePackageReference(
        string repositoryRoot,
        IReadOnlyCollection<string> packageLockRoots,
        PbxObject item)
    {
        var repositoryUrl = ReadPbxScalar(item.Body, "repositoryURL")?.Trim();
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            throw new InvalidOperationException("Remote Swift package reference is missing repositoryURL.");

        var exactRevision = Regex.IsMatch(
            item.Body,
            "(?s)requirement\\s*=\\s*\\{.*?kind\\s*=\\s*revision\\s*;.*?revision\\s*=\\s*[\"']?[A-Fa-f0-9]{40}[\"']?\\s*;",
            RegexOptions.CultureInvariant);
        var locks = FindTrackedPackageLocks(repositoryRoot, packageLockRoots, repositoryUrl!);
        if (locks.Length == 0 && !exactRevision)
        {
            throw new InvalidOperationException(
                $"Remote Swift package '{repositoryUrl}' must be bound by a tracked Package.resolved lock or an exact 40-character revision.");
        }
        foreach (var packageLock in locks)
            EnsureTrackedFile(repositoryRoot, packageLock, "Swift package resolution lock");
    }

    private string[] FindTrackedPackageLocks(
        string repositoryRoot,
        IReadOnlyCollection<string> searchRoots,
        string dependencyIdentity)
    {
        return RunGit(repositoryRoot, "ls-files", "-z")
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static path => Path.GetFileName(path).Equals("Package.resolved", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFullPath(Path.Combine(repositoryRoot, path)))
            .Where(path => searchRoots.Any(root => IsPathAtOrWithin(path, root)))
            .Where(path => PackageLockBindsDependency(path, dependencyIdentity))
            .ToArray();
    }

    private static bool PackageLockBindsDependency(string path, string dependencyIdentity)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Swift package resolution lock is not valid JSON: {path}", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            JsonElement pins;
            if (root.TryGetProperty("pins", out pins) ||
                (root.TryGetProperty("object", out var legacyObject) &&
                 legacyObject.TryGetProperty("pins", out pins)))
            {
                if (pins.ValueKind != JsonValueKind.Array)
                    return false;
                foreach (var pin in pins.EnumerateArray())
                {
                    if (!PackagePinMatchesIdentity(pin, dependencyIdentity) ||
                        !pin.TryGetProperty("state", out var state))
                    {
                        continue;
                    }

                    var revision = ReadJsonString(state, "revision");
                    if (!string.IsNullOrWhiteSpace(revision) &&
                        revision!.Length == 40 &&
                        revision.All(Uri.IsHexDigit))
                    {
                        return true;
                    }

                    if (!LooksLikeRepositoryLocation(dependencyIdentity) &&
                        !string.IsNullOrWhiteSpace(ReadJsonString(state, "version")))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool PackagePinMatchesIdentity(JsonElement pin, string dependencyIdentity)
    {
        var location = ReadJsonString(pin, "location") ?? ReadJsonString(pin, "repositoryURL");
        if (LooksLikeRepositoryLocation(dependencyIdentity))
        {
            return !string.IsNullOrWhiteSpace(location) &&
                   NormalizePackageLocation(location!).Equals(
                       NormalizePackageLocation(dependencyIdentity),
                       StringComparison.OrdinalIgnoreCase);
        }

        var identity = ReadJsonString(pin, "identity") ?? ReadJsonString(pin, "package") ?? location;
        return !string.IsNullOrWhiteSpace(identity) &&
               identity!.Trim().Equals(dependencyIdentity.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool LooksLikeRepositoryLocation(string value)
        => value.Contains("://", StringComparison.Ordinal) ||
           value.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
           value.IndexOf('/') >= 0 ||
           value.EndsWith(".git", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePackageLocation(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        return normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - 4)
            : normalized;
    }

    private static string[] ResolvePackageLockSearchRoots(
        string projectMetadataPath,
        IReadOnlyCollection<string> metadataPaths)
    {
        var projectContainer = Path.GetDirectoryName(projectMetadataPath)!;
        var roots = new HashSet<string>(GetPathComparer())
        {
            Path.GetDirectoryName(projectContainer)!
        };
        var knownMetadata = new HashSet<string>(metadataPaths.Select(Path.GetFullPath), GetPathComparer());
        foreach (var workspaceMetadata in knownMetadata.Where(path =>
                     path.EndsWith("contents.xcworkspacedata", StringComparison.OrdinalIgnoreCase) &&
                     File.Exists(path)))
        {
            if (WorkspaceReferencesContainer(
                    workspaceMetadata,
                    projectContainer,
                    knownMetadata,
                    new HashSet<string>(GetPathComparer())))
            {
                roots.Add(Path.GetDirectoryName(workspaceMetadata)!);
            }
        }

        return roots.ToArray();
    }

    private static bool WorkspaceReferencesContainer(
        string workspaceMetadata,
        string targetContainer,
        ISet<string> knownMetadata,
        ISet<string> visited)
    {
        var normalizedMetadata = Path.GetFullPath(workspaceMetadata);
        if (!visited.Add(normalizedMetadata))
            return false;

        var workspaceContainer = Path.GetDirectoryName(normalizedMetadata)!;
        var workspaceRoot = Path.GetDirectoryName(workspaceContainer)!;
        var document = XDocument.Load(normalizedMetadata, LoadOptions.None);
        foreach (var candidate in EnumerateWorkspaceReferences(document.Root, workspaceRoot, workspaceRoot))
        {
            var normalizedCandidate = Path.GetFullPath(candidate);
            if (GetPathComparer().Equals(normalizedCandidate, Path.GetFullPath(targetContainer)))
                return true;
            if (!normalizedCandidate.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase))
                continue;

            var nestedMetadata = Path.Combine(normalizedCandidate, "contents.xcworkspacedata");
            if (knownMetadata.Contains(nestedMetadata) &&
                WorkspaceReferencesContainer(nestedMetadata, targetContainer, knownMetadata, visited))
            {
                return true;
            }
        }

        return false;
    }

    private void ValidateResolvedProjectInput(
        string repositoryRoot,
        string candidate,
        PbxObject item,
        string metadataPath,
        IReadOnlyCollection<string> generatedOutputPaths,
        ISet<string> buildFileReferences)
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
        if (File.Exists(candidate))
            EnsureTrackedFile(repositoryRoot, candidate, $"Xcode {item.Isa} input");
        else if (Directory.Exists(candidate))
        {
            if (directoryFileReferenceIsBuilt ||
                item.Isa.Equals("XCVersionGroup", StringComparison.OrdinalIgnoreCase) ||
                item.Isa.Equals("PBXFileSystemSynchronizedRootGroup", StringComparison.OrdinalIgnoreCase))
            {
                EnsureTrackedDirectoryTree(repositoryRoot, candidate, $"Xcode {item.Isa} input");
            }
            else
            {
                EnsureNoLinkedTraversal(repositoryRoot, candidate, $"Xcode {item.Isa} input");
            }
        }
        else if (Path.IsPathRooted(item.Path ?? string.Empty) ||
                 (item.Path ?? string.Empty).Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new FileNotFoundException(
                $"Xcode project references a missing explicit path that cannot be proven: {candidate} ({metadataPath})",
                candidate);
        }
    }

    private void EnsureTrackedDirectoryTree(string repositoryRoot, string path, string name)
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
            }
        }
    }

    private static string ResolveBuildSettingPath(string projectDirectory, string value, string key)
    {
        var expanded = value.Trim();
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
