using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Management.Infrastructure;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationManagementProviderExecutionTests
{
    [WindowsFact]
    public async Task ReleaseProjectPacksExecutableManagementProviderPackage()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        var output = Path.Combine(Path.GetTempPath(), "PowerForgeManagementProviderPack", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var repoRoot = RepoRootLocator.Find();
            var project = Path.Combine(
                repoRoot,
                "PowerForge.PowerShell.Provider.Management.Runtime",
                "PowerForge.PowerShell.Provider.Management.Runtime.csproj");
            var pack = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet",
                repoRoot,
                new[] { "pack", project, "-c", "Release", "--no-build", "--no-restore", "-o", output, "--nologo" },
                TimeSpan.FromMinutes(2)));
            Assert.True(pack.Succeeded, pack.StdErr + Environment.NewLine + pack.StdOut);
            var package = Assert.Single(Directory.GetFiles(output, PowerShellManagementRuntimeProviderPackage.PackageId + ".*.nupkg"));
            var resolution = new PowerShellCompilationProviderPackageReader().Resolve(
                new[] { new PowerShellCompilationProviderPackageReference(package) },
                runtimeIdentifier: "win-x64");

            Assert.Equal(9, resolution.Providers.Length);
            var locked = Assert.Single(resolution.Lock.Packages);
            Assert.Equal(PowerShellManagementRuntimeProviderPackage.PackageId, locked.PackageId);
            Assert.Equal(3, locked.Assemblies.Length);
            Assert.Single(locked.NativeAssets);
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [WindowsFact]
    public async Task StrictExecutableRunsLockedLocalManagementQueryWithoutPowerShellRuntime()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = ManagementProviderFixture.Create();
        var request = new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Query,
            Query = "SELECT Caption, Version FROM Win32_OperatingSystem",
            MaximumResults = 4,
            TimeoutSeconds = 30
        };

        var result = fixture.BuildStrictExecutable("Invoke-ManagementQueryCore", request, "ManagementQueryProbe");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.True(result.Manifest.DependencyLockReviewed);
        Assert.True(result.Manifest.ProviderLockReviewed);
        Assert.Equal(3, result.Manifest.Files.Count(static file => file.Role == "CompilerProviderRuntime"));
        var deliveredNative = Assert.Single(result.Manifest.Files, static file => file.Role == "CompilerProviderNativeRuntime");
        var lockedPackage = Assert.Single(result.Manifest.ProviderLock!.Packages);
        var lockedNative = Assert.Single(lockedPackage.NativeAssets);
        Assert.Equal("win-x64", lockedNative.RuntimeIdentifier);
        Assert.Equal(lockedNative.Sha256, deliveredNative.Sha256);
        Assert.DoesNotContain(result.Manifest.Files, static file =>
            Path.GetFileName(file.Path).Equals("System.Management.Automation.dll", StringComparison.OrdinalIgnoreCase));

        var run = await RunProcessAsync(result.ArtifactPath!, TimeSpan.FromSeconds(60));
        Assert.Equal(0, run.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
        using var document = JsonDocument.Parse(run.StandardOutput.Trim());
        var root = document.RootElement;
        Assert.Equal("Query", root.GetProperty("Operation").GetString());
        Assert.True(root.GetProperty("OwnedSessionDisposed").GetBoolean());
        var instance = Assert.Single(root.GetProperty("Instances").EnumerateArray());
        Assert.Equal("Win32_OperatingSystem", instance.GetProperty("ClassName").GetString());
        Assert.Contains(instance.GetProperty("Properties").EnumerateArray(), static property =>
            property.GetProperty("Name").GetString() == "Caption" &&
            !string.IsNullOrWhiteSpace(property.GetProperty("Value").GetString()));

        var provenance = File.ReadAllText(Assert.Single(result.Manifest.Files, static file => file.Role == "BuildProvenance").Path);
        var sbom = File.ReadAllText(Assert.Single(result.Manifest.Files, static file => file.Role == "Sbom").Path);
        Assert.Contains(lockedNative.Sha256, provenance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CompilerProviderNativeRuntime", sbom, StringComparison.Ordinal);
        Assert.Contains(lockedNative.Sha256, sbom, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task StrictExecutableRunsLocalManagementEnumerationLookupMutationMethodAndAssociationFamily()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = ManagementProviderFixture.Create();
        var adapter = new PowerShellManagementProviderAdapter();
        using var operatingSystemResult = adapter.Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Enumerate,
            ClassName = "Win32_OperatingSystem",
            TimeoutSeconds = 30
        });
        _ = Assert.Single(operatingSystemResult.Instances);
        var environmentName = "PF_STRICT_" + Guid.NewGuid().ToString("N");
        var userName = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var environmentReference = new PowerShellManagementInstanceReference
        {
            ClassName = "Win32_Environment",
            Namespace = "root/cimv2",
            Keys = new[]
            {
                Property("Name", environmentName),
                Property("UserName", userName)
            }
        };
        var driveId = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        using var driveResult = adapter.Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Query,
            Query = $"SELECT * FROM Win32_LogicalDisk WHERE DeviceID = '{driveId}'",
            TimeoutSeconds = 30
        });
        var drive = Assert.Single(driveResult.Instances);
        var driveReference = Assert.IsType<PowerShellManagementInstanceReference>(drive.Reference);
        try
        {
            var operations = new[]
            {
                ("Invoke-ManagementEnumerateCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Enumerate,
                    ClassName = "Win32_Process",
                    ResultLimit = 2,
                    TimeoutSeconds = 30
                }),
                ("Invoke-ManagementGetCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Get,
                    InstanceReference = driveReference,
                    TimeoutSeconds = 30
                }),
                ("Invoke-ManagementCreateCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Create,
                    ClassName = "Win32_Environment",
                    Properties = new[]
                    {
                        Property("Name", environmentName),
                        Property("UserName", userName),
                        Property("VariableValue", "created")
                    },
                    TimeoutSeconds = 30
                }),
                ("Invoke-ManagementModifyCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Modify,
                    InstanceReference = environmentReference,
                    Properties = new[] { Property("VariableValue", "modified") },
                    TimeoutSeconds = 30
                }),
                ("Invoke-ManagementQueryCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Query,
                    Query = $"SELECT * FROM Win32_Environment WHERE Name = '{environmentName}' AND UserName = '{userName.Replace("\\", "\\\\", StringComparison.Ordinal)}'",
                    TimeoutSeconds = 30
                }),
                ("Invoke-ManagementMethodCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.InvokeMethod,
                    ClassName = "Win32_Process",
                    MethodName = "Create",
                    MethodParameters = new[] { Property("CommandLine", "cmd.exe /d /c exit 0") },
                    TimeoutSeconds = 30
                }),
                ("Invoke-ManagementAssociationCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Association,
                    InstanceReference = driveReference,
                    TimeoutSeconds = 30
                }),
                ("Invoke-ManagementDeleteCore", new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Delete,
                    InstanceReference = environmentReference,
                    TimeoutSeconds = 30
                })
            };
            var result = fixture.BuildStrictExecutable(operations, "ManagementOperationFamilyProbe");

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            var run = await RunProcessAsync(result.ArtifactPath!, TimeSpan.FromSeconds(90));
            Assert.Equal(0, run.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
            var lines = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(operations.Length, lines.Length);
            using var enumerate = JsonDocument.Parse(lines[0]);
            using var get = JsonDocument.Parse(lines[1]);
            using var create = JsonDocument.Parse(lines[2]);
            using var modify = JsonDocument.Parse(lines[3]);
            using var observe = JsonDocument.Parse(lines[4]);
            using var method = JsonDocument.Parse(lines[5]);
            using var association = JsonDocument.Parse(lines[6]);
            using var delete = JsonDocument.Parse(lines[7]);
            Assert.Equal(2, enumerate.RootElement.GetProperty("Instances").GetArrayLength());
            Assert.Equal("Win32_LogicalDisk", Assert.Single(get.RootElement.GetProperty("Instances").EnumerateArray()).GetProperty("ClassName").GetString());
            Assert.Contains(Assert.Single(create.RootElement.GetProperty("Instances").EnumerateArray()).GetProperty("Properties").EnumerateArray(),
                property => property.GetProperty("Name").GetString() == "Name" && property.GetProperty("Value").GetString() == environmentName);
            Assert.Contains(Assert.Single(modify.RootElement.GetProperty("Instances").EnumerateArray()).GetProperty("Properties").EnumerateArray(),
                property => property.GetProperty("Name").GetString() == "Name" && property.GetProperty("Value").GetString() == environmentName);
            Assert.Contains(Assert.Single(observe.RootElement.GetProperty("Instances").EnumerateArray()).GetProperty("Properties").EnumerateArray(),
                property => property.GetProperty("Name").GetString() == "VariableValue" && property.GetProperty("Value").GetString() == "modified");
            Assert.Equal("0", method.RootElement.GetProperty("ReturnValue").GetProperty("Value").GetString());
            Assert.NotEmpty(association.RootElement.GetProperty("Instances").EnumerateArray());
            Assert.Empty(delete.RootElement.GetProperty("Instances").EnumerateArray());
            foreach (var line in lines)
            {
                using var operation = JsonDocument.Parse(line);
                Assert.True(operation.RootElement.GetProperty("OwnedSessionDisposed").GetBoolean());
            }
        }
        finally
        {
            try
            {
                using var cleanup = adapter.Execute(new PowerShellManagementRequest
                {
                    Operation = PowerShellManagementOperation.Delete,
                    InstanceReference = environmentReference,
                    TimeoutSeconds = 10
                });
            }
            catch (CimException) { }
        }
    }

    [WindowsFact]
    public async Task StrictExecutableReceivesBoundedManagementSubscriptionAndAdapterCancelsPromptly()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = ManagementProviderFixture.Create();
        var result = fixture.BuildStrictExecutable(
            "Invoke-ManagementSubscriptionCore",
            new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Subscription,
                Query = "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'",
                MaximumResults = 1,
                TimeoutSeconds = 30
            },
            "ManagementSubscriptionProbe");
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        var execution = RunProcessAsync(result.ArtifactPath!, TimeSpan.FromSeconds(45));
        await Task.Delay(TimeSpan.FromSeconds(2));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var trigger = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/d /c exit 0",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (trigger is not null) await trigger.WaitForExitAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        var run = await execution;
        Assert.Equal(0, run.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
        using var document = JsonDocument.Parse(run.StandardOutput.Trim());
        Assert.Equal("Subscription", document.RootElement.GetProperty("Operation").GetString());
        Assert.True(document.RootElement.GetProperty("OwnedSessionDisposed").GetBoolean());
        Assert.Equal("__InstanceCreationEvent", Assert.Single(document.RootElement.GetProperty("Instances").EnumerateArray())
            .GetProperty("ClassName").GetString());

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() =>
            new PowerShellManagementProviderAdapter().Execute(new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Subscription,
                Query = "SELECT * FROM __InstanceCreationEvent WITHIN 30 WHERE TargetInstance ISA 'Win32_Process'",
                MaximumResults = 1,
                TimeoutSeconds = 60
            }, cancellation.Token)));
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Management cancellation took {stopwatch.Elapsed}.");
        using var recovery = new PowerShellManagementProviderAdapter().Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Query,
            Query = "SELECT Caption FROM Win32_OperatingSystem",
            TimeoutSeconds = 30
        });
        Assert.True(recovery.OwnedSessionDisposed);
        Assert.Single(recovery.Instances);
    }

    [RemoteManagementTargetFact]
    public async Task StrictExecutableQueriesDeclaredRemoteTargetOverWsManAndDcom()
    {
        var target = Environment.GetEnvironmentVariable(RemoteManagementTargetFactAttribute.TargetVariable)!;
        using var fixture = ManagementProviderFixture.Create();
        var operations = new[]
        {
            ("Invoke-ManagementQueryCore", new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Query,
                ComputerName = target,
                Transport = PowerShellManagementTransport.WsMan,
                Query = "SELECT Caption, Version FROM Win32_OperatingSystem",
                TimeoutSeconds = 30
            }),
            ("Invoke-ManagementQueryCore", new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Query,
                ComputerName = target,
                Transport = PowerShellManagementTransport.Dcom,
                Query = "SELECT Caption, Version FROM Win32_OperatingSystem",
                TimeoutSeconds = 30
            })
        };
        var result = fixture.BuildStrictExecutable(operations, "RemoteManagementTransportProbe");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = await RunProcessAsync(result.ArtifactPath!, TimeSpan.FromSeconds(90));
        Assert.Equal(0, run.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
        var lines = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var instance = Assert.Single(document.RootElement.GetProperty("Instances").EnumerateArray());
            Assert.Equal("Win32_OperatingSystem", instance.GetProperty("ClassName").GetString());
            Assert.False(string.IsNullOrWhiteSpace(instance.GetProperty("ServerName").GetString()));
            Assert.True(document.RootElement.GetProperty("OwnedSessionDisposed").GetBoolean());
        }

        using var callerOptions = new Microsoft.Management.Infrastructure.Options.WSManSessionOptions();
        using var callerSession = CimSession.Create(target, callerOptions);
        var adapter = new PowerShellManagementProviderAdapter();
        using (var callerResult = adapter.Execute(new PowerShellManagementRequest
               {
                   Operation = PowerShellManagementOperation.Query,
                   Session = callerSession,
                   Query = "SELECT Caption FROM Win32_OperatingSystem",
                   TimeoutSeconds = 30
               }))
        {
            Assert.False(callerResult.OwnedSessionDisposed);
            Assert.Single(callerResult.Instances);
        }
        using var callerReuse = adapter.Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Query,
            Session = callerSession,
            Query = "SELECT Version FROM Win32_OperatingSystem",
            TimeoutSeconds = 30
        });
        Assert.False(callerReuse.OwnedSessionDisposed);
        Assert.Single(callerReuse.Instances);

        using var invalidPassword = new System.Security.SecureString();
        foreach (var character in "invalid-runtime-credential") invalidPassword.AppendChar(character);
        invalidPassword.MakeReadOnly();
        var authenticationFailure = Record.Exception(() => new PowerShellManagementProviderAdapter().Execute(
            new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Query,
                ComputerName = target,
                Transport = PowerShellManagementTransport.WsMan,
                Authentication = PowerShellManagementAuthentication.NtlmDomain,
                Credential = new PowerShellManagementCredential(
                    "PF_INVALID_" + Guid.NewGuid().ToString("N"),
                    invalidPassword,
                    "."),
                Query = "SELECT Caption FROM Win32_OperatingSystem",
                TimeoutSeconds = 10
            }));
        Assert.NotNull(authenticationFailure);
        Assert.DoesNotContain("invalid-runtime-credential", authenticationFailure.ToString(), StringComparison.Ordinal);

        var disconnectedSession = CimSession.Create(target);
        disconnectedSession.Dispose();
        Assert.ThrowsAny<Exception>(() => new PowerShellManagementProviderAdapter().Execute(
            new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Query,
                Session = disconnectedSession,
                Query = "SELECT Caption FROM Win32_OperatingSystem",
                TimeoutSeconds = 10
            }));
    }

    [WindowsFact]
    public async Task StrictExecutableFailsClosedForMissingManagementNamespaceAndClass()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = ManagementProviderFixture.Create();
        var missingNamespace = fixture.BuildStrictExecutable(
            "Invoke-ManagementQueryCore",
            new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Query,
                Namespace = "root/powerforge_missing",
                Query = "SELECT * FROM Missing_Class",
                TimeoutSeconds = 10
            },
            "MissingManagementNamespaceProbe");
        var missingClass = fixture.BuildStrictExecutable(
            "Invoke-ManagementEnumerateCore",
            new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Enumerate,
                ClassName = "PowerForge_Missing_Class",
                TimeoutSeconds = 10
            },
            "MissingManagementClassProbe");
        var unreachable = fixture.BuildStrictExecutable(
            "Invoke-ManagementQueryCore",
            new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Query,
                ComputerName = "powerforge-unreachable.invalid",
                Transport = PowerShellManagementTransport.WsMan,
                Query = "SELECT Caption FROM Win32_OperatingSystem",
                TimeoutSeconds = 5
            },
            "UnreachableManagementTargetProbe");

        Assert.True(missingNamespace.Succeeded, missingNamespace.Error + Environment.NewLine + missingNamespace.BuildOutput);
        Assert.True(missingClass.Succeeded, missingClass.Error + Environment.NewLine + missingClass.BuildOutput);
        Assert.True(unreachable.Succeeded, unreachable.Error + Environment.NewLine + unreachable.BuildOutput);
        var namespaceRun = await RunProcessAsync(missingNamespace.ArtifactPath!, TimeSpan.FromSeconds(30));
        var classRun = await RunProcessAsync(missingClass.ArtifactPath!, TimeSpan.FromSeconds(30));
        var unreachableRun = await RunProcessAsync(unreachable.ArtifactPath!, TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, namespaceRun.ExitCode);
        Assert.NotEqual(0, classRun.ExitCode);
        Assert.NotEqual(0, unreachableRun.ExitCode);
        Assert.Contains("namespace", namespaceRun.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class", classRun.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", namespaceRun.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", classRun.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(unreachableRun.StandardError));
        Assert.DoesNotContain("password", unreachableRun.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProcessResult> RunProcessAsync(string path, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        Assert.True(process.Start());
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            throw new TimeoutException($"Management provider executable exceeded {timeout}.");
        }
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private sealed class ManagementProviderFixture : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        private ManagementProviderFixture(string root, string packagePath, PowerShellCompilationProviderResolution resolution)
        {
            Root = root;
            PackagePath = packagePath;
            Resolution = resolution;
        }

        private string Root { get; }
        private string PackagePath { get; }
        private PowerShellCompilationProviderResolution Resolution { get; }

        internal static ManagementProviderFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeManagementProviderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var packagePath = Path.Combine(root, PowerShellManagementRuntimeProviderPackage.PackageId + ".1.0.0.nupkg");
            var resolution = PowerShellManagementRuntimeProviderPackage.Build(packagePath, "1.0.0");
            return new ManagementProviderFixture(root, packagePath, resolution);
        }

        internal PowerShellCompilationBuildResult BuildStrictExecutable(
            string commandName,
            PowerShellManagementRequest request,
            string artifactName)
            => BuildStrictExecutable(new[] { (commandName, request) }, artifactName);

        internal PowerShellCompilationBuildResult BuildStrictExecutable(
            IEnumerable<(string CommandName, PowerShellManagementRequest Request)> operations,
            string artifactName)
        {
            var scriptPath = Path.Combine(Root, artifactName + ".ps1");
            var outputPath = Path.Combine(Root, artifactName);
            File.WriteAllText(scriptPath, string.Join(Environment.NewLine, operations.Select(operation =>
            {
                var requestJson = JsonSerializer.Serialize(operation.Request, JsonOptions);
                return operation.CommandName + " '" + requestJson.Replace("'", "''", StringComparison.Ordinal) + "'";
            })));
            var spec = new PowerShellCompilationBuildSpec(
                scriptPath,
                outputPath,
                artifactName,
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Strict)
            {
                TargetFramework = "net10.0",
                RuntimeIdentifier = "win-x64",
                SelfContained = false,
                SingleFile = false,
                TimeoutSeconds = 600,
                ProviderPackages = new[] { new PowerShellCompilationProviderPackageReference(PackagePath) },
                ExpectedProviderLock = Resolution.Lock,
                ProviderTrustPolicy = new PowerShellCompilationProviderTrustPolicy
                {
                    AllowedPackageIds = new[] { PowerShellManagementRuntimeProviderPackage.PackageId },
                    AllowedProviderIds = PowerShellManagementRuntimeProviderPackage.CreateProviders().Select(static provider => provider.ProviderId).ToArray(),
                    AllowedPublishers = new[] { "EvotecIT" },
                    AllowedLicenseExpressions = new[] { "MIT" },
                    RequireRedistributable = true
                }
            };
            spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
            return new PowerShellCompilationArtifactBuilder().Build(spec);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }
    }

    private static PowerShellManagementProperty Property(string name, string value)
        => new() { Name = name, Value = value, TypeName = "String" };

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

internal sealed class RemoteManagementTargetFactAttribute : FactAttribute
{
    internal const string TargetVariable = "POWERFORGE_TEST_CIM_TARGET";

    public RemoteManagementTargetFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only test.";
        else if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
                 System.Runtime.InteropServices.Architecture.X64)
            Skip = "The current management provider package is qualified for win-x64.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TargetVariable)))
            Skip = $"Set {TargetVariable} to a declared Windows management target.";
    }
}
