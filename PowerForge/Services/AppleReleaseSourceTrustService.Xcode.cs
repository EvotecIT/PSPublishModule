using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static readonly HashSet<string> ExternalXcodeSourceTrees = new(StringComparer.OrdinalIgnoreCase)
    {
        "BUILT_PRODUCTS_DIR", "SDKROOT", "DEVELOPER_DIR"
    };

    private void ValidateXcodeBuildGraph(
        string repositoryRoot,
        string projectRoot,
        IReadOnlyCollection<AppleAppConfiguration> apps,
        IReadOnlyCollection<string> metadataPaths)
    {
        foreach (var app in apps.Where(static value => value.Enabled && !string.IsNullOrWhiteSpace(value.ProjectPath)))
            EnsureTrackedSharedScheme(repositoryRoot, projectRoot, app, metadataPaths);

        foreach (var metadataPath in metadataPaths.Where(path =>
                     path.EndsWith("project.pbxproj", StringComparison.OrdinalIgnoreCase) && File.Exists(path)))
        {
            ValidateProjectGraph(repositoryRoot, metadataPath);
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

    private void ValidateProjectGraph(string repositoryRoot, string metadataPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetDirectoryName(metadataPath)!)!;
        var objects = ParsePbxObjects(File.ReadAllText(metadataPath));
        var parents = BuildPbxParentMap(objects);
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in objects.Values)
        {
            if (item.Isa.Equals("PBXShellScriptBuildPhase", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"PBX shell-script build phases are not accepted for exact-source checkpoints because arbitrary runtime inputs cannot be proven: {metadataPath}");
            }

            if (item.Isa.Equals("XCLocalSwiftPackageReference", StringComparison.OrdinalIgnoreCase))
            {
                ValidateLocalPackageReference(repositoryRoot, projectDirectory, item);
                continue;
            }

            if (!IsPathBearingPbxObject(item.Isa))
                continue;

            var candidate = ResolvePbxObjectPath(projectDirectory, item.Id, objects, parents, cache, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (candidate is null)
                continue;
            ValidateResolvedProjectInput(repositoryRoot, candidate, item, metadataPath);
        }
    }

    private void ValidateLocalPackageReference(
        string repositoryRoot,
        string projectDirectory,
        PbxObject item)
    {
        var relativePath = ReadPbxScalar(item.Body, "relativePath");
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("Local Swift package reference is missing relativePath.");
        var packageRoot = ResolvePbxPath(projectDirectory, relativePath!, "local Swift package");
        EnsureDirectoryWithinRepository(repositoryRoot, packageRoot, "Xcode local Swift package");
        EnsureTrackedFile(repositoryRoot, Path.Combine(packageRoot, "Package.swift"), "Xcode local Swift package manifest");
    }

    private void ValidateResolvedProjectInput(
        string repositoryRoot,
        string candidate,
        PbxObject item,
        string metadataPath)
    {
        EnsurePathWithinRepository(repositoryRoot, candidate, $"Xcode {item.Isa} input");
        if (File.Exists(candidate))
            EnsureTrackedFile(repositoryRoot, candidate, $"Xcode {item.Isa} input");
        else if (Directory.Exists(candidate))
            EnsureNoLinkedTraversal(repositoryRoot, candidate, $"Xcode {item.Isa} input");
        else if (Path.IsPathRooted(item.Path ?? string.Empty) ||
                 (item.Path ?? string.Empty).Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new FileNotFoundException(
                $"Xcode project references a missing explicit path that cannot be proven: {candidate} ({metadataPath})",
                candidate);
        }
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

    private static Dictionary<string, PbxObject> ParsePbxObjects(string text)
    {
        var objects = new Dictionary<string, PbxObject>(StringComparer.OrdinalIgnoreCase);
        foreach (Match start in Regex.Matches(
                     text,
                     "(?m)^[ \\t]*(?<id>[A-Fa-f0-9]{8,32})(?:[ \\t]+/\\*.*?\\*/)?[ \\t]*=[ \\t]*\\{",
                     RegexOptions.CultureInvariant))
        {
            var id = start.Groups["id"].Value;
            var openingBrace = text.IndexOf('{', start.Index + start.Length - 1);
            var closingBrace = FindMatchingPbxBrace(text, openingBrace);
            var body = text.Substring(openingBrace + 1, closingBrace - openingBrace - 1);
            var isa = ReadPbxScalar(body, "isa");
            if (string.IsNullOrWhiteSpace(isa))
                continue;
            objects[id] = new PbxObject
            {
                Id = id,
                Isa = isa!,
                Path = ReadPbxScalar(body, "path"),
                SourceTree = ReadPbxScalar(body, "sourceTree"),
                Body = body
            };
        }
        return objects;
    }

    private static int FindMatchingPbxBrace(string text, int openingBrace)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var index = openingBrace; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (inLineComment)
            {
                if (current == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    index++;
                }
                continue;
            }
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                index++;
            }
            else if (current == '/' && next == '*')
            {
                inBlockComment = true;
                index++;
            }
            else if (current == '"') inString = true;
            else if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return index;
        }
        throw new InvalidOperationException("Xcode project contains an unterminated PBX object.");
    }

    private static string? ReadPbxScalar(string body, string name)
    {
        var match = Regex.Match(
            body,
            "(?:^|[\\r\\n;])[ \\t]*" + Regex.Escape(name) +
            "[ \\t]*=[ \\t]*(?:\\\"(?<quoted>(?:\\\\.|[^\\\"])*)\\\"|(?<bare>[^;\\r\\n]+))[ \\t]*;",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var value = match.Groups["quoted"].Success
            ? UnescapePbxString(match.Groups["quoted"].Value)
            : match.Groups["bare"].Value.Trim();
        return value;
    }

    private static string[] ReadPbxReferences(string body, string name)
    {
        var match = Regex.Match(
            body,
            "(?:^|[\\r\\n;])[ \\t]*" + Regex.Escape(name) + "[ \\t]*=[ \\t]*\\((?<items>.*?)\\)[ \\t]*;",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
            return Array.Empty<string>();
        return Regex.Matches(match.Groups["items"].Value, "(?m)^[ \\t]*(?<id>[A-Fa-f0-9]{8,32})", RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(value => value.Groups["id"].Value)
            .ToArray();
    }

    private static string UnescapePbxString(string value)
    {
        var builder = new StringBuilder(value.Length);
        var escaped = false;
        foreach (var character in value)
        {
            if (escaped)
            {
                builder.Append(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else
            {
                builder.Append(character);
            }
        }
        if (escaped) builder.Append('\\');
        return builder.ToString();
    }

    private sealed class PbxObject
    {
        internal string Id { get; set; } = string.Empty;

        internal string Isa { get; set; } = string.Empty;

        internal string? Path { get; set; }

        internal string? SourceTree { get; set; }

        internal string Body { get; set; } = string.Empty;
    }
}
