using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationDependencyPlannerTests
{
    [Fact]
    public void Analyze_ClassifiesConventionalFoldersAsHintsWithoutIncludingThem()
    {
        using var fixture = new DependencyFixture();
        fixture.CreateModule();

        var input = new PowerShellCompilationInputResolver().Resolve(
            fixture.RootPath,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var dependencies = input.Dependencies;

        Assert.Contains(dependencies, dependency =>
            dependency.RelativePath == "Resources/site.css" &&
            dependency.Kind == PowerShellCompilationDependencyKind.StyleSheet &&
            dependency.Discovery == PowerShellCompilationDependencyDiscovery.ConventionalResourceDirectory &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.NotIncluded &&
            dependency.Selection == PowerShellCompilationDependencySelection.Unclassified);
        Assert.Contains(dependencies, dependency =>
            dependency.RelativePath == "Resources/app.js" &&
            dependency.Kind == PowerShellCompilationDependencyKind.JavaScript);
        Assert.Contains(dependencies, dependency =>
            dependency.RelativePath == "Lib/PowerForge.dll" &&
            dependency.Kind == PowerShellCompilationDependencyKind.ManagedAssembly);
        Assert.Contains(dependencies, dependency =>
            dependency.RelativePath == "Lib/native.dll" &&
            dependency.Kind == PowerShellCompilationDependencyKind.NativeLibrary);
        Assert.Contains(dependencies, dependency =>
            dependency.RelativePath == "Assets/data.txt" &&
            dependency.Discovery == PowerShellCompilationDependencyDiscovery.FileList &&
            dependency.Selection == PowerShellCompilationDependencySelection.Required);
        Assert.Contains(dependencies, dependency =>
            dependency.Name == "PSSharedGoods" &&
            !dependency.Exists &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.ExternalRequirement);
    }

    [Fact]
    public void Analyze_MarksAbsentFileListContentAsMissingRequiredPayload()
    {
        using var fixture = new DependencyFixture();
        fixture.Write("Demo.psm1", "function Get-Value { return 1 }");
        fixture.Write("Demo.psd1", "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'; FileList = @('Missing.txt', 'Missing/Other.txt') }");

        var input = new PowerShellCompilationInputResolver().Resolve(
            fixture.RootPath,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var missing = input.Dependencies.Where(dependency => dependency.RelativePath.StartsWith("Missing", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, missing.Length);
        Assert.All(missing, dependency =>
        {
            Assert.False(dependency.Exists);
            Assert.Equal(PowerShellCompilationDependencyDisposition.Missing, dependency.Disposition);
            Assert.Equal(PowerShellCompilationDependencySelection.Required, dependency.Selection);
            Assert.Contains("required", dependency.Note, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Analyze_SingleScriptDoesNotSweepNeighboringConventionalFolders()
    {
        using var fixture = new DependencyFixture();
        var script = fixture.Write("Tool.ps1", "param([string] $Name) return $Name");
        fixture.Write(Path.Combine("Resources", "app.js"), "console.log('tool');");

        var input = new PowerShellCompilationInputResolver().Resolve(
            script,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package);
        Assert.DoesNotContain(input.Dependencies, dependency => dependency.RelativePath == "Resources/app.js");
    }

    [Fact]
    public void Analyze_StrictModuleDistinguishesBinaryNestedManifestFromScriptHook()
    {
        using var fixture = new DependencyFixture();
        fixture.Write("Demo.psm1", "function Get-Value { return 1 }");
        fixture.Write(
            "Demo.psd1",
            "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'; NestedModules = @('Nested/Binary.psd1', 'Nested/Script.psm1') }");
        fixture.Write("Nested/Binary.psd1", "@{ RootModule = 'Binary.dll'; ModuleVersion = '1.0.0' }");
        var binaryPath = fixture.Write("Nested/Binary.dll", string.Empty);
        File.Copy(typeof(PowerShellCompilationPlan).Assembly.Location, binaryPath, overwrite: true);
        fixture.Write("Nested/Script.psm1", "function Get-Nested { return 2 }");

        var input = new PowerShellCompilationInputResolver().Resolve(
            fixture.RootPath,
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        Assert.Contains(input.Dependencies, dependency =>
            dependency.RelativePath == "Nested/Binary.psd1" &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent);
        Assert.Contains(input.Dependencies, dependency =>
            dependency.RelativePath == "Nested/Binary.dll" &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent);
        Assert.Contains(input.Dependencies, dependency =>
            dependency.RelativePath == "Nested/Script.psm1" &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.NotIncluded);
    }

    [Fact]
    public void Build_CompleteModulePreservesOptionalPayloadAndRecordsDisposition()
    {
        using var fixture = new DependencyFixture();
        fixture.CreateModule();
        var output = Path.Combine(fixture.RootPath, "out");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            Path.Combine(fixture.RootPath, "Demo.psm1"),
            output,
            "Demo.Compiled",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = Path.Combine(fixture.RootPath, "Demo.psd1"),
            CompilationSourcePaths = new[] { Path.Combine(fixture.RootPath, "Demo.psm1") },
            ResourceMode = PowerShellCompilationResourceMode.CompleteModule,
            TargetFramework = "net8.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var moduleDirectory = Path.GetDirectoryName(result.ArtifactPath!)!;
        Assert.Equal("body{}", File.ReadAllText(Path.Combine(moduleDirectory, "Resources", "site.css")));
        Assert.Equal("console.log('demo');", File.ReadAllText(Path.Combine(moduleDirectory, "Resources", "app.js")));
        Assert.True(File.Exists(Path.Combine(moduleDirectory, "Lib", "PowerForge.dll")));
        Assert.True(File.Exists(Path.Combine(moduleDirectory, "Lib", "native.dll")));
        Assert.True(File.Exists(Path.Combine(moduleDirectory, "Assets", "data.txt")));
        Assert.Contains(result.Manifest!.Dependencies, dependency =>
            dependency.RelativePath == "Resources/site.css" &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent);
        Assert.Contains(result.Manifest.Files, file => file.Role == "ModuleStyleSheet" && file.Path.EndsWith("site.css", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Manifest.Files, file => file.Role == "ManagedDependency" && file.Path.EndsWith("PowerForge.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            PowerShellCompilationArtifactSigner.GetBuildOwnedSignableFiles(result.Manifest.Files),
            path => path.EndsWith(Path.Combine("Lib", "PowerForge.dll"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_RejectsRuntimeSourceOutsideTheEntrypointRoot()
    {
        using var fixture = new DependencyFixture();
        var script = fixture.Write("Tool.ps1", "return 1");
        var outside = Path.Combine(Path.GetTempPath(), "PowerForge Outside Runtime Source", Guid.NewGuid().ToString("N") + ".ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "return 2");
        try
        {
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                script,
                Path.Combine(fixture.RootPath, "out"),
                "Escaping.Runtime.Source",
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true)
            {
                CompilationSourcePaths = new[] { script },
                RuntimeSourcePaths = new[] { script, outside }
            });

            Assert.False(result.Succeeded);
            Assert.Contains("escapes the root", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    private sealed class DependencyFixture : IDisposable
    {
        internal DependencyFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForge Dependency Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        internal string RootPath { get; }

        internal void CreateModule()
        {
            Write(
                "Demo.psm1",
                "function Get-TypedValue { return 1 }; function Get-FallbackValue { & { return 2 } }");
            Write(
                "Demo.psd1",
                "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-TypedValue', 'Get-FallbackValue'); CmdletsToExport = @(); AliasesToExport = @(); VariablesToExport = @(); RequiredModules = @('PSSharedGoods'); FileList = @('Assets/data.txt') }");
            Write(Path.Combine("Resources", "site.css"), "body{}");
            Write(Path.Combine("Resources", "app.js"), "console.log('demo');");
            Write(Path.Combine("Assets", "data.txt"), "payload");
            var libraryPath = Write(Path.Combine("Lib", "PowerForge.dll"), string.Empty);
            File.Copy(typeof(PowerShellCompilationPlan).Assembly.Location, libraryPath, overwrite: true);
            Write(Path.Combine("Lib", "native.dll"), "not-a-managed-assembly");
        }

        internal string Write(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
