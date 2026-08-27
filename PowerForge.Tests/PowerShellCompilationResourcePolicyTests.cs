using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationResourcePolicyTests
{
    [Fact]
    public void Analyze_ExplicitArbitraryFolderIncludesAndOptionalExclusionsAreDeterministic()
    {
        using var fixture = new ResourceFixture(module: true);
        fixture.Write("Vendor/tool.dat", "vendor");
        fixture.Write("Templates/report.html", "<html />");
        fixture.Write("Docs/readme.md", "docs");

        var resolved = fixture.ResolveModule();
        var plan = new PowerShellCompilationAnalyzer().Analyze(
            resolved,
            PowerShellCompilationMode.Hybrid,
            includeResource: new[] { "Vendor/**", "Templates" },
            excludeResource: new[] { "Docs/**" });

        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Vendor/tool.dat" &&
            dependency.Selection == PowerShellCompilationDependencySelection.ExplicitInclude &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent);
        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Templates/report.html" &&
            dependency.Selection == PowerShellCompilationDependencySelection.ExplicitInclude);
        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Docs/readme.md" &&
            dependency.Selection == PowerShellCompilationDependencySelection.Excluded &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.NotIncluded);
        Assert.Equal(2, plan.ResourceSummary.IncludedFiles);
        Assert.Equal(1, plan.ResourceSummary.ExcludedFiles);
    }

    [Fact]
    public void Analyze_RejectsExclusionOfManifestRequiredFile()
    {
        using var fixture = new ResourceFixture(module: true, fileList: "Data/required.json");
        fixture.Write("Data/required.json", "{}");
        var resolved = fixture.ResolveModule();

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
            resolved,
            PowerShellCompilationMode.Hybrid,
            excludeResource: new[] { "Data/**" }));

        Assert.Contains("cannot be excluded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RejectsExclusionWhenManifestRequiredFileIsAlsoACompilationSource()
    {
        using var fixture = new ResourceFixture(module: true, fileList: "Demo.psm1");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
            fixture.ResolveModule(),
            PowerShellCompilationMode.Hybrid,
            excludeResource: new[] { "Demo.psm1" }));

        Assert.Contains("cannot be excluded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RejectsConflictingAndUnmatchedPatterns()
    {
        using var fixture = new ResourceFixture(module: true);
        fixture.Write("Data/value.json", "{}");
        var resolved = fixture.ResolveModule();

        var conflict = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
            resolved,
            PowerShellCompilationMode.Hybrid,
            includeResource: new[] { "Data/**" },
            excludeResource: new[] { "Data/value.json" }));
        Assert.Contains("both IncludeResource and ExcludeResource", conflict.Message, StringComparison.Ordinal);

        var unmatched = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
            resolved,
            PowerShellCompilationMode.Hybrid,
            includeResource: new[] { "Missing/**" }));
        Assert.Contains("did not match", unmatched.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_DoubleStarDirectoryGlobMatchesZeroOrMoreNestedDirectories()
    {
        using var fixture = new ResourceFixture(module: true);
        fixture.Write("Vendor/root.pdb", "root");
        fixture.Write("Vendor/Nested/child.pdb", "child");
        fixture.Write("Vendor/keep.dat", "keep");

        var plan = new PowerShellCompilationAnalyzer().Analyze(
            fixture.ResolveModule(),
            PowerShellCompilationMode.Hybrid,
            resourceMode: PowerShellCompilationResourceMode.CompleteModule,
            excludeResource: new[] { "Vendor/**/*.pdb" });

        Assert.Equal(2, plan.Dependencies.Count(dependency =>
            dependency.RelativePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
            dependency.Selection == PowerShellCompilationDependencySelection.Excluded));
        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Vendor/keep.dat" &&
            dependency.Selection == PowerShellCompilationDependencySelection.PolicyInclude);
    }

    [Fact]
    public void Analyze_RejectsExclusionOfCompilationInput()
    {
        using var fixture = new ResourceFixture(module: true);

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
            fixture.ResolveModule(),
            PowerShellCompilationMode.Hybrid,
            excludeResource: new[] { "Demo.psm1" }));

        Assert.Contains("only to optional payload", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RejectsEscapingResourcePattern()
    {
        using var fixture = new ResourceFixture(module: true);
        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationAnalyzer().Analyze(
            fixture.ResolveModule(),
            PowerShellCompilationMode.Hybrid,
            includeResource: new[] { "../secret.txt" }));

        Assert.Contains("contained path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\rooted.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("C:\\Windows\\file.txt")]
    public void Analyze_RejectsRootedResourcePatternsBeforeNormalization(string pattern)
    {
        using var fixture = new ResourceFixture(module: true);
        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationAnalyzer().Analyze(
            fixture.ResolveModule(),
            PowerShellCompilationMode.Hybrid,
            includeResource: new[] { pattern }));

        Assert.Contains("contained path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RejectsExplicitResourceThatOverlapsOutputDirectory()
    {
        using var fixture = new ResourceFixture(module: false);
        fixture.Write("out/stale.json", "{}");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationInputResolver().Resolve(fixture.SourcePath),
            PowerShellCompilationMode.Package,
            includeResource: new[] { "out/stale.json" },
            outputDirectory: Path.Combine(fixture.RootPath, "out")));

        Assert.Contains("overlaps the durable output directory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RejectsLinkedOptionalPayload()
    {
        using var fixture = new ResourceFixture(module: true);
        var externalRoot = Path.Combine(Path.GetTempPath(), "PowerForge Resource Policy External", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalRoot);
        File.WriteAllText(Path.Combine(externalRoot, "secret.txt"), "secret");
        try
        {
            var link = Path.Combine(fixture.RootPath, "Vendor");
            try
            {
                Directory.CreateSymbolicLink(link, externalRoot);
            }
            catch (Exception linkException) when (linkException is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
                fixture.ResolveModule(),
                PowerShellCompilationMode.Hybrid,
                includeResource: new[] { "Vendor/**" }));

            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(externalRoot)) Directory.Delete(externalRoot, recursive: true);
        }
    }

    [Fact]
    public void Analyze_RejectsCaseCollidingOptionalPayloadOnCaseSensitiveFileSystems()
    {
        if (OperatingSystem.IsWindows()) return;
        using var fixture = new ResourceFixture(module: true);
        fixture.Write("Data/value.json", "one");
        fixture.Write("data/value.json", "two");

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationAnalyzer().Analyze(
            fixture.ResolveModule(),
            PowerShellCompilationMode.Hybrid,
            resourceMode: PowerShellCompilationResourceMode.CompleteModule));

        Assert.Contains("case-colliding", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_InfersOnlyHighConfidenceLiteralResourceAndReportsDynamicSiblingAsUnclassified()
    {
        using var fixture = new ResourceFixture(
            module: true,
            moduleSource: "function Get-Report { Get-Content -LiteralPath \"$PSScriptRoot/Templates/report.html\"; $name = 'dynamic.html'; Get-Content -LiteralPath (Join-Path $PSScriptRoot $name) }");
        fixture.Write("Templates/report.html", "literal");
        fixture.Write("Templates/dynamic.html", "dynamic");

        var plan = new PowerShellCompilationAnalyzer().Analyze(fixture.ResolveModule(), PowerShellCompilationMode.Hybrid);

        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Templates/report.html" &&
            dependency.Selection == PowerShellCompilationDependencySelection.Inferred);
        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Templates/dynamic.html" &&
            dependency.Selection == PowerShellCompilationDependencySelection.Unclassified);
        Assert.Equal(1, plan.ResourceSummary.InferredFiles);
        Assert.Equal(1, plan.ResourceSummary.UnclassifiedFiles);
    }

    [Fact]
    public void Analyze_MissingInferredLiteralFailsClosed()
    {
        using var fixture = new ResourceFixture(
            module: false,
            moduleSource: "Get-Content -LiteralPath \"$PSScriptRoot/Templates/missing.txt\"");

        var plan = new PowerShellCompilationAnalyzer().Analyze(
            new PowerShellCompilationInputResolver().Resolve(fixture.SourcePath),
            PowerShellCompilationMode.Package);

        Assert.False(plan.CanProceed);
        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Templates/missing.txt" &&
            dependency.Selection == PowerShellCompilationDependencySelection.Inferred &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.Missing);
    }

    [Fact]
    public void Analyze_ResourceModeNoneDisablesLiteralInference()
    {
        using var fixture = new ResourceFixture(
            module: true,
            moduleSource: "function Get-Report { Get-Content -LiteralPath \"$PSScriptRoot/Templates/report.txt\" }");
        fixture.Write("Templates/report.txt", "optional");

        var plan = new PowerShellCompilationAnalyzer().Analyze(
            fixture.ResolveModule(),
            PowerShellCompilationMode.Hybrid,
            resourceMode: PowerShellCompilationResourceMode.None);

        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Templates/report.txt" &&
            dependency.Selection == PowerShellCompilationDependencySelection.Unclassified &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.NotIncluded);
        Assert.Equal(0, plan.ResourceSummary.InferredFiles);
    }

    [Fact]
    public void Build_PackagedExecutableEmbedsAndExtractsInferredResource()
    {
        using var fixture = new ResourceFixture(
            module: false,
            moduleSource: "Get-Content -LiteralPath \"$PSScriptRoot/Templates/report.txt\"");
        fixture.Write("Templates/report.txt", "resource-proof");
        var output = Path.Combine(fixture.RootPath, "out");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.SourcePath,
            output,
            "Resource.Proof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(File.Exists(Path.Combine(output, "Templates", "report.txt")));
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.ArtifactPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("resource-proof", stdout, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        Assert.Contains(result.Manifest!.Dependencies, dependency =>
            dependency.RelativePath == "Templates/report.txt" &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted);
    }

    [Fact]
    public void Build_PackagedExecutableResolvesDynamicExplicitResourceFromExtractedRoot()
    {
        using var fixture = new ResourceFixture(
            module: false,
            moduleSource: "$name = 'dynamic.txt'; Get-Content -LiteralPath (Join-Path $PSScriptRoot $name)");
        fixture.Write("dynamic.txt", "dynamic-resource-proof");
        var output = Path.Combine(fixture.RootPath, "out");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.SourcePath,
            output,
            "Dynamic.Resource.Proof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true)
        {
            IncludeResource = new[] { "dynamic.txt" }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(File.Exists(Path.Combine(output, "dynamic.txt")));
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.ArtifactPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("dynamic-resource-proof", stdout, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        Assert.Contains(result.Manifest!.Dependencies, dependency =>
            dependency.RelativePath == "dynamic.txt" &&
            dependency.Selection == PowerShellCompilationDependencySelection.ExplicitInclude &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted);
    }

    [Fact]
    public void Build_PackagedExecutableResolvesNestedPSScriptRootAgainstDeclaringFile()
    {
        using var fixture = new ResourceFixture(
            module: false,
            moduleSource: ". \"$PSScriptRoot/Public/Get-Report.ps1\"; Get-Report");
        fixture.Write("Public/Get-Report.ps1", "function Get-Report { Get-Content -LiteralPath \"$PSScriptRoot/Templates/report.txt\" }");
        fixture.Write("Public/Templates/report.txt", "nested-resource-proof");
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            fixture.SourcePath,
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            Path.Combine(fixture.RootPath, "out"),
            "Nested.Resource.Proof",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.ArtifactPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.Contains("nested-resource-proof", stdout, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        Assert.Contains(result.Manifest!.Dependencies, dependency =>
            dependency.RelativePath == "Public/Templates/report.txt" &&
            dependency.Selection == PowerShellCompilationDependencySelection.Inferred);
    }

    [Fact]
    public void Analyze_RequiredButUnsupportedScriptHookIsNotCountedAsIncluded()
    {
        using var fixture = new ResourceFixture(module: true);
        fixture.Write("Initialize.ps1", "'initialize'");
        File.WriteAllText(fixture.ManifestPath!, "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'; ScriptsToProcess = @('Initialize.ps1') }");
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            fixture.RootPath,
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);

        var plan = new PowerShellCompilationAnalyzer().Analyze(resolved, PowerShellCompilationMode.Strict);

        Assert.Contains(plan.Dependencies, dependency =>
            dependency.RelativePath == "Initialize.ps1" &&
            dependency.Selection == PowerShellCompilationDependencySelection.Required &&
            dependency.Disposition == PowerShellCompilationDependencyDisposition.NotIncluded);
        Assert.Equal(0, plan.ResourceSummary.IncludedFiles);
        Assert.Equal(1, plan.ResourceSummary.RequiredFiles);
    }

    [Fact]
    public void Build_CompleteModuleIncludesArbitraryFoldersButHonorsOptionalExclusion()
    {
        using var fixture = new ResourceFixture(module: true);
        fixture.Write("Web/app.js", "app");
        fixture.Write("Data/value.json", "{}");
        fixture.Write("Docs/readme.md", "docs");
        var output = Path.Combine(fixture.RootPath, "out");
        var resolved = fixture.ResolveModule();
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            output,
            "Complete.Resources",
            resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = resolved.ModuleManifestPath,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            ResourceMode = PowerShellCompilationResourceMode.CompleteModule,
            ExcludeResource = new[] { "Docs/**" }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var root = Path.GetDirectoryName(result.ArtifactPath!)!;
        Assert.True(File.Exists(Path.Combine(root, "Web", "app.js")));
        Assert.True(File.Exists(Path.Combine(root, "Data", "value.json")));
        Assert.False(File.Exists(Path.Combine(root, "Docs", "readme.md")));
    }

    [Theory]
    [InlineData(".powerforge-compilation.json")]
    [InlineData(".generated")]
    public void Build_RejectsResourceThatOccupiesGeneratedNamespace(string reservedSuffix)
    {
        using var fixture = new ResourceFixture(module: true);
        const string artifactName = "Collision.Resources";
        var reservedResource = fixture.Write(artifactName + reservedSuffix, "source-owned");
        var output = Path.Combine(fixture.RootPath, "out");
        var resolved = fixture.ResolveModule();
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            output,
            artifactName,
            resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = resolved.ModuleManifestPath,
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            RuntimeSourcePaths = resolved.SourceFiles,
            IncludeResource = new[] { Path.GetFileName(reservedResource) }
        });

        Assert.False(result.Succeeded);
        Assert.Contains("generated artifact", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("source-owned", File.ReadAllText(reservedResource));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    private sealed class ResourceFixture : IDisposable
    {
        internal ResourceFixture(bool module, string? fileList = null, string? moduleSource = null)
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForge Resource Policy Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            SourcePath = Write(module ? "Demo.psm1" : "Tool.ps1", moduleSource ?? (module ? "function Get-Value { 1 }" : "'ok'"));
            if (module)
            {
                var fileListText = fileList is null ? string.Empty : $"; FileList = @('{fileList}')";
                ManifestPath = Write("Demo.psd1", $"@{{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'{fileListText} }}");
            }
        }

        internal string RootPath { get; }
        internal string SourcePath { get; }
        internal string? ManifestPath { get; }

        internal string Write(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        internal PowerShellCompilationResolvedInput ResolveModule()
            => new PowerShellCompilationInputResolver().Resolve(
                RootPath,
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid);

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
