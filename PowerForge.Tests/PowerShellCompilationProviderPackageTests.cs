using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.Loader;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationProviderPackageTests
{
    [Fact]
    public void SdkConformanceKitProducesOrderStableEvidenceAndRejectsAmbiguousRegistration()
    {
        using var fixture = ProviderFixture.Create();
        var first = fixture.Manifest.Providers[0];
        var second = new PowerShellCompilationCommandProviderContract
        {
            ProviderId = "generic.command.stream.warning",
            ProviderVersion = "1.0",
            FeatureId = "command.write-package-warning-core",
            Family = PowerShellCompilationCommandFamily.Stream,
            CommandName = "Write-PackageWarningCore",
            Parameters = new[] { new PowerShellCompilationCommandParameterContract { Name = "Message", Position = 0 } },
            Output = PowerShellCompilationCommandOutput.None,
            Cardinality = PowerShellCompilationCommandCardinality.None,
            Stream = "Warning",
            Errors = PowerShellCompilationCommandErrors.None,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = "WriteWarning",
                SemanticProfile = first.Adapter.SemanticProfile,
                RuntimeFree = true,
                AotCompatible = true,
                EntryPoint = first.Adapter.EntryPoint
            }
        };
        fixture.Manifest.Providers = new[] { first, second };
        var kit = new PowerShellCompilationProviderConformanceKit();

        var forward = kit.Validate(fixture.Manifest);
        fixture.Manifest.Providers = new[] { second, first };
        var reverse = kit.Validate(fixture.Manifest);

        Assert.Equal(forward.ContractSha256, reverse.ContractSha256);
        Assert.Equal(8, forward.PassedChecks.Length);
        second.Aliases = new[] { first.CommandName };
        Assert.Throws<InvalidOperationException>(() => kit.Validate(fixture.Manifest));
    }

    [Fact]
    public void SdkBuildsDeterministicPackageAndReaderLocksTrustClosureWithoutAssemblyLoad()
    {
        using var fixture = ProviderFixture.Create();
        var firstProvider = fixture.Manifest.Providers[0];
        firstProvider.ModuleNames = new[] { "Generic.Second", "Generic.First" };
        firstProvider.Aliases = new[] { "Write-NoticeZ", "Write-NoticeA" };
        firstProvider.Parameters[0].Aliases = new[] { "TextZ", "TextA" };
        firstProvider.Adapter.Dependencies = new[] { "Generic.Runtime.Z", "Generic.Runtime.A" };
        var secondProvider = new PowerShellCompilationCommandProviderContract
        {
            ProviderId = "generic.command.stream.warning",
            ProviderVersion = "1.0",
            FeatureId = "command.write-package-warning-core",
            Family = PowerShellCompilationCommandFamily.Stream,
            CommandName = "Write-PackageWarningCore",
            ModuleNames = new[] { "Generic.Warning.Z", "Generic.Warning.A" },
            Aliases = new[] { "Write-WarningZ", "Write-WarningA" },
            Parameters = new[]
            {
                new PowerShellCompilationCommandParameterContract
                {
                    Name = "Message",
                    Position = 0,
                    Aliases = new[] { "WarningZ", "WarningA" }
                }
            },
            Output = PowerShellCompilationCommandOutput.None,
            Cardinality = PowerShellCompilationCommandCardinality.None,
            Stream = "Warning",
            Errors = PowerShellCompilationCommandErrors.None,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = "WriteWarning",
                SemanticProfile = firstProvider.Adapter.SemanticProfile,
                RuntimeFree = true,
                AotCompatible = true,
                Dependencies = new[] { "Generic.Warning.Runtime.Z", "Generic.Warning.Runtime.A" },
                EntryPoint = firstProvider.Adapter.EntryPoint
            }
        };
        fixture.Manifest.Providers = new[] { firstProvider, secondProvider };
        var first = fixture.BuildPackage("first.nupkg");
        fixture.Manifest.Providers = new[] { secondProvider, firstProvider };
        Array.Reverse(firstProvider.ModuleNames);
        Array.Reverse(firstProvider.Aliases);
        Array.Reverse(firstProvider.Parameters[0].Aliases);
        Array.Reverse(firstProvider.Adapter.Dependencies);
        Array.Reverse(secondProvider.ModuleNames);
        Array.Reverse(secondProvider.Aliases);
        Array.Reverse(secondProvider.Parameters[0].Aliases);
        Array.Reverse(secondProvider.Adapter.Dependencies);
        var second = fixture.BuildPackage("second.nupkg");

        Assert.Equal(Hash(fixture.PackagePath("first.nupkg")), Hash(fixture.PackagePath("second.nupkg")));
        var package = Assert.Single(first.Lock.Packages);
        Assert.Equal("Generic.Semantic.Provider", package.PackageId);
        Assert.Equal("1.0.0", package.PackageVersion);
        Assert.Equal(PowerShellCompilationProviderAbi.CurrentVersion, package.ProviderAbiVersion);
        Assert.Equal("Unsigned", package.Signature);
        Assert.Equal("MIT", package.LicenseExpression);
        Assert.NotEmpty(package.PackageSha256);
        Assert.NotEmpty(package.ManifestSha256);
        Assert.NotEmpty(first.Lock.LockSha256);
        Assert.Equal(PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, first.Lock.SemanticProfileId);
        Assert.Equal(first.Lock.LockSha256, second.Lock.LockSha256);
        Assert.Single(first.Providers, static provider => provider.ProviderId == "generic.command.stream.notice");
        Assert.Single(first.Providers, static provider => provider.ProviderId == "generic.command.stream.warning");
    }

    [Fact]
    public void ReaderAppliesDenyBeforeAllowAndFailsClosedOnPublisherLicenseOrSignaturePolicy()
    {
        using var fixture = ProviderFixture.Create();
        fixture.BuildPackage("provider.nupkg");
        var reference = new PowerShellCompilationProviderPackageReference(fixture.PackagePath("provider.nupkg"));
        var reader = new PowerShellCompilationProviderPackageReader();

        Assert.Throws<InvalidOperationException>(() => reader.Resolve(new[] { reference }, new PowerShellCompilationProviderTrustPolicy
        {
            AllowedPackageIds = new[] { "Generic.Semantic.Provider" },
            DeniedPackageIds = new[] { "Generic.Semantic.Provider" }
        }));
        Assert.Throws<InvalidOperationException>(() => reader.Resolve(new[] { reference }, new PowerShellCompilationProviderTrustPolicy
        {
            AllowedPublishers = new[] { "Different Publisher" }
        }));
        Assert.Throws<InvalidOperationException>(() => reader.Resolve(new[] { reference }, new PowerShellCompilationProviderTrustPolicy
        {
            AllowedLicenseExpressions = new[] { "Apache-2.0" }
        }));
        Assert.Throws<InvalidOperationException>(() => reader.Resolve(new[] { reference }, new PowerShellCompilationProviderTrustPolicy
        {
            RequirePackageSignature = true
        }));
        Assert.Throws<InvalidOperationException>(() => reader.Resolve(new[] { reference }, new PowerShellCompilationProviderTrustPolicy
        {
            AllowedSignerFingerprints = new[] { new string('a', 64) }
        }));
    }

    [Fact]
    public void ReaderRejectsProviderPackageForDifferentSourceSemanticProfile()
    {
        using var fixture = ProviderFixture.Create();
        fixture.BuildPackage("provider.nupkg");
        var reference = new PowerShellCompilationProviderPackageReference(fixture.PackagePath("provider.nupkg"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageReader().Resolve(
                new[] { reference },
                semanticProfileId: PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId));

        Assert.Contains("does not support source semantic profile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsMissingExecutableAdapterMethodWithoutLoadingProviderAssembly()
    {
        using var fixture = ProviderFixture.Create();
        fixture.Manifest.Providers[0].Adapter.EntryPoint!.MethodName = "MissingMethod";

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.BuildPackage("provider.nupkg"));

        Assert.Contains("public static non-generic string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PowerShellCompilationMode.Strict)]
    [InlineData(PowerShellCompilationMode.Hybrid)]
    public void ArtifactBuildRequiresReviewedProviderLockAndExecutesAdapter(PowerShellCompilationMode mode)
    {
        using var providerFixture = ProviderFixture.Create();
        var packagePath = providerFixture.PackagePath("provider.nupkg");
        var resolution = providerFixture.BuildPackage("provider.nupkg");
        using var artifactFixture = ScriptFixture.Create("function Write-PackageNotice { Write-PackageNoticeCore 'locked' }");
        var reference = new PowerShellCompilationProviderPackageReference(packagePath);
        var unlocked = new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "UnreviewedProvider" + mode,
            PowerShellCompilationArtifactKind.Library,
            mode,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { reference }
        };

        var unlockedResult = new PowerShellCompilationArtifactBuilder().Build(unlocked);
        Assert.False(unlockedResult.Succeeded);
        Assert.Contains("reviewed provider lock", unlockedResult.Error, StringComparison.OrdinalIgnoreCase);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ReviewedProvider" + mode,
            PowerShellCompilationArtifactKind.Library,
            mode,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { reference },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = new PowerShellCompilationProviderTrustPolicy
            {
                AllowedPackageIds = new[] { "Generic.Semantic.Provider" },
                AllowedProviderIds = new[] { "generic.command.stream.notice" },
                AllowedPublishers = new[] { "Generic Publisher" },
                AllowedLicenseExpressions = new[] { "MIT" }
            }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.ProviderLockReviewed);
        Assert.Equal(resolution.Lock.LockSha256, result.Manifest.ProviderLock!.LockSha256);
        Assert.Equal(resolution.Lock.LockSha256, result.Manifest.Reproduction!.ProviderLockSha256);
        Assert.Single(result.Manifest.CommandProviders, static provider => provider.ProviderId == "generic.command.stream.notice");
        var sbomPath = Assert.Single(result.Manifest.Files, static file => file.Role == "Sbom").Path;
        var provenancePath = Assert.Single(result.Manifest.Files, static file => file.Role == "BuildProvenance").Path;
        Assert.Contains("Generic.Semantic.Provider", File.ReadAllText(sbomPath), StringComparison.Ordinal);
        Assert.Contains(resolution.Lock.LockSha256, File.ReadAllText(provenancePath), StringComparison.OrdinalIgnoreCase);
        var providerRuntime = Assert.Single(result.Manifest.Files, static file => file.Role == "CompilerProviderRuntime");
        Assert.Equal(Assert.Single(resolution.Lock.Packages).Assemblies[0].Sha256, providerRuntime.Sha256);

        var loadContext = new ArtifactLoadContext(Path.GetDirectoryName(result.ArtifactPath!)!);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(result.ArtifactPath!);
            var method = assembly.GetType("PowerForge.Compiled.ReviewedProvider" + mode + "Methods", throwOnError: true)!
                .GetMethod("Write_PackageNotice", BindingFlags.Public | BindingFlags.Static)!;
            var information = new List<string>();
            method.Invoke(null, new object[]
            {
                (Action<object?>)(_ => { }),
                (Action<string>)(_ => { }),
                (Action<string>)(_ => { }),
                (Action<string>)(_ => { }),
                (Action<string>)(information.Add),
                (Action<string>)(_ => { }),
                (Action<string>)(_ => { })
            });
            Assert.Equal(new[] { "provider:locked" }, information);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact]
    public void ReviewedProviderLockRejectsPackageDrift()
    {
        using var fixture = ProviderFixture.Create();
        var resolution = fixture.BuildPackage("provider.nupkg");
        fixture.Manifest.PackageVersion = "1.0.1";
        fixture.BuildPackage("provider.nupkg");
        var actual = new PowerShellCompilationProviderPackageReader().Resolve(new[]
        {
            new PowerShellCompilationProviderPackageReference(fixture.PackagePath("provider.nupkg"))
        });

        Assert.Throws<InvalidOperationException>(() =>
            PowerShellCompilationProviderPackageReader.EnsureMatches(resolution.Lock, actual.Lock));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return Convert.ToHexString(algorithm.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed class ProviderFixture : IDisposable
    {
        private ProviderFixture(string rootPath, PowerShellCompilationProviderPackageManifest manifest)
        {
            RootPath = rootPath;
            Manifest = manifest;
        }

        public string RootPath { get; }
        public PowerShellCompilationProviderPackageManifest Manifest { get; }

        public static ProviderFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeProviderPackageTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new ProviderFixture(root, new PowerShellCompilationProviderPackageManifest
            {
                PackageId = "Generic.Semantic.Provider",
                PackageVersion = "1.0.0",
                Publisher = "Generic Publisher",
                LicenseExpression = "MIT",
                SourceSemanticProfiles = new[]
                {
                    PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId
                },
                SemanticProfiles = new[]
                {
                    PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion
                },
                Providers = new[]
                {
                    new PowerShellCompilationCommandProviderContract
                    {
                        ProviderId = "generic.command.stream.notice",
                        ProviderVersion = "1.0",
                        FeatureId = "command.write-package-notice-core",
                        Family = PowerShellCompilationCommandFamily.Stream,
                        CommandName = "Write-PackageNoticeCore",
                        Parameters = new[]
                        {
                            new PowerShellCompilationCommandParameterContract { Name = "Message", Position = 0 }
                        },
                        Output = PowerShellCompilationCommandOutput.None,
                        Cardinality = PowerShellCompilationCommandCardinality.None,
                        Stream = "Information",
                        Errors = PowerShellCompilationCommandErrors.None,
                        Adapter = new PowerShellCompilationCommandAdapterContract
                        {
                            Operation = "WriteInformation",
                            SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                            RuntimeFree = true,
                            AotCompatible = true,
                            EntryPoint = new PowerShellCompilationProviderAdapterEntryPoint
                            {
                                AssemblyPath = "lib/net8.0/Generic.Semantic.Provider.dll",
                                TypeName = "Generic.Semantic.Provider.NoticeAdapter",
                                MethodName = "Transform"
                            }
                        }
                    }
                }
            });
        }

        public string PackagePath(string name) => Path.Combine(RootPath, name);

        public PowerShellCompilationProviderResolution BuildPackage(string name)
            => new PowerShellCompilationProviderPackageBuilder().Build(new PowerShellCompilationProviderPackageBuildRequest(
                PackagePath(name),
                Manifest)
            {
                Assemblies = new[]
                {
                    new PowerShellCompilationProviderAssemblyInput(
                        typeof(Generic.Semantic.Provider.NoticeAdapter).Assembly.Location,
                        "lib/net8.0/Generic.Semantic.Provider.dll")
                }
            });

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class ScriptFixture : IDisposable
    {
        private ScriptFixture(string rootPath, string scriptPath, string outputPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            OutputPath = outputPath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }
        public string OutputPath { get; }

        public static ScriptFixture Create(string source)
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeProviderArtifactTests", Guid.NewGuid().ToString("N"));
            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            var script = Path.Combine(root, "input.ps1");
            File.WriteAllText(script, source);
            return new ScriptFixture(root, script, output);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class ArtifactLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;

        internal ArtifactLoadContext(string directory)
            : base("ProviderArtifactProof-" + Guid.NewGuid().ToString("N"), isCollectible: true)
            => _directory = directory;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
