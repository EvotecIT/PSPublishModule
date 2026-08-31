using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.Loader;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationProviderPackageTests
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
        fixture.Manifest.Dependencies = new[]
        {
            new PowerShellCompilationProviderDependency { PackageId = "Generic.Runtime.A", Version = "1.0.0", ContentHash = "sha512-runtime-a" },
            new PowerShellCompilationProviderDependency { PackageId = "Generic.Runtime.Z", Version = "1.0.0", ContentHash = "sha512-runtime-z" },
            new PowerShellCompilationProviderDependency { PackageId = "Generic.Warning.Runtime.A", Version = "1.0.0", ContentHash = "sha512-warning-a" },
            new PowerShellCompilationProviderDependency { PackageId = "Generic.Warning.Runtime.Z", Version = "1.0.0", ContentHash = "sha512-warning-z" }
        };
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

    [Fact]
    public void ReaderRejectsCooperativeCancellationWhenEntrypointOmitsCancellationToken()
    {
        using var fixture = ProviderFixture.Create();
        fixture.Manifest.Providers[0].Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative;

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.BuildPackage("provider.nupkg"));

        Assert.Contains("string Method(string, CancellationToken)", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsSameNamedLocalCancellationTokenType()
    {
        using var fixture = ProviderFixture.Create();
        var provider = Assert.Single(fixture.Manifest.Providers);
        provider.Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative;
        provider.Adapter.EntryPoint!.AssemblyPath = "lib/net8.0/Generic.Semantic.ForgedCancellationProvider.dll";
        provider.Adapter.EntryPoint.TypeName = "Generic.Semantic.ForgedCancellationProvider.ForgedAdapter";
        var forgedAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..",
            "Fixtures",
            "PowerShellCompilationForgedCancellationProviderFixture",
            "bin",
            "Debug",
            "net8.0",
            "Generic.Semantic.ForgedCancellationProvider.dll"));
        Assert.True(File.Exists(forgedAssemblyPath), forgedAssemblyPath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageBuilder().Build(
                new PowerShellCompilationProviderPackageBuildRequest(
                    fixture.PackagePath("forged-cancellation-token.nupkg"),
                    fixture.Manifest)
                {
                    Assemblies = new[]
                    {
                        new PowerShellCompilationProviderAssemblyInput(
                            forgedAssemblyPath,
                            provider.Adapter.EntryPoint.AssemblyPath)
                    }
                }));

        Assert.Contains("string Method(string, CancellationToken)", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsSameNamedCancellationTokenFromNonFrameworkAssemblyReference()
    {
        using var fixture = ProviderFixture.Create();
        var provider = Assert.Single(fixture.Manifest.Providers);
        provider.Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative;
        provider.Adapter.EntryPoint!.AssemblyPath = "lib/net8.0/Generic.Semantic.ForgedCancellationReferenceProvider.dll";
        provider.Adapter.EntryPoint.TypeName = "Generic.Semantic.ForgedCancellationReferenceProvider.ForgedReferenceAdapter";
        var fixturesRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));
        var forgedProviderPath = Path.Combine(
            fixturesRoot,
            "PowerShellCompilationForgedCancellationReferenceProviderFixture",
            "bin",
            "Debug",
            "net8.0",
            "Generic.Semantic.ForgedCancellationReferenceProvider.dll");
        var forgedContractPath = Path.Combine(
            fixturesRoot,
            "PowerShellCompilationForgedCancellationContractFixture",
            "bin",
            "Debug",
            "net8.0",
            "Generic.Semantic.ForgedCancellationContract.dll");
        Assert.True(File.Exists(forgedProviderPath), forgedProviderPath);
        Assert.True(File.Exists(forgedContractPath), forgedContractPath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationProviderPackageBuilder().Build(
                new PowerShellCompilationProviderPackageBuildRequest(
                    fixture.PackagePath("forged-cancellation-reference.nupkg"),
                    fixture.Manifest)
                {
                    Assemblies = new[]
                    {
                        new PowerShellCompilationProviderAssemblyInput(
                            forgedProviderPath,
                            provider.Adapter.EntryPoint.AssemblyPath),
                        new PowerShellCompilationProviderAssemblyInput(
                            forgedContractPath,
                            "lib/net8.0/Generic.Semantic.ForgedCancellationContract.dll")
                    }
                }));

        Assert.Contains("string Method(string, CancellationToken)", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderRejectsExecutableResultTypeThatConflictsWithAssemblyMetadata()
    {
        using var fixture = ProviderFixture.Create();
        fixture.Manifest.Providers = new[]
        {
            Provider(
                "generic.command.output.mismatched-type",
                "Write-PackageMismatchedTypeCore",
                "Success",
                "Transform",
                PowerShellCompilationCommandOutput.Projected,
                PowerShellCompilationCommandCardinality.Scalar,
                resultType: PowerShellCompilationProviderValueType.Int32)
        };

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.BuildPackage("mismatched-type.nupkg"));

        Assert.Contains("int Method(string)", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuilderPreservesExistingPackageWhenReplacementFailsValidation()
    {
        using var fixture = ProviderFixture.Create();
        var packagePath = fixture.PackagePath("replace.nupkg");
        var valid = fixture.BuildPackage("replace.nupkg");
        var validHash = Hash(packagePath);
        var provider = Assert.Single(fixture.Manifest.Providers);
        provider.Stream = "Success";
        provider.Output = PowerShellCompilationCommandOutput.Projected;
        provider.Cardinality = PowerShellCompilationCommandCardinality.Scalar;
        provider.Adapter.Operation = "WriteOutput";
        provider.Adapter.EntryPoint!.ResultType = PowerShellCompilationProviderValueType.Int32;

        Assert.Throws<InvalidOperationException>(() => fixture.BuildPackage("replace.nupkg"));

        Assert.Equal(validHash, Hash(packagePath));
        var preserved = new PowerShellCompilationProviderPackageReader().Resolve(
            new[] { new PowerShellCompilationProviderPackageReference(packagePath) });
        Assert.Equal(valid.Lock.LockSha256, preserved.Lock.LockSha256);
    }

    [Fact]
    public void ReaderRejectsStaticAbstractInterfaceEntryPoint()
    {
        using var fixture = ProviderFixture.Create();
        var provider = Assert.Single(fixture.Manifest.Providers);
        provider.Adapter.EntryPoint!.TypeName = "Generic.Semantic.Provider.AbstractAdapter";

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.BuildPackage("abstract-entrypoint.nupkg"));

        Assert.Contains("public static non-generic string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderAppliesCanonicalContractValidationToManuallyAuthoredPackages()
    {
        using var fixture = ProviderFixture.Create();
        var packagePath = fixture.PackagePath("forged-contract.nupkg");
        fixture.BuildPackage("forged-contract.nupkg");
        RewriteJsonEntry<PowerShellCompilationProviderPackageManifest>(
            packagePath,
            PowerShellCompilationProviderPackageReader.ManifestPath,
            manifest =>
            {
                var provider = Assert.Single(manifest.Providers);
                provider.Stream = "Success";
                provider.Output = PowerShellCompilationCommandOutput.None;
                provider.Cardinality = PowerShellCompilationCommandCardinality.None;
            });

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationProviderPackageReader().Resolve(
            new[] { new PowerShellCompilationProviderPackageReference(packagePath) }));

        Assert.Contains("Success-stream", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReaderReconcilesExactNuGetDependencyIdentityAndVersion()
    {
        using var fixture = ProviderFixture.Create();
        fixture.Manifest.Dependencies = new[]
        {
            new PowerShellCompilationProviderDependency
            {
                PackageId = "Generic.Runtime.Dependency",
                Version = "1.0.0",
                ContentHash = "sha512-reviewed-content-identity"
            }
        };
        var packagePath = fixture.PackagePath("forged-dependency.nupkg");
        fixture.BuildPackage("forged-dependency.nupkg");
        RewriteTextEntry(
            packagePath,
            "Generic.Semantic.Provider.nuspec",
            content => content.Replace(
                "id=\"Generic.Runtime.Dependency\" version=\"[1.0.0]\"",
                "id=\"Generic.Runtime.Dependency\" version=\"[1.0.1]\"",
                StringComparison.Ordinal));

        var exception = Assert.Throws<InvalidOperationException>(() => new PowerShellCompilationProviderPackageReader().Resolve(
            new[] { new PowerShellCompilationProviderPackageReference(packagePath) }));

        Assert.Contains("dependency identities or exact versions", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private static void RewriteJsonEntry<T>(string packagePath, string entryPath, Action<T> update)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        RewriteTextEntry(packagePath, entryPath, content =>
        {
            var value = JsonSerializer.Deserialize<T>(content, options)
                ?? throw new InvalidDataException($"Archive entry '{entryPath}' was empty.");
            update(value);
            return JsonSerializer.Serialize(value, options);
        });
    }

    private static void RewriteTextEntry(string packagePath, string entryPath, Func<string, string> update)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        var entry = archive.GetEntry(entryPath) ?? throw new InvalidDataException($"Archive entry '{entryPath}' was absent.");
        string content;
        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false))
            content = reader.ReadToEnd();
        entry.Delete();
        var replacement = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        replacement.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false));
        writer.Write(update(content));
    }

    private static PowerShellCompilationCommandProviderContract Provider(
        string providerId,
        string commandName,
        string stream,
        string methodName,
        PowerShellCompilationCommandOutput output = PowerShellCompilationCommandOutput.None,
        PowerShellCompilationCommandCardinality cardinality = PowerShellCompilationCommandCardinality.None,
        PowerShellCompilationCommandErrors errors = PowerShellCompilationCommandErrors.None,
        PowerShellCompilationProviderValueType resultType = PowerShellCompilationProviderValueType.String)
        => new()
        {
            ProviderId = providerId,
            ProviderVersion = "1.0",
            FeatureId = "provider-matrix-" + providerId,
            Family = PowerShellCompilationCommandFamily.Stream,
            CommandName = commandName,
            Parameters = new[] { new PowerShellCompilationCommandParameterContract { Name = "Value", Position = 0 } },
            Output = output,
            Cardinality = cardinality,
            Stream = stream,
            Errors = errors,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = stream == "Success" ? "WriteOutput" : "Write" + stream,
                SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" +
                                  PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                RuntimeFree = true,
                AotCompatible = true,
                EntryPoint = new PowerShellCompilationProviderAdapterEntryPoint
                {
                    AssemblyPath = "lib/net8.0/Generic.Semantic.Provider.dll",
                    TypeName = "Generic.Semantic.Provider.NoticeAdapter",
                    MethodName = methodName,
                    ResultType = resultType
                }
            }
        };

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
