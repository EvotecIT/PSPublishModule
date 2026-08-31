using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationDirectoryProviderExecutionTests
{
    [WindowsFact]
    public async Task ReleaseProjectPacksExecutableDirectoryProviderPackage()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        var output = Path.Combine(Path.GetTempPath(), "PowerForgeDirectoryProviderPack", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var repoRoot = RepoRootLocator.Find();
            var project = Path.Combine(
                repoRoot,
                "PowerForge.PowerShell.Provider.Directory.Runtime",
                "PowerForge.PowerShell.Provider.Directory.Runtime.csproj");
            var pack = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet",
                repoRoot,
                new[] { "pack", project, "-c", "Release", "--no-build", "--no-restore", "-o", output, "--nologo" },
                TimeSpan.FromMinutes(2)));
            Assert.True(pack.Succeeded, pack.StdErr + Environment.NewLine + pack.StdOut);
            var package = Assert.Single(Directory.GetFiles(output, PowerShellDirectoryRuntimeProviderPackage.PackageId + ".*.nupkg"));
            var resolution = new PowerShellCompilationProviderPackageReader().Resolve(
                new[] { new PowerShellCompilationProviderPackageReference(package) },
                runtimeIdentifier: "win-x64");

            Assert.Equal(7, resolution.Providers.Length);
            Assert.All(resolution.Providers, static provider =>
                Assert.Equal(PowerShellCompilationProviderCancellation.PostInitializationCooperative, provider.Adapter.Cancellation));
            var locked = Assert.Single(resolution.Lock.Packages);
            Assert.Equal(PowerShellDirectoryRuntimeProviderPackage.PackageId, locked.PackageId);
            Assert.Equal(2, locked.Assemblies.Length);
            Assert.Empty(locked.NativeAssets);
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [WindowsFact]
    public async Task StrictExecutableDeliversLockedDirectoryProviderAndFailsClosedForMissingTarget()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64)
            return;

        using var fixture = DirectoryProviderFixture.Create();
        var result = fixture.BuildStrictExecutable(
            "Invoke-DirectorySearchCore",
            Search("powerforge-unreachable.invalid"),
            "MissingDirectoryTargetProbe");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.True(result.Manifest.ProviderLockReviewed);
        Assert.Equal(2, result.Manifest.Files.Count(static file => file.Role == "CompilerProviderRuntime"));
        Assert.DoesNotContain(result.Manifest.Files, static file =>
            Path.GetFileName(file.Path).Equals("System.Management.Automation.dll", StringComparison.OrdinalIgnoreCase));
        var run = await RunProcessAsync(result.ArtifactPath!, TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, run.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(run.StandardError));
        Assert.DoesNotContain("password", run.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [DirectoryTargetFact]
    public async Task StrictExecutableQueriesDeclaredLdapTargetAndProviderSessionRemainsReusable()
    {
        var target = Environment.GetEnvironmentVariable(DirectoryTargetFactAttribute.TargetVariable)!;
        using var fixture = DirectoryProviderFixture.Create();
        var result = fixture.BuildStrictExecutable(
            "Invoke-DirectorySearchCore",
            Search(target),
            "DirectoryRootDseProbe");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = await RunProcessAsync(result.ArtifactPath!, TimeSpan.FromSeconds(60));
        Assert.Equal(0, run.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
        using var document = JsonDocument.Parse(run.StandardOutput.Trim());
        Assert.Equal("Search", document.RootElement.GetProperty("Operation").GetString());
        Assert.Equal("Success", document.RootElement.GetProperty("ResultCode").GetString());
        Assert.True(document.RootElement.GetProperty("OwnedConnectionDisposed").GetBoolean());
        var entry = Assert.Single(document.RootElement.GetProperty("Entries").EnumerateArray());
        Assert.Contains(entry.GetProperty("Attributes").EnumerateArray(), static attribute =>
            attribute.GetProperty("Name").GetString()!.Equals("defaultNamingContext", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(Assert.Single(attribute.GetProperty("Values").EnumerateArray()).GetProperty("Text").GetString()));

        var adapter = new PowerShellDirectoryProviderAdapter();
        using var session = adapter.OpenSession(target, timeoutSeconds: 30);
        var firstRequest = Search(target);
        firstRequest.HostName = string.Empty;
        firstRequest.Port = 0;
        firstRequest.Session = session;
        var first = adapter.Execute(firstRequest);
        Assert.False(first.OwnedConnectionDisposed);
        var rootDse = Assert.Single(first.Entries);
        var defaultNamingContext = Assert.Single(rootDse.Attributes
            .Single(static attribute => attribute.Name.Equals("defaultNamingContext", StringComparison.OrdinalIgnoreCase))
            .Values).Text;
        Assert.False(string.IsNullOrWhiteSpace(defaultNamingContext));

        var secondRequest = Search(target);
        secondRequest.HostName = string.Empty;
        secondRequest.Port = 0;
        secondRequest.Session = session;
        secondRequest.AttributeNames = new[] { "dnsHostName" };
        var second = adapter.Execute(secondRequest);
        Assert.False(second.OwnedConnectionDisposed);
        Assert.Single(second.Entries);

        var pagedRequest = Search(target);
        pagedRequest.HostName = string.Empty;
        pagedRequest.Port = 0;
        pagedRequest.Session = session;
        pagedRequest.BaseDistinguishedName = defaultNamingContext;
        pagedRequest.Scope = PowerShellDirectorySearchScope.Subtree;
        pagedRequest.AttributeNames = new[] { "distinguishedName" };
        pagedRequest.PageSize = 2;
        pagedRequest.ResultLimit = 1;
        var paged = adapter.Execute(pagedRequest);
        Assert.False(paged.OwnedConnectionDisposed);
        Assert.Single(paged.Entries);
        Assert.True(paged.PagingCookieAbandoned || paged.PagingConnectionDisposed);
    }

    [DirectoryMutationTargetFact]
    public async Task StrictExecutableRunsDirectoryMutationCompareRenameAndCleanupFamily()
    {
        var target = Environment.GetEnvironmentVariable(DirectoryTargetFactAttribute.TargetVariable)!;
        var parent = Environment.GetEnvironmentVariable(DirectoryMutationTargetFactAttribute.BaseVariable)!;
        var token = "PF" + Guid.NewGuid().ToString("N");
        var originalDn = "CN=" + token + "," + parent;
        var renamedDn = "CN=" + token + "Renamed," + parent;
        using var fixture = DirectoryProviderFixture.Create();
        var operations = new[]
        {
            ("Invoke-DirectoryAddCore", Configure(DirectoryRequest(target, PowerShellDirectoryOperation.Add, originalDn), request =>
            {
                request.Attributes = new[]
                {
                    Attribute("objectClass", "top", "person"),
                    Attribute("cn", token),
                    Attribute("sn", token),
                    Attribute("description", "created")
                };
            })),
            ("Invoke-DirectoryModifyCore", Configure(DirectoryRequest(target, PowerShellDirectoryOperation.Modify, originalDn), request =>
            {
                request.Modifications = new[]
                {
                    new PowerShellDirectoryModification
                    {
                        Name = "description",
                        Operation = PowerShellDirectoryModificationOperation.Replace,
                        Values = new[] { new PowerShellDirectoryValue { Text = "modified" } }
                    }
                };
            })),
            ("Invoke-DirectoryCompareCore", Configure(DirectoryRequest(target, PowerShellDirectoryOperation.Compare, originalDn), request =>
            {
                request.CompareAttributeName = "description";
                request.CompareValue = new PowerShellDirectoryValue { Text = "modified" };
            })),
            ("Invoke-DirectoryCompareCore", Configure(DirectoryRequest(target, PowerShellDirectoryOperation.Compare, originalDn), request =>
            {
                request.CompareAttributeName = "description";
                request.CompareValue = new PowerShellDirectoryValue { Text = "not-modified" };
            })),
            ("Invoke-DirectorySearchCore", Configure(Search(target), request =>
            {
                request.BaseDistinguishedName = parent;
                request.Scope = PowerShellDirectorySearchScope.Subtree;
                request.Filter = "(cn=" + token + ")";
                request.AttributeNames = new[] { "cn", "description" };
            })),
            ("Invoke-DirectoryRenameCore", Configure(DirectoryRequest(target, PowerShellDirectoryOperation.ModifyDistinguishedName, originalDn), request =>
            {
                request.NewRelativeDistinguishedName = "CN=" + token + "Renamed";
            })),
            ("Invoke-DirectoryReadCore", Configure(DirectoryRequest(target, PowerShellDirectoryOperation.Read, renamedDn), request =>
            {
                request.AttributeNames = new[] { "cn", "description" };
            })),
            ("Invoke-DirectoryDeleteCore", DirectoryRequest(target, PowerShellDirectoryOperation.Delete, renamedDn)),
            ("Invoke-DirectorySearchCore", Configure(Search(target), request =>
            {
                request.BaseDistinguishedName = parent;
                request.Scope = PowerShellDirectorySearchScope.Subtree;
                request.Filter = "(cn=" + token + "Renamed)";
                request.AttributeNames = new[] { "cn" };
            }))
        };
        try
        {
            var result = fixture.BuildStrictExecutable(operations, "DirectoryOperationFamilyProbe");
            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            var run = await RunProcessAsync(result.ArtifactPath!, TimeSpan.FromSeconds(90));
            Assert.Equal(0, run.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
            var lines = run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(operations.Length, lines.Length);
            var results = lines.Select(static line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal("Success", results[0].RootElement.GetProperty("ResultCode").GetString());
                Assert.Equal("Success", results[1].RootElement.GetProperty("ResultCode").GetString());
                Assert.True(results[2].RootElement.GetProperty("Compared").GetBoolean());
                Assert.False(results[3].RootElement.GetProperty("Compared").GetBoolean());
                Assert.Single(results[4].RootElement.GetProperty("Entries").EnumerateArray());
                Assert.Equal("Success", results[5].RootElement.GetProperty("ResultCode").GetString());
                Assert.Equal(renamedDn, Assert.Single(results[6].RootElement.GetProperty("Entries").EnumerateArray())
                    .GetProperty("DistinguishedName").GetString(), ignoreCase: true);
                Assert.Equal("Success", results[7].RootElement.GetProperty("ResultCode").GetString());
                Assert.Empty(results[8].RootElement.GetProperty("Entries").EnumerateArray());
                Assert.All(results, static operation => Assert.True(operation.RootElement.GetProperty("OwnedConnectionDisposed").GetBoolean()));
            }
            finally
            {
                foreach (var document in results) document.Dispose();
            }
        }
        finally
        {
            DeleteIfPresent(target, renamedDn);
            DeleteIfPresent(target, originalDn);
        }
    }

    private static PowerShellDirectoryRequest Search(string target)
        => new()
        {
            Operation = PowerShellDirectoryOperation.Search,
            HostName = target,
            Port = 389,
            BaseDistinguishedName = string.Empty,
            Filter = "(objectClass=*)",
            Scope = PowerShellDirectorySearchScope.Base,
            AttributeNames = new[] { "defaultNamingContext", "dnsHostName" },
            Authentication = PowerShellDirectoryAuthentication.Negotiate,
            Transport = PowerShellDirectoryTransport.Ldap,
            PageSize = 0,
            ResultLimit = 1,
            TimeoutSeconds = 10
        };

    private static PowerShellDirectoryRequest DirectoryRequest(
        string target,
        PowerShellDirectoryOperation operation,
        string distinguishedName)
        => new()
        {
            Operation = operation,
            HostName = target,
            Port = 389,
            DistinguishedName = distinguishedName,
            Filter = "(objectClass=*)",
            Scope = PowerShellDirectorySearchScope.Base,
            Authentication = PowerShellDirectoryAuthentication.Negotiate,
            Transport = PowerShellDirectoryTransport.Ldap,
            PageSize = 0,
            TimeoutSeconds = 15
        };

    private static PowerShellDirectoryAttribute Attribute(string name, params string[] values)
        => new()
        {
            Name = name,
            Values = values.Select(static value => new PowerShellDirectoryValue { Text = value }).ToArray()
        };

    private static PowerShellDirectoryRequest Configure(
        PowerShellDirectoryRequest request,
        Action<PowerShellDirectoryRequest> configure)
    {
        configure(request);
        return request;
    }

    private static void DeleteIfPresent(string target, string distinguishedName)
    {
        try
        {
            _ = new PowerShellDirectoryProviderAdapter().Execute(
                DirectoryRequest(target, PowerShellDirectoryOperation.Delete, distinguishedName));
        }
        catch (DirectoryOperationException exception) when (exception.Response?.ResultCode == ResultCode.NoSuchObject) { }
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
            throw new TimeoutException($"Directory provider executable exceeded {timeout}.");
        }
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private sealed class DirectoryProviderFixture : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        private DirectoryProviderFixture(string root, string packagePath, PowerShellCompilationProviderResolution resolution)
        {
            Root = root;
            PackagePath = packagePath;
            Resolution = resolution;
        }

        private string Root { get; }
        private string PackagePath { get; }
        private PowerShellCompilationProviderResolution Resolution { get; }

        internal static DirectoryProviderFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeDirectoryProviderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var packagePath = Path.Combine(root, PowerShellDirectoryRuntimeProviderPackage.PackageId + ".1.0.0.nupkg");
            var resolution = PowerShellDirectoryRuntimeProviderPackage.Build(packagePath, "1.0.0");
            return new DirectoryProviderFixture(root, packagePath, resolution);
        }

        internal PowerShellCompilationBuildResult BuildStrictExecutable(
            string commandName,
            PowerShellDirectoryRequest request,
            string artifactName)
            => BuildStrictExecutable(new[] { (commandName, request) }, artifactName);

        internal PowerShellCompilationBuildResult BuildStrictExecutable(
            IEnumerable<(string CommandName, PowerShellDirectoryRequest Request)> operations,
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
                    AllowedPackageIds = new[] { PowerShellDirectoryRuntimeProviderPackage.PackageId },
                    AllowedProviderIds = PowerShellDirectoryRuntimeProviderPackage.CreateProviders().Select(static provider => provider.ProviderId).ToArray(),
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

internal sealed class DirectoryTargetFactAttribute : FactAttribute
{
    internal const string TargetVariable = "POWERFORGE_TEST_LDAP_TARGET";

    public DirectoryTargetFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only test.";
        else if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
                 System.Runtime.InteropServices.Architecture.X64)
            Skip = "The current directory provider package is qualified for win-x64.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TargetVariable)))
            Skip = $"Set {TargetVariable} to a declared LDAP target.";
    }
}

internal sealed class DirectoryMutationTargetFactAttribute : FactAttribute
{
    internal const string BaseVariable = "POWERFORGE_TEST_LDAP_MUTATION_BASE_DN";

    public DirectoryMutationTargetFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only test.";
        else if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
                 System.Runtime.InteropServices.Architecture.X64)
            Skip = "The current directory provider package is qualified for win-x64.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DirectoryTargetFactAttribute.TargetVariable)) ||
                 string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BaseVariable)))
            Skip = $"Set {DirectoryTargetFactAttribute.TargetVariable} and {BaseVariable} to an explicitly mutable LDAP test location.";
    }
}
