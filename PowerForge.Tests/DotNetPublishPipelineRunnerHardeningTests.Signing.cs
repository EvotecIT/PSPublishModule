using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerHardeningTests
{
    [Theory]
    [InlineData(unchecked((int)0x800B0100), true)]
    [InlineData(unchecked((int)0x800B0001), true)]
    [InlineData(unchecked((int)0x800B0003), true)]
    [InlineData(unchecked((int)0x800B0109), false)]
    [InlineData(unchecked((int)0x80096010), false)]
    [InlineData(0, false)]
    public void AuthenticodeSignaturePresence_DistinguishesMissingFromInvalidSignatures(int status, bool expectedNoSignature)
        => Assert.Equal(expectedNoSignature, WindowsAuthenticodeSignatureInspector.IsNoSignatureStatus(status));

    [Fact]
    public void TrySignOutput_WhenMissingToolAndPolicyFail_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "app.exe"), "dummy");
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                ToolPath = "definitely-not-a-real-signtool.exe",
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            var ex = Assert.Throws<TargetInvocationException>(() =>
                GetTrySignOutputMethod().Invoke(
                    new DotNetPublishPipelineRunner(new NullLogger()),
                    new object[] { outputDir, sign }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.True(
                ex.InnerException!.Message.Contains("Signing requested", StringComparison.OrdinalIgnoreCase)
                || ex.InnerException.Message.Contains("Signing failed", StringComparison.OrdinalIgnoreCase),
                $"Unexpected message: {ex.InnerException.Message}");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void TrySignOutput_SelectsExpectedPublishFiles(bool includeDlls, int expectedTargets)
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "app.exe"), "dummy");
            File.WriteAllText(Path.Combine(outputDir, "lib.dll"), "dummy");
            var logger = new CollectingLogger();
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                IncludeDlls = includeDlls,
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Skip
            };

            _ = GetTrySignOutputMethod().Invoke(
                new DotNetPublishPipelineRunner(logger),
                new object[] { outputDir, sign });

            Assert.Contains(
                logger.InfoMessages,
                message => message.Contains($"Signing {expectedTargets} file(s)", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySignOutput_DefaultPreservesExistingValidSignature()
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var executable = Path.Combine(outputDir, "app.exe");
            File.WriteAllText(executable, "dummy");
            var requests = new List<ProcessRunRequest>();
            var processRunner = new StubProcessRunner(request =>
            {
                requests.Add(request);
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            string[] signedFiles = Assert.IsType<string[]>(GetTrySignOutputMethod().Invoke(
                new DotNetPublishPipelineRunner(new NullLogger(), processRunner, _ => true),
                new object[] { outputDir, sign }));

            Assert.Equal(executable, Assert.Single(signedFiles));
            Assert.Empty(requests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySignOutput_OverwriteOptInSkipsExistingSignatureCheck()
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "app.exe"), "dummy");
            var requests = new List<ProcessRunRequest>();
            var processRunner = new StubProcessRunner(request =>
            {
                requests.Add(request);
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                OverwriteSigned = true,
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            string[] signedFiles = Assert.IsType<string[]>(GetTrySignOutputMethod().Invoke(
                new DotNetPublishPipelineRunner(new NullLogger(), processRunner, _ => true),
                new object[] { outputDir, sign }));

            Assert.Equal(Path.Combine(outputDir, "app.exe"), Assert.Single(signedFiles));
            Assert.Equal("sign", Assert.Single(requests).Arguments[0]);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySignOutput_WhenDllOnlyAndIncludeDllsDisabled_HonorsFailurePolicy()
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outputDir, "lib.dll"), "dummy");
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            var ex = Assert.Throws<TargetInvocationException>(() =>
                GetTrySignOutputMethod().Invoke(
                    new DotNetPublishPipelineRunner(new NullLogger()),
                    new object[] { outputDir, sign }));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
            Assert.Contains("no matching files were found", ex.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("IncludeDlls=true", ex.InnerException.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
