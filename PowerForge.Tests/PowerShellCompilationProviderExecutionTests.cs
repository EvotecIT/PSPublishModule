using System.Reflection;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationProviderPackageTests
{
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

    [Theory]
    [InlineData(PowerShellCompilationMode.Strict)]
    [InlineData(PowerShellCompilationMode.Hybrid)]
    public void ExecutableProviderMatrixRoutesValuesCardinalityStreamsAndErrors(PowerShellCompilationMode mode)
    {
        using var providerFixture = ProviderFixture.Create();
        providerFixture.Manifest.Providers = new[]
        {
            Provider("generic.command.output.scalar", "Write-PackageOutputCore", "Success", "Transform",
                PowerShellCompilationCommandOutput.Projected, PowerShellCompilationCommandCardinality.Scalar),
            Provider("generic.command.output.collection", "Write-PackageOutputManyCore", "Success", "TransformMany",
                PowerShellCompilationCommandOutput.Enumerated, PowerShellCompilationCommandCardinality.Collection),
            Provider("generic.command.stream.verbose", "Write-PackageVerboseCore", "Verbose", "Transform"),
            Provider("generic.command.stream.debug", "Write-PackageDebugCore", "Debug", "Transform"),
            Provider("generic.command.stream.warning", "Write-PackageWarningCore", "Warning", "Transform"),
            Provider("generic.command.stream.information", "Write-PackageInformationCore", "Information", "Transform"),
            Provider("generic.command.stream.host", "Write-PackageHostCore", "Host", "Transform"),
            Provider("generic.command.stream.error", "Write-PackageErrorCore", "Error", "Transform",
                errors: PowerShellCompilationCommandErrors.NonTerminating),
            Provider("generic.command.error.terminating", "Invoke-PackageFailureCore", "Error", "Fail",
                errors: PowerShellCompilationCommandErrors.Terminating),
            Provider("generic.command.value.null", "Invoke-PackageNullCore", "Information", "ReturnNull",
                errors: PowerShellCompilationCommandErrors.Terminating),
            Provider("generic.command.output.null-collection", "Invoke-PackageNullCollectionCore", "Success", "ReturnNullMany",
                PowerShellCompilationCommandOutput.Enumerated, PowerShellCompilationCommandCardinality.Collection,
                PowerShellCompilationCommandErrors.Terminating),
            Provider("generic.command.output.null-item", "Invoke-PackageNullItemCore", "Success", "ReturnNullItem",
                PowerShellCompilationCommandOutput.Enumerated, PowerShellCompilationCommandCardinality.Collection,
                PowerShellCompilationCommandErrors.Terminating),
            Provider("generic.command.keyword-entrypoint", "Write-PackageKeywordCore", "Information", "new")
        };
        providerFixture.Manifest.Providers[^1].Adapter.EntryPoint!.TypeName = "Generic.Semantic.Provider.class";
        var packagePath = providerFixture.PackagePath("matrix.nupkg");
        var resolution = providerFixture.BuildPackage("matrix.nupkg");
        using var artifactFixture = ScriptFixture.Create("""
function Invoke-ProviderMatrix {
    Write-PackageOutputCore 'value'
    Write-PackageOutputManyCore 'value'
    Write-PackageVerboseCore 'verbose'
    Write-PackageDebugCore 'debug'
    Write-PackageWarningCore 'warning'
    Write-PackageInformationCore 'information'
    Write-PackageHostCore 'host'
    Write-PackageErrorCore 'error'
}
function Invoke-ProviderFailure {
    Invoke-PackageFailureCore 'broken'
}
function Invoke-ProviderNull {
    Invoke-PackageNullCore 'broken'
}
function Invoke-ProviderNullCollection {
    Invoke-PackageNullCollectionCore 'broken'
}
function Invoke-ProviderNullItem {
    Invoke-PackageNullItemCore 'broken'
}
function Invoke-ProviderKeyword {
    Write-PackageKeywordCore 'escaped'
}
""");
        var providerIds = providerFixture.Manifest.Providers.Select(static provider => provider.ProviderId).ToArray();
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProviderMatrix" + mode,
            PowerShellCompilationArtifactKind.Library,
            mode,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = new PowerShellCompilationProviderTrustPolicy
            {
                AllowedPackageIds = new[] { "Generic.Semantic.Provider" },
                AllowedProviderIds = providerIds,
                AllowedPublishers = new[] { "Generic Publisher" },
                AllowedLicenseExpressions = new[] { "MIT" }
            }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(providerIds.OrderBy(static id => id, StringComparer.Ordinal),
            result.Manifest!.CommandProviders.Select(static provider => provider.ProviderId).OrderBy(static id => id, StringComparer.Ordinal));
        var loadContext = new ArtifactLoadContext(Path.GetDirectoryName(result.ArtifactPath!)!);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(result.ArtifactPath!);
            var type = assembly.GetType("PowerForge.Compiled.ProviderMatrix" + mode + "Methods", throwOnError: true)!;
            var output = new List<object?>();
            var verbose = new List<string>();
            var debug = new List<string>();
            var warning = new List<string>();
            var information = new List<string>();
            var host = new List<string>();
            var error = new List<string>();
            type.GetMethod("Invoke_ProviderMatrix", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[]
            {
                (Action<object?>)(output.Add),
                (Action<string>)(verbose.Add),
                (Action<string>)(debug.Add),
                (Action<string>)(warning.Add),
                (Action<string>)(information.Add),
                (Action<string>)(host.Add),
                (Action<string>)(error.Add)
            });

            Assert.Equal(new object?[] { "provider:value", "provider:first:value", "provider:second:value" }, output);
            Assert.Equal(new[] { "provider:verbose" }, verbose);
            Assert.Equal(new[] { "provider:debug" }, debug);
            Assert.Equal(new[] { "provider:warning" }, warning);
            Assert.Equal(new[] { "provider:information" }, information);
            Assert.Equal(new[] { "provider:host" }, host);
            Assert.Equal(new[] { "provider:error" }, error);

            var failure = Assert.Throws<TargetInvocationException>(() =>
                type.GetMethod("Invoke_ProviderFailure", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[]
                {
                    (Action<object?>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { })
                }));
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Contains("provider-failure:broken", failure.InnerException!.Message, StringComparison.Ordinal);

            var nullResult = Assert.Throws<TargetInvocationException>(() =>
                type.GetMethod("Invoke_ProviderNull", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[]
                {
                    (Action<object?>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { })
                }));
            Assert.IsType<InvalidOperationException>(nullResult.InnerException);
            Assert.Contains("outside its contract", nullResult.InnerException!.Message, StringComparison.Ordinal);

            var nullCollection = Assert.Throws<TargetInvocationException>(() =>
                type.GetMethod("Invoke_ProviderNullCollection", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, EmptySinks()));
            Assert.IsType<InvalidOperationException>(nullCollection.InnerException);
            Assert.Contains("outside its contract", nullCollection.InnerException!.Message, StringComparison.Ordinal);

            var nullItem = Assert.Throws<TargetInvocationException>(() =>
                type.GetMethod("Invoke_ProviderNullItem", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, EmptySinks()));
            Assert.IsType<InvalidOperationException>(nullItem.InnerException);
            Assert.Contains("outside its contract", nullItem.InnerException!.Message, StringComparison.Ordinal);

            var keywordInformation = new List<string>();
            var keywordSinks = EmptySinks();
            keywordSinks[4] = (Action<string>)(keywordInformation.Add);
            type.GetMethod("Invoke_ProviderKeyword", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, keywordSinks);
            Assert.Equal(new[] { "keyword:escaped" }, keywordInformation);
        }
        finally
        {
            loadContext.Unload();
        }

        static object[] EmptySinks() => new object[]
        {
            (Action<object?>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { }),
            (Action<string>)(_ => { })
        };
    }

    [Theory]
    [InlineData(PowerShellCompilationMode.Strict)]
    [InlineData(PowerShellCompilationMode.Hybrid)]
    public void ExecutableProviderCarriesAndInvokesItsLockedManagedDependencyClosure(PowerShellCompilationMode mode)
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = Provider(
            "generic.command.stream.dependency",
            "Write-PackageDependencyCore",
            "Information",
            "Transform");
        provider.Adapter.Dependencies = new[] { "Generic.Semantic.Provider.Dependency" };
        provider.Adapter.EntryPoint!.AssemblyPath = "lib/net8.0/Generic.Semantic.Provider.WithDependency.dll";
        provider.Adapter.EntryPoint.TypeName = "Generic.Semantic.Provider.DependencyAdapter";
        providerFixture.Manifest.Providers = new[] { provider };
        var runtimeAssembly = typeof(Generic.Semantic.Provider.DependencyAdapter).Assembly.Location;
        var dependencyAssembly = Path.Combine(
            Path.GetDirectoryName(runtimeAssembly)!,
            "Generic.Semantic.Provider.Dependency.dll");
        Assert.True(File.Exists(dependencyAssembly), dependencyAssembly);
        var packagePath = providerFixture.PackagePath("dependency.nupkg");
        var resolution = new PowerShellCompilationProviderPackageBuilder().Build(
            new PowerShellCompilationProviderPackageBuildRequest(packagePath, providerFixture.Manifest)
            {
                Assemblies = new[]
                {
                    new PowerShellCompilationProviderAssemblyInput(runtimeAssembly, "lib/net8.0/Generic.Semantic.Provider.WithDependency.dll"),
                    new PowerShellCompilationProviderAssemblyInput(dependencyAssembly, "lib/net8.0/Generic.Semantic.Provider.Dependency.dll")
                }
            });
        using var artifactFixture = ScriptFixture.Create(
            "function Write-PackageDependency { Write-PackageDependencyCore 'locked' }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "ProviderDependencyClosure" + mode,
            PowerShellCompilationArtifactKind.Library,
            mode,
            allowUnreviewedDependencyResolution: true)
        {
            ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(packagePath) },
            ExpectedProviderLock = resolution.Lock,
            ProviderTrustPolicy = new PowerShellCompilationProviderTrustPolicy
            {
                AllowedPackageIds = new[] { "Generic.Semantic.Provider" },
                AllowedProviderIds = new[] { provider.ProviderId },
                AllowedPublishers = new[] { "Generic Publisher" },
                AllowedLicenseExpressions = new[] { "MIT" }
            }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.Files.Count(static file => file.Role == "CompilerProviderRuntime"));
        Assert.Equal(2, Assert.Single(result.Manifest.ProviderLock!.Packages).Assemblies.Length);
        var loadContext = new ArtifactLoadContext(Path.GetDirectoryName(result.ArtifactPath!)!);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(result.ArtifactPath!);
            var information = new List<string>();
            assembly.GetType("PowerForge.Compiled.ProviderDependencyClosure" + mode + "Methods", throwOnError: true)!
                .GetMethod("Write_PackageDependency", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[]
                {
                    (Action<object?>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { }),
                    (Action<string>)(information.Add),
                    (Action<string>)(_ => { }),
                    (Action<string>)(_ => { })
                });
            Assert.Equal(new[] { "dependency:locked" }, information);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
