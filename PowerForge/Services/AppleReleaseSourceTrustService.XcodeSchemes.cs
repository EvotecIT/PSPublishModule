using System.Xml.Linq;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private XcodeSchemeTargetScope EnsureTrackedSharedScheme(
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
        return ValidateScheme(repositoryRoot, candidates[0], metadataPaths);
    }

    private XcodeSchemeTargetScope ValidateScheme(
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

        var targets = new List<XcodeTargetReference>();
        foreach (var buildableReference in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals(
                         "BuildActionEntry",
                         StringComparison.Ordinal))
                     .SelectMany(entry => entry.Descendants().Where(element =>
                         element.Name.LocalName.Equals(
                             "BuildableReference",
                             StringComparison.Ordinal))))
        {
            var blueprintIdentifier = buildableReference
                .Attribute("BlueprintIdentifier")?.Value;
            var referencedContainer = buildableReference
                .Attribute("ReferencedContainer")?.Value;
            if (string.IsNullOrWhiteSpace(blueprintIdentifier) ||
                string.IsNullOrWhiteSpace(referencedContainer))
            {
                throw new InvalidOperationException(
                    $"Xcode scheme build entries must identify a target and its project container: {schemePath}");
            }

            var targetContainer = ResolveSchemeContainer(
                referencedContainer!,
                containerRoot);
            if (!targetContainer.EndsWith(
                    ".xcodeproj",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Xcode scheme build target references must identify an Xcode project: {targetContainer}");
            }
            var targetMetadataPath = Path.GetFullPath(Path.Combine(
                targetContainer,
                "project.pbxproj"));
            if (!knownMetadata.Contains(targetMetadataPath))
            {
                throw new InvalidOperationException(
                    $"Xcode scheme build target is outside the validated project/workspace graph: {targetContainer}");
            }
            targets.Add(new XcodeTargetReference(
                targetMetadataPath,
                ParsePbxObjectIdentifier(
                    blueprintIdentifier!,
                    "scheme BlueprintIdentifier")));
        }

        return new XcodeSchemeTargetScope(
            targets.Count > 0,
            targets);
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
}
