using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerHardeningTests
{
    [Fact]
    public void Plan_ResolvesRelativeAzureDlibPathAgainstProjectRoot()
    {
        string root = CreateTempRoot();
        try
        {
            string project = CreateProjectFile(root, "App.csproj");
            DotNetPublishSpec spec = CreateBaseSpec(root, project);
            spec.Targets[0].Publish.Sign = AzureSign(Path.Combine("tools", "Azure.CodeSigning.Dlib.dll"));

            DotNetPublishPlan plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);

            Assert.Equal(
                Path.Combine(root, "tools", "Azure.CodeSigning.Dlib.dll"),
                Assert.Single(plan.Targets).Publish.Sign?.AzureArtifactSigning?.DlibPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_PreservesBareAzureDlibNameForPathLookup()
    {
        string root = CreateTempRoot();
        try
        {
            string project = CreateProjectFile(root, "App.csproj");
            DotNetPublishSpec spec = CreateBaseSpec(root, project);
            spec.Targets[0].Publish.Sign = AzureSign("Azure.CodeSigning.Dlib.dll");

            DotNetPublishPlan plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);

            Assert.Equal(
                "Azure.CodeSigning.Dlib.dll",
                Assert.Single(plan.Targets).Publish.Sign?.AzureArtifactSigning?.DlibPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("Endpoint")]
    [InlineData("AccountName")]
    [InlineData("CertificateProfileName")]
    [InlineData("DlibPath")]
    [InlineData("SubjectName")]
    [InlineData("InsecureEndpoint")]
    public void Plan_RejectsIncompleteEnabledAzureSigningProfile(string missingSetting)
    {
        string root = CreateTempRoot();
        try
        {
            string project = CreateProjectFile(root, "App.csproj");
            DotNetPublishSpec spec = CreateBaseSpec(root, project);
            DotNetPublishSignOptions sign = AzureSign("Azure.CodeSigning.Dlib.dll");
            switch (missingSetting)
            {
                case "Endpoint": sign.AzureArtifactSigning!.Endpoint = null; break;
                case "AccountName": sign.AzureArtifactSigning!.AccountName = null; break;
                case "CertificateProfileName": sign.AzureArtifactSigning!.CertificateProfileName = null; break;
                case "DlibPath": sign.AzureArtifactSigning!.DlibPath = null; break;
                case "SubjectName": sign.SubjectName = null; break;
                case "InsecureEndpoint": sign.AzureArtifactSigning!.Endpoint = "http://codesigning.example.invalid/"; break;
            }
            spec.Targets[0].Publish.Sign = sign;

            Assert.Throws<ArgumentException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_RejectsUndefinedSigningProvider()
    {
        string root = CreateTempRoot();
        try
        {
            string project = CreateProjectFile(root, "App.csproj");
            DotNetPublishSpec spec = CreateBaseSpec(root, project);
            spec.Targets[0].Publish.Sign = new DotNetPublishSignOptions
            {
                Enabled = true,
                Provider = (DotNetPublishSigningProvider)999
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains("not supported", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void NewConfigurationDotNetSignCommand_EmitsAzureProviderConfiguration()
    {
        var azure = new DotNetPublishAzureArtifactSigningOptions
        {
            Endpoint = "https://wus.codesigning.azure.net/",
            AccountName = "EvotecSigning",
            CertificateProfileName = "PublicTrust",
            DlibPath = "Azure.CodeSigning.Dlib.dll"
        };
        var command = new PSPublishModule.NewConfigurationDotNetSignCommand
        {
            Enabled = true,
            Provider = DotNetPublishSigningProvider.AzureArtifactSigning,
            SubjectName = "CN=Evotec Artifact Signing",
            AzureArtifactSigning = azure
        };

        DotNetPublishSignOptions result = command.CreateOptions();

        Assert.True(result.Enabled);
        Assert.Equal(DotNetPublishSigningProvider.AzureArtifactSigning, result.Provider);
        Assert.Same(azure, result.AzureArtifactSigning);
        Assert.Equal("CN=Evotec Artifact Signing", result.SubjectName);
    }

    [Fact]
    public void TrySignOutput_AzureArtifactSigningUsesDlibMetadataWithoutLocalCertificateSelectors()
    {
        if (!DotNetPublishPipelineRunner.IsWindows()) return;
        string root = CreateTempRoot();
        try
        {
            string output = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            string executable = Path.Combine(output, "app.exe");
            string dlib = Path.Combine(root, "Azure.CodeSigning.Dlib.dll");
            File.WriteAllText(executable, "payload");
            File.WriteAllText(dlib, "dlib");
            ProcessRunRequest? captured = null;
            string? metadataJson = null;
            var processRunner = new StubProcessRunner(request =>
            {
                captured = request;
                int metadataIndex = request.Arguments.ToList().IndexOf("/dmdf");
                metadataJson = File.ReadAllText(request.Arguments[metadataIndex + 1]);
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            DotNetPublishSignOptions sign = AzureSign(dlib);
            sign.Thumbprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            DotNetPublishSignOptions? publisherMatch = null;

            string[] signed = Assert.IsType<string[]>(GetTrySignOutputMethod().Invoke(
                new DotNetPublishPipelineRunner(
                    new NullLogger(),
                    processRunner,
                    _ => false,
                    signatureMatchesPublisher: (_, options) =>
                    {
                        publisherMatch = options;
                        return true;
                    }),
                new object[] { output, sign }));

            Assert.Equal(executable, Assert.Single(signed));
            Assert.NotNull(captured);
            Assert.Contains("/dlib", captured!.Arguments);
            Assert.Contains("/dmdf", captured.Arguments);
            Assert.DoesNotContain("/sha1", captured.Arguments);
            Assert.DoesNotContain("/n", captured.Arguments);
            Assert.DoesNotContain("/a", captured.Arguments);
            Assert.NotNull(publisherMatch);
            Assert.Null(publisherMatch!.Thumbprint);
            Assert.Equal(sign.SubjectName, publisherMatch.SubjectName);
            using JsonDocument metadata = JsonDocument.Parse(metadataJson!);
            Assert.Equal("https://wus.codesigning.azure.net/", metadata.RootElement.GetProperty("Endpoint").GetString());
            Assert.Equal("EvotecSigning", metadata.RootElement.GetProperty("CodeSigningAccountName").GetString());
            Assert.Equal("PublicTrust", metadata.RootElement.GetProperty("CertificateProfileName").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TrySignOutput_AzureSubjectMismatchUsesSignFailurePolicy()
    {
        if (!DotNetPublishPipelineRunner.IsWindows()) return;
        string root = CreateTempRoot();
        try
        {
            string output = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            string dlib = Path.Combine(root, "Azure.CodeSigning.Dlib.dll");
            File.WriteAllText(Path.Combine(output, "app.exe"), "payload");
            File.WriteAllText(dlib, "dlib");
            var processRunner = new StubProcessRunner(request =>
                new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false));
            DotNetPublishSignOptions sign = AzureSign(dlib);
            sign.OnSignFailure = DotNetPublishPolicyMode.Fail;

            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() =>
                GetTrySignOutputMethod().Invoke(
                    new DotNetPublishPipelineRunner(
                        new NullLogger(),
                        processRunner,
                        _ => false,
                        signatureMatchesPublisher: (_, _) => false),
                    new object[] { output, sign }));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("publisher subject", exception.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void SignPortableInventory_AzureArtifactSigningProducesDetachedPkcs7AndCleansMetadata()
    {
        if (!DotNetPublishPipelineRunner.IsWindows()) return;
        string root = CreateTempRoot();
        string? metadataRoot = null;
        try
        {
            byte[] content = [1, 2, 3];
            using X509Certificate2 certificate = CreateCmsCertificate("CN=Evotec Artifact Signing");
            string dlib = Path.Combine(root, "Azure.CodeSigning.Dlib.dll");
            File.WriteAllText(dlib, "dlib");
            var processRunner = new StubProcessRunner(request =>
            {
                Assert.Contains("DetachedSignedData", request.Arguments);
                Assert.Contains("1.2.840.113549.1.7.1", request.Arguments);
                Assert.DoesNotContain("1.3.6.1.5.5.7.3.3", request.Arguments);
                int metadataIndex = request.Arguments.ToList().IndexOf("/dmdf");
                metadataRoot = Path.GetDirectoryName(request.Arguments[metadataIndex + 1]);
                int outputIndex = request.Arguments.ToList().IndexOf("/p7");
                string signaturePath = Path.Combine(request.Arguments[outputIndex + 1], "inventory.p7");
                File.WriteAllBytes(signaturePath, CreateDetachedCms(content, certificate));
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var runner = new DotNetPublishPipelineRunner(new NullLogger(), processRunner);

            byte[] signature = runner.SignPortableInventory(content, AzureSign(dlib));

            PowerForgePayloadInventorySignature verified = PowerForgePortablePayloadInventoryCms.Verify(content, signature);
            Assert.Equal("CN=Evotec Artifact Signing", verified.Subject);
            Assert.NotNull(metadataRoot);
            Assert.False(Directory.Exists(metadataRoot));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("wrong-content")]
    [InlineData("wrong-subject")]
    public void SignPortableInventory_AzureArtifactSigningRejectsUnverifiedDetachedSignature(string failure)
    {
        if (!DotNetPublishPipelineRunner.IsWindows()) return;
        string root = CreateTempRoot();
        string? metadataRoot = null;
        try
        {
            byte[] content = [1, 2, 3];
            using X509Certificate2 certificate = CreateCmsCertificate(
                failure == "wrong-subject" ? "CN=Different Publisher" : "CN=Evotec Artifact Signing");
            string dlib = Path.Combine(root, "Azure.CodeSigning.Dlib.dll");
            File.WriteAllText(dlib, "dlib");
            var processRunner = new StubProcessRunner(request =>
            {
                int metadataIndex = request.Arguments.ToList().IndexOf("/dmdf");
                metadataRoot = Path.GetDirectoryName(request.Arguments[metadataIndex + 1]);
                int outputIndex = request.Arguments.ToList().IndexOf("/p7");
                string signaturePath = Path.Combine(request.Arguments[outputIndex + 1], "inventory.p7");
                byte[] signature = failure == "malformed"
                    ? [9, 8, 7]
                    : CreateDetachedCms(failure == "wrong-content" ? [4, 5, 6] : content, certificate);
                File.WriteAllBytes(signaturePath, signature);
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var runner = new DotNetPublishPipelineRunner(new NullLogger(), processRunner);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                runner.SignPortableInventory(content, AzureSign(dlib)));

            Assert.Contains(
                failure == "wrong-subject" ? "publisher subject" : "invalid detached",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(metadataRoot);
            Assert.False(Directory.Exists(metadataRoot));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void SigningProfileClonePreservesAzureProviderConfiguration()
    {
        DotNetPublishSignOptions source = AzureSign("Azure.CodeSigning.Dlib.dll");
        DotNetPublishSignOptions clone = DotNetPublishSigningProfileResolver.CloneSignOptions(source)!;

        Assert.Equal(DotNetPublishSigningProvider.AzureArtifactSigning, clone.Provider);
        Assert.Equal("EvotecSigning", clone.AzureArtifactSigning?.AccountName);
        Assert.Equal(source.AzureArtifactSigning?.ExcludeCredentials, clone.AzureArtifactSigning?.ExcludeCredentials);
        Assert.NotSame(source.AzureArtifactSigning?.ExcludeCredentials, clone.AzureArtifactSigning?.ExcludeCredentials);
    }

    [Fact]
    public void SigningProfileOverrideToAzureClearsInheritedCertificateStoreSelectors()
    {
        var configured = new DotNetPublishSignOptions
        {
            Enabled = true,
            Provider = DotNetPublishSigningProvider.CertificateStore,
            Thumbprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            SubjectName = "CN=Local Publisher",
            Csp = "Local CSP",
            KeyContainer = "Local Key"
        };
        var patch = new DotNetPublishSignPatch
        {
            Provider = DotNetPublishSigningProvider.AzureArtifactSigning,
            SubjectName = "CN=Azure Publisher",
            AzureArtifactSigning = new DotNetPublishAzureArtifactSigningOptions
            {
                Endpoint = "https://wus.codesigning.azure.net/",
                AccountName = "EvotecSigning",
                CertificateProfileName = "PublicTrust",
                DlibPath = "Azure.CodeSigning.Dlib.dll"
            }
        };

        DotNetPublishSigningProfileResolver.ApplySignPatch(configured, patch);

        Assert.Equal(DotNetPublishSigningProvider.AzureArtifactSigning, configured.Provider);
        Assert.Null(configured.Thumbprint);
        Assert.Null(configured.Csp);
        Assert.Null(configured.KeyContainer);
        Assert.Equal("CN=Azure Publisher", configured.SubjectName);
    }

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
            File.WriteAllText(Path.Combine(nested, "payload" + PowerForgePortablePayloadInventory.DirectInventorySuffix), "direct payload metadata");
            File.WriteAllText(Path.Combine(nested, "payload" + PowerForgePortablePayloadInventory.DirectSignatureSuffix), "direct payload signature");
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
                new string('b', 64),
                executable,
                "Sample",
                "1.2.3",
                signedFiles);
            Assert.Equal("app.exe", Assert.Single(inventory.SignedFilePaths));
            Assert.Equal(5, inventory.SchemaVersion);
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
            Assert.Contains(inventory.Entries, entry => string.Equals(
                entry.Path,
                "nested/payload" + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                StringComparison.Ordinal));
            Assert.Contains(inventory.Entries, entry => string.Equals(
                entry.Path,
                "nested/payload" + PowerForgePortablePayloadInventory.DirectSignatureSuffix,
                StringComparison.Ordinal));

            InvalidOperationException foreignPrimary = Assert.Throws<InvalidOperationException>(() =>
                PowerForgePortablePayloadInventoryCms.Create(
                    outputDir,
                    "Sample",
                    "win-x64",
                    "net10.0",
                    "PortableCompat",
                    new string('a', 40),
                    new string('b', 64),
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
    public void ResolvePortableInventorySigningOptions_AzureKeepsExpectedSubjectWithoutLocalThumbprint()
    {
        var runner = new DotNetPublishPipelineRunner(
            new NullLogger(),
            new StubProcessRunner(_ => throw new InvalidOperationException("Process execution was not expected.")),
            readAuthenticodeSignature: _ => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                true,
                0,
                "CN=Evotec Artifact Signing",
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
        DotNetPublishSignOptions configured = AzureSign("Azure.CodeSigning.Dlib.dll");

        DotNetPublishSignOptions resolved = runner.ResolvePortableInventorySigningOptions(
            new[] { "app.exe", "library.dll" },
            configured);

        Assert.Equal(DotNetPublishSigningProvider.AzureArtifactSigning, resolved.Provider);
        Assert.Null(resolved.Thumbprint);
        Assert.Equal(configured.SubjectName, resolved.SubjectName);
    }

    [Fact]
    public void ResolvePortableInventorySigningOptions_AzureAcceptsRotatedPayloadCertificates()
    {
        var runner = new DotNetPublishPipelineRunner(
            new NullLogger(),
            new StubProcessRunner(_ => throw new InvalidOperationException("Process execution was not expected.")),
            readAuthenticodeSignature: path => new DotNetPublishReleaseArtifactVerifier.AuthenticodeResult(
                true,
                0,
                "CN=Evotec Artifact Signing",
                path.EndsWith("app.exe", StringComparison.OrdinalIgnoreCase)
                    ? "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
                    : "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"));
        DotNetPublishSignOptions configured = AzureSign("Azure.CodeSigning.Dlib.dll");

        DotNetPublishSignOptions resolved = runner.ResolvePortableInventorySigningOptions(
            new[] { "app.exe", "library.dll" },
            configured);

        Assert.Null(resolved.Thumbprint);
        Assert.Equal(configured.SubjectName, resolved.SubjectName);
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

    private static DotNetPublishSignOptions AzureSign(string dlibPath) => new()
    {
        Enabled = true,
        OverwriteSigned = true,
        Provider = DotNetPublishSigningProvider.AzureArtifactSigning,
        ToolPath = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        SubjectName = "CN=Evotec Artifact Signing",
        OnMissingTool = DotNetPublishPolicyMode.Fail,
        OnSignFailure = DotNetPublishPolicyMode.Fail,
        AzureArtifactSigning = new DotNetPublishAzureArtifactSigningOptions
        {
            Endpoint = "https://wus.codesigning.azure.net/",
            AccountName = "EvotecSigning",
            CertificateProfileName = "PublicTrust",
            DlibPath = dlibPath,
            ExcludeCredentials = ["ManagedIdentityCredential"]
        }
    };

    private static X509Certificate2 CreateCmsCertificate(string subject)
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static byte[] CreateDetachedCms(byte[] content, X509Certificate2 certificate)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.ComputeSignature(new CmsSigner(certificate) { IncludeOption = X509IncludeOption.EndCertOnly });
        return cms.Encode();
    }
}
