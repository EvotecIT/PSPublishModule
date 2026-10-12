using System.Runtime.InteropServices;
using System.Security;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class PowerShellCompilationSdkProcessEnvironmentTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void AuthenticatedSdkProducerPreservesCacheLockingAndExcludesAmbientBuildCode()
    {
        using var workspace = PowerShellCompilationWorkspace.Create(keep: false);
        var sdk = PowerShellCompilationToolchainFingerprint.ResolveSelectedSdk();
        var packageRoot = Path.Combine(workspace.Path, "packages");
        var scratch = Path.Combine(workspace.Path, "shared-locks");
        var http = Path.Combine(workspace.Path, "http-cache");
        using var environment = new ScopedEnvironment(new Dictionary<string, string?>
        {
            ["NUGET_SCRATCH"] = scratch,
            ["NUGET_HTTP_CACHE_PATH"] = http,
            ["HTTPS_PROXY"] = "http://127.0.0.1:9",
            ["BuildDependsOn"] = "CallerOwnedTarget",
            ["ComSpec"] = "caller-owned-shell",
            ["MSBuildSDKsPath"] = "caller-owned-sdk",
            ["DOTNET_STARTUP_HOOKS"] = "caller-owned-hook"
        });

        var request = PowerShellCompilationSdkProcessEnvironment.CreateRequest(
            sdk, workspace.Path, new[] { "publish", "generated.csproj" }, TimeSpan.FromSeconds(30), packageRoot);

        Assert.False(request.InheritEnvironment);
        Assert.Contains("-noAutoResponse", request.Arguments);
        Assert.True(Path.IsPathFullyQualified(request.FileName));
        var dotnetRoot = Directory.GetParent(Directory.GetParent(sdk.RootPath)!.FullName)!.FullName;
        Assert.Equal(dotnetRoot, Path.GetDirectoryName(request.FileName));
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string?>>(request.EnvironmentVariables);
        Assert.Equal(dotnetRoot, values["DOTNET_ROOT"]);
        Assert.Equal(request.FileName, values["DOTNET_HOST_PATH"]);
        Assert.Equal(dotnetRoot, values["PATH"]!.Split(Path.PathSeparator)[0]);
        Assert.Equal(packageRoot, values["NUGET_PACKAGES"]);
        Assert.Equal(scratch, values["NUGET_SCRATCH"]);
        Assert.Equal(http, values["NUGET_HTTP_CACHE_PATH"]);
        Assert.Equal("http://127.0.0.1:9", values["HTTPS_PROXY"]);
        Assert.StartsWith(workspace.Path, values["HOME"], StringComparison.Ordinal);
        Assert.StartsWith(workspace.Path, values["TEMP"], StringComparison.Ordinal);
        Assert.Equal(values["HOME"], values["DOTNET_CLI_HOME"]);
        Assert.Equal("false", values["DOTNET_GENERATE_ASPNET_CERTIFICATE"]);
        Assert.Equal("false", values["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"]);
        Assert.Equal("false", values["UseSharedCompilation"]);
        Assert.Equal("false", values["MSBuildEnableWorkloadResolver"]);
        Assert.Equal("false", values["ImportUserLocationsByWildcardBeforeMicrosoftCommonProps"]);
        Assert.Equal("false", values["ImportUserLocationsByWildcardAfterMicrosoftCommonTargets"]);
        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), values["ComSpec"]);
        else
            Assert.DoesNotContain("ComSpec", values.Keys);
        Assert.DoesNotContain("BuildDependsOn", values.Keys);
        Assert.DoesNotContain("MSBuildSDKsPath", values.Keys);
        Assert.DoesNotContain("DOTNET_STARTUP_HOOKS", values.Keys);
    }

    [WindowsOrLinuxX64SdkEnvironmentFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task AuthenticatedTrimmedProducerIgnoresInheritedSdkTargetsAndRuntimeInjection()
    {
        using var fixture = PowerShellCompilationWorkspace.Create(keep: false);
        var rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        await PowerShellCompilationTestPrerequisites.AcquireRuntimePackAsync("net10.0", rid);
        var sdk = PowerShellCompilationToolchainFingerprint.ResolveSelectedSdk();
        var callerSdk = Directory.CreateDirectory(Path.Combine(fixture.Path, "caller-sdks", "Microsoft.NET.Sdk", "Sdk")).FullName;
        var sentinel = Path.Combine(fixture.Path, "caller-imported.txt");
        var callerTarget = Path.Combine(fixture.Path, "caller.targets");
        File.WriteAllText(callerTarget,
            "<Project><Target Name=\"CallerOwnedTarget\" BeforeTargets=\"Restore;Build;Publish\">" +
            $"<WriteLinesToFile File=\"{SecurityElement.Escape(sentinel)}\" Lines=\"Caller build code imported\" Overwrite=\"true\" />" +
            "</Target></Project>");
        foreach (var name in new[] { "Sdk.props", "Sdk.targets" })
        {
            var sdkFile = Path.Combine(sdk.RootPath, "Sdks", "Microsoft.NET.Sdk", "Sdk", name);
            File.WriteAllText(Path.Combine(callerSdk, name),
                $"<Project><Import Project=\"{SecurityElement.Escape(sdkFile)}\" />" +
                $"<Import Project=\"{SecurityElement.Escape(callerTarget)}\" /></Project>");
        }
        var source = Path.Combine(fixture.Path, "main.ps1");
        File.WriteAllText(source, "return 7");
        File.WriteAllText(Path.Combine(fixture.Path, "Directory.Build.rsp"),
            $"-p:CustomBeforeMicrosoftCommonTargets=\"{callerTarget}\"");
        var compilerTemp = Directory.CreateDirectory(Path.Combine(fixture.Path, "compiler-temp")).FullName;
        var workspaceRoot = Path.Combine(compilerTemp, "PowerForge", "powershell-compilation");
        PowerShellCompilationBuildResult build;
        using (new ScopedEnvironment(new Dictionary<string, string?>
        {
            ["TEMP"] = compilerTemp,
            ["TMP"] = compilerTemp,
            ["TMPDIR"] = compilerTemp,
            ["MSBuildSDKsPath"] = Path.Combine(fixture.Path, "caller-sdks"),
            ["DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR"] = Path.Combine(fixture.Path, "caller-sdks"),
            ["CustomBeforeMicrosoftCommonTargets"] = callerTarget,
            ["DirectoryBuildTargetsPath"] = callerTarget,
            ["BuildDependsOn"] = "CallerOwnedTarget",
            ["DOTNET_STARTUP_HOOKS"] = Path.Combine(fixture.Path, "caller-startup-hook.dll"),
            ["DOTNET_ADDITIONAL_DEPS"] = Path.Combine(fixture.Path, "caller.deps.json"),
            ["CORECLR_ENABLE_PROFILING"] = "1",
            ["CORECLR_PROFILER_PATH"] = Path.Combine(fixture.Path, "caller-profiler.dll")
        }))
        {
            build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                source, Path.Combine(fixture.Path, "out"), "ControlledTrimmed", PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = "net10.0", RuntimeIdentifier = rid, SelfContained = true, SingleFile = true,
                Optimization = PowerShellCompilationExecutableOptimization.Trimmed, UseBuildCache = false
            });
        }

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(File.Exists(sentinel));
        Assert.True(Assert.IsType<PowerShellCompilationArtifactManifest>(build.Manifest).DependencyClosureVerified);
        var run = await new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(
            build.ArtifactPath!, Path.GetDirectoryName(build.ArtifactPath!)!, Array.Empty<string>(), TimeSpan.FromSeconds(30)));
        Assert.True(run.Succeeded, run.StdErr);
        Assert.Equal("7", run.StdOut.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StdErr), run.StdErr);
        // A completed producer must release its private home/TEMP as part of normal
        // workspace disposal, including any compiler-server file handles.
        Assert.Empty(Directory.Exists(workspaceRoot) ? Directory.GetDirectories(workspaceRoot) : Array.Empty<string>());
    }

    private sealed class ScopedEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _original;
        internal ScopedEnvironment(IReadOnlyDictionary<string, string?> values)
        {
            _original = values.Keys.ToDictionary(static name => name, Environment.GetEnvironmentVariable);
            foreach (var item in values) Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
        public void Dispose()
        {
            foreach (var item in _original) Environment.SetEnvironmentVariable(item.Key, item.Value);
        }
    }
}

internal sealed class WindowsOrLinuxX64SdkEnvironmentFactAttribute : FactAttribute
{
    public WindowsOrLinuxX64SdkEnvironmentFactAttribute()
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip = "Authenticated trimmed producer execution qualification requires Windows or Linux x64.";
    }
}
