using PowerForge;
using PowerForge.Compiler.App.Engine;
using PowerForge.Compiler.App.ViewModels;

namespace PowerForge.Compiler.App.Tests;

public sealed class SettingsAndCheckOrderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-compiler-order-" + Guid.NewGuid().ToString("N"));

    public SettingsAndCheckOrderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SavingAnotherScriptIntoExistingSettingsIsRefusedAndLeavesTheFileUnchanged()
    {
        var engine = new InterimCompileEngine();
        var report = WriteScript("Report.ps1");
        var other = WriteScript("Other.ps1");
        var settingsPath = Path.Combine(_root, InterimCompileEngine.DefaultSettingsFileName);
        engine.SaveSettings(Request(engine, settingsPath, report, PowerShellCompilationMode.Hybrid, null));
        var before = File.ReadAllText(settingsPath);

        var result = engine.SaveSettings(Request(engine, settingsPath, other, PowerShellCompilationMode.Hybrid, null));

        Assert.Null(result.Settings);
        Assert.Contains("Report.ps1", Assert.IsType<CompileDiagnostic>(result.Problem).Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void ClearingTheOutputFolderRestoresTheDefault()
    {
        var engine = new InterimCompileEngine();
        var script = WriteScript("Report.ps1");
        var settingsPath = Path.Combine(_root, InterimCompileEngine.DefaultSettingsFileName);
        var custom = Path.Combine(_root, "custom-out");
        var first = engine.SaveSettings(Request(engine, settingsPath, script, PowerShellCompilationMode.Hybrid, custom)).Settings!;

        var cleared = engine.SaveSettings(Request(engine, settingsPath, script, PowerShellCompilationMode.Hybrid, null)).Settings!;

        Assert.Equal(custom, first.Targets[0].OutputDirectory, ignoreCase: true);
        Assert.NotEqual(custom, cleared.Targets[0].OutputDirectory, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(Path.Combine(_root, "artifacts"), cleared.Targets[0].OutputDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReopeningRestoresTheMostRecentlySavedTarget()
    {
        var engine = new InterimCompileEngine();
        var script = WriteScript("Report.ps1");
        var settingsPath = Path.Combine(_root, InterimCompileEngine.DefaultSettingsFileName);
        engine.SaveSettings(Request(engine, settingsPath, script, PowerShellCompilationMode.Hybrid, null));
        engine.SaveSettings(Request(engine, settingsPath, script, PowerShellCompilationMode.Package, null));

        var reopened = engine.OpenSettings(settingsPath).Settings!;

        Assert.Equal(PowerShellCompilationMode.Package, reopened.Targets[0].Mode);
        Assert.Equal(2, reopened.Targets.Count);
    }

    [Fact]
    public async Task ACheckThatFinishesAfterANewerOneDoesNotReplaceIt()
    {
        var engine = new OrderedEngine();
        var viewModel = new MainWindowViewModel(engine);
        var slow = engine.Hold("A.ps1");

        var first = viewModel.SetInputAsync("A.ps1");
        await viewModel.SetInputAsync("B.ps1");
        slow.SetResult(OrderedEngine.Result("A.ps1", PowerShellCompilationMode.Strict));
        await first;

        Assert.Equal("B.ps1", viewModel.InputPath);
        Assert.False(viewModel.IsChecking);
        Assert.Equal(PowerShellCompilationMode.Package, viewModel.SelectedMode?.Mode);
    }

    [Fact]
    public async Task ASavedTargetIsNotAppliedToADifferentScript()
    {
        var engine = new OrderedEngine
        {
            Settings = new BuildSettings("A.psproject.json", "A.ps1", FileRules.Default,
                new[] { new SavedTarget("saved", PowerShellCompilationMode.Strict, "net10.0", "win-x64", PowerShellCompilationDeploymentModel.FrameworkDependent, "saved-out") })
        };
        var viewModel = new MainWindowViewModel(engine);
        engine.CancelNext = true;
        await viewModel.OpenSettingsAsync("A.psproject.json");

        await viewModel.SetInputAsync("B.ps1");

        Assert.Equal(PowerShellCompilationMode.Package, viewModel.SelectedMode?.Mode);
        Assert.Equal(string.Empty, viewModel.OutputDirectory);
    }

    private static SaveSettingsRequest Request(InterimCompileEngine engine, string settingsPath, string script, PowerShellCompilationMode mode, string? output) =>
        new(settingsPath, script, FileRules.Default, mode, engine.GetTargets(mode, PowerShellCompilationArtifactKind.Executable)[0], output);

    private string WriteScript(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "function Get-Total { param([int] $A) return $A * 2 }\nGet-Total -A 4\n");
        return path;
    }

    // Recommends Package for every input unless told otherwise, and can hold a check open to finish it later.
    private sealed class OrderedEngine : ICompileEngine
    {
        private readonly Dictionary<string, TaskCompletionSource<CheckResult>> _held = new();

        public bool CancelNext { get; set; }

        public BuildSettings? Settings { get; set; }

        public TaskCompletionSource<CheckResult> Hold(string path)
        {
            var source = new TaskCompletionSource<CheckResult>();
            _held[path] = source;
            return source;
        }

        public static CheckResult Result(string path, PowerShellCompilationMode recommended)
        {
            var modes = new[] { PowerShellCompilationMode.Package, PowerShellCompilationMode.Hybrid, PowerShellCompilationMode.Strict }
                .Select(static mode => new ModeCheck(mode, mode == PowerShellCompilationMode.Strict ? CompileOutcome.Native : CompileOutcome.Packaged,
                    0, 1, 0, Array.Empty<CompileDiagnostic>(), Array.Empty<CompileDependency>()))
                .ToArray();
            return new CheckResult(path, PowerShellCompilationArtifactKind.Executable, new[] { path }, modes, recommended,
                Array.Empty<CompileDiagnostic>(), TimeSpan.Zero);
        }

        public Task<CheckResult> CheckAsync(string inputPath, FileRules files, CancellationToken cancellationToken,
            PowerShellCompilationArtifactKind? kind = null)
        {
            if (CancelNext)
            {
                CancelNext = false;
                return Task.FromCanceled<CheckResult>(new CancellationToken(canceled: true));
            }

            // A held check ignores cancellation, like a Task.Run delegate that has already started.
            return _held.Remove(inputPath, out var held)
                ? held.Task
                : Task.FromResult(Result(inputPath, PowerShellCompilationMode.Package));
        }

        public IReadOnlyList<CompileTarget> GetTargets(PowerShellCompilationMode mode, PowerShellCompilationArtifactKind kind) =>
            new[] { new CompileTarget("win", mode, "net10.0", "win-x64", PowerShellCompilationDeploymentModel.FrameworkDependent, "Supported", true) };

        public IReadOnlyList<SigningCertificate> GetSigningCertificates() => Array.Empty<SigningCertificate>();

        public Task<CompileReviewResult> ReviewAsync(CompileRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CompileResult> CompileAsync(CompileRequest request, PowerShellCompilationReview review,
            IProgress<CompileProgress>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public SettingsResult OpenSettings(string settingsPath) => new(Settings, null);

        public SettingsResult SaveSettings(SaveSettingsRequest request) => throw new NotSupportedException();
    }
}
