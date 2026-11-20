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

        var result = await new InterimCompileEngine().CheckAsync(missing, CancellationToken.None);

        Assert.Empty(result.Modes);
        Assert.Null(result.Recommended);
        var problem = Assert.Single(result.InputDiagnostics);
        Assert.Equal(CompileSeverity.Error, problem.Severity);
        Assert.Contains("does not exist", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckBlocksPackageModeOnRequiresAndRecommendsAModeThatBuilds()
    {
        var script = Write("Report.ps1", "#requires -Version 7.4\nGet-ChildItem -Path $PSScriptRoot | Select-Object Name\n");

        var result = await new InterimCompileEngine().CheckAsync(script, CancellationToken.None);

        Assert.Equal(PowerShellCompilationArtifactKind.Executable, result.Kind);
        var package = Assert.Single(result.Modes, static mode => mode.Mode == PowerShellCompilationMode.Package);
        Assert.Equal(CompileOutcome.Blocked, package.Outcome);
        var requires = Assert.Single(package.Diagnostics, static diagnostic => diagnostic.Severity == CompileSeverity.Error);
        Assert.Equal(1, requires.Line);
        Assert.Equal(script, requires.FilePath, ignoreCase: true);
        Assert.Contains("#requires", requires.Message, StringComparison.Ordinal);

        var hybrid = Assert.Single(result.Modes, static mode => mode.Mode == PowerShellCompilationMode.Hybrid);
        Assert.NotEqual(CompileOutcome.Blocked, hybrid.Outcome);
        Assert.Equal(PowerShellCompilationMode.Hybrid, result.Recommended);
    }

    [Fact]
    public async Task CheckCountsTypedFunctionsInHybridMode()
    {
        var script = Write("Add.ps1", "function Add-Numbers {\n    param([int] $A, [int] $B)\n    return $A + $B\n}\nAdd-Numbers -A 2 -B 3\n");

        var result = await new InterimCompileEngine().CheckAsync(script, CancellationToken.None);

        var hybrid = Assert.Single(result.Modes, static mode => mode.Mode == PowerShellCompilationMode.Hybrid);
        Assert.Equal(CompileOutcome.Mixed, hybrid.Outcome);
        Assert.Equal(1, hybrid.TypedUnits);
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
