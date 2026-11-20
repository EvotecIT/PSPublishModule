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
        var engine = new InterimCompileEngine();
        var script = WriteScript();
        var target = engine.GetTargets(PowerShellCompilationMode.Hybrid, PowerShellCompilationArtifactKind.Executable)[0];
        var files = new FileRules(PowerShellCompilationResourceMode.Declared, new[] { "assets/*.json" }, new[] { "tests/**" });
        var settingsPath = Path.Combine(_root, InterimCompileEngine.DefaultSettingsFileName);

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
        var engine = new InterimCompileEngine();
        var script = WriteScript();
        var settingsPath = Path.Combine(_root, InterimCompileEngine.DefaultSettingsFileName);
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
        var settings = Path.Combine(_root, InterimCompileEngine.DefaultSettingsFileName);
        var contract = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, "net10.0", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            selfContained: false, singleFile: false, PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var projects = new PowerShellCompilationProjectManifestService();
        projects.Save(settings, projects.Create(settings, script, "DirectoryTarget", contract));

        var engine = new InterimCompileEngine();
        var model = new MainWindowViewModel(engine);
        await model.OpenSettingsAsync(settings);
        var selected = Assert.IsType<CompileTarget>(model.SelectedTarget);
        Assert.Equal(PowerShellCompilationMode.Strict, selected.Mode);
        var review = await engine.ReviewAsync(new CompileRequest(script, selected.Mode, selected, model.OutputDirectory),
            CancellationToken.None);

        Assert.NotNull(review.Review?.Target);
        Assert.False(review.Review.Target.SingleFile);
        Assert.Equal(contract.ContractSha256, review.Review.Target.ContractSha256);
    }

    [Fact]
    public void ABrokenSettingsFileIsReportedInsteadOfThrown()
    {
        var settingsPath = Path.Combine(_root, InterimCompileEngine.DefaultSettingsFileName);
        File.WriteAllText(settingsPath, "{ not json");

        var opened = new InterimCompileEngine().OpenSettings(settingsPath);

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
