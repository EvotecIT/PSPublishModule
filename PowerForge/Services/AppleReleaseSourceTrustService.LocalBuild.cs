namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    internal void ValidateLocalBuildInputContainment(
        string repositoryRoot,
        string projectPath)
    {
        lock (_validationGate)
        {
            ResetValidationState();
            _inspectRemotePackageSource = false;
            try
            {
                var root = Path.GetFullPath(repositoryRoot);
                var configuredProjectPath = Path.GetFullPath(projectPath);
                EnsurePathWithinRepository(
                    root,
                    configuredProjectPath,
                    "Local Apple ProjectPath");

                string metadataPath;
                if (File.Exists(configuredProjectPath))
                {
                    metadataPath = configuredProjectPath;
                }
                else if (configuredProjectPath.EndsWith(
                             ".xcworkspace",
                             StringComparison.OrdinalIgnoreCase))
                {
                    metadataPath = Path.Combine(
                        configuredProjectPath,
                        "contents.xcworkspacedata");
                }
                else
                {
                    metadataPath = Path.Combine(
                        configuredProjectPath,
                        "project.pbxproj");
                }

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

                foreach (var projectMetadata in metadataPaths.Where(path =>
                             path.EndsWith(
                                 "project.pbxproj",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    ValidateProjectGraph(
                        root,
                        projectMetadata,
                        metadataPaths,
                        Array.Empty<string>(),
                        inspectRemotePackageSource: false);
                }
            }
            finally
            {
                ResetValidationState();
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
