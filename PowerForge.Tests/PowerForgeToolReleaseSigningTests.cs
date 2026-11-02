using System.Diagnostics;
using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class PowerForgeToolReleaseSigningTests
{
    [Fact]
    public void RepositoryConfiguration_PlansFailClosedSigningForBothCliTargets()
    {
        var root = PowerForgeCliTestHost.FindRepositoryRoot();
        var path = Path.Combine(root, "Build", "release.json");
        var configuration = PowerForgeReleaseService.LoadConfiguration(path);
        var plan = new PowerForgeToolReleaseService(new NullLogger()).Plan(configuration.Tools!, path);
        Assert.Equal(new[] { "PowerForge", "PowerForgeWeb" }, plan.Targets.Select(target => target.Name));
        Assert.Equal(11, plan.Targets.Sum(target => target.Combinations.Length));
        foreach (var target in plan.Targets)
        {
            Assert.NotNull(target.Sign);
            Assert.True(target.Sign.Enabled);
            Assert.Equal(configuration.Packages!.CertificateThumbprint, target.Sign.Thumbprint);
            Assert.False(target.Sign.IncludeDlls);
            Assert.Equal(DotNetPublishPolicyMode.Fail, target.Sign.OnMissingTool);
            Assert.Equal(DotNetPublishPolicyMode.Fail, target.Sign.OnSignFailure);
        }
        var unsupported = Assert.Throws<InvalidOperationException>(() =>
            new PowerForgeToolReleaseService(new NullLogger()).Plan(configuration.Tools!, path,
                new PowerForgeReleaseRequest { SignProfile = "named-profile" }));
        Assert.Contains("Tools.DotNetPublish", unsupported.Message);
    }

    [Theory]
    [InlineData("win-x64", "success")]
    [InlineData("win-arm64", "success")]
    [InlineData("linux-x64", "success")]
    [InlineData("win-x64", "disabled")]
    [InlineData("win-x64", "overrides")]
    [InlineData("win-x64", "failure")]
    [InlineData("win-x64", "invalid-signature")]
    [InlineData("win-x64", "canceled")]
    public void Run_SignsWindowsExecutablesBeforeArchivingAndStopsOnSigningFailure(string runtime, string outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolSigningTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "Sample.Tool.csproj");
            File.WriteAllText(project, "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");
            var signTool = Path.Combine(root, "signtool.exe");
            File.WriteAllText(signTool, "process fixture; never executed");
            using var cancellation = new CancellationTokenSource();
            var signingCalls = 0;
            var signingRunner = new DotNetPublishPipelineRunner(
                new NullLogger(),
                new SigningProcess(request =>
                {
                    signingCalls++;
                    Assert.Equal(signTool, request.Request.FileName);
                    Assert.Equal(cancellation.Token, request.Token);
                    var executable = request.Request.Arguments.Last();
                    Assert.Equal("Renamed.exe", Path.GetFileName(executable));
                    if (outcome == "canceled")
                        cancellation.Cancel();
                    else if (outcome is "success" or "overrides")
                    {
                        if (outcome == "overrides")
                            Assert.Contains("publisher-B", request.Request.Arguments);
                        File.WriteAllText(executable, "signed fixture bytes");
                    }
                    return new ProcessRunResult(outcome == "failure" ? 1 : 0, "", "", signTool, TimeSpan.Zero, false);
                }),
                hasAuthenticodeSignature: _ => false,
                signatureMatchesPublisher: (path, options) =>
                {
                    if (outcome == "overrides")
                        Assert.Equal("publisher-B", options.Thumbprint);
                    return File.ReadAllText(path) == "signed fixture bytes";
                });
            var service = new PowerForgeToolReleaseService(new NullLogger(), (startInfo, _) =>
            {
                var directory = ReadPublishDirectory(startInfo);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, runtime.StartsWith("win-") ? "Sample.Tool.exe" : "Sample.Tool"), "unsigned fixture bytes");
                return new PowerForgeToolReleaseService.ProcessExecutionResult(0, "", "");
            }, signingRunner);
            var plan = service.Plan(new PowerForgeToolReleaseSpec
            {
                ProjectRoot = root,
                Targets = [new PowerForgeToolReleaseTarget
                {
                    Name = "Sample.Tool", ProjectPath = project, OutputName = "Renamed",
                    Runtimes = [runtime], Frameworks = ["net10.0"], UseStaging = true,
                    ArtifactRootPath = "artifacts",
                    Sign = new DotNetPublishSignOptions
                    {
                        Enabled = outcome != "overrides",
                        ToolPath = outcome == "overrides" ? Path.Combine(root, "unused-signer.exe") : signTool,
                        Thumbprint = "synthetic-publisher",
                        OnMissingTool = DotNetPublishPolicyMode.Fail, OnSignFailure = DotNetPublishPolicyMode.Fail
                    }
                }]
            }, Path.Combine(root, "release.json"), new PowerForgeReleaseRequest
            {
                EnableSigning = outcome == "disabled" ? false : outcome == "overrides" ? true : null,
                SignToolPath = outcome == "overrides" ? signTool : null,
                SignThumbprint = outcome == "overrides" ? "publisher-B" : null
            });
            var combination = Assert.Single(Assert.Single(plan.Targets).Combinations);
            if (OperatingSystem.IsWindows() && outcome == "canceled")
            {
                Assert.ThrowsAny<OperationCanceledException>(() => service.Run(plan, cancellationToken: cancellation.Token));
                Assert.False(File.Exists(combination.ZipPath));
                return;
            }
            var result = service.Run(plan, cancellationToken: cancellation.Token);
            var windows = runtime.StartsWith("win-");
            var signingEnabled = windows && outcome != "disabled";
            if (signingEnabled && (!OperatingSystem.IsWindows() || outcome is not ("success" or "overrides")))
            {
                Assert.False(result.Success);
                Assert.False(File.Exists(combination.ZipPath));
                Assert.Empty(result.Artefacts);
                Assert.Contains(!OperatingSystem.IsWindows() ? "OS is not Windows" : outcome == "failure" ? "Signing failed" : "valid signature", result.ErrorMessage);
            }
            else
            {
                Assert.True(result.Success, result.ErrorMessage);
                var artifact = Assert.Single(result.Artefacts);
                using var archive = ZipFile.OpenRead(artifact.ZipPath!);
                var entry = archive.GetEntry(windows ? "Renamed.exe" : "Renamed");
                Assert.NotNull(entry);
                using var reader = new StreamReader(entry.Open());
                Assert.Equal(signingEnabled ? "signed fixture bytes" : "unsigned fixture bytes", reader.ReadToEnd());
            }
            Assert.Equal(signingEnabled && OperatingSystem.IsWindows() ? 1 : 0, signingCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadPublishDirectory(ProcessStartInfo startInfo)
    {
        const string prefix = "/p:PublishDir=";
        var start = startInfo.Arguments.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        Assert.True(start >= prefix.Length);
        var arguments = startInfo.Arguments.Substring(start);
        return arguments.StartsWith('"') ? arguments.Split('"')[1] : arguments.Split(' ')[0];
    }

    private sealed class SigningProcess(Func<(ProcessRunRequest Request, CancellationToken Token), ProcessRunResult> run) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(run((request, cancellationToken)));
    }
}
