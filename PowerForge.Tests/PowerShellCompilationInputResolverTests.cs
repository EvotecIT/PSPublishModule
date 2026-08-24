using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationInputResolverTests
{
    [Fact]
    public void PublicCompilationResultConstructorsPreserveOriginalBinarySignatures()
    {
        Assert.NotNull(typeof(PowerShellCompiledMethod).GetConstructor(new[]
        {
            typeof(string), typeof(string), typeof(string), typeof(PowerShellCompilationParameter[]), typeof(int)
        }));
        Assert.NotNull(typeof(PowerShellTypedCompilationResult).GetConstructor(new[]
        {
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(PowerShellCompiledMethod[]), typeof(PowerShellCompilationDiagnostic[])
        }));
    }

    [Fact]
    public void Resolve_StandaloneScriptInfersPackagedExecutable()
    {
        using var fixture = ResolverFixture.Create();
        var script = fixture.Write("Invoke-Proof.ps1", "param([string] $Name) $Name");

        var resolved = new PowerShellCompilationInputResolver().Resolve(script);

        Assert.Equal(script, resolved.RequestedPath);
        Assert.Equal(script, resolved.SourcePath);
        Assert.Null(resolved.ModuleManifestPath);
        Assert.Equal("Invoke-Proof", resolved.ArtifactName);
        Assert.Equal(PowerShellCompilationArtifactKind.Executable, resolved.Kind);
        Assert.Equal(PowerShellCompilationMode.Package, resolved.Mode);
        Assert.Equal(new[] { script }, resolved.SourceFiles);
    }

    [Fact]
    public void Resolve_LooseScriptSetInfersStrictTypedLibrary()
    {
        using var fixture = ResolverFixture.Create();
        var first = fixture.Write("Get-One.ps1", "function Get-One { return 1 }");
        var second = fixture.Write("Public/Get-Two.ps1", "function Get-Two { return 2 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(new[] { first, second });

        Assert.Equal(first, resolved.SourcePath);
        Assert.Null(resolved.ModuleManifestPath);
        Assert.Equal(PowerShellCompilationArtifactKind.Library, resolved.Kind);
        Assert.Equal(PowerShellCompilationMode.Strict, resolved.Mode);
        Assert.Equal(new[] { first, second }, resolved.CompilationSourceFiles);
    }

    [Fact]
    public void Resolve_LooseExecutableSetUsesExplicitEntrypointAndReachableDependencyClosure()
    {
        using var fixture = ResolverFixture.Create();
        var first = fixture.Write("Main.ps1", ". \"$PSScriptRoot/Helper.ps1\"; Get-Helper");
        var second = fixture.Write("Helper.ps1", "function Get-Helper { return 2 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(
            new[] { first, second },
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package,
            first);

        Assert.Equal(first, resolved.SourcePath);
        Assert.Equal(new[] { first, second }, resolved.CompilationSourceFiles);
        Assert.Equal(PowerShellCompilationMode.Package, resolved.Mode);
    }

    [Fact]
    public void Resolve_LooseBinaryModuleSetRequiresStrictMode()
    {
        using var fixture = ResolverFixture.Create();
        var first = fixture.Write("Get-One.ps1", "function Get-One { return 1 }");
        var second = fixture.Write("Get-Two.ps1", "function Get-Two { return 2 }");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(
            new[] { first, second },
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.Contains("requires Strict mode", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ModuleDirectoryUsesMatchingManifestAndDiscoversLiteralSources()
    {
        using var fixture = ResolverFixture.Create("SampleModule");
        var manifest = fixture.Write(
            "SampleModule.psd1",
            "@{ RootModule = 'SampleModule.psm1'; ScriptsToProcess = @('Initialize.ps1'); FunctionsToExport = @('Get-Proof') }");
        var rootModule = fixture.Write(
            "SampleModule.psm1",
            """
            . "$PSScriptRoot/Public/Get-Proof.ps1"
            Export-ModuleMember -Function Get-Proof
            """);
        var initializer = fixture.Write("Initialize.ps1", "$script:Initialized = $true");
        var function = fixture.Write("Public/Get-Proof.ps1", "function Get-Proof { param([int] $Value) return $Value + 1 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.Root);

        Assert.Equal(rootModule, resolved.SourcePath);
        Assert.Equal(manifest, resolved.ModuleManifestPath);
        Assert.Equal("SampleModule", resolved.ArtifactName);
        Assert.Equal(PowerShellCompilationArtifactKind.BinaryModule, resolved.Kind);
        Assert.Equal(PowerShellCompilationMode.Hybrid, resolved.Mode);
        Assert.Equal(
            new[] { initializer, function, rootModule }.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
            resolved.SourceFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { function, rootModule }.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
            resolved.CompilationSourceFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_ExplicitManifestAllowsKindAndModeOverrides()
    {
        using var fixture = ResolverFixture.Create("DifferentDirectory");
        var manifest = fixture.Write("Product.psd1", "@{ RootModule = 'Product.psm1' }");
        var module = fixture.Write("Product.psm1", "function Get-Proof { return 1 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(
            manifest,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);

        Assert.Equal(module, resolved.SourcePath);
        Assert.Equal(manifest, resolved.ModuleManifestPath);
        Assert.Equal(PowerShellCompilationArtifactKind.Library, resolved.Kind);
        Assert.Equal(PowerShellCompilationMode.Strict, resolved.Mode);
    }

    [Fact]
    public void Resolve_CompilationScopeIncludesOnlyUnconditionalTopLevelDotSources()
    {
        using var fixture = ResolverFixture.Create("ScopedModule");
        fixture.Write("ScopedModule.psd1", "@{ RootModule = 'ScopedModule.psm1' }");
        var root = fixture.Write(
            "ScopedModule.psm1",
            """
            . "$PSScriptRoot/Public.ps1"
            if ($true) { . "$PSScriptRoot/Conditional.ps1" }
            function Invoke-Locally { . "$PSScriptRoot/Local.ps1" }
            """);
        var direct = fixture.Write("Public.ps1", "function Get-Public { return 1 }");
        var conditional = fixture.Write("Conditional.ps1", "function Get-Conditional { return 2 }");
        var local = fixture.Write("Local.ps1", "function Get-Local { return 3 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.Root);

        Assert.Equal(new[] { direct, root }.OrderBy(static path => path), resolved.CompilationSourceFiles.OrderBy(static path => path));
        Assert.Equal(new[] { conditional, direct, local, root }.OrderBy(static path => path), resolved.SourceFiles.OrderBy(static path => path));
    }

    [Fact]
    public void Resolve_ConventionalModuleLoaderDiscoversAuthoredPowerShellGlobsWithoutExecutingModuleCode()
    {
        using var fixture = ResolverFixture.Create("ConventionalModule");
        fixture.Write("ConventionalModule.psd1", "@{ RootModule = 'ConventionalModule.psm1' }");
        var root = fixture.Write(
            "ConventionalModule.psm1",
            """
            $Public = @(Get-ChildItem -Path $PSScriptRoot\Public\*.ps1 -Recurse)
            $Private = @(Get-ChildItem -Path $PSScriptRoot/Private/*.ps1 -Recurse)
            $Libraries = @(Get-ChildItem -Path $PSScriptRoot\Lib\*.dll -Recurse)
            function Find-Locally { Get-ChildItem -Path $PSScriptRoot\Ignored\*.ps1 -Recurse }
            foreach ($Import in @($Private + $Public)) { . $Import.FullName }
            """);
        var publicFunction = fixture.Write("Public/Get-PublicProof.ps1", "function Get-PublicProof { return 1 }");
        var privateFunction = fixture.Write("Private/Get-PrivateProof.ps1", "function Get-PrivateProof { return 2 }");
        fixture.Write("Ignored/Get-IgnoredProof.ps1", "function Get-IgnoredProof { return 3 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.Root);

        Assert.Equal(
            new[] { privateFunction, publicFunction, root }.OrderBy(static path => path),
            resolved.CompilationSourceFiles.OrderBy(static path => path));
        Assert.DoesNotContain(resolved.SourceFiles, path => path.Contains("Ignored", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_ConventionalModuleLoaderDoesNotHideUnrelatedDynamicDotSource()
    {
        using var fixture = ResolverFixture.Create("ConventionalModule");
        fixture.Write("ConventionalModule.psd1", "@{ RootModule = 'ConventionalModule.psm1' }");
        fixture.Write(
            "ConventionalModule.psm1",
            """
            $Public = @(Get-ChildItem -Path $PSScriptRoot\Public\*.ps1 -Recurse)
            foreach ($Import in $Public) { . $Import.FullName }
            . $Outside.FullName
            """);
        fixture.Write("Public/Get-PublicProof.ps1", "function Get-PublicProof { return 1 }");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(fixture.Root));

        Assert.Contains("literal $PSScriptRoot path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_BackslashScriptRootPathIsPortable()
    {
        using var fixture = ResolverFixture.Create("PortableModule");
        fixture.Write("PortableModule.psd1", "@{ RootModule = 'PortableModule.psm1'; ScriptsToProcess = @('Hooks\\Initialize.ps1') }");
        var root = fixture.Write("PortableModule.psm1", ". \"$PSScriptRoot\\Public\\Get-Proof.ps1\"");
        var function = fixture.Write("Public/Get-Proof.ps1", "function Get-Proof { return 1 }");
        var hook = fixture.Write("Hooks/Initialize.ps1", "$script:Ready = $true");

        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.Root);

        Assert.Contains(function, resolved.CompilationSourceFiles);
        Assert.Contains(hook, resolved.SourceFiles);
        Assert.Contains(root, resolved.SourceFiles);
    }

    [Fact]
    public void Resolve_RuntimeHookWinsWhenFileIsAlsoTopLevelDotSourced()
    {
        using var fixture = ResolverFixture.Create("DualRoleModule");
        fixture.Write("DualRoleModule.psd1", "@{ RootModule = 'DualRoleModule.psm1'; ScriptsToProcess = @('Shared.ps1') }");
        var root = fixture.Write("DualRoleModule.psm1", ". \"$PSScriptRoot/Shared.ps1\"");
        var shared = fixture.Write("Shared.ps1", "function Get-SharedValue { return 42 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.Root);

        Assert.Equal(new[] { root }, resolved.CompilationSourceFiles);
        Assert.Contains(shared, resolved.SourceFiles);
    }

    [Fact]
    public void Resolve_ExplicitModuleRejectsUnrelatedSiblingManifest()
    {
        using var fixture = ResolverFixture.Create();
        var selected = fixture.Write("Product.psm1", "function Get-Product { return 1 }");
        fixture.Write("Product.psd1", "@{ RootModule = 'Other.psm1' }");
        fixture.Write("Other.psm1", "function Get-Other { return 2 }");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(selected));

        Assert.Contains("does not point back", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ModuleInputRejectsExecutableOverride()
    {
        using var fixture = ResolverFixture.Create();
        var module = fixture.Write("Product.psm1", "function Get-Product { return 1 }");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(
            module,
            PowerShellCompilationArtifactKind.Executable));

        Assert.Contains("standalone .ps1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_DirectoryRejectsAmbiguousManifests()
    {
        using var fixture = ResolverFixture.Create("Container");
        fixture.Write("One.psd1", "@{ RootModule = 'One.psm1' }");
        fixture.Write("One.psm1", "function Get-One { return 1 }");
        fixture.Write("Two.psd1", "@{ RootModule = 'Two.psm1' }");
        fixture.Write("Two.psm1", "function Get-Two { return 2 }");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(fixture.Root));

        Assert.Contains("multiple top-level module manifests", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("One.psd1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Two.psd1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ManifestRejectsAlreadyBinaryRootModule()
    {
        using var fixture = ResolverFixture.Create();
        var manifest = fixture.Write("Binary.psd1", "@{ RootModule = 'Binary.dll' }");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationInputResolver().Resolve(manifest));

        Assert.Contains("already points to binary RootModule", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transpile_ModuleScopeRejectsDuplicateFunctionNamesAcrossFiles()
    {
        using var fixture = ResolverFixture.Create();
        var first = fixture.Write("One.ps1", "function Get-Proof { param([int] $Value) return $Value }");
        var second = fixture.Write("Two.ps1", "function Get-Proof { param([string] $Value) return $Value }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(new[] { first, second });

        Assert.Empty(result.Methods);
        Assert.Equal(new[] { first, second }, result.SourcePaths);
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Message.Contains("declared more than once", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.FilePath == first);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.FilePath == second);
    }

    [Fact]
    public void Transpile_ModuleScopeRejectsCrossFileGeneratedSignatureCollision()
    {
        using var fixture = ResolverFixture.Create();
        var first = fixture.Write("One.ps1", "function Get-Proof { param([int] $Value) return $Value }");
        var second = fixture.Write("Two.ps1", "function Get_Proof { param([int] $Value) return $Value }");

        var result = new PowerShellTypedCompilationTranspiler().Transpile(new[] { first, second });

        Assert.Empty(result.Methods);
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Message.Contains("generated CLR method signature", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class ResolverFixture : IDisposable
    {
        private ResolverFixture(string root, string cleanupRoot)
        {
            Root = root;
            CleanupRoot = cleanupRoot;
        }

        internal string Root { get; }
        private string CleanupRoot { get; }

        internal static ResolverFixture Create(string? directoryName = null)
        {
            var parent = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            var root = string.IsNullOrWhiteSpace(directoryName) ? parent : Path.Combine(parent, directoryName);
            Directory.CreateDirectory(root);
            return new ResolverFixture(root, parent);
        }

        internal string Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return Path.GetFullPath(path);
        }

        public void Dispose()
        {
            try { Directory.Delete(CleanupRoot, recursive: true); } catch { }
        }
    }
}
