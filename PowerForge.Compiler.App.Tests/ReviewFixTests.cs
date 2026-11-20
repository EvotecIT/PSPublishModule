using PowerForge;
using PowerForge.Compiler.App.Engine;
using PowerForge.Compiler.App.ViewModels;

namespace PowerForge.Compiler.App.Tests;

public sealed class ReviewFixTests
{
    [Fact]
    public void TargetsKeepTheirFrameworkAndNameEveryPlatform()
    {
        var engine = new InterimCompileEngine();

        var targets = new[] { PowerShellCompilationMode.Hybrid, PowerShellCompilationMode.Strict }
            .SelectMany(mode => new[] { PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationArtifactKind.Executable }
                .SelectMany(kind => engine.GetTargets(mode, kind)))
            .ToArray();

        Assert.All(targets, static target => Assert.False(string.IsNullOrWhiteSpace(target.TargetFramework)));
        Assert.All(targets, static target => Assert.DoesNotMatch("^,", target.DisplayName));
        Assert.Equal("Any platform, needs .NET installed",
            new CompileTarget("portable", PowerShellCompilationMode.Hybrid, "net10.0", string.Empty, PowerShellCompilationDeploymentModel.FrameworkDependent, "Supported", true).DisplayName);
        Assert.Equal("Windows PowerShell 5.1",
            new CompileTarget("legacy", PowerShellCompilationMode.Hybrid, "net472", string.Empty, PowerShellCompilationDeploymentModel.FrameworkDependent, "Supported", true).DisplayName);
    }

    [Theory]
    [InlineData(PowerShellCompilationDeploymentModel.FrameworkDependent, false)]
    [InlineData(PowerShellCompilationDeploymentModel.SelfContained, true)]
    [InlineData(PowerShellCompilationDeploymentModel.Trimmed, true)]
    [InlineData(PowerShellCompilationDeploymentModel.NativeAot, true)]
    public void TrimmedAndNativeAotBuildsAreSelfContained(PowerShellCompilationDeploymentModel deployment, bool expected) =>
        Assert.Equal(expected, InterimCompileEngine.RequiresSelfContained(deployment));

    [Fact]
    public async Task CancellingTheCheckOfANewInputLeavesNothingToCompile()
    {
        var engine = new StubEngine();
        var viewModel = new MainWindowViewModel(engine);
        await viewModel.SetInputAsync("A.ps1");
        Assert.True(viewModel.CanCompile);

        engine.CancelNextCheck = true;
        await viewModel.SetInputAsync("B.ps1");

        Assert.Equal("B.ps1", viewModel.InputPath);
        Assert.False(viewModel.HasCheck);
        Assert.Null(viewModel.SelectedMode);
        Assert.False(viewModel.CanCompile);
    }

    private sealed class StubEngine : ICompileEngine
    {
        public bool CancelNextCheck { get; set; }

        public Task<CheckResult> CheckAsync(string inputPath, FileRules files, CancellationToken cancellationToken)
        {
            if (CancelNextCheck) return Task.FromCanceled<CheckResult>(new CancellationToken(canceled: true));
            var mode = new ModeCheck(PowerShellCompilationMode.Hybrid, CompileOutcome.Mixed, 1, 1, 0,
                Array.Empty<CompileDiagnostic>(), Array.Empty<CompileDependency>());
            return Task.FromResult(new CheckResult(inputPath, PowerShellCompilationArtifactKind.Executable, new[] { inputPath },
                new[] { mode }, PowerShellCompilationMode.Hybrid, Array.Empty<CompileDiagnostic>(), TimeSpan.Zero));
        }

        public IReadOnlyList<CompileTarget> GetTargets(PowerShellCompilationMode mode, PowerShellCompilationArtifactKind kind) =>
            new[] { new CompileTarget("win", mode, "net10.0", "win-x64", PowerShellCompilationDeploymentModel.FrameworkDependent, "Supported", true) };

        public IReadOnlyList<SigningCertificate> GetSigningCertificates() => Array.Empty<SigningCertificate>();

        public Task<CompileResult> CompileAsync(CompileRequest request, IProgress<CompileProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public SettingsResult OpenSettings(string settingsPath) => throw new NotSupportedException();

        public SettingsResult SaveSettings(SaveSettingsRequest request) => throw new NotSupportedException();
    }
}
