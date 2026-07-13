using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetNuGetClientTests
{
    [Fact]
    public async Task PushPackageAsync_UsesResponseFileAndCleansItUp()
    {
        ProcessRunRequest? captured = null;
        string? responseFilePath = null;
        string? responseFileContent = null;
        var processRunner = new StubProcessRunner(request => {
            captured = request;
            responseFilePath = request.Arguments.Single()[1..];
            responseFileContent = File.ReadAllText(responseFilePath);
            return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var runtimeDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"))).FullName;
        var packagePath = Path.Combine(runtimeDirectory, "Artifacts", "Test.1.0.0.nupkg");
        var client = new DotNetNuGetClient(processRunner, runtimeDirectoryRoot: runtimeDirectory);

        try
        {
            var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
                packagePath,
                apiKey: "secret",
                source: "https://api.nuget.org/v3/index.json",
                skipDuplicate: true,
                workingDirectory: null,
                timeout: null,
                suppressCompanionSymbols: true));

            Assert.NotNull(captured);
            Assert.Equal("dotnet", captured!.FileName);
            Assert.Single(captured.Arguments);
            Assert.StartsWith("@", captured.Arguments[0], StringComparison.Ordinal);
            Assert.NotNull(responseFileContent);
            Assert.Equal(
                string.Join(Environment.NewLine,
                [
                    "nuget",
                    "push",
                    packagePath,
                    "--api-key",
                    "secret",
                    "--source",
                    "https://api.nuget.org/v3/index.json",
                    "--skip-duplicate",
                    "--no-symbols"
                ]),
                responseFileContent);
            Assert.True(result.Succeeded);
            Assert.NotNull(responseFilePath);
            Assert.False(File.Exists(responseFilePath!));
        }
        finally
        {
            try { Directory.Delete(runtimeDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PushRequest_RetainsOriginalSixArgumentConstructor()
    {
        var constructor = typeof(DotNetNuGetPushRequest).GetConstructor(new[]
        {
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(bool),
            typeof(string),
            typeof(TimeSpan?)
        });

        Assert.NotNull(constructor);
        var request = Assert.IsType<DotNetNuGetPushRequest>(constructor!.Invoke(new object?[]
        {
            "Sample.1.0.0.nupkg",
            "key",
            "https://api.nuget.org/v3/index.json",
            true,
            null,
            null
        }));
        Assert.False(request.SuppressCompanionSymbols);
    }

    [Fact]
    public async Task PushPackageAsync_StagesCompanionSymbolsUnderConfigurationDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var configurationDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "repository"));
        var packageDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts"));
        var packagePath = Path.Combine(packageDirectory.FullName, "Sample.1.0.0.nupkg");
        var symbolPackagePath = Path.ChangeExtension(packagePath, ".snupkg");
        File.WriteAllText(packagePath, "primary");
        File.WriteAllText(symbolPackagePath, "symbols");

        ProcessRunRequest? captured = null;
        string? stagedPackagePath = null;
        string? pushedSource = null;
        var processRunner = new StubProcessRunner(request =>
        {
            captured = request;
            var responseFile = request.Arguments.Single()[1..];
            var responseFileLines = File.ReadAllLines(responseFile);
            stagedPackagePath = responseFileLines[2];
            pushedSource = responseFileLines[6];
            Assert.True(File.Exists(stagedPackagePath));
            Assert.True(File.Exists(Path.ChangeExtension(stagedPackagePath, ".snupkg")));
            return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var client = new DotNetNuGetClient(
            processRunner,
            runtimeDirectoryRoot: Path.Combine(root.FullName, "runtime"));

        try
        {
            var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
                packagePath,
                "key",
                "./feed",
                skipDuplicate: true,
                workingDirectory: configurationDirectory.FullName,
                timeout: null,
                suppressCompanionSymbols: false));

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.StartsWith(configurationDirectory.FullName, captured!.WorkingDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(configurationDirectory.FullName, captured.WorkingDirectory);
            Assert.NotEqual(packagePath, stagedPackagePath);
            Assert.Equal(Path.Combine(configurationDirectory.FullName, "feed"), pushedSource);
            Assert.False(Directory.Exists(captured.WorkingDirectory));
            Assert.True(File.Exists(packagePath));
            Assert.True(File.Exists(symbolPackagePath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task PushPackageAsync_UsesPackageDirectoryWithinConfigurationHierarchy()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var packageDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts", "packages"));
        var packagePath = Path.Combine(packageDirectory.FullName, "Sample.1.0.0.nupkg");
        var symbolPackagePath = Path.ChangeExtension(packagePath, ".snupkg");
        File.WriteAllText(packagePath, "primary");
        File.WriteAllText(symbolPackagePath, "symbols");

        ProcessRunRequest? captured = null;
        string? pushedPackagePath = null;
        string? pushedSource = null;
        var processRunner = new StubProcessRunner(request =>
        {
            captured = request;
            var responseFileLines = File.ReadAllLines(request.Arguments.Single()[1..]);
            pushedPackagePath = responseFileLines[2];
            pushedSource = responseFileLines[6];
            return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var client = new DotNetNuGetClient(
            processRunner,
            runtimeDirectoryRoot: Path.Combine(root.FullName, "runtime"));

        try
        {
            var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
                Path.GetRelativePath(root.FullName, packagePath),
                "key",
                "./feed",
                skipDuplicate: true,
                workingDirectory: root.FullName,
                timeout: null,
                suppressCompanionSymbols: false));

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(packageDirectory.FullName, captured!.WorkingDirectory);
            Assert.Equal(packagePath, pushedPackagePath);
            Assert.Equal(Path.Combine(root.FullName, "feed"), pushedSource);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task PushPackageAsync_PreservesResponseFileValuesWithSpacesWithoutLiteralQuotes()
    {
        string? responseFileContent = null;
        var processRunner = new StubProcessRunner(request => {
            responseFileContent = File.ReadAllText(request.Arguments.Single()[1..]);
            return new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var runtimeDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge Tests", Guid.NewGuid().ToString("N"))).FullName;
        var packagePath = Path.Combine(runtimeDirectory, "repo with spaces", "Artifacts", "Test.1.0.0.nupkg");
        var source = Path.Combine(runtimeDirectory, "local feed with spaces");
        var client = new DotNetNuGetClient(processRunner, runtimeDirectoryRoot: runtimeDirectory);

        try
        {
            var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
                packagePath,
                apiKey: "secret value",
                source));

            Assert.Equal(
                string.Join(Environment.NewLine,
                [
                    "nuget",
                    "push",
                    packagePath,
                    "--api-key",
                    "secret value",
                    "--source",
                    source,
                    "--skip-duplicate"
                ]),
                responseFileContent);
            Assert.True(result.Succeeded);
        }
        finally
        {
            try { Directory.Delete(runtimeDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PushPackageAsync_PublishesRelativePathsWithSpacesUsingDotNetCli()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge NuGet Push",
            Guid.NewGuid().ToString("N")));
        var repositoryDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Repository With Spaces"));
        var packageDirectory = Directory.CreateDirectory(Path.Combine(repositoryDirectory.FullName, "Artifacts With Spaces"));
        var feedDirectory = Directory.CreateDirectory(Path.Combine(repositoryDirectory.FullName, "Feed With Spaces"));
        var packageFileName = "Sample.Package.1.0.0.nupkg";
        var packagePath = Path.Combine(packageDirectory.FullName, packageFileName);
        TestPackageFactory.Create(packagePath, "Sample.Package", "1.0.0");
        var client = new DotNetNuGetClient(
            runtimeDirectoryRoot: Path.Combine(root.FullName, "Runtime With Spaces"));

        try
        {
            var result = await client.PushPackageAsync(new DotNetNuGetPushRequest(
                packagePath: Path.Combine("Artifacts With Spaces", packageFileName),
                apiKey: "unused key",
                source: Path.Combine(".", "Feed With Spaces"),
                skipDuplicate: true,
                workingDirectory: repositoryDirectory.FullName,
                timeout: TimeSpan.FromMinutes(1),
                suppressCompanionSymbols: true));

            Assert.True(
                result.Succeeded,
                $"{result.ErrorMessage}{Environment.NewLine}{result.StdOut}{Environment.NewLine}{result.StdErr}");
            Assert.Single(Directory.GetFiles(
                feedDirectory.FullName,
                packageFileName,
                SearchOption.AllDirectories));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SignPackageAsync_BuildsStructuredArguments()
    {
        ProcessRunRequest? captured = null;
        var processRunner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var client = new DotNetNuGetClient(processRunner);

        var result = await client.SignPackageAsync(new DotNetNuGetSignRequest(
            packagePath: @"C:\repo\Artifacts\Test.1.0.0.nupkg",
            certificateFingerprint: "ABC123",
            certificateStoreLocation: "CurrentUser",
            timeStampServer: "http://timestamp.digicert.com"));

        Assert.NotNull(captured);
        Assert.Equal(
            [
                "nuget",
                "sign",
                @"C:\repo\Artifacts\Test.1.0.0.nupkg",
                "--certificate-fingerprint",
                "ABC123",
                "--certificate-store-location",
                "CurrentUser",
                "--certificate-store-name",
                "My",
                "--timestamper",
                "http://timestamp.digicert.com",
                "--overwrite"
            ],
            captured!.Arguments);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SignPackageAsync_AcceptsMultiplePackagesInOneProcess()
    {
        ProcessRunRequest? captured = null;
        var processRunner = new StubProcessRunner(request => {
            captured = request;
            return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
        });
        var client = new DotNetNuGetClient(processRunner);

        var result = await client.SignPackageAsync(new DotNetNuGetSignRequest(
            packagePaths: new[]
            {
                @"C:\repo\Artifacts\One.1.0.0.nupkg",
                @"C:\repo\Artifacts\Two.1.0.0.nupkg"
            },
            certificateFingerprint: "ABC123",
            certificateStoreLocation: "CurrentUser",
            timeStampServer: "http://timestamp.digicert.com"));

        Assert.NotNull(captured);
        Assert.Equal(
            [
                "nuget",
                "sign",
                @"C:\repo\Artifacts\One.1.0.0.nupkg",
                @"C:\repo\Artifacts\Two.1.0.0.nupkg",
                "--certificate-fingerprint",
                "ABC123",
                "--certificate-store-location",
                "CurrentUser",
                "--certificate-store-name",
                "My",
                "--timestamper",
                "http://timestamp.digicert.com",
                "--overwrite"
            ],
            captured!.Arguments);
        Assert.True(result.Succeeded);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}
