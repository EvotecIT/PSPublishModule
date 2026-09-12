namespace PowerForge.Tests;

public sealed class ReleaseValidationSelectionRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Selection.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationSelectionRegressionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("Linuz", false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("Linuz", true)]
    public async Task Direct_command_rejects_every_invalid_platform_before_skip_or_execution(string invalid, bool includeCurrent)
    {
        var calls = 0;
        var service = new ReleaseValidationService(new Runner(_ => { calls++; return Success(); }));
        var command = new ReleaseCommandValidation { FileName = "probe", WorkingDirectory = _root,
            Platforms = includeCurrent ? [CurrentPlatform, invalid] : [invalid] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunCommandAsync(command));

        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("mixed")]
    [InlineData("noncurrent")]
    [InlineData("current-lowercase")]
    public async Task Loaded_json_platform_selection_rejects_typos_and_preserves_valid_selection(string selection)
    {
        var platforms = selection switch {
            "invalid" => new[] { "Linuz" },
            "mixed" => new[] { CurrentPlatform, "Linuz" },
            "noncurrent" => new[] { CurrentPlatform == "Windows" ? "Linux" : "Windows" },
            _ => new[] { CurrentPlatform.ToLowerInvariant() }
        };
        var path = Path.Combine(_root, "validation.json");
        File.WriteAllText(path, ReleaseValidationService.Serialize(new() { Commands = [new() {
            Name = "Selected probe", FileName = "probe", Platforms = platforms, ExpectedOutput = "observed"
        }] }));
        var calls = 0;
        var report = await new ReleaseValidationService(new Runner(_ => { calls++; return Success(); }))
            .RunAsync(ReleaseValidationService.Load(path), path, new() { ProjectRoot = _root });

        var invalid = selection is "invalid" or "mixed";
        Assert.Equal(!invalid, report.Success);
        Assert.Equal(selection == "current-lowercase" ? 1 : 0, calls);
        if (invalid) { Assert.Single(report.Errors); Assert.Empty(report.Checks); }
        else if (selection == "noncurrent") Assert.Contains("not applicable", Assert.Single(report.Checks));
        else Assert.Equal("Selected probe", Assert.Single(report.Checks));
    }

    [WindowsFact]
    public async Task Default_signature_patterns_select_existing_script_payload_as_a_union()
    {
        CreateModule();
        var report = await ValidateSignaturesAsync(new());

        Assert.False(report.Success);
        var error = Assert.Single(report.Errors);
        Assert.Contains("valid allowed signature", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Checks);
    }

    [WindowsFact]
    public async Task Empty_signature_selection_cannot_succeed_without_verification()
    {
        CreateModule();
        var report = await ValidateSignaturesAsync(new() { Include = [] });

        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.Empty(report.Checks);
    }

    [WindowsFact]
    public async Task Signature_patterns_matching_no_payload_fail_before_runtime_probe()
    {
        CreateModule();
        var calls = 0;
        var report = await new ReleaseValidationService(new Runner(_ => { calls++; return Success(); })).RunAsync(new() {
            Modules = [new() { Path = _root, Manifest = "Example.psd1", Signatures = new() { Include = ["**/*.dll"] },
                ProbeScript = Path.Combine(_root, "Example.psm1"), Hosts = ["probe"] }]
        }, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.Empty(report.Checks);
        Assert.Equal(0, calls);
    }

    private void CreateModule()
    {
        File.WriteAllText(Path.Combine(_root, "Example.psd1"), "@{ ModuleVersion = '1.2.3'; RootModule = 'Example.psm1' }");
        File.WriteAllText(Path.Combine(_root, "Example.psm1"), "function Get-Example { 'fixture' }");
    }
    private Task<ReleaseValidationReport> ValidateSignaturesAsync(PayloadSignatureValidation signatures)
        => new ReleaseValidationService().RunAsync(new() { Modules = [new() {
            Path = _root, Manifest = "Example.psd1", Signatures = signatures
        }] }, request: new() { ProjectRoot = _root });
    private static string CurrentPlatform => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "OSX" : "Linux";
    private static ProcessRunResult Success() => new(0, "observed", "", "probe", TimeSpan.Zero, false);
    private sealed class Runner(Func<ProcessRunRequest, ProcessRunResult> execute) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(execute(request));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
