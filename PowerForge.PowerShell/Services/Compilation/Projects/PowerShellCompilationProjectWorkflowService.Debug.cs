using System.Reflection.Metadata;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    private static readonly Guid Sha256DocumentHashAlgorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    /// <summary>Resolves current authored source locations for an authenticated executable or module PDB.</summary>
    public PowerShellCompilationProjectDebugPlan CreateDebugPlan(string projectPath, string? targetName = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var selected = SelectArtifacts(context, targetName is null ? null : new[] { targetName });
        if (selected.Length != 1)
            throw new InvalidOperationException("Debug plan requires exactly one target; select it with --target.");
        var artifact = selected[0];
        if (artifact.Target.ArtifactKind is not (PowerShellCompilationArtifactKind.Executable or PowerShellCompilationArtifactKind.BinaryModule) ||
            artifact.Target.Mode == PowerShellCompilationMode.Package)
            throw new InvalidOperationException("Debug plan requires a typed executable or binary module target.");

        var validated = ValidateBuildReceipt(context, artifact);
        var pdbPath = Path.ChangeExtension(validated.ArtifactPath, ".pdb");
        var pdbRelativePath = FrameworkCompatibility.GetRelativePath(validated.OutputRoot, pdbPath).Replace('\\', '/');
        if (!validated.Files.Any(file => file.Path.Equals(pdbRelativePath, StringComparison.Ordinal)))
            throw new InvalidDataException("The authenticated artifact set does not contain a matching portable PDB.");
        var input = ResolveInput(context, artifact);
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(input.SourcePath)) ?? context.Root;
        var sourceById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourcePath in new[] { input.SourcePath }.Concat(input.CompilationSourceFiles))
        {
            var fullPath = Path.GetFullPath(sourcePath);
            sourceById[PowerShellSourceParser.CreateDocumentId(fullPath, sourceRoot)] = fullPath;
        }
        if (artifact.Target.ArtifactKind == PowerShellCompilationArtifactKind.Executable &&
            artifact.Target.Mode == PowerShellCompilationMode.Strict)
            sourceById[PowerShellSourceParser.CreateDocumentId(input.SourcePath + ".powerforge-entry.ps1", sourceRoot)] =
                Path.GetFullPath(input.SourcePath);

        var sourceFileMap = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var stream = File.OpenRead(pdbPath))
        using (var provider = MetadataReaderProvider.FromPortablePdbStream(stream))
        {
            var reader = provider.GetMetadataReader();
            foreach (var handle in reader.Documents)
            {
                var document = reader.GetDocument(handle);
                var name = reader.GetString(document.Name);
                if (!name.StartsWith("/_/src/", StringComparison.Ordinal) || name.Length != "/_/src/".Length + 64 ||
                    !name.Skip("/_/src/".Length).All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
                    continue;
                var documentId = name.Substring("/_/src/".Length);
                if (!sourceById.TryGetValue(documentId, out var sourcePath))
                    throw new InvalidDataException($"Portable PDB refers to unknown PowerShell document '{documentId}'.");
                if (document.HashAlgorithm.IsNil || reader.GetGuid(document.HashAlgorithm) != Sha256DocumentHashAlgorithm || document.Hash.IsNil)
                    throw new InvalidDataException($"Portable PDB has no SHA-256 checksum for PowerShell document '{documentId}'.");
                var checksum = BitConverter.ToString(reader.GetBlobBytes(document.Hash)).Replace("-", string.Empty);
                if (!checksum.Equals(PowerShellCompilationProjectManifestService.ComputeSha256(sourcePath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Authored source differs from the PDB checksum for PowerShell document '{documentId}'; rebuild before debugging.");
                sourceFileMap.Add(name, sourcePath);
            }
        }
        if (sourceFileMap.Count == 0)
            throw new InvalidDataException("The authenticated portable PDB contains no mapped typed PowerShell source; inspect project explain.");
        return new PowerShellCompilationProjectDebugPlan
        {
            TargetName = artifact.Name,
            ArtifactKind = artifact.Target.ArtifactKind.ToString(),
            ArtifactPath = validated.ArtifactPath,
            PdbPath = pdbPath,
            WorkingDirectory = context.Root,
            SourceFileMap = sourceFileMap
        };
    }
}
