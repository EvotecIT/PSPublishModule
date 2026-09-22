using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Describes a local NuGet package to create from a successful Strict library compilation.</summary>
public sealed class PowerShellCompilationLibraryPackageBuildRequest
{
    /// <summary>Creates a compiled-library package request.</summary>
    public PowerShellCompilationLibraryPackageBuildRequest(
        PowerShellCompilationBuildResult compilation,
        string outputPath,
        string packageId,
        string packageVersion)
    {
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        OutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? throw new ArgumentException("A package output path is required.", nameof(outputPath))
            : Path.GetFullPath(outputPath.Trim().Trim('"'));
        PackageId = packageId ?? string.Empty;
        PackageVersion = packageVersion ?? string.Empty;
    }

    /// <summary>Successful Strict runtime-free library build, including an emitted source project.</summary>
    public PowerShellCompilationBuildResult Compilation { get; }

    /// <summary>Destination <c>.nupkg</c> path.</summary>
    public string OutputPath { get; }

    /// <summary>NuGet package identity.</summary>
    public string PackageId { get; }

    /// <summary>Three-part stable package version.</summary>
    public string PackageVersion { get; }

    /// <summary>Package authors.</summary>
    public string Authors { get; set; } = "PowerForge";

    /// <summary>Package description.</summary>
    public string Description { get; set; } = "Runtime-free CLR library compiled from PowerShell.";

    /// <summary>SPDX license expression.</summary>
    public string LicenseExpression { get; set; } = "MIT";

    /// <summary>Optional repository URL.</summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>Optional repository commit.</summary>
    public string? RepositoryCommit { get; set; }

    /// <summary>Optional earlier ABI that the candidate package must preserve.</summary>
    public PowerShellCompilationAbiManifest? CompatibilityBaseline { get; set; }

    /// <summary>Maximum time allowed for the independent generated-project rebuild.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Optional isolated NuGet global-packages folder for the independent rebuild.</summary>
    public string? NuGetPackageRoot { get; set; }
}

/// <summary>Evidence returned after creating a compiled PowerShell library package.</summary>
public sealed class PowerShellCompilationLibraryPackageBuildResult
{
    /// <summary>Final package path.</summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>SHA-256 of the final package.</summary>
    public string PackageSha256 { get; set; } = string.Empty;

    /// <summary>Target framework of the packaged library.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Public ABI hash verified in the independently rebuilt assembly.</summary>
    public string PublicAbiSha256 { get; set; } = string.Empty;

    /// <summary>Stable package-relative files included in the archive.</summary>
    public string[] Files { get; set; } = Array.Empty<string>();

    /// <summary>Bounded output from the independent rebuild.</summary>
    public string RebuildOutput { get; set; } = string.Empty;
}

/// <summary>
/// Independently rebuilds emitted Strict library source and packages its CLR assets and compiler
/// evidence into a deterministic NuGet package suitable for a local feed.
/// </summary>
public sealed partial class PowerShellCompilationLibraryPackageBuilder
{
    private static readonly DateTimeOffset DeterministicTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly IProcessRunner _processRunner;

    /// <summary>Creates a package builder using the standard process runner.</summary>
    public PowerShellCompilationLibraryPackageBuilder() : this(new ProcessRunner()) { }

    /// <summary>Creates a package builder with an explicit process runner.</summary>
    public PowerShellCompilationLibraryPackageBuilder(IProcessRunner processRunner)
        => _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    /// <summary>Builds and validates one deterministic local-feed package.</summary>
    public PowerShellCompilationLibraryPackageBuildResult Build(
        PowerShellCompilationLibraryPackageBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = Clone(request.Compilation.Manifest!);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "powerforge-library-package-" + Guid.NewGuid().ToString("N"));
        var snapshotDirectory = Path.Combine(temporaryRoot, "snapshot");
        var sourceDirectory = Path.Combine(snapshotDirectory, "generated-source");
        var rebuildDirectory = Path.Combine(temporaryRoot, "rebuild");
        Exception? failure = null;
        try
        {
            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(rebuildDirectory);
            var inputs = SnapshotInputs(manifest, request.Compilation.GeneratedSourcePath!, snapshotDirectory, sourceDirectory, cancellationToken);
            var projectPaths = Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();
            if (projectPaths.Length != 1)
                throw new InvalidOperationException("The verified emitted source snapshot must contain exactly one generated project.");
            var environment = string.IsNullOrWhiteSpace(request.NuGetPackageRoot)
                ? null
                : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["NUGET_PACKAGES"] = Path.GetFullPath(request.NuGetPackageRoot!.Trim().Trim('"'))
                };
            var run = _processRunner.RunAsync(new ProcessRunRequest(
                    "dotnet",
                    sourceDirectory,
                    new[]
                    {
                        "build", projectPaths[0], "--configuration", "Release", "--output", rebuildDirectory,
                        "--nologo", "--verbosity", "minimal"
                    },
                    request.Timeout,
                    environment), cancellationToken)
                .GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            var rebuildOutput = BoundedOutput(run);
            if (!run.Succeeded)
                throw new InvalidOperationException("The emitted source project did not rebuild independently. " + rebuildOutput);

            var primaryInput = inputs.Single(static input => input.File.Role.Equals("Primary", StringComparison.Ordinal));
            var assemblyName = Path.GetFileName(primaryInput.File.Path);
            var rebuiltAssembly = Path.Combine(rebuildDirectory, assemblyName);
            if (!File.Exists(rebuiltAssembly))
                throw new FileNotFoundException("The independent rebuild did not produce the expected library.", rebuiltAssembly);
            var abiValues = ReadAssemblyMetadataValues(rebuiltAssembly, "PowerForge.PublicAbiSha256").ToArray();
            var expectedAbi = manifest.PublicAbi!.Sha256;
            if (abiValues.Length != 1 || !abiValues[0].Equals(expectedAbi, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The independently rebuilt library does not contain the expected public ABI identity.");
            EnsureReproduced(primaryInput.File, rebuiltAssembly, "library", cancellationToken);
            var debugInput = inputs.SingleOrDefault(static input => input.File.Role.Equals("DebugSymbols", StringComparison.Ordinal));
            if (debugInput is not null)
                EnsureReproduced(debugInput.File, Path.Combine(rebuildDirectory, Path.GetFileName(debugInput.File.Path)), "portable PDB", cancellationToken);

            var entries = CreateEntries(rebuildDirectory, manifest, inputs, cancellationToken);
            var packageHash = PublishPackage(request.OutputPath,
                stream =>
                {
                    WritePackage(stream, request, entries, cancellationToken);
                    // Clean the rebuild workspace before publication: a cleanup failure must
                    // not report failure only after a new package has already been committed.
                    Directory.Delete(temporaryRoot, recursive: true);
                }, cancellationToken);

            return new PowerShellCompilationLibraryPackageBuildResult
            {
                PackagePath = request.OutputPath,
                PackageSha256 = packageHash,
                TargetFramework = manifest.TargetFramework,
                PublicAbiSha256 = expectedAbi,
                Files = entries.Select(static entry => entry.PackagePath).OrderBy(static path => path, StringComparer.Ordinal).ToArray(),
                RebuildOutput = rebuildOutput
            };
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            Cleanup(() => { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true); }, failure);
        }
    }

    private static void ValidateRequest(PowerShellCompilationLibraryPackageBuildRequest request)
    {
        var result = request.Compilation;
        var manifest = result.Manifest;
        if (!result.Succeeded || manifest is null || string.IsNullOrWhiteSpace(result.ArtifactPath) || !File.Exists(result.ArtifactPath))
            throw new ArgumentException("A successful compilation result with a durable artifact is required.", nameof(request));
        if (manifest.Kind != PowerShellCompilationArtifactKind.Library || manifest.Mode != PowerShellCompilationMode.Strict ||
            manifest.RequiresPowerShellRuntime || manifest.UsesPowerShellRuntimeFallback || !manifest.DependencyClosureVerified ||
            manifest.DependencyClosure?.Verified != true || manifest.DependencyClosure.Limitations.Count != 0)
            throw new InvalidOperationException("Only a certified runtime-free Strict library can be packaged.");
        if (manifest.PublicAbi is null || string.IsNullOrWhiteSpace(manifest.PublicAbi.Sha256))
            throw new InvalidOperationException("A packageable compiled library must contain a public ABI manifest.");
        if (manifest.AuthenticodeSigned || manifest.AuthenticodeSignedFiles != 0 ||
            !string.IsNullOrWhiteSpace(manifest.SigningCertificateThumbprint))
            throw new InvalidOperationException("Packaging a signed compilation is not supported because the independent rebuild must reproduce the exact unsigned library bytes.");
        if (string.IsNullOrWhiteSpace(result.ManifestPath) || !File.Exists(result.ManifestPath))
            throw new InvalidOperationException("The compilation manifest is missing.");
        if (string.IsNullOrWhiteSpace(result.GeneratedSourcePath) || !Directory.Exists(result.GeneratedSourcePath))
            throw new InvalidOperationException("EmitSource must be enabled so the package can independently rebuild its input.");
        if (string.IsNullOrWhiteSpace(manifest.GeneratedSourcePath) ||
            !PowerShellCompilationPathSafety.PathEquals(manifest.GeneratedSourcePath, result.GeneratedSourcePath))
            throw new InvalidOperationException("The compilation result and manifest disagree about the generated source project.");
        if (!manifest.TargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) &&
            !manifest.TargetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Compiled library packages support only net10.0 and net472.");
        if (!PowerShellCompilationPackageIdentity.IsValidId(request.PackageId)) throw new ArgumentException("PackageId is not a valid NuGet identity.", nameof(request));
        if (!PowerShellCompilationPackageIdentity.IsCanonicalVersion(request.PackageVersion))
            throw new ArgumentException("PackageVersion must be a stable three-part version such as 1.2.3.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Authors) || string.IsNullOrWhiteSpace(request.Description) || string.IsNullOrWhiteSpace(request.LicenseExpression))
            throw new ArgumentException("Authors, Description, and LicenseExpression are required.", nameof(request));
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(request), "Timeout must be positive.");
        if (string.IsNullOrWhiteSpace(Path.GetFileName(request.OutputPath)) ||
            !Path.GetExtension(request.OutputPath).Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("OutputPath must end with .nupkg.", nameof(request));
        if (request.CompatibilityBaseline is not null)
            PowerShellCompilationAbiCompatibility.EnsureCompatible(request.CompatibilityBaseline, manifest.PublicAbi);
        if (string.IsNullOrWhiteSpace(request.RepositoryUrl) != string.IsNullOrWhiteSpace(request.RepositoryCommit))
            throw new InvalidOperationException("RepositoryUrl and RepositoryCommit must be supplied together.");
        PowerShellCompilationArtifactEvidence.Validate(manifest);
    }

    private static PackageEntry[] CreateEntries(
        string rebuildDirectory,
        PowerShellCompilationArtifactManifest manifest,
        IReadOnlyCollection<VerifiedInput> inputs,
        CancellationToken cancellationToken)
    {
        var entries = new List<PackageEntry>();
        var frameworkRoot = "lib/" + manifest.TargetFramework + "/";
        foreach (var input in inputs.OrderBy(static item => item.File.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(input.File.Path);
            string packagePath;
            string sourcePath;
            switch (input.File.Role)
            {
                case "Primary":
                case "DebugSymbols":
                case "CompilerProviderRuntime":
                    packagePath = frameworkRoot + fileName;
                    sourcePath = Path.Combine(rebuildDirectory, fileName);
                    break;
                case "CompilerProviderNativeRuntime":
                    var native = (manifest.ProviderLock?.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
                        .SelectMany(static package => package.NativeAssets ?? Array.Empty<PowerShellCompilationProviderNativeAsset>())
                        .Single(asset => asset.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                                         asset.RuntimeIdentifier.Equals(manifest.RuntimeIdentifier ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    packagePath = "runtimes/" + native.RuntimeIdentifier + "/native/" + fileName;
                    sourcePath = Path.Combine(rebuildDirectory, fileName);
                    break;
                default:
                    if (input.GeneratedRelativePath is not null)
                    {
                        packagePath = "powerforge/generated-source/" + input.GeneratedRelativePath;
                        sourcePath = input.SnapshotPath;
                    }
                    else
                    {
                        packagePath = "powerforge/evidence/" + PackageSegment(input.File.Role) + "/" + fileName;
                        sourcePath = input.SnapshotPath;
                    }
                    break;
            }
            if (!File.Exists(sourcePath))
                throw new InvalidOperationException($"Verified package input '{input.File.Role}' is missing from its immutable snapshot or rebuild output.");
            if (input.File.Role is "Primary" or "DebugSymbols" or "CompilerProviderRuntime" or "CompilerProviderNativeRuntime")
                EnsureReproduced(input.File, sourcePath, input.File.Role, cancellationToken);
            entries.Add(PackageEntry.File(packagePath, sourcePath, input.File.Role, input.File.Path));
        }

        foreach (var package in manifest.ProviderLock?.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
        {
            foreach (var assembly in package.Assemblies ?? Array.Empty<PowerShellCompilationProviderAssembly>())
            {
                var path = Path.Combine(rebuildDirectory, assembly.AssemblyName + ".dll");
                if (!File.Exists(path) || !ComputeSha256(path, cancellationToken).Equals(assembly.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Independently rebuilt provider assembly '{assembly.AssemblyName}' does not match its reviewed lock.");
            }
        }

        var assemblyName = Path.GetFileNameWithoutExtension(manifest.ArtifactPath);
        var xmlPath = Path.Combine(rebuildDirectory, assemblyName + ".xml");
        if (!File.Exists(xmlPath)) throw new InvalidOperationException("The independent rebuild did not produce public XML documentation.");
        entries.Add(PackageEntry.File(frameworkRoot + Path.GetFileName(xmlPath), xmlPath));
        EnsureUniquePackagePaths(entries);
        var portableAbi = CreatePortableAbi(manifest, manifest.PublicAbi!);
        entries.Add(PackageEntry.Text(
            "powerforge/compilation-manifest.json",
            CreatePortableManifest(manifest, entries, portableAbi)));
        entries.Add(PackageEntry.Text("powerforge/public-abi.json", Serialize(portableAbi)));
        entries.Add(PackageEntry.Text("powerforge/dependency-lock.json", Serialize(manifest.DependencyGraph)));
        entries.Add(PackageEntry.Text("powerforge/target-contract.json", Serialize(manifest.TargetContract)));
        if (manifest.ProviderLock is not null)
            entries.Add(PackageEntry.Text("powerforge/provider-lock.json", Serialize(manifest.ProviderLock)));
        EnsureUniquePackagePaths(entries);
        return entries.OrderBy(static entry => entry.PackagePath, StringComparer.Ordinal).ToArray();
    }

    private static string CreatePortableManifest(
        PowerShellCompilationArtifactManifest source,
        IReadOnlyCollection<PackageEntry> entries,
        PowerShellCompilationAbiManifest portableAbi)
    {
        var options = CreateJsonOptions();
        var manifest = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
                           JsonSerializer.Serialize(source, options), options)
                       ?? throw new InvalidOperationException("The compilation manifest could not be cloned for package publication.");
        var artifactEntry = entries.Single(static entry => entry.Role == "Primary");
        manifest.ArtifactPath = artifactEntry.PackagePath;
        manifest.ArtifactRelativePath = artifactEntry.PackagePath;
        manifest.GeneratedSourcePath = "powerforge/generated-source";
        manifest.PublicAbi = portableAbi;
        var portableSources = (manifest.Reproduction?.Sources ?? Array.Empty<PowerShellCompilationReproductionSource>())
            .Select(static item => item.RelativePath.Replace('\\', '/'))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        manifest.SourcePath = GetPortableSourcePath(source, manifest.SourcePath);
        manifest.SourceFiles = portableSources;
        manifest.Dependencies = (manifest.Dependencies ?? Array.Empty<PowerShellCompilationDependency>())
            .Select(static dependency => new PowerShellCompilationDependency(
                dependency.Name,
                dependency.SourcePath is null ? null : dependency.RelativePath.Replace('\\', '/'),
                dependency.RelativePath.Replace('\\', '/'),
                dependency.Kind,
                dependency.Discovery,
                dependency.Disposition,
                dependency.Exists,
                dependency.SizeBytes,
                dependency.Note,
                dependency.Selection))
            .ToArray();
        manifest.Files = source.Files.Select(file =>
        {
            var entry = entries.Single(candidate => candidate.OriginalPath is not null &&
                                                    PowerShellCompilationPathSafety.PathEquals(candidate.OriginalPath, file.Path));
            return new PowerShellCompilationArtifactFile
            {
                Path = entry.PackagePath,
                RelativePath = entry.PackagePath,
                Role = file.Role,
                Sha256 = file.Sha256,
                SizeBytes = file.SizeBytes
            };
        })
            .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        PowerShellCompilationReproductionEvidenceBuilder.Validate(manifest);
        return JsonSerializer.Serialize(manifest, options);
    }

    private static VerifiedInput[] SnapshotInputs(
        PowerShellCompilationArtifactManifest manifest,
        string generatedSourcePath,
        string snapshotDirectory,
        string cleanSourceDirectory,
        CancellationToken cancellationToken)
    {
        if (manifest.Files is null || manifest.Files.Length == 0 || manifest.Files.Any(static file => file is null))
            throw new InvalidOperationException("The compilation manifest does not contain a complete artifact file inventory.");
        var sourceRoot = Path.GetFullPath(generatedSourcePath);
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
            sourceRoot,
            "The generated source root traverses a symbolic link or junction.");
        var generatedFiles = manifest.Files
            .Where(static file => file.Role.StartsWith("Generated", StringComparison.Ordinal))
            .ToArray();
        if (generatedFiles.Length == 0)
            throw new InvalidOperationException("The compilation manifest does not identify generated project inputs.");
        var expectedGenerated = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        foreach (var file in generatedFiles)
        {
            var path = Path.GetFullPath(file.Path);
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, path, "A generated project input escaped its declared source root.");
            if (!expectedGenerated.Add(path))
                throw new InvalidOperationException($"The generated project inventory contains duplicate path '{file.Path}'.");
        }
        EnsureExactSourceInventory(sourceRoot, expectedGenerated);

        var inputs = new List<VerifiedInput>(manifest.Files.Length);
        var uniquePaths = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        for (var index = 0; index < manifest.Files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[index];
            if (string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(file.Role) ||
                string.IsNullOrWhiteSpace(file.Sha256) || file.SizeBytes < 0)
                throw new InvalidOperationException("The compilation manifest contains an incomplete artifact file entry.");
            var source = Path.GetFullPath(file.Path);
            if (!uniquePaths.Add(source))
                throw new InvalidOperationException($"The compilation manifest contains duplicate file '{file.Path}'.");
            PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(
                source,
                $"Compilation input '{file.Path}' traverses a symbolic link or junction.");
            ValidateFileIdentity(file, source, cancellationToken);

            string destination;
            string? generatedRelativePath = null;
            if (file.Role.StartsWith("Generated", StringComparison.Ordinal))
            {
                generatedRelativePath = FrameworkCompatibility.GetRelativePath(sourceRoot, source).Replace('\\', '/');
                if (Path.IsPathRooted(generatedRelativePath) || generatedRelativePath.StartsWith("../", StringComparison.Ordinal) ||
                    generatedRelativePath.Equals("..", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Generated input '{file.Path}' has an unsafe relative path.");
                destination = Path.Combine(cleanSourceDirectory, generatedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                PowerShellCompilationPathSafety.EnsureContained(cleanSourceDirectory, destination, "A generated input escaped the clean source snapshot.");
            }
            else
            {
                destination = Path.Combine(snapshotDirectory, "evidence", index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(source));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                CopyStream(input, output, cancellationToken);
            ValidateFileIdentity(file, destination, cancellationToken);
            inputs.Add(new VerifiedInput(file, destination, generatedRelativePath));
        }

        EnsureExactSourceInventory(sourceRoot, expectedGenerated);
        var cleanHash = PowerShellRuntimeFreeArtifactContract.ComputeGeneratedSourceSha256(cleanSourceDirectory);
        if (!cleanHash.Equals(manifest.GeneratedSourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The verified generated source snapshot does not match the compilation manifest identity.");
        return inputs.ToArray();
    }

    private static void EnsureExactSourceInventory(string sourceRoot, ISet<string> expected)
    {
        var actual = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        if (!actual.SetEquals(expected))
            throw new InvalidOperationException("The emitted source directory contains missing, changed, or unrecorded build inputs.");
    }

    private static void ValidateFileIdentity(PowerShellCompilationArtifactFile expected, string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("A manifest-bound compilation input is missing.", path);
        var info = new FileInfo(path);
        if (info.Length != expected.SizeBytes || !ComputeSha256(path, cancellationToken).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Compilation input '{expected.Role}' no longer matches its recorded size and SHA-256 identity.");
    }

    private static void EnsureReproduced(PowerShellCompilationArtifactFile expected, string path, string label, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"The independent rebuild did not produce the expected {label}.", path);
        var info = new FileInfo(path);
        if (info.Length != expected.SizeBytes || !ComputeSha256(path, cancellationToken).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The independent rebuild did not reproduce the original unsigned {label} bytes.");
    }

    private static PowerShellCompilationAbiManifest CreatePortableAbi(
        PowerShellCompilationArtifactManifest manifest,
        PowerShellCompilationAbiManifest source)
    {
        var abi = Clone(source);
        if (abi.ModuleLifetime is not null)
            abi.ModuleLifetime.SourcePath = GetPortableSourcePath(manifest, abi.ModuleLifetime.SourcePath);
        var canonical = PowerShellCompilationAbiBuilder.ComputeSha256(PowerShellCompilationAbiBuilder.GetNormalizedText(abi));
        if (!canonical.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Portable ABI normalization changed the public ABI identity.");
        return abi;
    }

    private static string GetPortableSourcePath(PowerShellCompilationArtifactManifest manifest, string path)
    {
        var sources = (manifest.Reproduction?.Sources ?? Array.Empty<PowerShellCompilationReproductionSource>())
            .Select(static item => item.RelativePath.Replace('\\', '/'))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        if (sources.Length == 0)
            throw new InvalidOperationException("The compilation manifest has no portable authored-source identities.");
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        if (Path.IsPathRooted(path))
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(manifest.SourcePath)) ?? Directory.GetCurrentDirectory();
            normalized = FrameworkCompatibility.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
        }
        var match = sources.SingleOrDefault(source => source.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new InvalidOperationException($"Authored source '{path}' cannot be mapped to its portable reproduction identity.");
        return match;
    }

    private static void EnsureUniquePackagePaths(IEnumerable<PackageEntry> entries)
    {
        var duplicate = entries.GroupBy(static entry => entry.PackagePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"More than one package input maps to '{duplicate.Key}'.");
    }

    private static T Clone<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CreateJsonOptions()), CreateJsonOptions())
           ?? throw new InvalidOperationException($"Could not clone {typeof(T).Name} for package publication.");

    private static void WritePackage(
        Stream stream,
        PowerShellCompilationLibraryPackageBuildRequest request,
        IReadOnlyCollection<PackageEntry> entries,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        AddText(archive, request.PackageId + ".nuspec", CreateNuspec(request));
        AddText(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"json\" ContentType=\"application/json\"/><Default Extension=\"dll\" ContentType=\"application/octet\"/><Default Extension=\"pdb\" ContentType=\"application/octet\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"nuspec\" ContentType=\"application/octet\"/></Types>");
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.SourcePath is not null) AddFile(archive, entry.PackagePath, entry.SourcePath, cancellationToken);
            else AddText(archive, entry.PackagePath, entry.Content!);
        }
    }

    private static string CreateNuspec(PowerShellCompilationLibraryPackageBuildRequest request)
    {
        var repository = string.IsNullOrWhiteSpace(request.RepositoryUrl)
            ? string.Empty
            : $"<repository type=\"git\" url=\"{Xml(request.RepositoryUrl!.Trim())}\" commit=\"{Xml(request.RepositoryCommit!.Trim())}\" />";
        return $"<?xml version=\"1.0\" encoding=\"utf-8\"?><package><metadata><id>{Xml(request.PackageId)}</id><version>{Xml(request.PackageVersion)}</version><authors>{Xml(request.Authors)}</authors><description>{Xml(request.Description)}</description><license type=\"expression\">{Xml(request.LicenseExpression)}</license>{repository}</metadata></package>";
    }

    private static void AddText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void AddFile(ZipArchive archive, string packagePath, string sourcePath, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(packagePath, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;
        using var target = entry.Open();
        using var source = File.OpenRead(sourcePath);
        CopyStream(source, target, cancellationToken);
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, CreateJsonOptions());
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static IEnumerable<string> ReadAssemblyMetadataValues(string path, string key)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata) yield break;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) yield break;
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (!GetAttributeTypeName(reader, attribute.Constructor).Equals(
                    "System.Reflection.AssemblyMetadataAttribute", StringComparison.Ordinal)) continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) continue;
            var attributeKey = blob.ReadSerializedString();
            var attributeValue = blob.ReadSerializedString();
            if (key.Equals(attributeKey, StringComparison.Ordinal) && attributeValue is not null) yield return attributeValue;
        }
    }

    private static string GetAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        var type = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => GetTypeName(reader, reader.GetTypeReference((TypeReferenceHandle)type)),
            HandleKind.TypeDefinition => GetTypeName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)type)),
            _ => string.Empty
        };
    }

    private static string GetTypeName(MetadataReader reader, TypeReference type)
        => reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);

    private static string GetTypeName(MetadataReader reader, TypeDefinition type)
        => reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);

    private static string BoundedOutput(ProcessRunResult result)
    {
        var value = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdOut + Environment.NewLine + result.StdErr;
        const int limit = 64 * 1024;
        return value.Length <= limit ? value : value.Substring(value.Length - limit, limit);
    }

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string PackageSegment(string value)
    {
        var result = new string((value ?? string.Empty).Select(static character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(result) ? "Other" : result;
    }

    private sealed class PackageEntry
    {
        private PackageEntry(string packagePath, string? sourcePath, string? content, string? role, string? originalPath)
        {
            PackagePath = packagePath;
            SourcePath = sourcePath;
            Content = content;
            Role = role;
            OriginalPath = originalPath;
        }

        internal string PackagePath { get; }
        internal string? SourcePath { get; }
        internal string? Content { get; }
        internal string? Role { get; }
        internal string? OriginalPath { get; }
        internal static PackageEntry File(string packagePath, string sourcePath, string? role = null, string? originalPath = null) => new(packagePath, sourcePath, null, role, originalPath);
        internal static PackageEntry Text(string packagePath, string content) => new(packagePath, null, content, null, null);
    }

    private sealed class VerifiedInput
    {
        internal VerifiedInput(PowerShellCompilationArtifactFile file, string snapshotPath, string? generatedRelativePath)
        {
            File = file;
            SnapshotPath = snapshotPath;
            GeneratedRelativePath = generatedRelativePath;
        }

        internal PowerShellCompilationArtifactFile File { get; }
        internal string SnapshotPath { get; }
        internal string? GeneratedRelativePath { get; }
    }
}
