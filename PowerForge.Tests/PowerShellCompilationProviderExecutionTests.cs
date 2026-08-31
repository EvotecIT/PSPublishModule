using System.Reflection;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed partial class PowerShellCompilationProviderPackageTests
{
    [Fact]
    public void CooperativeProviderRejectsCompilerTokenParameterCollision()
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = Assert.Single(providerFixture.Manifest.Providers);
        provider.CommandName = "Invoke-PackageCancellationCore";
        provider.Adapter.EntryPoint!.MethodName = "WaitForCancellation";
        provider.Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative;
        using var artifactFixture = ScriptFixture.Create(
            "function Invoke-Collision { param([string] $__providerCancellationToken) " +
            "Invoke-PackageCancellationCore 'value' }");

        var typed = new PowerShellTypedCompilationTranspiler(providerFixture.Manifest.Providers).Transpile(
            artifactFixture.ScriptPath,
            "PowerForge.Compiled",
            "CancellationCollisionMethods",
            "net8.0");

        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, static diagnostic =>
            diagnostic.FeatureId.Equals("PSL1009", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("__providerCancellationToken", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("_providerCancellation")]
    [InlineData("_providerCancellationGate")]
    [InlineData("_providerCancellationActiveCancels")]
    [InlineData("_providerCancellationDisposed")]
    [InlineData("DisposeProviderCancellation")]
    [InlineData("Dispose")]
    public void BinaryCmdletRejectsCooperativeCancellationMemberCollisions(string parameterName)
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = Assert.Single(providerFixture.Manifest.Providers);
        provider.CommandName = "Invoke-PackageCancellationCore";
        provider.Adapter.EntryPoint!.MethodName = "WaitForCancellation";
        provider.Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative;
        using var artifactFixture = ScriptFixture.Create(
            $"function Invoke-Collision {{ [CmdletBinding()] param([string] ${parameterName}) " +
            "Invoke-PackageCancellationCore 'value' }");
        var typed = new PowerShellTypedCompilationTranspiler(providerFixture.Manifest.Providers)
            .TranspileForBinaryModule(
                new[] { artifactFixture.ScriptPath },
                "PowerForge.Compiled",
                "CancellationMemberCollisionMethods",
                "net8.0");

        var prepared = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(
            typed,
            new[] { "Invoke-Collision" },
            "net8.0");

        Assert.Empty(prepared.Methods);
        Assert.Contains(prepared.Diagnostics, diagnostic =>
            diagnostic.FeatureId == "binary-module.cmdlet-shape" &&
            diagnostic.Message.Contains(parameterName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BinaryCmdletRoutesStopProcessingToCooperativeProviderToken()
    {
        using var providerFixture = ProviderFixture.Create();
        var provider = Assert.Single(providerFixture.Manifest.Providers);
        provider.CommandName = "Invoke-PackageCancellationCore";
        provider.Adapter.EntryPoint!.MethodName = "WaitForCancellation";
        provider.Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative;
        var cancellationPath = Path.Combine(providerFixture.RootPath, "binary-cancellation.started");
        using var artifactFixture = ScriptFixture.Create(
            "function Invoke-PackageCancellationCoreWrapper { [CmdletBinding()] param() Invoke-PackageCancellationCore '" +
            cancellationPath.Replace("'", "''", StringComparison.Ordinal) + "' }; " +
            "function Invoke-PackageCancellation { [CmdletBinding()] param() Invoke-PackageCancellationCoreWrapper }");
        var typed = new PowerShellTypedCompilationTranspiler(providerFixture.Manifest.Providers)
            .TranspileForBinaryModule(
                new[] { artifactFixture.ScriptPath },
                "PowerForge.Compiled",
                "CooperativeProviderMethods",
                "net8.0");

        Assert.Empty(typed.Diagnostics);
        var wrapper = Assert.Single(typed.Methods, static method =>
            method.SourceName == "Invoke-PackageCancellation");
        Assert.True(wrapper.RequiresProviderCancellation);
        var abiMethod = Assert.Single(
            PowerShellCompilationAbiBuilder.Create(typed.NamespaceName, typed.TypeName, new[] { wrapper }).Methods);
        Assert.Contains(abiMethod.Parameters, static parameter =>
            parameter.ClrName == "__providerCancellationToken" &&
            parameter.TypeName == "System.Threading.CancellationToken" &&
            parameter.CompilerPurpose == "ProviderCancellation");
        Assert.Contains(abiMethod.Parameters, static parameter =>
            parameter.ClrName == "__writeHost" && parameter.CompilerPurpose == "HostStream");
        var source = PowerShellBinaryCmdletSourceGenerator.Generate(
            typed,
            new[] { "Invoke-PackageCancellation" },
            "net8.0");

        Assert.Contains(
            "private readonly global::System.Threading.CancellationTokenSource _providerCancellation = new();",
            source,
            StringComparison.Ordinal);
        Assert.Contains("protected override void StopProcessing()", source, StringComparison.Ordinal);
        Assert.Contains("_providerCancellation.Cancel();", source, StringComparison.Ordinal);
        Assert.Contains("_providerCancellation.Token", source, StringComparison.Ordinal);
        Assert.Contains("public void Dispose() => DisposeProviderCancellation();", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("DisposeProviderCancellation();", source, StringComparison.Ordinal);

        var packagePath = providerFixture.PackagePath("cooperative-provider.nupkg");
        var resolution = providerFixture.BuildPackage("cooperative-provider.nupkg");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            artifactFixture.ScriptPath,
            artifactFixture.OutputPath,
            "CooperativeProviderBinaryModule",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
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
        var moduleAssemblyPath = Directory.EnumerateFiles(
            artifactFixture.OutputPath,
            "CooperativeProviderBinaryModule.dll",
            SearchOption.AllDirectories).Single();
        var moduleAssembly = Assembly.LoadFrom(moduleAssemblyPath);
        var commandType = moduleAssembly.GetTypes().Single(static type =>
            type.Name == "InvokePackageCancellationCommand");
        var command = Activator.CreateInstance(commandType)!;
        var cancellationSource = Assert.IsType<CancellationTokenSource>(commandType
            .GetField("_providerCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(command));
        var stopProcessing = commandType.GetMethod("StopProcessing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dispose = commandType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public)!;
        using var callbackEntered = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var registration = cancellationSource.Token.Register(() =>
        {
            callbackEntered.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(5));
        });
        var firstStop = Task.Run(() => Record.Exception(() => stopProcessing.Invoke(command, null)));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)), "First cancellation callback did not start.");
        var secondStop = Task.Run(() => Record.Exception(() => stopProcessing.Invoke(command, null)));
        var secondStopResult = await secondStop.WaitAsync(TimeSpan.FromSeconds(5));
        var disposeTask = Task.Run(() => Record.Exception(() => dispose.Invoke(command, null)));
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.False(disposeTask.IsCompleted, "Cancellation source disposed while a cancellation callback was active.");
        releaseCallback.Set();
        var firstStopResult = await firstStop.WaitAsync(TimeSpan.FromSeconds(5));
        var disposeResult = await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(firstStopResult);
        Assert.Null(secondStopResult);
        Assert.Null(disposeResult);

        using var shell = System.Management.Automation.PowerShell.Create();
        shell.AddCommand("Import-Module")
            .AddParameter("Name", result.ArtifactPath!)
            .AddParameter("Force");
        _ = shell.Invoke();
        Assert.False(shell.HadErrors, string.Join(Environment.NewLine, shell.Streams.Error.Select(static error => error.ToString())));
        shell.Commands.Clear();
        shell.Streams.ClearStreams();
        shell.AddCommand("Invoke-PackageCancellation");
        var invocation = shell.BeginInvoke();
        Assert.True(SpinWait.SpinUntil(() => File.Exists(cancellationPath), TimeSpan.FromSeconds(5)));
        var stopDuration = System.Diagnostics.Stopwatch.StartNew();
        shell.Stop();
        stopDuration.Stop();
        Assert.True(stopDuration.Elapsed < TimeSpan.FromSeconds(5), $"Generated cmdlet stop took {stopDuration.Elapsed}.");
        Assert.True(invocation.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)), "Generated cmdlet did not stop promptly.");
        Assert.Contains(shell.InvocationStateInfo.State, new[]
        {
            System.Management.Automation.PSInvocationState.Stopped,
            System.Management.Automation.PSInvocationState.Failed
        });
        using (new FileStream(cancellationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        File.Delete(cancellationPath);
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

    [Theory]
    [InlineData(PowerShellCompilationMode.Strict)]
    [InlineData(PowerShellCompilationMode.Hybrid)]
    public async Task ExecutableProviderMatrixRoutesValuesCardinalityStreamsAndErrors(PowerShellCompilationMode mode)
    {
        using var providerFixture = ProviderFixture.Create();
        providerFixture.Manifest.Providers = new[]
        {
            Provider("generic.command.output.scalar", "Write-PackageOutputCore", "Success", "Transform",
                PowerShellCompilationCommandOutput.Projected, PowerShellCompilationCommandCardinality.Scalar),
            Provider("generic.command.output.collection", "Write-PackageOutputManyCore", "Success", "TransformMany",
                PowerShellCompilationCommandOutput.Enumerated, PowerShellCompilationCommandCardinality.Collection),
            Provider("generic.command.output.int32", "Write-PackageInt32Core", "Success", "ParseInt32",
                PowerShellCompilationCommandOutput.Projected, PowerShellCompilationCommandCardinality.Scalar,
                resultType: PowerShellCompilationProviderValueType.Int32),
            Provider("generic.command.output.int32-collection", "Write-PackageInt32ManyCore", "Success", "ParseInt32Many",
                PowerShellCompilationCommandOutput.Enumerated, PowerShellCompilationCommandCardinality.Collection,
                resultType: PowerShellCompilationProviderValueType.Int32),
            Provider("generic.command.output.boolean", "Write-PackageBooleanCore", "Success", "ParseBoolean",
                PowerShellCompilationCommandOutput.Projected, PowerShellCompilationCommandCardinality.Scalar,
                resultType: PowerShellCompilationProviderValueType.Boolean),
            Provider("generic.command.output.int64", "Write-PackageInt64Core", "Success", "ParseInt64",
                PowerShellCompilationCommandOutput.Projected, PowerShellCompilationCommandCardinality.Scalar,
                resultType: PowerShellCompilationProviderValueType.Int64),
            Provider("generic.command.output.double", "Write-PackageDoubleCore", "Success", "ParseDouble",
                PowerShellCompilationCommandOutput.Projected, PowerShellCompilationCommandCardinality.Scalar,
                resultType: PowerShellCompilationProviderValueType.Double),
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
            Provider("generic.command.keyword-entrypoint", "Write-PackageKeywordCore", "Information", "new"),
            Provider("generic.command.output.cancellation", "Invoke-PackageCancellationCore", "Success", "WaitForCancellation",
                PowerShellCompilationCommandOutput.Projected, PowerShellCompilationCommandCardinality.Scalar,
                errors: PowerShellCompilationCommandErrors.Terminating),
            Provider("generic.command.cleanup.file", "Invoke-PackageCleanupCore", "Information", "UseFileAndRelease",
                errors: PowerShellCompilationCommandErrors.Terminating),
            Provider("generic.command.cleanup.file.failure", "Invoke-PackageCleanupFailureCore", "Information", "UseFileAndFail",
                errors: PowerShellCompilationCommandErrors.Terminating)
        };
        providerFixture.Manifest.Providers.Single(static provider =>
            provider.ProviderId == "generic.command.keyword-entrypoint").Adapter.EntryPoint!.TypeName = "Generic.Semantic.Provider.class";
        providerFixture.Manifest.Providers.Single(static provider =>
            provider.ProviderId == "generic.command.output.cancellation").Adapter.Cancellation = PowerShellCompilationProviderCancellation.Cooperative;
        providerFixture.Manifest.Providers.Single(static provider =>
            provider.ProviderId == "generic.command.cleanup.file").Adapter.Cleanup = PowerShellCompilationProviderCleanup.Deterministic;
        providerFixture.Manifest.Providers.Single(static provider =>
            provider.ProviderId == "generic.command.cleanup.file.failure").Adapter.Cleanup = PowerShellCompilationProviderCleanup.Deterministic;
        var packagePath = providerFixture.PackagePath("matrix.nupkg");
        var resolution = providerFixture.BuildPackage("matrix.nupkg");
        var cleanupPath = Path.Combine(providerFixture.RootPath, "owned-resource.bin");
        var cancellationPath = Path.Combine(providerFixture.RootPath, "cancellation.started");
        using var artifactFixture = ScriptFixture.Create("""
function Invoke-ProviderMatrix {
    Write-PackageOutputCore 'value'
    Write-PackageOutputManyCore 'value'
    Write-PackageInt32Core '42'
    Write-PackageInt32ManyCore '7'
    Write-PackageBooleanCore 'true'
    Write-PackageInt64Core '9007199254740991'
    Write-PackageDoubleCore '3.5'
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
function Invoke-ProviderCancellation {
    Invoke-PackageCancellationCore '{{CANCELLATION_PATH}}'
}
function Invoke-ProviderCancellationWrapper {
    Invoke-ProviderCancellation
}
function Invoke-ProviderCleanup {
    Invoke-PackageCleanupCore '{{CLEANUP_PATH}}'
}
function Invoke-ProviderCleanupFailure {
    Invoke-PackageCleanupFailureCore '{{CLEANUP_FAILURE_PATH}}'
}
""".Replace("{{CLEANUP_PATH}}", cleanupPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
    .Replace("{{CLEANUP_FAILURE_PATH}}", (cleanupPath + ".failure").Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal)
    .Replace("{{CANCELLATION_PATH}}", cancellationPath.Replace("'", "''", StringComparison.Ordinal), StringComparison.Ordinal));
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

            Assert.Equal(
                new object?[]
                {
                    "provider:value", "provider:first:value", "provider:second:value",
                    42, 7, 8, true, 9007199254740991L, 3.5d
                },
                output);
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

            using var cancellation = new CancellationTokenSource();
            var cancellationInvocation = Task.Run(() => Assert.Throws<TargetInvocationException>(() =>
                type.GetMethod("Invoke_ProviderCancellationWrapper", BindingFlags.Public | BindingFlags.Static)!.Invoke(
                    null,
                    EmptySinks().Concat(new object[] { cancellation.Token }).ToArray())));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(cancellationPath), TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            var cancellationFailure = await cancellationInvocation;
            Assert.IsType<OperationCanceledException>(cancellationFailure.InnerException);
            using (new FileStream(cancellationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(cancellationPath);

            var cleanupInformation = new List<string>();
            var cleanupSinks = EmptySinks();
            cleanupSinks[4] = (Action<string>)(cleanupInformation.Add);
            type.GetMethod("Invoke_ProviderCleanup", BindingFlags.Public | BindingFlags.Static)!.Invoke(
                null,
                cleanupSinks);
            Assert.Equal(new[] { "released:" + cleanupPath }, cleanupInformation);
            using (new FileStream(cleanupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(cleanupPath);
            Assert.False(File.Exists(cleanupPath));

            var cleanupFailurePath = cleanupPath + ".failure";
            var cleanupFailure = Assert.Throws<TargetInvocationException>(() =>
                type.GetMethod("Invoke_ProviderCleanupFailure", BindingFlags.Public | BindingFlags.Static)!.Invoke(
                    null,
                    EmptySinks()));
            Assert.IsType<InvalidOperationException>(cleanupFailure.InnerException);
            Assert.Contains("provider-cleanup-failure:", cleanupFailure.InnerException!.Message, StringComparison.Ordinal);
            using (new FileStream(cleanupFailurePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(cleanupFailurePath);
            Assert.False(File.Exists(cleanupFailurePath));
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
