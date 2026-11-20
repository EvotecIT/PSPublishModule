using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliPowerShellCompilationTests
{
    [Fact]
    public async Task ReviewedBuild_UsesCheckedTargetAndRejectsChangedSource()
    {
        var repository = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge reviewed build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Example.ps1");
            var review = Path.Combine(root, "review.json");
            var output = Path.Combine(root, "output");
            File.WriteAllText(source, "param([int] $Count = 3); return [int]($Count + 4)");

            var check = await RunCliAsync(repository,
                new[] { "powershell", "check", source, "--mode", "Strict", "--out", output,
                    "--write-review", review, "--output", "json" });
            Assert.True(check.ExitCode == 0, check.StdOut + check.StdErr);
            Assert.False(Directory.Exists(output));
            using var checkedResult = JsonDocument.Parse(check.StdOut);
            var result = checkedResult.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("canProceed").GetBoolean());
            Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());

            var wrongTarget = await RunCliAsync(repository,
                new[] { "powershell", "build", source, "--mode", "Package", "--out", output,
                    "--reviewed-check", review, "--output", "json" });
            Assert.Equal(1, wrongTarget.ExitCode);
            Assert.Contains("differs from the reviewed target", wrongTarget.StdOut, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));

            var buildArguments = new[] { "powershell", "build", source, "--mode", "Strict", "--out", output,
                "--reviewed-check", review, "--output", "json" };
            var build = await RunCliAsync(repository, buildArguments);
            Assert.True(build.ExitCode == 0, build.StdOut + build.StdErr);
            using var built = JsonDocument.Parse(build.StdOut);
            var artifact = built.RootElement.GetProperty("result").GetProperty("artifactPath").GetString()!;
            var run = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                artifact, root, new[] { "--Count", "10" }, TimeSpan.FromSeconds(30)));
            Assert.True(run.Succeeded, run.StdOut + run.StdErr);
            Assert.Equal("14", run.StdOut.Trim());

            var artifactBytes = File.ReadAllBytes(artifact);
            File.WriteAllText(source, "param([int] $Count = 3); return [int]($Count + 5)");
            var changed = await RunCliAsync(repository, buildArguments);
            Assert.Equal(1, changed.ExitCode);
            Assert.Contains("dependency lock drifted", changed.StdOut, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(artifactBytes, File.ReadAllBytes(artifact));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReviewedBuild_RejectsStagedResourceDriftAndPreservesPublishedModule()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge reviewed resource", Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Example");
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(moduleRoot);
        try
        {
            var source = Path.Combine(moduleRoot, "Example.psm1");
            var resource = Path.Combine(moduleRoot, "label.txt");
            File.WriteAllText(source, "function Get-Example { return 42 }");
            var manifest = Path.Combine(moduleRoot, "Example.psd1");
            File.WriteAllText(manifest, "@{ RootModule = 'Example.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-Example') }");
            File.WriteAllText(resource, "reviewed content");
            var host = new PowerShellCompilationHostService();
            var request = new PowerShellCompilationCheckRequest(manifest)
            {
                OutputDirectory = output, IncludeResource = new[] { "label.txt" }
            };
            var check = host.Check(request);
            Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
            Assert.Contains(check.Dependencies, item => item.RelativePath == "label.txt" &&
                item.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent);
            var spec = new PowerShellCompilationBuildSpec(source, output, "Example",
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid)
            {
                ModuleManifestPath = manifest, IncludeResource = request.IncludeResource
            };
            var built = host.Compile(spec, check.ProposedReview!);
            Assert.True(built.Succeeded, built.Error + built.BuildOutput);
            var deployed = Path.Combine(Path.GetDirectoryName(built.ArtifactPath)!, "label.txt");
            Assert.Equal("reviewed content", File.ReadAllText(deployed));
            var artifactBytes = File.ReadAllBytes(built.ArtifactPath!);

            var changed = host.Compile(spec, check.ProposedReview!, progress: new AtPhase(PowerShellCompilationHostPhase.Publishing,
                () => File.WriteAllText(resource, "changed during publication")));

            Assert.False(changed.Succeeded);
            Assert.Equal(PowerShellCompilationFailureStage.Publication, changed.Failure!.Stage);
            Assert.Contains("Staged PowerShell compilation payload 'label.txt' differs from the dependency lock",
                changed.Error, StringComparison.Ordinal);
            Assert.Equal("reviewed content", File.ReadAllText(deployed));
            Assert.Equal(artifactBytes, File.ReadAllBytes(built.ArtifactPath!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("label.txt", false)]
    [InlineData("label.ps1", true)]
    public void ReviewedBuild_RejectsChangedEmbeddedInputBeforeCompilation(string fileName, bool scriptDependency)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge reviewed package", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Example.ps1");
            var resource = Path.Combine(root, fileName);
            var output = Path.Combine(root, "output");
            File.WriteAllText(source, scriptDependency
                ? ". \"$PSScriptRoot/label.ps1\""
                : "Get-Content -LiteralPath \"$PSScriptRoot/label.txt\"");
            File.WriteAllText(resource, scriptDependency ? "'reviewed content'" : "reviewed content");
            var host = new PowerShellCompilationHostService();
            var request = new PowerShellCompilationCheckRequest(source)
            {
                Mode = PowerShellCompilationMode.Package,
                OutputDirectory = output,
                IncludeResource = scriptDependency ? Array.Empty<string>() : new[] { fileName }
            };
            var check = host.Check(request);
            Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
            Assert.Contains(check.Dependencies, item => item.RelativePath == fileName &&
                item.Disposition == PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted);
            var spec = new PowerShellCompilationBuildSpec(source, output, "Example",
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package)
            {
                IncludeResource = request.IncludeResource,
                CompilationSourcePaths = scriptDependency ? new[] { resource } : Array.Empty<string>()
            };
            var changed = host.Compile(spec, check.ProposedReview!, progress: new AtPhase(
                PowerShellCompilationHostPhase.Shaping, () => File.WriteAllText(resource, "unreviewed content")));

            Assert.False(changed.Succeeded);
            Assert.Contains($"Staged PowerShell compilation payload '{fileName}' differs from the dependency lock",
                changed.Error, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Missing.ps1", PowerShellCompilationMode.Hybrid, "Missing.ps1")]
    [InlineData("../Outside.ps1", PowerShellCompilationMode.Hybrid, "dot-source expression")]
    [InlineData("Present.ps1", PowerShellCompilationMode.Strict, "dot-source expression")]
    public void CheckRejectsUnqualifiedModulePathAlternatives(string fallback, PowerShellCompilationMode mode, string diagnostic)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge module paths", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Example.psm1");
            File.WriteAllText(Path.Combine(root, "Present.ps1"), "$script:answer = 21");
            File.WriteAllText(source, $$"""
                $path = switch ($PSVersionTable.PSVersion.Major) {
                    { $_ -ge 7 } { 'Present.ps1' }
                    default { '{{fallback}}' }
                }
                . (Join-Path $PSScriptRoot $path)
                """);
            var check = new PowerShellCompilationHostService().Check(new PowerShellCompilationCheckRequest(source)
            {
                Mode = mode,
                OutputDirectory = Path.Combine(root, "output")
            });

            Assert.False(check.CanProceed);
            Assert.Null(check.ProposedReview);
            Assert.Contains(check.Diagnostics, item => item.Severity == PowerShellCompilationHostSeverity.Error &&
                item.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ModuleSwitchAlternativesRequireRuntimeOwnershipAndRemainLockedWhenInactive()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge module review", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Example.psm1");
            var active = Path.Combine(root, "Active.ps1");
            var inactive = Path.Combine(root, "Inactive.ps1");
            var output = Path.Combine(root, "output");
            File.WriteAllText(active, "$script:value = 21");
            File.WriteAllText(inactive, "$script:value = 13");
            File.WriteAllText(source, """
                $path = switch ('active') { 'active' { 'Active.ps1' } default { 'Inactive.ps1' } }
                . (Join-Path $PSScriptRoot $path)
                function Get-Example { $script:value }
                Export-ModuleMember -Function Get-Example
                """);
            var host = new PowerShellCompilationHostService();
            var check = host.Check(new PowerShellCompilationCheckRequest(source) { OutputDirectory = output });
            Assert.True(check.CanProceed, string.Join(Environment.NewLine, check.Diagnostics.Select(item => item.Message)));
            foreach (var file in new[] { "Active.ps1", "Inactive.ps1" })
                Assert.Contains(check.Dependencies, item => item.RelativePath == file &&
                    item.Disposition == PowerShellCompilationDependencyDisposition.PreservedScript);

            var spec = new PowerShellCompilationBuildSpec(source, output, "Example",
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                allowUnreviewedDependencyResolution: true);
            spec.ExcludeResource = new[] { "Inactive.ps1" };
            var excluded = new PowerShellCompilationArtifactBuilder().Build(spec);
            Assert.False(excluded.Succeeded);
            Assert.Contains("Inactive.ps1' cannot be excluded", excluded.Error, StringComparison.OrdinalIgnoreCase);

            spec.ExcludeResource = Array.Empty<string>();
            spec.CompilationSourcePaths = new[] { inactive };
            var compiled = new PowerShellCompilationArtifactBuilder().Build(spec);
            Assert.False(compiled.Succeeded);
            Assert.Contains("cannot also be a compilation source", compiled.Error, StringComparison.OrdinalIgnoreCase);

            spec.CompilationSourcePaths = Array.Empty<string>();
            spec.AllowUnreviewedDependencyResolution = false;
            File.WriteAllText(inactive, "$script:value = 15");
            var changed = host.Compile(spec, check.ProposedReview!);
            Assert.False(changed.Succeeded);
            Assert.Contains("dependency lock drifted", changed.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AtPhase(PowerShellCompilationHostPhase phase, Action callback) : IProgress<PowerShellCompilationProgress>
    {
        public void Report(PowerShellCompilationProgress value)
        {
            if (value.Phase == phase)
                callback();
        }
    }

    [Fact]
    public async Task Check_RejectsWritingReviewIntoModuleInventory()
    {
        var repository = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge review location", Guid.NewGuid().ToString("N"));
        var moduleRoot = Path.Combine(root, "Example");
        Directory.CreateDirectory(moduleRoot);
        try
        {
            var source = Path.Combine(moduleRoot, "Example.psm1");
            var review = Path.Combine(moduleRoot, "review.json");
            File.WriteAllText(source, "function Get-Example { return 42 }");
            var check = await RunCliAsync(repository,
                new[] { "powershell", "check", source, "--write-review", review, "--output", "json" });
            Assert.Equal(1, check.ExitCode);
            Assert.Contains("Save the review outside the module source directory", check.StdOut, StringComparison.Ordinal);
            Assert.False(File.Exists(review));

            var outsideReview = Path.Combine(root, "review.json");
            var outside = await RunCliAsync(repository,
                new[] { "powershell", "check", source, "--write-review", outsideReview, "--output", "json" });
            Assert.True(outside.ExitCode == 0, outside.StdOut + outside.StdErr);
            Assert.True(File.Exists(outsideReview));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Doctor_RejectsAnUnknownOracleAndAnIncompatibleRuntimeContract()
    {
        var host = new PowerShellCompilationHostService();
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, "net10.0", null, false, true,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var profile = target.SemanticProfileId;
        target.SemanticProfileId = "Unknown/Profile";
        target.ContractSha256 = string.Empty;
        var unknown = Assert.Single(host.Doctor(target).Diagnostics);
        Assert.Contains("Unknown PowerForge semantic-oracle profile", unknown.Message, StringComparison.Ordinal);

        target.SemanticProfileId = profile;
        target.RuntimeRequirement = PowerShellCompilationRuntimeRequirement.PowerShell;
        var invalid = Assert.Single(host.Doctor(target).Diagnostics);
        Assert.Contains("conflicts with the resolved semantic, runtime, or deployment build contract", invalid.Message,
            StringComparison.Ordinal);

        var unsupported = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Hybrid, "net10.0", "win-x64", true, true,
            PowerShellCompilationExecutableOptimization.NativeAot, explicitContract: true);
        var invalidOptimization = Assert.Single(host.Doctor(unsupported).Diagnostics);
        Assert.Contains("optimization is supported only for Strict", invalidOptimization.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Package", "-Version 99.0", "PowerShell 99.0", false)]
    [InlineData("Hybrid", "-Version 99.0", "PowerShell 99.0", false)]
    [InlineData("Package", "-Version 6.9", "PowerShell 6.9", false)]
    [InlineData("Package", "\u2013Modules ExampleNotInstalled", "-Modules", false)]
    [InlineData("Package", "-PSEdition Desktop", "'Desktop'", false)]
    [InlineData("Hybrid", "-Version 5.1", "Helper.ps1", true)]
    public async Task Check_ReportsPackageDirectiveDiagnosticWithoutWritingReview(
        string mode, string requirement, string expectedFinding, bool onDependency)
    {
        var repository = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "PowerForge blocked check", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Directive.ps1");
            var review = Path.Combine(root, "review.json");
            if (onDependency)
                File.WriteAllText(source, ". \"$PSScriptRoot/Helper.ps1\"");
            File.WriteAllText(onDependency ? Path.Combine(root, "Helper.ps1") : source,
                $"#requires {requirement}\n'example'");

            var check = await RunCliAsync(repository,
                new[] { "powershell", "check", source, "--mode", mode, "--write-review", review, "--output", "json" });

            Assert.Equal(1, check.ExitCode);
            Assert.False(File.Exists(review));
            using var json = JsonDocument.Parse(check.StdOut);
            var diagnostics = json.RootElement.GetProperty("result").GetProperty("diagnostics");
            var finding = Assert.Single(diagnostics.EnumerateArray(),
                item => item.GetProperty("severity").GetString() == "Error" &&
                        item.GetProperty("message").GetString()!.Contains("#requires", StringComparison.Ordinal));
            Assert.Contains(expectedFinding, finding.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(1, finding.GetProperty("line").GetInt32());
            Assert.Equal("powershell.package.validation", finding.GetProperty("featureId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty("fix").GetString()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Check_CancelsAtTheShapingBoundaryWithoutProducingAReview()
    {
        var source = Path.GetTempFileName();
        var script = Path.ChangeExtension(source, ".ps1");
        File.Move(source, script);
        try
        {
            File.WriteAllText(script, "return 42");
            using var cancellation = new CancellationTokenSource();
            var progress = new CancelAtShaping(cancellation);
            var request = new PowerShellCompilationCheckRequest(script) { Mode = PowerShellCompilationMode.Strict };

            Assert.Throws<OperationCanceledException>(() => new PowerShellCompilationHostService().Check(
                request, cancellation.Token, progress));
            Assert.Equal(new[] { PowerShellCompilationHostPhase.Resolving, PowerShellCompilationHostPhase.Analyzing,
                PowerShellCompilationHostPhase.Shaping }, progress.Observed);
        }
        finally
        {
            File.Delete(script);
        }
    }

    private sealed class CancelAtShaping(CancellationTokenSource cancellation) : IProgress<PowerShellCompilationProgress>
    {
        public List<PowerShellCompilationHostPhase> Observed { get; } = new();

        public void Report(PowerShellCompilationProgress value)
        {
            Observed.Add(value.Phase);
            if (value.Phase == PowerShellCompilationHostPhase.Shaping)
                cancellation.Cancel();
        }
    }

    [Fact]
    public void CompilerDiagnosticCatalog_ProvidesActionForEveryCode()
    {
        foreach (var code in Enum.GetValues<PowerShellCompilationDiagnosticCode>())
            Assert.False(string.IsNullOrWhiteSpace(PowerShellCompilationHostDiagnosticCatalog.GetFix(code)));
    }
}
