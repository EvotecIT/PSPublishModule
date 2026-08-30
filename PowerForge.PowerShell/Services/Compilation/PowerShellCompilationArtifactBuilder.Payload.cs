namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static PowerShellCompilationArtifactFile[] CopyPlannedPayload(
        string primaryArtifactPath,
        string artifactName,
        IEnumerable<PowerShellCompilationDependency> dependencies,
        IEnumerable<PowerShellCompilationArtifactFile> existingFiles)
    {
        var plannedDependencies = dependencies.ToArray();
        var payloadRoot = Path.GetDirectoryName(Path.GetFullPath(primaryArtifactPath))
                          ?? throw new InvalidOperationException("The staged artifact has no containing directory.");
        var existingArtifacts = existingFiles
            .GroupBy(static file => Path.GetFullPath(file.Path), PowerShellCompilationPathSafety.PathComparer)
            .ToDictionary(static group => group.Key, static group => group.First(), PowerShellCompilationPathSafety.PathComparer);
        var existing = existingArtifacts.Keys.ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var copied = new List<PowerShellCompilationArtifactFile>();
        foreach (var dependency in plannedDependencies.Where(static dependency =>
                     dependency.Exists &&
                     dependency.SourcePath is not null &&
                     dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent &&
                     dependency.Selection is PowerShellCompilationDependencySelection.Required or
                         PowerShellCompilationDependencySelection.ExplicitInclude or
                         PowerShellCompilationDependencySelection.Inferred or
                         PowerShellCompilationDependencySelection.PolicyInclude))
        {
            var relativePath = PowerShellCompiledModuleManifest.NormalizeManifestRelativePath(dependency.RelativePath);
            var targetPath = Path.GetFullPath(Path.Combine(payloadRoot, relativePath));
            PowerShellCompilationPathSafety.EnsureContained(
                payloadRoot,
                targetPath,
                $"Planned payload '{dependency.RelativePath}' escapes the generated artifact root.");
            EnsurePayloadDoesNotOccupyGeneratedNamespace(relativePath, artifactName);
            if (existing.Contains(targetPath))
            {
                // The hybrid composer may already have produced a deliberately transformed
                // version of a source dependency. Trust that explicit generated owner, not
                // mere membership in the input graph; every other collision requires byte identity.
                if ((existingArtifacts.TryGetValue(targetPath, out var existingArtifact) &&
                     (existingArtifact.Role.Equals("GeneratedModuleDependency", StringComparison.Ordinal) ||
                      existingArtifact.Role.Equals("PrimaryModule", StringComparison.Ordinal) &&
                      dependency.Kind == PowerShellCompilationDependencyKind.PowerShellSource)) ||
                    FilesHaveSameContent(dependency.SourcePath!, targetPath))
                    continue;
                throw new InvalidOperationException($"Planned payload '{dependency.RelativePath}' collides with a generated artifact.");
            }
            if (File.Exists(targetPath))
                throw new InvalidOperationException($"Planned payload '{dependency.RelativePath}' collides with a generated artifact.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? payloadRoot);
            File.Copy(dependency.SourcePath!, targetPath, overwrite: false);
            existing.Add(targetPath);
            copied.Add(CreateArtifactFile(targetPath, GetPayloadRole(dependency.Kind)));
        }
        return copied.ToArray();
    }

    private static void EnsurePayloadDoesNotOccupyGeneratedNamespace(string relativePath, string artifactName)
    {
        var normalized = relativePath.Replace('\\', '/');
        var generatedDirectory = artifactName + ".generated/";
        if (normalized.Equals(artifactName, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(artifactName + ".exe", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(artifactName + ".dll", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(artifactName + ".pdb", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(artifactName + ".generated", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals(artifactName + ".powerforge-compilation.json", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(generatedDirectory, StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("." + artifactName + ".artifact-publish.lock", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("." + artifactName + ".artifact-staging-", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("." + artifactName + ".artifact-backup-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Planned payload '{relativePath}' overlaps the generated artifact or publication-control namespace.");
        }
    }

    private static bool FilesHaveSameContent(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (!firstInfo.Exists || !secondInfo.Exists || firstInfo.Length != secondInfo.Length)
            return false;
        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        var firstBuffer = new byte[81920];
        var secondBuffer = new byte[81920];
        while (true)
        {
            var firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            var secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
            if (firstRead != secondRead) return false;
            if (firstRead == 0) return true;
            if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
                return false;
        }
    }

    private static string GetPayloadRole(PowerShellCompilationDependencyKind kind)
        => kind switch
        {
            PowerShellCompilationDependencyKind.ManagedAssembly => "ManagedDependency",
            PowerShellCompilationDependencyKind.NativeLibrary => "NativeDependency",
            PowerShellCompilationDependencyKind.JavaScript => "ModuleJavaScript",
            PowerShellCompilationDependencyKind.StyleSheet => "ModuleStyleSheet",
            PowerShellCompilationDependencyKind.TypeData => "ModuleTypeData",
            PowerShellCompilationDependencyKind.FormatData => "ModuleFormatData",
            _ => "ModuleResource"
        };
}
