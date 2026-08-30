using Xunit;
using System.Runtime.InteropServices;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationDependencyGraphTests
{
    [Fact]
    public void Resolve_BuildsDeterministicSharedDependencyGraphWithoutExecutingSource()
    {
        using var fixture = new GraphFixture();
        var managed = fixture.Write("Managed.dll", string.Empty);
        File.Copy(typeof(PowerShellCompilationPlan).Assembly.Location, managed, overwrite: true);
        var script = fixture.Write(
            "Demo.ps1",
            """
            #requires -Modules @{ ModuleName='External.Tools'; ModuleVersion='1.0.0'; MaximumVersion='2.0.0'; Guid='00000000-0000-0000-0000-000000000123' }, @{ ModuleName='Exact.Tools'; RequiredVersion='3.0.0' }
            using assembly './Managed.dll'
            Import-Module ActiveDirectory
            Add-Type -Path './Managed.dll'
            Start-Process 'tool.exe'
            New-Object -ComObject 'Scripting.FileSystemObject'
            [type]::GetTypeFromProgID('Shell.Application')
            [type]::GetTypeFromCLSID([guid]'0D43FE01-F093-11CF-8940-00A0C9054228')
            $nativeSignature = "[DllImport('native-demo')]"
            """);

        var input = new PowerShellCompilationInputResolver().Resolve(
            script,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);
        var repeated = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(input, targetFramework: "net8.0", runtimeIdentifier: "win-x64");

        Assert.Equal(repeated.LockSha256, new PowerShellCompilationDependencyPlanner().AnalyzeGraph(input, targetFramework: "net8.0", runtimeIdentifier: "win-x64").LockSha256);
        Assert.All(repeated.Nodes, node => Assert.False(string.IsNullOrWhiteSpace(node.Id)));
        Assert.All(repeated.Edges, edge =>
        {
            Assert.Contains(repeated.Nodes, node => node.Id == edge.FromId);
            Assert.Contains(repeated.Nodes, node => node.Id == edge.ToId);
        });
        Assert.Contains(repeated.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.ManagedLibrary && node.Identity.Provenance == "ManagedMetadataReadOnly");
        Assert.Contains(repeated.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.ExternalModule && node.Identity.Name == "ActiveDirectory" && node.Disposition == PowerShellCompilationDependencyGraphDisposition.Rejected);
        Assert.Contains(repeated.Nodes, node => node.Identity.Name == "External.Tools" && node.Identity.MinimumVersion == "1.0.0" && node.Identity.MaximumVersion == "2.0.0" && node.Identity.Guid == "00000000-0000-0000-0000-000000000123");
        Assert.Contains(repeated.Nodes, node => node.Identity.Name == "Exact.Tools" && node.Identity.RequiredVersion == "3.0.0");
        Assert.Contains(repeated.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.NativeLibrary && node.Identity.Name == "native-demo");
        Assert.Contains(repeated.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.ExternalProcess && node.Disposition == PowerShellCompilationDependencyGraphDisposition.Rejected);
        Assert.Contains(repeated.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.ComObject && node.Disposition == PowerShellCompilationDependencyGraphDisposition.Rejected);
        Assert.Contains(repeated.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.ComObject && node.Identity.InteropAdapter == "System.Type.GetTypeFromProgID" && node.Identity.ApartmentState == "HostThread");
        Assert.Contains(repeated.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.ComObject && node.Identity.Guid == "0D43FE01-F093-11CF-8940-00A0C9054228");
        Assert.Contains(repeated.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.UsingAssembly);
        Assert.Contains(repeated.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.RequiresModule);
        Assert.Contains(repeated.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.ImportModule);
        Assert.Contains(repeated.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.ManagedReference);
        Assert.Contains(repeated.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.NativeLoad);
        Assert.Contains(repeated.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.ProcessTarget);
        Assert.Contains(repeated.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.ComActivation);
    }

    [Fact]
    public void Analyze_UsesSameLockedGraphForSemanticAndDeploymentViews()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write("Demo.ps1", "function Get-Demo { return 1 }");
        var input = new PowerShellCompilationInputResolver().Resolve(
            script,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);

        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Strict, "net8.0");

        Assert.NotNull(plan.DependencyGraph);
        Assert.Equal(
            new PowerShellCompilationDependencyPlanner().AnalyzeGraph(input, PowerShellCompilationMode.Strict, targetFramework: "net8.0").LockSha256,
            plan.DependencyGraph!.LockSha256);
        Assert.Contains(plan.DependencyGraph.Nodes, node =>
            node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Semantic) &&
            node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Dependency) &&
            node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Deployment));
    }

    [Fact]
    public void Build_PublishesTheSameLockedDependencyGraphUsedByAnalysis()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write("Demo.ps1", "function Get-Demo { return 1 }");
        var input = new PowerShellCompilationInputResolver().Resolve(
            script,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);
        var expected = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(
            input,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            script,
            Path.Combine(fixture.Root, "out"),
            "Dependency.Graph",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net8.0",
            ExpectedDependencyLock = expected
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(11, result.Manifest!.SchemaVersion);
        Assert.True(result.Manifest.DependencyLockReviewed);
        Assert.NotNull(result.Manifest.DependencyGraph);
        Assert.Equal(expected.LockSha256, result.Manifest.DependencyGraph!.LockSha256);
    }

    [Fact]
    public void BuildSpecPlannerProducesExactExecutableLockAfterDefaultRidResolution()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write("Program.ps1", "param([int] $Value); return $Value");
        var spec = new PowerShellCompilationBuildSpec(
            script,
            Path.Combine(fixture.Root, "out"),
            "Dependency.Executable",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net10.0"
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.DependencyLockReviewed);
        Assert.Equal(spec.ExpectedDependencyLock.LockSha256, result.Manifest.DependencyGraph!.LockSha256);
        Assert.False(string.IsNullOrWhiteSpace(spec.RuntimeIdentifier));
    }

    [Fact]
    public void TargetContractOnlyPlannerProducesTheExactNativeAotLockConsumedByBuild()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write("TargetContract.ps1", "param([int] $Value); return $Value");
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var runtimeIdentifier = OperatingSystem.IsWindows() ? $"win-{architecture}" : OperatingSystem.IsLinux() ? $"linux-{architecture}" : $"osx-{architecture}";
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            "net10.0",
            runtimeIdentifier,
            selfContained: true,
            singleFile: true,
            PowerShellCompilationExecutableOptimization.NativeAot,
            explicitContract: true);
        var spec = new PowerShellCompilationBuildSpec(
            script,
            Path.Combine(fixture.Root, "target-contract-out"),
            "Dependency.TargetContract",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict)
        {
            TargetContract = target
        };

        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var runtimePackVersion = PowerShellCompilationToolchainFingerprint.ResolveRuntimePackVersion("net10.0");
        Assert.Contains(spec.ExpectedDependencyLock.Nodes, node =>
            node.Identity.Provenance == "DotNetRuntimePack" &&
            node.Identity.Source.Contains($"/{runtimePackVersion}/", StringComparison.Ordinal));
        Assert.Equal("net10.0", spec.TargetFramework);
        Assert.Equal(runtimeIdentifier, spec.RuntimeIdentifier);
        Assert.Equal(PowerShellCompilationExecutableOptimization.NativeAot, spec.Optimization);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(spec.ExpectedDependencyLock.LockSha256, result.Manifest!.DependencyGraph!.LockSha256);
        Assert.Equal(target.ContractSha256, result.Manifest.TargetContract!.ContractSha256);
    }

    [Fact]
    public void Build_RejectsReviewedDependencyLockAfterSourceDrift()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write("Demo.ps1", "function Get-Demo { return 1 }");
        var input = new PowerShellCompilationInputResolver().Resolve(
            script,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);
        var expected = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(
            input,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0");
        File.WriteAllText(script, "function Get-Demo { return 2 }");
        var output = Path.Combine(fixture.Root, "out");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            script,
            output,
            "Dependency.Graph.Drift",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net8.0",
            ExpectedDependencyLock = expected
        });

        Assert.False(result.Succeeded);
        Assert.Contains("dependency lock drifted", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public void Build_RejectsReviewedDependencyLockWhoseContentHashWasTampered()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write("Demo.ps1", "function Get-Demo { return 1 }");
        var expected = new PowerShellCompilationInputResolver().Resolve(
            script,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict).DependencyGraph;
        expected.Nodes[0].Identity.Name = "tampered-after-review";
        var output = Path.Combine(fixture.Root, "out");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            script,
            output,
            "Dependency.Graph.Tampered",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            ExpectedDependencyLock = expected
        });

        Assert.False(result.Succeeded);
        Assert.Contains("invalid content hash", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public void Build_RequiresReviewedDependencyLockUnlessDevelopmentOptOutIsExplicit()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write("Demo.ps1", "function Get-Demo { return 1 }");
        var output = Path.Combine(fixture.Root, "out");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            script,
            output,
            "Dependency.Graph.Required",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("separately reviewed dependency lock", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public void Resolve_RequiredModuleRangeChoosesHighestSatisfyingVersionAndSkipsInvalidUnversionedCandidate()
    {
        using var fixture = new GraphFixture();
        fixture.Write("Demo.psm1", "function Get-Demo { return 1 }");
        fixture.Write("Demo.psd1", "@{ RootModule='Demo.psm1'; ModuleVersion='1.0.0'; RequiredModules=@(@{ ModuleName='Foo'; ModuleVersion='1.0.0'; MaximumVersion='2.5.0' }) }");
        fixture.Write("Foo/Foo.psd1", "@{ ModuleVersion='invalid' }");
        fixture.Write("Foo/1.5.0/Foo.psd1", "@{ ModuleVersion='1.5.0' }");
        var selected = fixture.Write("Foo/2.0.0/Foo.psd1", "@{ ModuleVersion='2.0.0' }");
        fixture.Write("Foo/3.0.0/Foo.psd1", "@{ ModuleVersion='3.0.0' }");

        var input = new PowerShellCompilationInputResolver().Resolve(
            Path.Combine(fixture.Root, "Demo.psd1"),
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var graph = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(input, PowerShellCompilationMode.Hybrid);

        var module = Assert.Single(graph.Nodes, node =>
            node.Identity.Source.Replace('\\', '/').EndsWith("Foo/2.0.0/Foo.psd1", StringComparison.OrdinalIgnoreCase));
        Assert.True(module.Exists);
        Assert.Equal("Foo", module.Identity.Name);
        Assert.Equal("2.0.0", module.Identity.Version);
        Assert.Equal("1.0.0", module.Identity.MinimumVersion);
        Assert.Equal("2.5.0", module.Identity.MaximumVersion);
        Assert.Equal("2.0.0", ModuleManifestValueReader.ReadTopLevelString(selected, "ModuleVersion"));
        Assert.EndsWith("Foo/2.0.0/Foo.psd1", module.Identity.Source.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ConflictsOnDifferentSelectedVersionsOfTheSameLocalModule()
    {
        using var fixture = new GraphFixture();
        fixture.Write("Demo.psm1", "function Get-Demo { return 1 }");
        fixture.Write("Demo.psd1", "@{ RootModule='Demo.psm1'; ModuleVersion='1.0.0'; RequiredModules=@(@{ModuleName='Shared';RequiredVersion='1.0.0'},@{ModuleName='Shared';RequiredVersion='2.0.0'}) }");
        fixture.Write("Shared/1.0.0/Shared.psd1", "@{ ModuleVersion='1.0.0' }");
        fixture.Write("Shared/2.0.0/Shared.psd1", "@{ ModuleVersion='2.0.0' }");

        var input = new PowerShellCompilationInputResolver().Resolve(
            Path.Combine(fixture.Root, "Demo.psd1"),
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var graph = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(input, PowerShellCompilationMode.Hybrid);

        Assert.Contains(graph.Nodes, static node => node.Identity.Name == "Shared" && node.Identity.Version == "1.0.0");
        Assert.Contains(graph.Nodes, static node => node.Identity.Name == "Shared" && node.Identity.Version == "2.0.0");
        Assert.Contains(graph.Conflicts, static conflict => conflict.Contains("Shared", StringComparison.OrdinalIgnoreCase) && conflict.Contains("1.0.0", StringComparison.Ordinal) && conflict.Contains("2.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public void DependencyGraphConflictsIncludeExactManagedAndModuleIdentityCollisions()
    {
        var managed = new[]
        {
            DependencyNode(PowerShellCompilationDependencyNodeKind.ManagedLibrary, "Shared.Managed", "1.0.0.0", publicKeyToken: "aaaaaaaaaaaaaaaa", culture: "neutral", sha256: "1111"),
            DependencyNode(PowerShellCompilationDependencyNodeKind.ManagedLibrary, "Shared.Managed", "1.0.0.0", publicKeyToken: "bbbbbbbbbbbbbbbb", culture: "fr-FR", sha256: "2222")
        };
        var modules = new[]
        {
            DependencyNode(PowerShellCompilationDependencyNodeKind.ModuleManifest, "Shared.Module", "1.0.0", guid: "00000000-0000-0000-0000-000000000001", sha256: "3333"),
            DependencyNode(PowerShellCompilationDependencyNodeKind.ModuleManifest, "Shared.Module", "1.0.0", guid: "00000000-0000-0000-0000-000000000002", sha256: "4444")
        };

        var conflicts = PowerShellCompilationDependencyGraphBuilder.FindConflicts(managed.Concat(modules));

        Assert.Contains(conflicts, static conflict => conflict.Contains("Shared.Managed", StringComparison.Ordinal) && conflict.Contains("public-key token", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(conflicts, static conflict => conflict.Contains("Shared.Managed", StringComparison.Ordinal) && conflict.Contains("culture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(conflicts, static conflict => conflict.Contains("Shared.Managed", StringComparison.Ordinal) && conflict.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(conflicts, static conflict => conflict.Contains("Shared.Module", StringComparison.Ordinal) && conflict.Contains("GUID", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(conflicts, static conflict => conflict.Contains("Shared.Module", StringComparison.Ordinal) && conflict.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));

        var signedAndUnsigned = PowerShellCompilationDependencyGraphBuilder.FindConflicts(new[]
        {
            DependencyNode(PowerShellCompilationDependencyNodeKind.ManagedLibrary, "Mixed.Identity", "1.0.0.0", publicKeyToken: string.Empty),
            DependencyNode(PowerShellCompilationDependencyNodeKind.ManagedLibrary, "Mixed.Identity", "1.0.0.0", publicKeyToken: "aaaaaaaaaaaaaaaa")
        });
        Assert.Contains(signedAndUnsigned, static conflict =>
            conflict.Contains("Mixed.Identity", StringComparison.Ordinal) &&
            conflict.Contains("<unsigned>", StringComparison.Ordinal) &&
            conflict.Contains("aaaaaaaaaaaaaaaa", StringComparison.Ordinal));

        static PowerShellCompilationDependencyNode DependencyNode(
            PowerShellCompilationDependencyNodeKind kind,
            string name,
            string version,
            string publicKeyToken = "",
            string culture = "",
            string guid = "",
            string sha256 = "")
            => new()
            {
                Kind = kind,
                Identity = new PowerShellCompilationDependencyIdentity
                {
                    Name = name,
                    Version = version,
                    PublicKeyToken = publicKeyToken,
                    Culture = culture,
                    Guid = guid,
                    Sha256 = sha256
                }
            };
    }

    [Fact]
    public void DependencyGraphConflictsKeepFrameworkAndRidVariantsIndependent()
    {
        var nodes = new[]
        {
            Variant("Shared.Managed", "net47", string.Empty, "1111"),
            Variant("Shared.Managed", "netstandard2.1", string.Empty, "2222"),
            Variant("Native.Managed", "net8.0", "win-x64", "3333"),
            Variant("Native.Managed", "net8.0", "linux-x64", "4444"),
            Variant("Edition.Managed", "net8.0", string.Empty, "5555", "Desktop"),
            Variant("Edition.Managed", "net8.0", string.Empty, "6666", "Core")
        };

        Assert.Empty(PowerShellCompilationDependencyGraphBuilder.FindConflicts(nodes));

        static PowerShellCompilationDependencyNode Variant(string name, string framework, string rid, string hash, string edition = "")
            => new()
            {
                Kind = PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                Identity = new PowerShellCompilationDependencyIdentity
                {
                    Name = name,
                    Version = "1.0.0.0",
                    Edition = edition,
                    TargetFramework = framework,
                    RuntimeIdentifier = rid,
                    Sha256 = hash
                }
            };
    }

    [Fact]
    public void DependencyGraphAllowsExternalManagedReferenceVersionUnification()
    {
        var nodes = new[] { External("3.0.0.0"), External("7.4.6.500") };

        Assert.Empty(PowerShellCompilationDependencyGraphBuilder.FindConflicts(nodes));

        static PowerShellCompilationDependencyNode External(string version)
            => new()
            {
                Kind = PowerShellCompilationDependencyNodeKind.ManagedLibrary,
                Disposition = PowerShellCompilationDependencyGraphDisposition.External,
                Identity = new PowerShellCompilationDependencyIdentity
                {
                    Name = "Runtime.Provided.Assembly",
                    Version = version,
                    TargetFramework = "net8.0",
                    PublicKeyToken = "aaaaaaaaaaaaaaaa"
                }
            };
    }

    [Fact]
    public void DependencyLockHashUsesOneFixedLineFeedOnEveryOperatingSystem()
    {
        var graph = new PowerShellCompilationDependencyGraph { Conflicts = new[] { "x" } };

        Assert.Equal("6cc5d449f3396b3b17e061bfb8637d51998fd1329d110b2fce32c4c699c3db91", PowerShellCompilationDependencyLockHasher.ComputeSha256(graph));
    }

    [Fact]
    public void Resolve_RecordsTransitiveManifestIdentityHooksAndCycle()
    {
        using var fixture = new GraphFixture();
        fixture.Write("Demo.psm1", "function Get-Demo { return 1 }");
        fixture.Write(
            "Demo.psd1",
            "@{ RootModule='Demo.psm1'; ModuleVersion='1.2.3'; RequiredModules=@(@{ModuleName='External.One';RequiredVersion='2.0.0'},@{ModuleName='./Child/Child.psd1';RequiredVersion='2.1.0'}); NestedModules=@('Nested/Nested.psd1'); ScriptsToProcess=@('Initialize.ps1'); TypesToProcess=@('Demo.Types.ps1xml') }");
        fixture.Write("Initialize.ps1", "$script:initialized = $true");
        fixture.Write("Demo.Types.ps1xml", "<Types />");
        fixture.Write("Nested/Nested.psd1", "@{ RootModule='../Demo.psd1'; ModuleVersion='1.0.0' }");
        fixture.Write("Child/Child.psm1", "function Get-Child { return 1 }");
        fixture.Write("Child/Child.psd1", "@{ RootModule='Child.psm1'; ModuleVersion='2.1.0'; RequiredModules=@(@{ModuleName='../Grand/Grand.psd1';RequiredVersion='4.0.0'}) }");
        fixture.Write("Grand/Grand.psm1", "function Get-Grand { return 1 }");
        fixture.Write("Grand/Grand.psd1", "@{ RootModule='Grand.psm1'; ModuleVersion='4.0.0' }");

        var input = new PowerShellCompilationInputResolver().Resolve(
            fixture.Root,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var graph = input.DependencyGraph;

        Assert.Contains(graph.Nodes, node => node.Identity.Name == "External.One" && node.Identity.Version == "2.0.0");
        Assert.Contains(graph.Nodes, node => node.Identity.Name == "./Child/Child.psd1" && node.Identity.RequiredVersion == "2.1.0");
        Assert.Contains(graph.Nodes, node => node.Identity.Name == "../Grand/Grand.psd1" && node.Identity.RequiredVersion == "4.0.0");
        Assert.Contains(graph.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.ModuleInitialization);
        Assert.Contains(graph.Edges, edge => edge.Kind == PowerShellCompilationDependencyEdgeKind.Metadata);
        Assert.NotEmpty(graph.Cycles);
        Assert.All(graph.Nodes.Where(node => node.Disposition == PowerShellCompilationDependencyGraphDisposition.Bundled), node =>
            Assert.Equal("Unverified", node.Policy.Redistribution));
    }

    [Fact]
    public void Hybrid_SeparatesManagedHostedAndAdapterContracts()
    {
        using var fixture = new GraphFixture();
        var managed = fixture.Write("Wrapper.dll", string.Empty);
        File.Copy(typeof(PowerShellCompilationPlan).Assembly.Location, managed, overwrite: true);
        var script = fixture.Write(
            "Demo.ps1",
            """
            using assembly './Wrapper.dll'
            Import-Module ActiveDirectory
            function Invoke-Demo { Write-Verbose 'adapter'; Get-ADUser -Identity 'demo' }
            """);
        var input = new PowerShellCompilationInputResolver().Resolve(
            script,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);

        Assert.Contains(input.DependencyGraph.Nodes, node => node.Kind == PowerShellCompilationDependencyNodeKind.ManagedLibrary && node.Disposition == PowerShellCompilationDependencyGraphDisposition.Referenced);
        Assert.Contains(input.DependencyGraph.Nodes, node => node.Identity.Name == "ActiveDirectory" && node.Disposition == PowerShellCompilationDependencyGraphDisposition.Hosted);
        var provider = PowerShellCommandSemanticRegistry.Default.Resolve("Write-Verbose");
        Assert.NotNull(provider.Contract);
        Assert.NotNull(provider.Contract!.Adapter);
        Assert.True(provider.Contract.Adapter!.RuntimeFree);
    }

    [Fact]
    public async Task GraphLocksAdjacentManagedReferencesAndManagedWrapperNativeImportsTransitively()
    {
        using var fixture = new GraphFixture();
        var dependency = Path.Combine(fixture.Root, "Dependency");
        var wrapper = Path.Combine(fixture.Root, "Wrapper");
        var output = Path.Combine(fixture.Root, "out");
        Directory.CreateDirectory(dependency);
        Directory.CreateDirectory(wrapper);
        fixture.Write("Dependency/Dependency.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Managed.Child</AssemblyName></PropertyGroup></Project>");
        fixture.Write("Dependency/Value.cs", "namespace Managed.Child; public static class Value { public static int Get() => 1; }");
        fixture.Write("Wrapper/Wrapper.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>Managed.Wrapper</AssemblyName></PropertyGroup><ItemGroup><ProjectReference Include=\"../Dependency/Dependency.csproj\" /></ItemGroup></Project>");
        fixture.Write("Wrapper/Value.cs", "using System.Runtime.InteropServices; public static class WrapperValue { [DllImport(\"nativeproof\")] private static extern int Native(); public static int Get() => Managed.Child.Value.Get(); }");
        var build = await new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet", fixture.Root, new[] { "build", Path.Combine(wrapper, "Wrapper.csproj"), "-c", "Release", "-o", output, "--nologo", "--verbosity", "quiet" }, TimeSpan.FromSeconds(60)));
        Assert.True(build.Succeeded, build.StdErr + Environment.NewLine + build.StdOut);
        var script = fixture.Write("Demo.ps1", "using assembly './out/Managed.Wrapper.dll'\nfunction Get-Demo { return 1 }");
        var input = new PowerShellCompilationInputResolver().Resolve(script, PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict);

        var graph = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(
            input,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0",
            runtimeIdentifier: "win-x64");

        var wrapperNode = Assert.Single(graph.Nodes, static node => node.Identity.Name == "Managed.Wrapper");
        var childNode = Assert.Single(graph.Nodes, static node => node.Identity.Name == "Managed.Child");
        var nativeNode = Assert.Single(graph.Nodes, static node => node.Kind == PowerShellCompilationDependencyNodeKind.NativeLibrary && node.Identity.Name == "nativeproof");
        Assert.Equal(PowerShellCompilationDependencyGraphDisposition.External, nativeNode.Disposition);
        Assert.Equal("win-x64", nativeNode.Identity.RuntimeIdentifier);
        Assert.Contains(graph.Edges, edge => edge.FromId == wrapperNode.Id && edge.ToId == childNode.Id && edge.Kind == PowerShellCompilationDependencyEdgeKind.ManagedReference);
        Assert.Contains(graph.Edges, edge => edge.FromId == wrapperNode.Id && edge.ToId == nativeNode.Id && edge.Kind == PowerShellCompilationDependencyEdgeKind.NativeLoad);
    }

    [Fact]
    public void InteropMatrixLocksRidErrorsCancellationCleanupAndComApartmentWithoutActivation()
    {
        using var fixture = new GraphFixture();
        var script = fixture.Write(
            "Interop.ps1",
            "[type]::GetTypeFromProgID('Shell.Application'); [type]::GetTypeFromCLSID([guid]'0D43FE01-F093-11CF-8940-00A0C9054228'); Start-Process 'tool.exe'; $signature = \"[DllImport('native-demo')]\"");

        PowerShellCompilationDependencyGraph Resolve(PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode)
            => new PowerShellCompilationDependencyPlanner().AnalyzeGraph(
                new PowerShellCompilationInputResolver().Resolve(script, kind, mode),
                mode,
                targetFramework: "net8.0",
                runtimeIdentifier: "win-x64");

        var package = Resolve(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package);
        var hybrid = Resolve(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        var strict = Resolve(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict);

        Assert.Equal(6, strict.SchemaVersion);
        Assert.All(new[] { package, hybrid }, graph => Assert.All(
            graph.Nodes.Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.ComObject),
            static node =>
            {
                Assert.Equal(PowerShellCompilationDependencyGraphDisposition.Hosted, node.Disposition);
                Assert.Equal("Windows", node.Interop.Platform);
                Assert.Equal("HostStop", node.Interop.Cancellation);
                Assert.Contains("ReleaseComObject", node.Interop.Cleanup, StringComparison.Ordinal);
                Assert.Contains("HostThread", node.Interop.Threading, StringComparison.Ordinal);
            }));
        Assert.All(strict.Nodes.Where(static node => node.Kind == PowerShellCompilationDependencyNodeKind.ComObject), static node =>
        {
            Assert.Equal(PowerShellCompilationDependencyGraphDisposition.Rejected, node.Disposition);
            Assert.Equal("TypedComAdapterRequired", node.Interop.Owner);
            Assert.Equal("RejectedBeforePublication", node.Interop.Errors);
        });

        var strictProcess = Assert.Single(strict.Nodes, static node => node.Kind == PowerShellCompilationDependencyNodeKind.ExternalProcess);
        Assert.Equal(PowerShellCompilationDependencyGraphDisposition.Rejected, strictProcess.Disposition);
        Assert.Equal("win-x64", strictProcess.Interop.Platform);
        Assert.Equal("ExplicitAdapterRequired", strictProcess.Interop.Cancellation);
        Assert.Equal("ExplicitChildCleanupRequired", strictProcess.Interop.Cleanup);
        var strictNative = Assert.Single(strict.Nodes, static node => node.Kind == PowerShellCompilationDependencyNodeKind.NativeLibrary);
        Assert.Equal(PowerShellCompilationDependencyGraphDisposition.Rejected, strictNative.Disposition);
        Assert.Equal("TypedNativeAdapterRequired", strictNative.Interop.Owner);
        Assert.Equal("ExplicitHandleAndUnloadRequired", strictNative.Interop.Cleanup);
        Assert.Equal(strict.LockSha256, PowerShellCompilationDependencyLockHasher.ComputeSha256(strict));
    }

    private sealed class GraphFixture : IDisposable
    {
        internal GraphFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "PowerForge Dependency Graph Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        internal string Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
