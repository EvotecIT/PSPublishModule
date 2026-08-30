using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static PowerShellCompilationBoundaryEvidence CreateBoundaryEvidence(
        PowerShellCompilationUnitDispositionLedger ledger,
        PowerShellCompilationBoundaryRuntimeProfile? runtimeProfile)
    {
        var typedEntries = ledger.EmittedUnits;
        var hostedRegions = ledger.RuntimeCommandRegions;
        var staticAdvisory = ledger.RuntimeRoutedUnits > typedEntries
            ? "Runtime fallback units exceed typed entry points; profile this Hybrid artifact before assuming compilation improves the workload."
            : string.Empty;
        return new PowerShellCompilationBoundaryEvidence
        {
            TypedEntryPoints = typedEntries,
            HostedRegionSites = hostedRegions,
            RuntimeFallbackUnits = ledger.RuntimeRoutedUnits,
            RuntimeProfile = runtimeProfile,
            Advisory = !string.IsNullOrWhiteSpace(runtimeProfile?.Advisory)
                ? runtimeProfile!.Advisory
                : staticAdvisory
        };
    }

    internal static void ApplyExplicitTargetContract(PowerShellCompilationBuildSpec spec)
    {
        if (spec.TargetContract is null) return;
        var target = PowerShellCompilationTargetContractService.Normalize(spec.TargetContract);
        if (target.ArtifactKind != spec.Kind || target.Mode != spec.Mode)
            throw new ArgumentException("The explicit PowerShell compilation target kind and mode must match the build request.", nameof(spec));
        spec.TargetFramework = target.TargetFramework;
        spec.RuntimeIdentifier = string.IsNullOrWhiteSpace(target.RuntimeIdentifier) ? null : target.RuntimeIdentifier;
        spec.SingleFile = target.SingleFile;
        spec.SelfContained = target.Deployment is PowerShellCompilationDeploymentModel.SelfContained or
            PowerShellCompilationDeploymentModel.Trimmed or PowerShellCompilationDeploymentModel.ReadyToRun or
            PowerShellCompilationDeploymentModel.NativeAot;
        spec.Optimization = target.Deployment switch
        {
            PowerShellCompilationDeploymentModel.Trimmed => PowerShellCompilationExecutableOptimization.Trimmed,
            PowerShellCompilationDeploymentModel.NativeAot => PowerShellCompilationExecutableOptimization.NativeAot,
            PowerShellCompilationDeploymentModel.ReadyToRun => throw new ArgumentException(
                "ReadyToRun remains a benchmark-only lane and cannot be selected as a public artifact target.", nameof(spec)),
            _ => PowerShellCompilationExecutableOptimization.None
        };
    }

    private static PowerShellCompilationTargetContract ResolveTargetContract(
        PowerShellCompilationBuildSpec spec,
        string? runtimeIdentifier)
    {
        var target = spec.TargetContract is null
            ? PowerShellCompilationTargetContractService.Create(
                spec.Kind,
                spec.Mode,
                spec.TargetFramework,
                runtimeIdentifier,
                spec.SelfContained,
                spec.SingleFile,
                spec.Optimization,
                explicitContract: false)
            : PowerShellCompilationTargetContractService.Normalize(spec.TargetContract);
        var expected = PowerShellCompilationTargetContractService.Create(
            spec.Kind,
            spec.Mode,
            spec.TargetFramework,
            runtimeIdentifier,
            spec.SelfContained,
            spec.SingleFile,
            spec.Optimization,
            explicitContract: target.Explicit);
        if (target.ArtifactKind != expected.ArtifactKind || target.Mode != expected.Mode ||
            !target.TargetFramework.Equals(expected.TargetFramework, StringComparison.OrdinalIgnoreCase) ||
            !target.RuntimeIdentifier.Equals(expected.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) ||
            target.RuntimeRequirement != expected.RuntimeRequirement || target.Deployment != expected.Deployment ||
            target.SingleFile != expected.SingleFile ||
            target.AllowsPowerShellRuntimeEvaluation != expected.AllowsPowerShellRuntimeEvaluation ||
            !target.SupportLevel.Equals(expected.SupportLevel, StringComparison.Ordinal))
            throw new InvalidOperationException("The explicit PowerShell compilation target conflicts with the resolved semantic, runtime, or deployment build contract.");
        target.OperatingSystem = expected.OperatingSystem;
        target.Architecture = expected.Architecture;
        target.ContractSha256 = PowerShellCompilationTargetContractService.ComputeSha256(target);
        return target;
    }

    private static PowerShellCompilationToolchainEvidence CaptureToolchain(
        string workspace,
        PowerShellCompilationTargetContract target,
        PowerShellCompilationDependencyGraph dependencyGraph)
    {
        var sdkVersion = PowerShellCompilationToolchainFingerprint.ResolveSelectedSdk().Version;
        WriteSdkSelection(workspace, sdkVersion);
        var workspaceSdk = new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet",
            workspace,
            new[] { "--version" },
            TimeSpan.FromSeconds(30))).GetAwaiter().GetResult();
        if (!workspaceSdk.Succeeded || !workspaceSdk.StdOut.Trim().Equals(sdkVersion, StringComparison.Ordinal))
            throw new InvalidOperationException("Generated compilation workspace did not select the recorded dotnet SDK identity.");
        return new PowerShellCompilationToolchainEvidence
        {
            DotNetSdkVersion = sdkVersion,
            DotNetSdkSha256 = PowerShellCompilationToolchainFingerprint.ComputeSdkSha256(sdkVersion),
            CompilerVersion = typeof(PowerShellCompilationArtifactBuilder).Assembly.GetName().Version?.ToString() ?? string.Empty,
            CompilerSha256 = ComputeSha256(typeof(PowerShellCompilationArtifactBuilder).Assembly.Location),
            BuildOperatingSystem = RuntimeInformation.OSDescription,
            BuildArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            TargetContractSha256 = target.ContractSha256,
            DependencyLockSha256 = dependencyGraph.LockSha256
        };
    }

    private static void WriteSdkSelection(string workspace, string sdkVersion)
    {
        if (string.IsNullOrWhiteSpace(sdkVersion))
            throw new InvalidOperationException("The selected dotnet SDK returned an empty version identity.");
        File.WriteAllText(
            Path.Combine(workspace, "global.json"),
            JsonSerializer.Serialize(new
            {
                sdk = new
                {
                    version = sdkVersion,
                    rollForward = "disable",
                    allowPrerelease = true
                }
            }, EvidenceJsonOptions),
            new UTF8Encoding(false));
    }

    private static void WriteTargetContract(string workspace, PowerShellCompilationTargetContract target)
        => File.WriteAllText(
            Path.Combine(workspace, "PowerForge.TargetContract.json"),
            JsonSerializer.Serialize(target, EvidenceJsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static PowerShellCompilationArtifactFile[] WriteBuildEvidence(
        string workspace,
        string stagingDirectory,
        string artifactName,
        PowerShellCompilationBuildSpec spec,
        PowerShellCompilationTargetContract target,
        PowerShellCompilationToolchainEvidence toolchain,
        PowerShellCompilationDependencyGraph graph,
        PowerShellCompilationProviderLock providerLock,
        string generatedSourceSha256)
    {
        var resolvedPackages = PowerShellCompilationResolvedPackageCatalog.ReadAndVerify(workspace, graph);
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var sources = new[] { spec.SourcePath }.Concat(spec.CompilationSourcePaths ?? Array.Empty<string>())
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer)
            .Select(path => new
            {
                path = FrameworkCompatibility.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                sha256 = ComputeSha256(path)
            }).ToArray();
        var targetPath = Path.Combine(stagingDirectory, artifactName + ".powerforge-target.json");
        var provenancePath = Path.Combine(stagingDirectory, artifactName + ".powerforge-provenance.json");
        var sbomPath = Path.Combine(stagingDirectory, artifactName + ".powerforge-sbom.cdx.json");
        File.WriteAllText(targetPath, JsonSerializer.Serialize(target, EvidenceJsonOptions), new UTF8Encoding(false));
        File.WriteAllText(provenancePath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            targetContractSha256 = target.ContractSha256,
            dependencyLockSha256 = graph.LockSha256,
            providerLockSha256 = providerLock.Packages.Length == 0 ? string.Empty : providerLock.LockSha256,
            generatedSourceSha256,
            reviewedDependencyLock = spec.ExpectedDependencyLock is not null,
            reviewedProviderLock = providerLock.Packages.Length > 0 && spec.ExpectedProviderLock is not null,
            toolchain,
            sources,
            providerPackages = providerLock.Packages.Select(static package => new
            {
                id = package.PackageId,
                version = package.PackageVersion,
                package.SignerFingerprint,
                package.Signature,
                package.Publisher,
                package.LicenseExpression,
                package.PackageSha256,
                package.ManifestSha256
            }).ToArray(),
            resolvedPackages = resolvedPackages.Select(static package => new
            {
                id = package.Id,
                version = package.Version,
                contentHashAlgorithm = package.ContentHashAlgorithm,
                contentHash = package.ContentHash,
                directCompilerReference = package.DirectCompilerReference
            }).ToArray()
        }, EvidenceJsonOptions), new UTF8Encoding(false));
        File.WriteAllText(sbomPath, JsonSerializer.Serialize(new
        {
            bomFormat = "CycloneDX",
            specVersion = "1.5",
            serialNumber = "urn:uuid:" + GuidFromSha256(target.ContractSha256 + graph.LockSha256),
            version = 1,
            metadata = new { component = new { type = "application", name = artifactName } },
            components = graph.Nodes
                .Where(static node => node.Kind != PowerShellCompilationDependencyNodeKind.NuGetPackage &&
                                      (node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Deployment) ||
                                       node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Build)))
                .OrderBy(static node => node.Id, StringComparer.Ordinal)
                .Select(static node => (object)new
                {
                    type = node.Kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or PowerShellCompilationDependencyNodeKind.BinaryModule ? "library" : "file",
                    name = node.Identity.Name,
                    version = node.Identity.Version,
                    hashes = string.IsNullOrWhiteSpace(node.Identity.Sha256)
                        ? Array.Empty<object>()
                        : new object[] { new { alg = "SHA-256", content = node.Identity.Sha256 } },
                    properties = new[]
                    {
                        new { name = "powerforge:disposition", value = node.Disposition.ToString() },
                        new { name = "powerforge:source", value = node.Identity.Source }
                    }
                })
                .Concat(resolvedPackages.Select(static package => (object)new
                {
                    type = "library",
                    name = package.Id,
                    version = package.Version,
                    purl = "pkg:nuget/" + Uri.EscapeDataString(package.Id) + "@" + Uri.EscapeDataString(package.Version),
                    hashes = new object[]
                    {
                        new
                        {
                            alg = package.ContentHashAlgorithm,
                            content = string.Concat(Convert.FromBase64String(package.ContentHash)
                                .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)))
                        }
                    },
                    properties = new[]
                    {
                        new { name = "powerforge:disposition", value = "Referenced" },
                        new { name = "powerforge:source", value = "NuGetRestore" },
                        new { name = "powerforge:directCompilerReference", value = package.DirectCompilerReference.ToString() }
                    }
                }))
                .Concat(providerLock.Packages.Select(static package => (object)new
                {
                    type = "library",
                    name = package.PackageId,
                    version = package.PackageVersion,
                    purl = "pkg:nuget/" + Uri.EscapeDataString(package.PackageId) + "@" + Uri.EscapeDataString(package.PackageVersion),
                    hashes = new object[] { new { alg = "SHA-256", content = package.PackageSha256 } },
                    licenses = new object[] { new { license = new { id = package.LicenseExpression } } },
                    properties = new[]
                    {
                        new { name = "powerforge:disposition", value = "CompilerProvider" },
                        new { name = "powerforge:publisher", value = package.Publisher },
                        new { name = "powerforge:signature", value = package.Signature },
                        new { name = "powerforge:signerFingerprint", value = package.SignerFingerprint },
                        new { name = "powerforge:providerAbi", value = package.ProviderAbiVersion }
                    }
                }))
                .ToArray()
        }, EvidenceJsonOptions), new UTF8Encoding(false));
        return new[]
        {
            CreateArtifactFile(targetPath, "TargetContract"),
            CreateArtifactFile(provenancePath, "BuildProvenance"),
            CreateArtifactFile(sbomPath, "Sbom")
        };
    }

    private static string GuidFromSha256(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.Take(16).ToArray()).ToString("D");
    }

    private static JsonSerializerOptions EvidenceJsonOptions { get; } = new() { WriteIndented = true };
}
