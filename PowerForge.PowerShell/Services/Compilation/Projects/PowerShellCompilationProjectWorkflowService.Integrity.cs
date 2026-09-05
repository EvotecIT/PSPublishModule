using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    private static ValidatedProjectBuild ValidateBuildReceipt(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact)
    {
        var receipt = ReadBuildReceipt(context, artifact);
        if (!receipt.Succeeded || receipt.Manifest is null || string.IsNullOrWhiteSpace(receipt.ArtifactPath))
            throw new InvalidDataException("A successful build receipt with compiler evidence is required.");
        var outputRoot = context.Resolve(artifact.OutputDirectory);
        if (!Directory.Exists(outputRoot)) throw new DirectoryNotFoundException($"Artifact output is missing: {outputRoot}");
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(outputRoot, "Artifact output traverses a symbolic link or junction.");

        var expectedManifestPath = Path.Combine(outputRoot, GetArtifactName(context.Manifest, artifact) + ".powerforge-compilation.json");
        var receiptManifestPath = Path.GetFullPath(receipt.ManifestPath ?? throw new InvalidDataException("Build receipt has no compiler-manifest path."));
        if (!PowerShellCompilationPathSafety.PathEquals(receiptManifestPath, expectedManifestPath))
            throw new InvalidDataException("Build receipt compiler-manifest path does not belong to the selected project target.");
        if (!File.Exists(expectedManifestPath)) throw new FileNotFoundException("Compiler manifest is missing.", expectedManifestPath);
        PowerShellCompilationPathSafety.EnsureNoLinks(outputRoot, expectedManifestPath, "Compiler manifest traverses a symbolic link or junction.");
        var manifest = ReadJson<PowerShellCompilationArtifactManifest>(expectedManifestPath);
        if (!CanonicalJsonSha256(manifest).Equals(CanonicalJsonSha256(receipt.Manifest), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Build receipt compiler evidence differs from the durable compiler manifest.");
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
        var environment = ReadEnvironment(context);

        var target = manifest.TargetContract is null
            ? throw new InvalidDataException("Compiler manifest has no target contract.")
            : PowerShellCompilationTargetContractService.Normalize(manifest.TargetContract);
        if (!target.ContractSha256.Equals(artifact.Target.ContractSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.Toolchain?.TargetContractSha256, artifact.Target.ContractSha256, StringComparison.OrdinalIgnoreCase) ||
            manifest.Kind != artifact.Target.ArtifactKind || manifest.Mode != artifact.Target.Mode ||
            !manifest.TargetFramework.Equals(artifact.Target.TargetFramework, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.RuntimeIdentifier ?? string.Empty, artifact.Target.RuntimeIdentifier ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Build receipt does not belong to the selected project target contract.");

        var dependencyLock = ReadJson<PowerShellCompilationDependencyGraph>(context.Resolve(artifact.DependencyLock));
        PowerShellCompilationDependencyLockHasher.EnsureValid(dependencyLock, artifact.Name);
        if (!dependencyLock.LockSha256.Equals(manifest.DependencyGraph?.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Built artifact dependency identity differs from the reviewed project lock.");
        var resolvedLock = environment.ResolvedLocks.Single(item =>
            item.TargetName.Equals(artifact.Name, StringComparison.Ordinal));
        if (!resolvedLock.Sha256.Equals(manifest.ResolvedPackageLockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Built artifact NuGet closure identity differs from the exact project restore lock.");
        var currentProviders = ResolveProviders(context, artifact);
        var currentInput = ResolveInput(context, artifact);
        var currentPlan = CreatePlan(context, artifact, currentInput, currentProviders.Providers, environment.PackageRoot);
        if (!currentPlan.CanProceed ||
            !string.Equals(currentPlan.DependencyGraph?.LockSha256, dependencyLock.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Current project sources or resources differ from the reviewed build dependency lock.");
        if (!string.IsNullOrWhiteSpace(artifact.ProviderLock))
        {
            var providerLock = ReadJson<PowerShellCompilationProviderLock>(context.Resolve(artifact.ProviderLock!));
            var providerHash = PowerShellCompilationProviderPackageReader.ComputeLockSha256(providerLock);
            if (!providerLock.LockSha256.Equals(providerHash, StringComparison.OrdinalIgnoreCase) ||
                !providerLock.LockSha256.Equals(manifest.ProviderLock?.LockSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Built artifact provider identity differs from the reviewed project lock.");
            PowerShellCompilationProviderPackageReader.EnsureMatches(providerLock, currentProviders.Lock);
        }
        else if (manifest.ProviderLock is not null)
        {
            throw new InvalidDataException("Built artifact records providers that are absent from the selected project target.");
        }

        var artifactPath = Path.GetFullPath(receipt.ArtifactPath!);
        if (!PowerShellCompilationPathSafety.PathEquals(artifactPath, manifest.ArtifactPath))
            throw new InvalidDataException("Build receipt primary path differs from the compiler manifest.");
        PowerShellCompilationPathSafety.EnsureContained(outputRoot, artifactPath, "Build receipt primary path escapes the selected output directory.");
        PowerShellCompilationPathSafety.EnsureNoLinks(outputRoot, artifactPath, "Build receipt primary path traverses a symbolic link or junction.");

        var expectedFiles = (manifest.Files ?? Array.Empty<PowerShellCompilationArtifactFile>())
            .Select(file => ValidateManifestFile(outputRoot, file))
            .Append(CreateFileEvidence(outputRoot, expectedManifestPath))
            .OrderBy(static file => file.Path, StringComparer.Ordinal)
            .ToArray();
        if (expectedFiles.Length < 2) throw new InvalidDataException("Compiler manifest records no artifact payload.");
        var duplicate = expectedFiles.GroupBy(static file => file.Path, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Compiler manifest records duplicate artifact path '{duplicate.Key}'.");
        var actualFiles = Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
            .Where(path => IsArtifactPayloadFile(path, context, artifact))
            .Select(path => CreateFileEvidence(outputRoot, path))
            .OrderBy(static file => file.Path, StringComparer.Ordinal)
            .ToArray();
        if (!FileInventoriesEqual(expectedFiles, actualFiles))
            throw new InvalidDataException("Current artifact output inventory differs from the compiler build evidence: " +
                                           DescribeFileInventoryDifference(expectedFiles, actualFiles));
        var primary = actualFiles.SingleOrDefault(file =>
            PowerShellCompilationPathSafety.PathEquals(Path.Combine(outputRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)), artifactPath))
            ?? throw new InvalidDataException("Primary artifact is absent from the authenticated output inventory.");
        if (!primary.Sha256.Equals(manifest.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Primary built artifact differs from the compiler manifest hash.");

        return new ValidatedProjectBuild(
            receipt,
            manifest,
            outputRoot,
            artifactPath,
            actualFiles,
            ComputeArtifactSetSha256(actualFiles));
    }

    private static QualifiedProjectFile ValidateManifestFile(string outputRoot, PowerShellCompilationArtifactFile file)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.Path)) throw new InvalidDataException("Compiler manifest contains an incomplete artifact file.");
        var path = Path.GetFullPath(file.Path);
        PowerShellCompilationPathSafety.EnsureContained(outputRoot, path, "Compiler artifact file escapes the selected output directory.");
        PowerShellCompilationPathSafety.EnsureNoLinks(outputRoot, path, "Compiler artifact file traverses a symbolic link or junction.");
        if (!File.Exists(path)) throw new FileNotFoundException("Compiler artifact file is missing.", path);
        var actual = CreateFileEvidence(outputRoot, path);
        if (!actual.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) || actual.Size != file.SizeBytes)
            throw new InvalidDataException($"Compiler artifact file '{actual.Path}' differs from its build evidence.");
        return actual;
    }

    private static QualifiedProjectFile CreateFileEvidence(string root, string path)
    {
        var fullPath = Path.GetFullPath(path);
        PowerShellCompilationPathSafety.EnsureContained(root, fullPath, "Artifact file escapes its declared root.");
        PowerShellCompilationPathSafety.EnsureNoLinks(root, fullPath, "Artifact file traverses a symbolic link or junction.");
        return new QualifiedProjectFile
        {
            Path = FrameworkCompatibility.GetRelativePath(root, fullPath).Replace('\\', '/'),
            Sha256 = PowerShellCompilationProjectManifestService.ComputeSha256(fullPath),
            Size = new FileInfo(fullPath).Length
        };
    }

    private static bool FileInventoriesEqual(QualifiedProjectFile[] expected, QualifiedProjectFile[] actual)
        => expected.Length == actual.Length && expected.Where((file, index) =>
            !file.Path.Equals(actual[index].Path, StringComparison.Ordinal) ||
            !file.Sha256.Equals(actual[index].Sha256, StringComparison.OrdinalIgnoreCase) ||
            file.Size != actual[index].Size).Any() == false;

    private static string DescribeFileInventoryDifference(QualifiedProjectFile[] expected, QualifiedProjectFile[] actual)
    {
        var expectedByPath = expected.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        var actualByPath = actual.ToDictionary(static file => file.Path, StringComparer.Ordinal);
        var missing = expectedByPath.Keys.Except(actualByPath.Keys, StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal);
        var unexpected = actualByPath.Keys.Except(expectedByPath.Keys, StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal);
        var changed = expectedByPath.Keys.Intersect(actualByPath.Keys, StringComparer.Ordinal)
            .Where(path => !expectedByPath[path].Sha256.Equals(actualByPath[path].Sha256, StringComparison.OrdinalIgnoreCase) ||
                           expectedByPath[path].Size != actualByPath[path].Size)
            .OrderBy(static path => path, StringComparer.Ordinal);
        return "missing=[" + string.Join(",", missing) + "]; unexpected=[" + string.Join(",", unexpected) +
               "]; changed=[" + string.Join(",", changed) + "]";
    }

    private static string ComputeArtifactSetSha256(IEnumerable<QualifiedProjectFile> files)
        => CanonicalJsonSha256(files.OrderBy(static file => file.Path, StringComparer.Ordinal)
            .Select(static file => new { file.Path, file.Sha256, file.Size }).ToArray());

    internal static string ComputeQualifiedInstallationIdentity(
        string targetContractSha256,
        string artifactSetSha256)
        => Convert.ToBase64String(CanonicalJsonHash(new
            {
                TargetContractSha256 = targetContractSha256.ToLowerInvariant(),
                ArtifactSetSha256 = artifactSetSha256.ToLowerInvariant()
            }))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool IsArtifactPayloadFile(
        string path,
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact)
        => !Path.GetFileName(path).Equals(
            "." + GetArtifactName(context.Manifest, artifact) + ".artifact-publish.lock",
            StringComparison.OrdinalIgnoreCase);

    private static PowerShellCompilationProjectTestEvidence CreateTestEvidence(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact,
        ValidatedProjectBuild validated)
    {
        var evidence = new PowerShellCompilationProjectTestEvidence
        {
            ProjectSha256 = PowerShellCompilationProjectManifestService.ComputeSha256(context.ProjectPath),
            TargetName = artifact.Name,
            TargetContractSha256 = artifact.Target.ContractSha256,
            ArtifactSetSha256 = validated.ArtifactSetSha256,
            ArtifactSha256 = validated.Manifest.ArtifactSha256,
            DependencyLockSha256 = validated.Manifest.DependencyGraph?.LockSha256 ?? string.Empty,
            ProviderLockSha256 = validated.Manifest.ProviderLock?.LockSha256 ?? string.Empty,
            Outcome = "Passed"
        };
        evidence.EvidenceSha256 = ComputeTestEvidenceSha256(evidence);
        return evidence;
    }

    private static PowerShellCompilationProjectTestEvidence ReadTestEvidence(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact,
        ValidatedProjectBuild validated)
    {
        var path = context.Resolve($".powerforge/test/{artifact.Name}.json");
        if (!File.Exists(path)) throw new FileNotFoundException("Run project test before packing or installing.", path);
        var evidence = ReadJson<PowerShellCompilationProjectTestEvidence>(path);
        var projectSha = PowerShellCompilationProjectManifestService.ComputeSha256(context.ProjectPath);
        if (evidence.SchemaVersion != 1 || evidence.Outcome != "Passed" ||
            !evidence.ProjectSha256.Equals(projectSha, StringComparison.OrdinalIgnoreCase) ||
            !evidence.TargetName.Equals(artifact.Name, StringComparison.Ordinal) ||
            !evidence.TargetContractSha256.Equals(artifact.Target.ContractSha256, StringComparison.OrdinalIgnoreCase) ||
            !evidence.ArtifactSetSha256.Equals(validated.ArtifactSetSha256, StringComparison.OrdinalIgnoreCase) ||
            !evidence.ArtifactSha256.Equals(validated.Manifest.ArtifactSha256, StringComparison.OrdinalIgnoreCase) ||
            !evidence.DependencyLockSha256.Equals(validated.Manifest.DependencyGraph?.LockSha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !evidence.ProviderLockSha256.Equals(validated.Manifest.ProviderLock?.LockSha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
            !evidence.EvidenceSha256.Equals(ComputeTestEvidenceSha256(evidence), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Project test evidence does not authenticate the current project target and artifact set.");
        return evidence;
    }

    private static string ComputeTestEvidenceSha256(PowerShellCompilationProjectTestEvidence evidence)
        => CanonicalJsonSha256(new
        {
            evidence.SchemaVersion,
            evidence.ProjectSha256,
            evidence.TargetName,
            evidence.TargetContractSha256,
            evidence.ArtifactSetSha256,
            evidence.ArtifactSha256,
            evidence.DependencyLockSha256,
            evidence.ProviderLockSha256,
            evidence.Outcome
        });

    private static PowerShellCompilationQualifiedPackageDescriptor CreatePackageDescriptor(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact,
        ValidatedProjectBuild validated,
        PowerShellCompilationProjectTestEvidence testEvidence)
        => new()
        {
            Project = context.Manifest.Name,
            ProjectSha256 = PowerShellCompilationProjectManifestService.ComputeSha256(context.ProjectPath),
            SemanticProfile = context.Manifest.SemanticProfileId,
            TargetName = artifact.Name,
            Target = artifact.Target,
            DependencyLockSha256 = validated.Manifest.DependencyGraph?.LockSha256 ?? string.Empty,
            ProviderLockSha256 = validated.Manifest.ProviderLock?.LockSha256 ?? string.Empty,
            PublicAbiSha256 = validated.Manifest.PublicAbi?.Sha256 ?? string.Empty,
            ArtifactSha256 = validated.Manifest.ArtifactSha256,
            ArtifactSetSha256 = validated.ArtifactSetSha256,
            TestEvidenceSha256 = testEvidence.EvidenceSha256,
            Files = validated.Files
        };

    private static void ValidateQualifiedPackage(
        string packagePath,
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact,
        ValidatedProjectBuild validated,
        PowerShellCompilationProjectTestEvidence testEvidence)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var descriptorEntry = archive.GetEntry("powerforge-package.json")
            ?? throw new InvalidDataException("Qualified package has no descriptor.");
        PowerShellCompilationQualifiedPackageDescriptor descriptor;
        using (var stream = descriptorEntry.Open())
        {
            descriptor = JsonSerializer.Deserialize<PowerShellCompilationQualifiedPackageDescriptor>(stream, PowerShellCompilationProjectManifestService.JsonOptions)
                ?? throw new InvalidDataException("Qualified package descriptor is empty.");
        }
        var expected = CreatePackageDescriptor(context, artifact, validated, testEvidence);
        if (descriptor.SchemaVersion != 1 ||
            !descriptor.Project.Equals(expected.Project, StringComparison.Ordinal) ||
            !descriptor.ProjectSha256.Equals(expected.ProjectSha256, StringComparison.OrdinalIgnoreCase) ||
            !descriptor.SemanticProfile.Equals(expected.SemanticProfile, StringComparison.Ordinal) ||
            !descriptor.TargetName.Equals(expected.TargetName, StringComparison.Ordinal) ||
            !PowerShellCompilationTargetContractService.Normalize(descriptor.Target).ContractSha256.Equals(artifact.Target.ContractSha256, StringComparison.OrdinalIgnoreCase) ||
            !descriptor.DependencyLockSha256.Equals(expected.DependencyLockSha256, StringComparison.OrdinalIgnoreCase) ||
            !descriptor.ProviderLockSha256.Equals(expected.ProviderLockSha256, StringComparison.OrdinalIgnoreCase) ||
            !descriptor.PublicAbiSha256.Equals(expected.PublicAbiSha256, StringComparison.OrdinalIgnoreCase) ||
            !descriptor.ArtifactSha256.Equals(expected.ArtifactSha256, StringComparison.OrdinalIgnoreCase) ||
            !descriptor.ArtifactSetSha256.Equals(expected.ArtifactSetSha256, StringComparison.OrdinalIgnoreCase) ||
            !descriptor.TestEvidenceSha256.Equals(expected.TestEvidenceSha256, StringComparison.OrdinalIgnoreCase) ||
            !FileInventoriesEqual(expected.Files, descriptor.Files.OrderBy(static file => file.Path, StringComparer.Ordinal).ToArray()))
            throw new InvalidDataException("Qualified package descriptor differs from the current tested project artifact.");

        var allFileEntries = archive.Entries
            .Where(static entry => entry.FullName.Length > 0 && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .Select(static entry => entry.FullName)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var expectedEntryNames = expected.Files.Select(static file => "artifact/" + file.Path)
            .Append("powerforge-package.json")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (!allFileEntries.SequenceEqual(expectedEntryNames, StringComparer.Ordinal))
            throw new InvalidDataException("Qualified package contains an entry outside its authenticated descriptor inventory.");

        var entries = archive.Entries
            .Where(static entry => entry.FullName.StartsWith("artifact/", StringComparison.Ordinal) && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .OrderBy(static entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length != expected.Files.Length) throw new InvalidDataException("Qualified package payload inventory differs from its descriptor.");
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException($"Qualified package entry '{entry.FullName}' is a symbolic link.");
            var relative = entry.FullName.Substring("artifact/".Length);
            if (!relative.Equals(expected.Files[index].Path, StringComparison.Ordinal) || entry.Length != expected.Files[index].Size)
                throw new InvalidDataException("Qualified package payload inventory differs from its descriptor.");
            using var stream = entry.Open();
            using var algorithm = SHA256.Create();
            var hash = PowerShellCompilationProjectManifestService.ToHex(algorithm.ComputeHash(stream));
            if (!hash.Equals(expected.Files[index].Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Qualified package entry '{entry.FullName}' differs from its descriptor hash.");
        }
    }

    private static string CanonicalJsonSha256<T>(T value)
        => PowerShellCompilationProjectManifestService.ToHex(CanonicalJsonHash(value));

    private static byte[] CanonicalJsonHash<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, PowerShellCompilationProjectManifestService.JsonOptions);
        using var algorithm = SHA256.Create();
        return algorithm.ComputeHash(Encoding.UTF8.GetBytes(json));
    }

    private sealed class ValidatedProjectBuild
    {
        internal ValidatedProjectBuild(
            PowerShellCompilationBuildResult receipt,
            PowerShellCompilationArtifactManifest manifest,
            string outputRoot,
            string artifactPath,
            QualifiedProjectFile[] files,
            string artifactSetSha256)
        {
            Receipt = receipt;
            Manifest = manifest;
            OutputRoot = outputRoot;
            ArtifactPath = artifactPath;
            Files = files;
            ArtifactSetSha256 = artifactSetSha256;
        }

        internal PowerShellCompilationBuildResult Receipt { get; }
        internal PowerShellCompilationArtifactManifest Manifest { get; }
        internal string OutputRoot { get; }
        internal string ArtifactPath { get; }
        internal QualifiedProjectFile[] Files { get; }
        internal string ArtifactSetSha256 { get; }
    }

    private sealed class QualifiedProjectFile
    {
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long Size { get; set; }
    }

    private sealed class PowerShellCompilationProjectTestEvidence
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProjectSha256 { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string TargetContractSha256 { get; set; } = string.Empty;
        public string ArtifactSetSha256 { get; set; } = string.Empty;
        public string ArtifactSha256 { get; set; } = string.Empty;
        public string DependencyLockSha256 { get; set; } = string.Empty;
        public string ProviderLockSha256 { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string EvidenceSha256 { get; set; } = string.Empty;
    }

    private sealed class PowerShellCompilationQualifiedPackageDescriptor
    {
        public int SchemaVersion { get; set; } = 1;
        public string Project { get; set; } = string.Empty;
        public string ProjectSha256 { get; set; } = string.Empty;
        public string SemanticProfile { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public PowerShellCompilationTargetContract Target { get; set; } = new();
        public string DependencyLockSha256 { get; set; } = string.Empty;
        public string ProviderLockSha256 { get; set; } = string.Empty;
        public string PublicAbiSha256 { get; set; } = string.Empty;
        public string ArtifactSha256 { get; set; } = string.Empty;
        public string ArtifactSetSha256 { get; set; } = string.Empty;
        public string TestEvidenceSha256 { get; set; } = string.Empty;
        public QualifiedProjectFile[] Files { get; set; } = Array.Empty<QualifiedProjectFile>();
    }
}
