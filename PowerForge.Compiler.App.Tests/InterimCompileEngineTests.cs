using PowerForge;
using PowerForge.Compiler.App.Engine;

namespace PowerForge.Compiler.App.Tests;

public sealed class InterimCompileEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-compiler-app-" + Guid.NewGuid().ToString("N"));

    public InterimCompileEngineTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task CheckReportsAMissingPathAsAProblemInsteadOfThrowing()
    {
        var missing = Path.Combine(_root, "missing.ps1");

        var result = await new InterimCompileEngine().CheckAsync(missing, FileRules.Default, CancellationToken.None);

        Assert.Empty(result.Modes);
        Assert.Null(result.Recommended);
        var problem = Assert.Single(result.InputDiagnostics);
        Assert.Equal(CompileSeverity.Error, problem.Severity);
        Assert.Contains("does not exist", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckReportsAnIncompatiblePowerShellRequirementInPackageMode()
    {
        var script = Write("Report.ps1", "#requires -Version 99.0\nGet-ChildItem -Path $PSScriptRoot | Select-Object Name\n");

        var result = await new InterimCompileEngine().CheckAsync(script, FileRules.Default, CancellationToken.None);

        Assert.Equal(PowerShellCompilationArtifactKind.Executable, result.Kind);
        var package = Assert.Single(result.Modes, static mode => mode.Mode == PowerShellCompilationMode.Package);
        Assert.Equal(CompileOutcome.Blocked, package.Outcome);
        var requires = Assert.Single(package.Diagnostics, static diagnostic => diagnostic.Severity == CompileSeverity.Error);
        Assert.Equal(1, requires.Line);
        Assert.Equal(script, requires.FilePath, ignoreCase: true);
        Assert.Contains("#requires", requires.Message, StringComparison.Ordinal);

    }

    [Fact]
    public async Task ReviewedSelectionBuildsAndRejectsChangedSource()
    {
        var script = Write("Reviewed.ps1", "return 21");
        var engine = new InterimCompileEngine();
        var target = new CompileTarget("local", PowerShellCompilationMode.Strict, "net10.0",
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            PowerShellCompilationDeploymentModel.FrameworkDependent, "Supported", true);
        var output = Path.Combine(_root, "output");
        var request = new CompileRequest(script, PowerShellCompilationMode.Strict, target, output);
        var check = await engine.ReviewAsync(request, CancellationToken.None);
        Assert.NotNull(check.Review);

        var first = await engine.CompileAsync(request, check.Review, progress: null, CancellationToken.None);
        Assert.True(first.Succeeded, string.Join(Environment.NewLine, first.Diagnostics.Select(static item => item.Message)));
        var delivered = File.ReadAllBytes(first.ArtifactPath!);
        File.WriteAllText(script, "return 22");

        var second = await engine.CompileAsync(request, check.Review, progress: null, CancellationToken.None);
        Assert.False(second.Succeeded);
        Assert.Contains(second.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("review", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Message.Contains("lock", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(delivered, File.ReadAllBytes(first.ArtifactPath!));
    }

    [Fact]
    public async Task CheckCountsTypedFunctionAndEntryUnitsInHybridMode()
    {
        var script = Write("Add.ps1", "function Add-Numbers {\n    param([int] $A, [int] $B)\n    return $A + $B\n}\nAdd-Numbers -A 2 -B 3\n");

        var result = await new InterimCompileEngine().CheckAsync(script, FileRules.Default, CancellationToken.None);

        var hybrid = Assert.Single(result.Modes, static mode => mode.Mode == PowerShellCompilationMode.Hybrid);
        Assert.Equal(CompileOutcome.Mixed, hybrid.Outcome);
        Assert.Equal(2, hybrid.TypedUnits);
        Assert.All(result.Modes, static mode => Assert.DoesNotContain(mode.Dependencies, static dependency => dependency.Name == "Add.ps1"));
    }

    [Fact]
    public void RecommendationPrefersNativeThenMixedThenPackaged()
    {
        static ModeCheck Mode(PowerShellCompilationMode mode, CompileOutcome outcome) =>
            new(mode, outcome, 0, 0, 0, Array.Empty<CompileDiagnostic>(), Array.Empty<CompileDependency>());

        Assert.Equal(PowerShellCompilationMode.Strict, InterimCompileEngine.Recommend(new[]
        {
            Mode(PowerShellCompilationMode.Package, CompileOutcome.Packaged),
            Mode(PowerShellCompilationMode.Hybrid, CompileOutcome.Mixed),
            Mode(PowerShellCompilationMode.Strict, CompileOutcome.Native)
        }));
        Assert.Equal(PowerShellCompilationMode.Hybrid, InterimCompileEngine.Recommend(new[]
        {
            Mode(PowerShellCompilationMode.Package, CompileOutcome.Packaged),
            Mode(PowerShellCompilationMode.Hybrid, CompileOutcome.Mixed),
            Mode(PowerShellCompilationMode.Strict, CompileOutcome.Blocked)
        }));
        Assert.Null(InterimCompileEngine.Recommend(new[]
        {
            Mode(PowerShellCompilationMode.Package, CompileOutcome.Blocked),
            Mode(PowerShellCompilationMode.Hybrid, CompileOutcome.Blocked),
            Mode(PowerShellCompilationMode.Strict, CompileOutcome.Blocked)
        }));
    }

    [Fact]
    public void AFailedBuildWithoutSourceLocationsStillReportsWhy()
    {
        var failed = new PowerShellCompilationBuildResult { Succeeded = false, Error = "dotnet publish failed." };

        var result = InterimCompileEngine.MapBuildResult(failed, PowerShellCompilationMode.Hybrid, TimeSpan.FromSeconds(1));

        Assert.False(result.Succeeded);
        Assert.Equal(CompileOutcome.Blocked, result.Summary);
        var problem = Assert.Single(result.Diagnostics);
        Assert.Equal(CompileSeverity.Error, problem.Severity);
        Assert.Equal("dotnet publish failed.", problem.Message);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
}
