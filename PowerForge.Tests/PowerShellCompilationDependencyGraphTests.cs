using Xunit;

namespace PowerForge.Tests;

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
        Assert.Equal(4, result.Manifest!.SchemaVersion);
        Assert.NotNull(result.Manifest.DependencyGraph);
        Assert.Equal(expected.LockSha256, result.Manifest.DependencyGraph!.LockSha256);
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
