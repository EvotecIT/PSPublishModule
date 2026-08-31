namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static Dictionary<string, ISet<string>> ResolveExecutionMetadataScopes(
        IReadOnlyCollection<string> metadataPaths,
        IReadOnlyCollection<XcodeTargetReference> selectedTargets)
    {
        var projects = metadataPaths
            .Where(path => path.EndsWith(
                "project.pbxproj",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Distinct(GetPathComparer())
            .ToDictionary(
                static path => path,
                static path => new XcodeExecutionProject(
                    path,
                    ParsePbxObjects(File.ReadAllText(path))),
                GetPathComparer());
        var scopes = projects.Keys.ToDictionary(
            static path => path,
            static _ => (ISet<string>)new HashSet<string>(
                StringComparer.OrdinalIgnoreCase),
            GetPathComparer());
        var pending = new Queue<XcodeTargetReference>(selectedTargets);
        var inspectedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var reference = pending.Dequeue();
            if (!projects.TryGetValue(reference.MetadataPath, out var project))
            {
                throw new InvalidOperationException(
                    $"Xcode scheme target references unknown project metadata: {reference.MetadataPath}");
            }
            var targetKey = reference.MetadataPath + "\0" + reference.TargetId;
            if (!inspectedTargets.Add(targetKey))
                continue;
            if (!project.Objects.TryGetValue(reference.TargetId, out var target) ||
                !IsXcodeBuildTarget(target.Isa))
            {
                throw new InvalidOperationException(
                    $"Xcode scheme or target dependency references unknown build target '{reference.TargetId}': {reference.MetadataPath}");
            }

            var scope = scopes[reference.MetadataPath];
            scope.Add(target.Id);
            foreach (var phaseId in ReadPbxReferences(
                         target.Body,
                         "buildPhases"))
            {
                if (!project.Objects.TryGetValue(phaseId, out var phase))
                {
                    throw new InvalidOperationException(
                        $"Xcode target '{target.Id}' references unknown build phase '{phaseId}': {reference.MetadataPath}");
                }
                scope.Add(phaseId);
                AddImplicitTargetDependencies(
                    project,
                    phase,
                    projects,
                    pending);
            }

            foreach (var buildRuleId in ReadPbxReferences(
                         target.Body,
                         "buildRules"))
            {
                if (!project.Objects.ContainsKey(buildRuleId))
                {
                    throw new InvalidOperationException(
                        $"Xcode target '{target.Id}' references unknown build rule '{buildRuleId}': {reference.MetadataPath}");
                }
                scope.Add(buildRuleId);
            }

            foreach (var dependencyId in ReadPbxReferences(
                         target.Body,
                         "dependencies"))
            {
                if (!project.Objects.TryGetValue(dependencyId, out var dependency) ||
                    !dependency.Isa.Equals(
                        "PBXTargetDependency",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Xcode target '{target.Id}' references invalid target dependency '{dependencyId}': {reference.MetadataPath}");
                }
                pending.Enqueue(ResolveTargetDependency(
                    project,
                    dependency,
                    projects));
            }
        }

        return scopes;
    }

    private static void AddImplicitTargetDependencies(
        XcodeExecutionProject project,
        PbxObject phase,
        IReadOnlyDictionary<string, XcodeExecutionProject> projects,
        Queue<XcodeTargetReference> pending)
    {
        foreach (var buildFileId in ReadPbxReferences(phase.Body, "files"))
        {
            if (!project.Objects.TryGetValue(buildFileId, out var buildFile) ||
                !buildFile.Isa.Equals("PBXBuildFile", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Xcode build phase '{phase.Id}' references invalid build file '{buildFileId}': {project.MetadataPath}");
            }

            var fileReference = ReadOptionalPbxObjectIdentifier(
                buildFile.Body,
                "fileRef",
                $"build file reference '{buildFileId}'");
            if (fileReference is null)
                continue;
            if (project.ProductTargets.TryGetValue(
                    fileReference,
                    out var localTarget))
            {
                pending.Enqueue(new XcodeTargetReference(
                    project.MetadataPath,
                    localTarget));
                continue;
            }
            if (!project.Objects.TryGetValue(fileReference, out var reference) ||
                !reference.Isa.Equals(
                    "PBXReferenceProxy",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remoteRef = ReadOptionalPbxObjectIdentifier(
                reference.Body,
                "remoteRef",
                $"reference proxy '{reference.Id}'");
            if (remoteRef is null ||
                !project.Objects.TryGetValue(remoteRef, out var proxy))
            {
                throw new InvalidOperationException(
                    $"Xcode reference proxy '{reference.Id}' cannot resolve its remote target: {project.MetadataPath}");
            }
            pending.Enqueue(ResolveProxyTarget(
                project,
                proxy,
                projects));
        }
    }

    private static XcodeTargetReference ResolveTargetDependency(
        XcodeExecutionProject project,
        PbxObject dependency,
        IReadOnlyDictionary<string, XcodeExecutionProject> projects)
    {
        var directTarget = ReadOptionalPbxObjectIdentifier(
            dependency.Body,
            "target",
            $"target dependency '{dependency.Id}'");
        if (directTarget is not null)
        {
            return new XcodeTargetReference(
                project.MetadataPath,
                directTarget);
        }

        var targetProxy = ReadOptionalPbxObjectIdentifier(
            dependency.Body,
            "targetProxy",
            $"target dependency '{dependency.Id}'");
        if (targetProxy is null ||
            !project.Objects.TryGetValue(targetProxy, out var proxy))
        {
            throw new InvalidOperationException(
                $"Xcode target dependency '{dependency.Id}' cannot resolve its target: {project.MetadataPath}");
        }
        return ResolveProxyTarget(project, proxy, projects);
    }

    private static XcodeTargetReference ResolveProxyTarget(
        XcodeExecutionProject sourceProject,
        PbxObject proxy,
        IReadOnlyDictionary<string, XcodeExecutionProject> projects)
    {
        if (!proxy.Isa.Equals(
                "PBXContainerItemProxy",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Xcode target proxy '{proxy.Id}' is not a PBXContainerItemProxy: {sourceProject.MetadataPath}");
        }
        var targetId = ReadOptionalPbxObjectIdentifier(
            proxy.Body,
            "remoteGlobalIDString",
            $"container item proxy '{proxy.Id}'")
            ?? throw new InvalidOperationException(
                $"Xcode target proxy '{proxy.Id}' is missing remoteGlobalIDString: {sourceProject.MetadataPath}");
        var candidates = projects.Values
            .Where(project => project.Objects.TryGetValue(
                targetId,
                out var target) && IsXcodeBuildTarget(target.Isa))
            .Select(project => project.MetadataPath)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidOperationException(
                $"Xcode target proxy '{proxy.Id}' must resolve one unambiguous target '{targetId}' inside the validated graph: {sourceProject.MetadataPath}");
        }
        return new XcodeTargetReference(candidates[0], targetId);
    }

    private static string? ReadOptionalPbxObjectIdentifier(
        string body,
        string name,
        string context)
    {
        var value = ReadPbxScalar(body, name);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ParsePbxObjectIdentifier(value!, context);
    }

    private static bool IsXcodeBuildTarget(string isa)
        => isa.Equals("PBXNativeTarget", StringComparison.OrdinalIgnoreCase) ||
           isa.Equals("PBXAggregateTarget", StringComparison.OrdinalIgnoreCase) ||
           isa.Equals("PBXLegacyTarget", StringComparison.OrdinalIgnoreCase);

    private sealed class XcodeExecutionProject
    {
        internal XcodeExecutionProject(
            string metadataPath,
            IReadOnlyDictionary<string, PbxObject> objects)
        {
            MetadataPath = metadataPath;
            Objects = objects;
            ProductTargets = objects.Values
                .Where(static value => value.Isa.Equals(
                    "PBXNativeTarget",
                    StringComparison.OrdinalIgnoreCase))
                .Select(target => new
                {
                    Target = target.Id,
                    Product = ReadOptionalPbxObjectIdentifier(
                        target.Body,
                        "productReference",
                        $"native target '{target.Id}'")
                })
                .Where(static value => value.Product is not null)
                .ToDictionary(
                    static value => value.Product!,
                    static value => value.Target,
                    StringComparer.OrdinalIgnoreCase);
        }

        internal string MetadataPath { get; }

        internal IReadOnlyDictionary<string, PbxObject> Objects { get; }

        internal IReadOnlyDictionary<string, string> ProductTargets { get; }
    }

    private sealed class XcodeSchemeTargetScope
    {
        internal XcodeSchemeTargetScope(
            bool isComplete,
            IReadOnlyCollection<XcodeTargetReference> targets)
        {
            IsComplete = isComplete;
            Targets = targets;
        }

        internal bool IsComplete { get; }

        internal IReadOnlyCollection<XcodeTargetReference> Targets { get; }
    }

    private sealed class XcodeTargetReference
    {
        internal XcodeTargetReference(
            string metadataPath,
            string targetId)
        {
            MetadataPath = Path.GetFullPath(metadataPath);
            TargetId = targetId;
        }

        internal string MetadataPath { get; }

        internal string TargetId { get; }
    }
}
