using PowerForge;
using PowerForge.Compiler.App.Engine;
using PowerForge.Compiler.App.ViewModels;

namespace PowerForge.Compiler.App.Tests;

public sealed class BuildSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-compiler-settings-" + Guid.NewGuid().ToString("N"));

    public BuildSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SavedSettingsOpenWithTheSameScriptFilesAndTarget()
    {
        var engine = new CompilerAppEngine();
        var script = WriteScript();
        var target = engine.GetTargets(PowerShellCompilationMode.Hybrid, PowerShellCompilationArtifactKind.Executable)[0];
        var files = new FileRules(PowerShellCompilationResourceMode.Declared, new[] { "assets/*.json" }, new[] { "tests/**" });
        var settingsPath = Path.Combine(_root, CompilerAppEngine.DefaultSettingsFileName);

        var saved = engine.SaveSettings(new SaveSettingsRequest(settingsPath, script, files, PowerShellCompilationMode.Hybrid, target, Path.Combine(_root, "out")));
        var opened = engine.OpenSettings(settingsPath);

        Assert.Null(saved.Problem);
        var settings = Assert.IsType<BuildSettings>(opened.Settings);
        Assert.Equal(script, settings.InputPath, ignoreCase: true);
        Assert.Equal(files.Mode, settings.Files.Mode);
        Assert.Equal(files.Include, settings.Files.Include);
        Assert.Equal(files.Exclude, settings.Files.Exclude);
        var savedTarget = Assert.Single(settings.Targets);
        Assert.Equal(PowerShellCompilationMode.Hybrid, savedTarget.Mode);
        Assert.Equal(target.RuntimeIdentifier, savedTarget.RuntimeIdentifier);
        Assert.Equal(target.Deployment, savedTarget.Deployment);
        Assert.Equal(Path.Combine(_root, "out"), savedTarget.OutputDirectory, ignoreCase: true);
    }

    [Fact]
    public void SavingAnotherModeKeepsTheTargetsAlreadyInTheFile()
    {
        var engine = new CompilerAppEngine();
        var script = WriteScript();
        var settingsPath = Path.Combine(_root, CompilerAppEngine.DefaultSettingsFileName);
        var hybrid = engine.GetTargets(PowerShellCompilationMode.Hybrid, PowerShellCompilationArtifactKind.Executable)[0];
        var package = engine.GetTargets(PowerShellCompilationMode.Package, PowerShellCompilationArtifactKind.Executable)[0];

        engine.SaveSettings(new SaveSettingsRequest(settingsPath, script, FileRules.Default, PowerShellCompilationMode.Hybrid, hybrid, null));
        var second = engine.SaveSettings(new SaveSettingsRequest(settingsPath, script, FileRules.Default, PowerShellCompilationMode.Package, package, null));

        Assert.Null(second.Problem);
        var settings = Assert.IsType<BuildSettings>(second.Settings);
        Assert.Equal(
            new[] { PowerShellCompilationMode.Hybrid, PowerShellCompilationMode.Package }.OrderBy(static mode => mode),
            settings.Targets.Select(static saved => saved.Mode).OrderBy(static mode => mode));
    }

    [Fact]
    public async Task OpeningADirectoryTargetPreservesItsExactReviewContract()
    {
        var script = Path.Combine(_root, "DirectoryTarget.ps1");
        File.WriteAllText(script, "return 21");
        var settings = Path.Combine(_root, CompilerAppEngine.DefaultSettingsFileName);
        var contract = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, "net10.0", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            selfContained: false, singleFile: false, PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var projects = new PowerShellCompilationProjectManifestService();
        var singleFile = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, "net10.0", contract.RuntimeIdentifier,
            selfContained: false, singleFile: true, PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        projects.Save(settings, projects.Create(settings, script, "DirectoryTarget", singleFile));

        var engine = new CompilerAppEngine();
        var target = engine.GetTargets(PowerShellCompilationMode.Strict, PowerShellCompilationArtifactKind.Executable)
            .First(candidate => candidate.RuntimeIdentifier == contract.RuntimeIdentifier);
        var saved = engine.SaveSettings(new SaveSettingsRequest(settings, script, FileRules.Default, target.Mode,
            target with { ExactContract = contract }, null));
        Assert.Null(saved.Problem);
        Assert.Single(Assert.IsType<BuildSettings>(saved.Settings).Targets);

        var model = new MainWindowViewModel(engine);
        await model.OpenSettingsAsync(settings);
        await model.SetInputAsync(script);
        var selected = Assert.IsType<CompileTarget>(model.SelectedTarget);
        Assert.Equal(PowerShellCompilationMode.Strict, selected.Mode);
        var review = await engine.ReviewAsync(new CompileRequest(script, selected.Mode, selected, model.OutputDirectory),
            CancellationToken.None);

        Assert.NotNull(review.Review?.Target);
        Assert.False(review.Review.Target.SingleFile);
        Assert.Equal(contract.ContractSha256, review.Review.Target.ContractSha256);
    }

    [Fact]
    public async Task AProjectCanReviewAndBuildItsScriptModuleAsADotNetLibrary()
    {
        var script = Path.Combine(_root, "Library.psm1");
        File.WriteAllText(script, "function Get-ReportCount { return 21 }");
        var settings = Path.Combine(_root, CompilerAppEngine.DefaultSettingsFileName);
        var contract = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, "net10.0", null, selfContained: false, singleFile: false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var projects = new PowerShellCompilationProjectManifestService();
        projects.Save(settings, projects.Create(settings, script, "Library", contract));

        var engine = new CompilerAppEngine();
        var model = new MainWindowViewModel(engine);
        await model.OpenSettingsAsync(settings);
        var selected = Assert.IsType<CompileTarget>(model.SelectedTarget);
        Assert.Equal(PowerShellCompilationArtifactKind.Library, selected.ExactContract?.ArtifactKind);
        var request = new CompileRequest(script, selected.Mode, selected, model.OutputDirectory);
        var review = await engine.ReviewAsync(request, CancellationToken.None);
        Assert.NotNull(review.Review);

        var built = await engine.CompileAsync(request, review.Review, progress: null, CancellationToken.None);
        Assert.True(built.Succeeded, string.Join(Environment.NewLine, built.Diagnostics.Select(static item => item.Message)));
        Assert.EndsWith(".dll", built.ArtifactPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(built.ArtifactPath!));
    }

    [Theory]
    [InlineData("loose source set")]
    [InlineData("explicit entrypoint")]
    [InlineData("diagnostics policy")]
    public void OpeningSettingsRefusesProjectOptionsTheAppCannotApply(string option)
    {
        var script = WriteScript();
        var helper = Path.Combine(_root, "Helper.ps1");
        File.WriteAllText(helper, "function Get-Helper { return 42 }");
        var settingsPath = Path.Combine(_root, CompilerAppEngine.DefaultSettingsFileName);
        var engine = new CompilerAppEngine();
        var target = engine.GetTargets(PowerShellCompilationMode.Strict, PowerShellCompilationArtifactKind.Executable)[0];
        var service = new PowerShellCompilationProjectManifestService();
        var manifest = service.Create(settingsPath, script, "Report", target.ExactContract!);
        switch (option)
        {
            case "loose source set":
                manifest.Sources = new[] { "Report.ps1", "Helper.ps1" };
                manifest.EntryPoint = "Report.ps1";
                break;
            case "explicit entrypoint":
                manifest.EntryPoint = "Helper.ps1";
                break;
            case "diagnostics policy":
                manifest.Diagnostics.RecommendedFailureBundleRetentionDays = 30;
                break;
        }
        service.Save(settingsPath, manifest);

        var opened = engine.OpenSettings(settingsPath);

        Assert.Null(opened.Settings);
        Assert.Contains(option, Assert.IsType<CompileDiagnostic>(opened.Problem).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningSettingsRefusesARequiredModuleAcceptanceScript()
    {
        var module = Path.Combine(_root, "Example.psm1");
        var acceptance = Path.Combine(_root, "Test-Example.ps1");
        File.WriteAllText(module, "function Get-Example { return 42 }");
        File.WriteAllText(acceptance, "param($ArtifactPath)\nImport-Module $ArtifactPath");
        var settingsPath = Path.Combine(_root, CompilerAppEngine.DefaultSettingsFileName);
        var engine = new CompilerAppEngine();
        var target = engine.GetTargets(PowerShellCompilationMode.Hybrid, PowerShellCompilationArtifactKind.BinaryModule)[0];
        var service = new PowerShellCompilationProjectManifestService();
        var manifest = service.Create(settingsPath, module, "Example", target.ExactContract!);
        manifest.Artifacts[0].ModuleTest = new PowerShellCompilationProjectModuleTest
        {
            Script = Path.GetFileName(acceptance),
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(acceptance)))
        };
        service.Save(settingsPath, manifest);

        var opened = engine.OpenSettings(settingsPath);

        Assert.Null(opened.Settings);
        Assert.Contains("required module acceptance script", Assert.IsType<CompileDiagnostic>(opened.Problem).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABrokenSettingsFileIsReportedInsteadOfThrown()
    {
        var settingsPath = Path.Combine(_root, CompilerAppEngine.DefaultSettingsFileName);
        File.WriteAllText(settingsPath, "{ not json");

        var opened = new CompilerAppEngine().OpenSettings(settingsPath);

        Assert.Null(opened.Settings);
        var problem = Assert.IsType<CompileDiagnostic>(opened.Problem);
        Assert.Equal(CompileSeverity.Error, problem.Severity);
        Assert.StartsWith("These settings can't be opened", problem.Message, StringComparison.Ordinal);
    }

    private string WriteScript()
    {
        var path = Path.Combine(_root, "Report.ps1");
        File.WriteAllText(path, "function Get-Total { param([int] $A) return $A * 2 }\nGet-Total -A 4\n");
        return path;
    }
}
