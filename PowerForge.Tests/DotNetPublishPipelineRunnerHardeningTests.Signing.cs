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
                SubjectName = "Test Publisher",
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            string[] signedFiles = Assert.IsType<string[]>(GetTrySignOutputMethod().Invoke(
                new DotNetPublishPipelineRunner(
                    new NullLogger(),
                    processRunner,
                    _ => true,
                    signatureMatchesPublisher: (_, _) => true),
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
    public void TrySignOutput_PreservedForeignSignature_IsNotClaimedAsPublisherOwned()
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            return;

        var root = CreateTempRoot();
        try
        {
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            var executable = Path.Combine(outputDir, "app.exe");
            var dependency = Path.Combine(outputDir, "dependency.dll");
            File.WriteAllText(executable, "dummy");
            File.WriteAllText(dependency, "dummy");
            string nested = Directory.CreateDirectory(Path.Combine(outputDir, "nested")).FullName;
            File.WriteAllText(Path.Combine(nested, PowerForgePortablePayloadInventory.InventoryFileName), "payload metadata");
            File.WriteAllText(Path.Combine(nested, PowerForgePortablePayloadInventory.SignatureFileName), "payload signature");
            var requests = new List<ProcessRunRequest>();
            var processRunner = new StubProcessRunner(request =>
            {
                requests.Add(request);
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                IncludeDlls = true,
                SubjectName = "Configured Publisher",
                ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                OnMissingTool = DotNetPublishPolicyMode.Fail,
                OnSignFailure = DotNetPublishPolicyMode.Fail
            };

            string[] signedFiles = Assert.IsType<string[]>(GetTrySignOutputMethod().Invoke(
                new DotNetPublishPipelineRunner(
                    new NullLogger(),
                    processRunner,
                    path => string.Equals(path, dependency, StringComparison.OrdinalIgnoreCase),
                    signatureMatchesPublisher: (_, _) => false),
                new object[] { outputDir, sign }));

            Assert.Equal(executable, Assert.Single(signedFiles));
            Assert.Equal(executable, Assert.Single(requests).Arguments[^1]);

            PowerForgePortablePayloadInventory inventory = PowerForgePortablePayloadInventoryCms.Create(
                outputDir,
                "Sample",
                "win-x64",
                "net10.0",
                "PortableCompat",
                new string('a', 40),
                executable,
                "Sample",
                "1.2.3",
                signedFiles);
            Assert.Equal("app.exe", Assert.Single(inventory.SignedFilePaths));
            Assert.Equal(3, inventory.SchemaVersion);
            Assert.Equal("win-x64", inventory.Runtime);
            Assert.Equal("net10.0", inventory.Framework);
            Assert.Equal("PortableCompat", inventory.Style);
            Assert.Contains(inventory.Entries, entry => string.Equals(entry.Path, "dependency.dll", StringComparison.Ordinal));
            Assert.Contains(inventory.Entries, entry => string.Equals(
                entry.Path,
                "nested/" + PowerForgePortablePayloadInventory.InventoryFileName,
                StringComparison.Ordinal));
            Assert.Contains(inventory.Entries, entry => string.Equals(
                entry.Path,
                "nested/" + PowerForgePortablePayloadInventory.SignatureFileName,
                StringComparison.Ordinal));

            InvalidOperationException foreignPrimary = Assert.Throws<InvalidOperationException>(() =>
                PowerForgePortablePayloadInventoryCms.Create(
                    outputDir,
                    "Sample",
                    "win-x64",
                    "net10.0",
                    "PortableCompat",
                    new string('a', 40),
                    executable,
                    "Sample",
                    "1.2.3",
                    new[] { dependency }));
            Assert.Contains("primary executable", foreignPrimary.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolvePortableInventorySigningOptions_UsesActualAutomaticSignerThumbprint()
    {
        var runner = new DotNetPublishPipelineRunner(
            new NullLogger(),
            new StubProcessRunner(_ => throw new InvalidOperationException("Process execution was not expected.")),
            readAuthenticodeSignature: _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                true,
                0,
                "CN=Automatic Publisher",
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        DotNetPublishSignOptions resolved = runner.ResolvePortableInventorySigningOptions(
            new[] { "app.exe", "library.dll" },
            new DotNetPublishSignOptions { Enabled = true });

        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", resolved.Thumbprint);
        Assert.Null(resolved.SubjectName);
    }

    [Fact]
    public void ResolvePrimaryExecutable_UsesConfiguredIdentityInsteadOfFileSize()
    {
        string root = CreateTempRoot();
        try
        {
            string expected = Path.Combine(root, "Sample.CLI.exe");
            string largerHelper = Path.Combine(root, "Updater.exe");
            File.WriteAllText(expected, "small");
            File.WriteAllText(largerHelper, new string('x', 4096));

            string? selected = DotNetPublishPipelineRunner.ResolvePrimaryExecutable(
                root,
                "win-x64",
                new[] { "Sample.CLI" });

            Assert.Equal(expected, selected);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ResolvePrimaryExecutable_UsesIdentityMatchingDllWhenWindowsAppHostIsDisabled()
    {
        string root = CreateTempRoot();
        try
        {
            string entryPoint = Path.Combine(root, "Sample.CLI.dll");
            File.WriteAllText(entryPoint, "managed entrypoint");
            File.WriteAllText(Path.Combine(root, "Updater.exe"), "helper");

            string? selected = DotNetPublishPipelineRunner.ResolvePrimaryExecutable(
                root,
                "win-x64",
                new[] { "Sample.CLI" });

            Assert.Equal(entryPoint, selected);
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
