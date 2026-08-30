using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    /// <summary>Builds selected variants from reviewed locks and an isolated acquired environment.</summary>
    public PowerShellCompilationProjectResult Build(string projectPath, IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var environment = ReadEnvironment(context);
        var providers = ResolveProviders(context);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var lockPath = context.Resolve(artifact.DependencyLock);
                var dependencyLock = ReadJson<PowerShellCompilationDependencyGraph>(lockPath);
                PowerShellCompilationDependencyLockHasher.EnsureValid(dependencyLock, artifact.Name);
                if (!environment.DependencyLockSha256.Contains(dependencyLock.LockSha256, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The isolated environment does not contain reviewed lock '{dependencyLock.LockSha256}' for target '{artifact.Name}'; run restore for this target.");
                PowerShellCompilationProviderLock? providerLock = null;
                if (!string.IsNullOrWhiteSpace(artifact.ProviderLock))
                    providerLock = ReadJson<PowerShellCompilationProviderLock>(context.Resolve(artifact.ProviderLock!));
                var input = ResolveInput(context, artifact);
                var resolvedLock = environment.ResolvedLocks.Single(item =>
                    item.TargetName.Equals(artifact.Name, StringComparison.Ordinal));
                var spec = new PowerShellCompilationBuildSpec(
                    input.SourcePath,
                    context.Resolve(artifact.OutputDirectory),
                    GetArtifactName(context.Manifest, artifact),
                    artifact.Target.ArtifactKind,
                    artifact.Target.Mode)
                {
                    ModuleManifestPath = input.ModuleManifestPath,
                    CompilationSourcePaths = input.CompilationSourceFiles,
                    RuntimeSourcePaths = input.SourceFiles,
                    ResourceMode = context.Manifest.Resources.Mode,
                    IncludeResource = context.Manifest.Resources.Include,
                    ExcludeResource = context.Manifest.Resources.Exclude,
                    TargetContract = artifact.Target,
                    SemanticProfileId = context.Manifest.SemanticProfileId,
                    ExpectedDependencyLock = dependencyLock,
                    ProviderPackages = context.Manifest.ProviderPackages.Select(path => new PowerShellCompilationProviderPackageReference(context.Resolve(path))).ToArray(),
                    ExpectedProviderLock = providerLock,
                    ProviderTrustPolicy = context.Manifest.ProviderTrust,
                    ExpectedPublicAbiSha256 = artifact.ExpectedAbiSha256,
                    EmitSource = artifact.EmitSource,
                    EmitIrSnapshots = artifact.EmitIr,
                    DiagnosticsPolicy = context.Manifest.Diagnostics,
                    NuGetPackageRoot = environment.PackageRoot,
                    NuGetLockFilePath = context.Resolve(resolvedLock.Path),
                    OfflineRestore = true,
                    GeneratedOutputDirectories = GetGeneratedOutputDirectories(context),
                    UseBuildCache = false
                };
                var build = new PowerShellCompilationArtifactBuilder().Build(spec);
                var receiptPath = context.Resolve($".powerforge/build/{artifact.Name}.json");
                WriteJson(receiptPath, build);
                if (!build.Succeeded) throw new InvalidOperationException(build.Error + Environment.NewLine + build.BuildOutput);
                results.Add(Pass(
                    artifact,
                    "Artifact was built from reviewed locks with offline isolated package restore.",
                    build.ArtifactPath,
                    build.Manifest?.DependencyGraph?.LockSha256,
                    build.Manifest?.ArtifactSha256));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete("build", context.ProjectPath, results);
    }

    /// <summary>Executes or imports selected built variants through their declared surface.</summary>
    public PowerShellCompilationProjectResult Test(string projectPath, IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var validated = ValidateBuildReceipt(context, artifact);
                TestArtifact(artifact, validated.ArtifactPath);
                var testEvidence = CreateTestEvidence(context, artifact, validated);
                WriteJson(context.Resolve($".powerforge/test/{artifact.Name}.json"), testEvidence);
                results.Add(Pass(artifact, "Integrity-validated artifact passed its direct execution, clean import, or CLR metadata test.", validated.ArtifactPath, validated.Manifest.DependencyGraph?.LockSha256, validated.Manifest.ArtifactSha256));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete("test", context.ProjectPath, results);
    }

    /// <summary>Creates deterministic qualified variant archives from tested artifact sets.</summary>
    public PowerShellCompilationProjectResult Pack(string projectPath, IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var validated = ValidateBuildReceipt(context, artifact);
                var testEvidence = ReadTestEvidence(context, artifact, validated);
                var packageRoot = context.Resolve(".powerforge/packages");
                Directory.CreateDirectory(packageRoot);
                var packagePath = Path.Combine(packageRoot, GetArtifactName(context.Manifest, artifact) + ".zip");
                WriteDeterministicPackage(packagePath, context, artifact, validated, testEvidence);
                results.Add(Pass(artifact, "Qualified tested artifact archive includes exact inventory, target, lock, SBOM, provenance, and test evidence.", packagePath, validated.Manifest.DependencyGraph?.LockSha256, validated.Manifest.ArtifactSha256));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete("pack", context.ProjectPath, results);
    }

    /// <summary>Installs qualified packages into an immutable project-local content-addressed root and verifies their declared surface.</summary>
    public PowerShellCompilationProjectResult Install(string projectPath, IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var validated = ValidateBuildReceipt(context, artifact);
                var testEvidence = ReadTestEvidence(context, artifact, validated);
                var manifest = validated.Manifest;
                var artifactSha256 = manifest.ArtifactSha256;
                if (string.IsNullOrWhiteSpace(artifactSha256)) throw new InvalidDataException("Build receipt has no artifact identity.");
                var packagePath = context.Resolve($".powerforge/packages/{GetArtifactName(context.Manifest, artifact)}.zip");
                if (!File.Exists(packagePath)) throw new FileNotFoundException("Run project pack before install.", packagePath);
                ValidateQualifiedPackage(packagePath, context, artifact, validated, testEvidence);
                var installRoot = context.Resolve(
                    $".powerforge/i/{artifact.Target.ContractSha256.Substring(0, 16).ToLowerInvariant()}/{artifactSha256.Substring(0, 24).ToLowerInvariant()}");
                var primaryRelative = FrameworkCompatibility.GetRelativePath(
                    validated.OutputRoot,
                    validated.ArtifactPath).Replace('\\', '/');
                if (Directory.Exists(installRoot))
                    EnsureInstalledPackageMatches(packagePath, installRoot);
                else
                    InstallPackageAtomically(packagePath, installRoot);
                var installedPrimary = Path.GetFullPath(Path.Combine(installRoot, "artifact", primaryRelative.Replace('/', Path.DirectorySeparatorChar)));
                PowerShellCompilationPathSafety.EnsureContained(installRoot, installedPrimary, "Installed primary artifact escapes its qualified installation root.");
                if (!File.Exists(installedPrimary)) throw new FileNotFoundException("Installed primary artifact is missing.", installedPrimary);
                var actualHash = PowerShellCompilationProjectManifestService.ComputeSha256(installedPrimary);
                if (!actualHash.Equals(artifactSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installed primary artifact differs from its compiler identity.");
                TestArtifact(artifact, installedPrimary);
                results.Add(Pass(artifact, "Qualified package was installed by exact target and artifact identity and passed its declared surface test.", installRoot, manifest.DependencyGraph?.LockSha256, actualHash));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete("install", context.ProjectPath, results);
    }

    /// <summary>Verifies build receipts, canonical reproduction evidence, locks, and primary artifact hashes.</summary>
    public PowerShellCompilationProjectResult Diagnose(string projectPath, IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var validated = ValidateBuildReceipt(context, artifact);
                results.Add(Pass(artifact, "Target, locks, reproduction evidence, and complete artifact-set integrity are valid.", validated.ArtifactPath, validated.Manifest.DependencyGraph?.LockSha256, validated.Manifest.ArtifactSha256));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete("diagnose", context.ProjectPath, results);
    }

    private static PowerShellCompilationProjectEnvironment ReadEnvironment(
        PowerShellCompilationProjectManifestService.ProjectContext context)
    {
        var path = context.Resolve(".powerforge/environment/environment.json");
        if (!File.Exists(path)) throw new FileNotFoundException("Run project restore before build.", path);
        var environment = ReadJson<PowerShellCompilationProjectEnvironment>(path);
        if (environment.SchemaVersion != 2)
            throw new InvalidDataException($"Unsupported isolated environment schema {environment.SchemaVersion}; run restore again.");
        var projectSha = PowerShellCompilationProjectManifestService.ComputeSha256(context.ProjectPath);
        if (!environment.ProjectSha256.Equals(projectSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The isolated environment belongs to a different project-manifest revision; run restore again.");
        var expectedPackageRoot = context.Resolve(".powerforge/environment/packages");
        if (!PowerShellCompilationPathSafety.PathEquals(environment.PackageRoot, expectedPackageRoot))
            throw new InvalidDataException("The isolated environment package root differs from the project-owned path.");
        if (!Directory.Exists(environment.PackageRoot))
            throw new DirectoryNotFoundException($"The isolated project package root is missing: {environment.PackageRoot}");
        environment.DependencyLockSha256 ??= Array.Empty<string>();
        environment.Packages ??= Array.Empty<PowerShellCompilationProjectPackage>();
        environment.ResolvedLocks ??= Array.Empty<PowerShellCompilationProjectResolvedLock>();
        if (environment.DependencyLockSha256.Length == 0)
            throw new InvalidDataException("The isolated environment records no reviewed dependency locks.");
        foreach (var lockSha256 in environment.DependencyLockSha256)
        {
            if (lockSha256.Length != 64 || !lockSha256.All(static character =>
                    character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
                throw new InvalidDataException("The isolated environment contains an invalid dependency-lock identity.");
        }
        foreach (var package in environment.Packages)
            _ = PowerShellCompilationNuGetPackageVerifier.Verify(environment.PackageRoot, package);
        if (environment.ResolvedLocks.Length == 0)
            throw new InvalidDataException("The isolated environment records no exact NuGet transitive-closure locks.");
        foreach (var resolvedLock in environment.ResolvedLocks)
        {
            if (!context.Manifest.Artifacts.Any(artifact => artifact.Name.Equals(resolvedLock.TargetName, StringComparison.Ordinal)))
                throw new InvalidDataException($"The isolated environment references unknown target '{resolvedLock.TargetName}'.");
            var resolvedPath = context.Resolve(resolvedLock.Path);
            if (!File.Exists(resolvedPath)) throw new FileNotFoundException("A resolved NuGet closure lock is missing.", resolvedPath);
            var actual = PowerShellCompilationProjectManifestService.ComputeSha256(resolvedPath);
            if (!actual.Equals(resolvedLock.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Resolved NuGet closure lock for '{resolvedLock.TargetName}' differs from environment evidence.");
        }
        var expectedEnvironmentSha = ComputeEnvironmentSha256(
            environment.ProjectSha256,
            environment.DependencyLockSha256,
            environment.Packages,
            environment.ResolvedLocks);
        if (!environment.EnvironmentSha256.Equals(expectedEnvironmentSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The isolated environment evidence differs from its recorded content identity; run restore again.");
        return environment;
    }

    private static PowerShellCompilationBuildResult ReadBuildReceipt(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact)
    {
        var path = context.Resolve($".powerforge/build/{artifact.Name}.json");
        if (!File.Exists(path)) throw new FileNotFoundException("Run project build before this operation.", path);
        return ReadJson<PowerShellCompilationBuildResult>(path);
    }

    private static string GetArtifactName(PowerShellCompilationProjectManifest manifest, PowerShellCompilationProjectArtifact artifact)
        => manifest.Name + "." + artifact.Name;

    private static void TestArtifact(PowerShellCompilationProjectArtifact artifact, string path)
    {
        switch (artifact.Target.ArtifactKind)
        {
            case PowerShellCompilationArtifactKind.Executable:
                EnsureCurrentTargetHost(artifact.Target);
                RunExecutable(path);
                break;
            case PowerShellCompilationArtifactKind.BinaryModule:
                ImportModule(path);
                break;
            case PowerShellCompilationArtifactKind.Library:
                _ = AssemblyName.GetAssemblyName(path);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void RunExecutable(string path)
    {
        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
            path,
            Path.GetDirectoryName(path)!,
            Array.Empty<string>(),
            TimeSpan.FromMinutes(2))).GetAwaiter().GetResult();
        if (!run.Succeeded)
            throw new InvalidOperationException($"Executable test failed with exit {run.ExitCode}: {run.StdOut}{Environment.NewLine}{run.StdErr}".Trim());
    }

    private static void EnsureCurrentTargetHost(PowerShellCompilationTargetContract target)
    {
        if (string.IsNullOrWhiteSpace(target.RuntimeIdentifier)) return;
        var currentOs = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unknown";
        var currentArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        if (!target.OperatingSystem.Equals(currentOs, StringComparison.OrdinalIgnoreCase) ||
            !target.Architecture.Equals(currentArchitecture, StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException(
                $"Target '{target.RuntimeIdentifier}' must be tested on its actual host; current host is '{currentOs}-{currentArchitecture}'.");
    }

    private static void ImportModule(string manifestPath)
    {
        var script = "& { $env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules'); " +
                     "Import-Module -Name $args[0] -Force -ErrorAction Stop; 'ok' }";
        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
            "pwsh",
            Path.GetDirectoryName(manifestPath)!,
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script, manifestPath },
            TimeSpan.FromMinutes(2),
            new Dictionary<string, string?> { ["POWERSHELL_TELEMETRY_OPTOUT"] = "1" })).GetAwaiter().GetResult();
        if (!run.Succeeded || !run.StdOut.Trim().Equals("ok", StringComparison.Ordinal))
            throw new InvalidOperationException($"Clean module import failed with exit {run.ExitCode}: {run.StdOut}{Environment.NewLine}{run.StdErr}".Trim());
    }

    private static void InstallPackageAtomically(string packagePath, string installRoot)
    {
        var parent = Path.GetDirectoryName(installRoot) ?? throw new InvalidOperationException("Qualified installation root has no parent.");
        Directory.CreateDirectory(parent);
        var staging = installRoot + "." + Guid.NewGuid().ToString("N") + ".staging";
        try
        {
            ExtractQualifiedPackage(packagePath, staging);
            EnsureInstalledPackageMatches(packagePath, staging);
            Directory.Move(staging, installRoot);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void ExtractQualifiedPackage(string packagePath, string installRoot)
    {
        Directory.CreateDirectory(installRoot);
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries.OrderBy(static entry => entry.FullName, StringComparer.Ordinal))
        {
            if (entry.FullName.Length == 0 || entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException($"Qualified package entry '{entry.FullName}' is a symbolic link.");
            var destination = Path.GetFullPath(Path.Combine(installRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            PowerShellCompilationPathSafety.EnsureContained(installRoot, destination, $"Qualified package entry '{entry.FullName}' escapes the installation root.");
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void EnsureInstalledPackageMatches(string packagePath, string installRoot)
    {
        PowerShellCompilationPathSafety.EnsureNoLinks(installRoot, installRoot, "Qualified installation root traverses a symbolic link or junction.");
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(static entry => entry.FullName.Length > 0 && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .OrderBy(static entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();
        var installed = Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories)
            .Select(path => FrameworkCompatibility.GetRelativePath(installRoot, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (!entries.Select(static entry => entry.FullName).SequenceEqual(installed, StringComparer.Ordinal))
            throw new InvalidDataException("Qualified installation file inventory differs from its package.");
        foreach (var entry in entries)
        {
            var path = Path.GetFullPath(Path.Combine(installRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            PowerShellCompilationPathSafety.EnsureNoLinks(installRoot, path, $"Installed package entry '{entry.FullName}' traverses a symbolic link or junction.");
            using var expectedStream = entry.Open();
            using var actualStream = File.OpenRead(path);
            using var expectedHash = System.Security.Cryptography.SHA256.Create();
            using var actualHash = System.Security.Cryptography.SHA256.Create();
            if (!expectedHash.ComputeHash(expectedStream).SequenceEqual(actualHash.ComputeHash(actualStream)))
                throw new InvalidDataException($"Installed package entry '{entry.FullName}' differs from its archive.");
        }
    }

    private static void WriteDeterministicPackage(
        string packagePath,
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact,
        ValidatedProjectBuild validated,
        PowerShellCompilationProjectTestEvidence testEvidence)
    {
        var outputRoot = validated.OutputRoot;
        if (!Directory.Exists(outputRoot)) throw new DirectoryNotFoundException($"Artifact output is missing: {outputRoot}");
        var temporary = packagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
                             .Where(path => IsArtifactPayloadFile(path, context, artifact))
                             .OrderBy(path => FrameworkCompatibility.GetRelativePath(outputRoot, path), StringComparer.Ordinal))
                {
                    PowerShellCompilationPathSafety.EnsureNoLinks(outputRoot, file, "Artifact package input traverses a symbolic link or junction.");
                    var relative = FrameworkCompatibility.GetRelativePath(outputRoot, file).Replace('\\', '/');
                    var entry = archive.CreateEntry("artifact/" + relative, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var input = File.OpenRead(file);
                    using var output = entry.Open();
                    input.CopyTo(output);
                }
                var descriptor = CreatePackageDescriptor(context, artifact, validated, testEvidence);
                var descriptorEntry = archive.CreateEntry("powerforge-package.json", CompressionLevel.Optimal);
                descriptorEntry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(descriptorEntry.Open(), new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(descriptor, PowerShellCompilationProjectManifestService.JsonOptions));
            }
            ValidateQualifiedPackage(temporary, context, artifact, validated, testEvidence);
            if (File.Exists(packagePath)) File.Delete(packagePath);
            File.Move(temporary, packagePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
