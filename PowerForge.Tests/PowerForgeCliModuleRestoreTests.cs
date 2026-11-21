using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed class PowerForgeCliModuleRestoreTests
{
    [Fact]
    public async Task PlanAndFrozenRestoreUsePinnedRootsAndTransitivePackageBytes()
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed packages");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "restored modules");
        WritePackage(feed, "App", "1.0.0", new TestDependency("Pinned", "[1.0.0,3.0.0)", null));
        WritePackage(feed, "Pinned", "1.0.0", new TestDependency("Leaf", "[1.0.0,2.0.0)", null));
        WritePackage(feed, "Pinned", "2.0.0");
        var leaf = WritePackage(feed, "Leaf", "1.0.0");
        File.WriteAllText(requirements, """{"App":"1.0.0","Pinned":"1.0.0"}""");
        var command = new[] { "--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed };

        using var preview = await RunCliAsync(temp.Path, 0, [.. command, "--write-lock", "--plan"]);
        Assert.Equal(1, preview.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(preview.RootElement.GetProperty("result").GetProperty("plan").GetBoolean());
        Assert.False(File.Exists(lockPath));
        Assert.False(Directory.Exists(destination));

        var appRoot = Path.Combine(destination, "App");
        Directory.CreateDirectory(Path.Combine(appRoot, "2.0.0"));
        File.WriteAllText(Path.Combine(appRoot, "App.psd1"), "@{ ModuleVersion = '1.0.0' }");
        var otherVersion = Path.Combine(appRoot, "2.0.0", "App.psd1");
        File.WriteAllText(otherVersion, "@{ ModuleVersion = '2.0.0' }");

        using var write = await RunCliAsync(temp.Path, 0, [.. command, "--write-lock"]);
        Assert.True(write.RootElement.GetProperty("result").GetProperty("lockWritten").GetBoolean());
        var originalLock = File.ReadAllText(lockPath);
        using var evidence = JsonDocument.Parse(originalLock);
        Assert.Equal(feed, evidence.RootElement.GetProperty("source").GetString());
        var packages = evidence.RootElement.GetProperty("packages").EnumerateArray().ToArray();
        Assert.Equal(new[] { "App", "Leaf", "Pinned" }, packages.Select(package => package.GetProperty("name").GetString()));
        Assert.Equal("1.0.0", packages[2].GetProperty("version").GetString());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(leaf))).ToLowerInvariant(),
            packages[1].GetProperty("sha256").GetString());
        Assert.True(File.Exists(Path.Combine(destination, "App", "1.0.0", "App.psd1")));
        Assert.True(File.Exists(otherVersion));
        Assert.True(File.Exists(Path.Combine(appRoot, "App.psd1")));

        WritePackage(feed, "Leaf", "1.1.0");
        File.WriteAllText(Path.Combine(destination, "Leaf", "1.0.0", "Leaf.psm1"), "# local change");
        using var frozen = await RunCliAsync(temp.Path, 0, command);
        Assert.False(frozen.RootElement.GetProperty("result").GetProperty("lockWritten").GetBoolean());
        Assert.Equal(originalLock, File.ReadAllText(lockPath));
        Assert.Equal("# Leaf 1.0.0", File.ReadAllText(Path.Combine(destination, "Leaf", "1.0.0", "Leaf.psm1")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Leaf", "1.1.0")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Pinned", "2.0.0")));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("replaced")]
    [InlineData("requirements")]
    [InlineData("source")]
    public async Task FrozenRestoreRejectsDriftBeforeCreatingDestination(string drift)
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "empty destination");
        WritePackage(feed, "App", "1.0.0", new TestDependency("Leaf", "[1.0.0]", null));
        var leafPath = WritePackage(feed, "Leaf", "1.0.0");
        File.WriteAllText(requirements, """{"App":"1.0.0"}""");
        var command = new[] { "--requirements", requirements, "--lock-file", lockPath, "--source", feed };
        using var write = await RunCliAsync(temp.Path, 0, [.. command, "--destination", Path.Combine(temp.Path, "initial"), "--write-lock"]);

        string expectedError;
        switch (drift)
        {
            case "missing":
                var document = JsonNode.Parse(File.ReadAllText(lockPath))!;
                document["packages"]!.AsArray().RemoveAt(1);
                File.WriteAllText(lockPath, document.ToJsonString());
                expectedError = "no matching package";
                break;
            case "replaced":
                File.Delete(leafPath);
                WritePackage(feed, "Leaf", "1.0.0", content: "# replaced package");
                expectedError = "SHA256";
                break;
            case "requirements":
                File.WriteAllText(requirements, """{"App":"2.0.0"}""");
                expectedError = "does not match the requirements";
                break;
            default:
                var otherFeed = Path.Combine(temp.Path, "different-feed");
                Directory.CreateDirectory(otherFeed);
                command[^1] = otherFeed;
                expectedError = "source differs";
                break;
        }

        var previousLock = File.ReadAllText(lockPath);
        using var frozen = await RunCliAsync(temp.Path, 1, [.. command, "--destination", destination]);
        Assert.False(frozen.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(expectedError, frozen.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(previousLock, File.ReadAllText(lockPath));
    }

    [Theory]
    [InlineData("conflict")]
    [InlineData("missing")]
    [InlineData("cycle")]
    public async Task RestoreBacktracksWhenLatestDependencyCannotCompleteGraph(string obstacle)
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        WritePackage(feed, "App", "1.0.0", new TestDependency("Branch", "[1.0.0,3.0.0)", null));
        WritePackage(feed, "Peer", "1.0.0", new TestDependency("Leaf", "[1.0.0]", null));
        WritePackage(feed, "Branch", "1.0.0", new TestDependency("Leaf", "[1.0.0]", null));
        WritePackage(feed, "Leaf", "1.0.0");
        if (obstacle == "cycle")
        {
            WritePackage(feed, "Branch", "2.0.0", new TestDependency("Cycle", "[1.0.0]", null));
            WritePackage(feed, "Cycle", "1.0.0", new TestDependency("Branch", "[2.0.0]", null));
        }
        else
        {
            WritePackage(feed, "Branch", "2.0.0", new TestDependency("Leaf", "[2.0.0]", null));
            if (obstacle == "conflict")
                WritePackage(feed, "Leaf", "2.0.0");
        }

        File.WriteAllText(requirements, obstacle == "conflict" ? """{"App":"1.0.0","Peer":"1.0.0"}""" : """{"App":"1.0.0"}""");
        var destination = Path.Combine(temp.Path, "modules");

        using var result = await RunCliAsync(temp.Path, 0,
            ["--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed, "--write-lock"]);
        Assert.True(File.Exists(Path.Combine(destination, "Branch", "1.0.0", "Branch.psd1")));
        Assert.True(File.Exists(Path.Combine(destination, "Leaf", "1.0.0", "Leaf.psd1")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Branch", "2.0.0")));
        Assert.False(Directory.Exists(Path.Combine(destination, "Leaf", "2.0.0")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestoreRejectsControlFilesInsideDestination(bool putLockInDestination)
    {
        using var temp = new TemporaryDirectory();
        var destination = Path.Combine(temp.Path, "modules");
        var inside = Path.Combine(destination, "App", "1.0.0", "App.psm1");
        var outside = Path.Combine(temp.Path, "modules.json");
        var requirements = putLockInDestination ? outside : inside;
        var lockPath = putLockInDestination ? inside : outside;

        using var result = await RunCliAsync(temp.Path, 2,
            ["--requirements", requirements, "--lock-file", lockPath, "--destination", destination + " ", "--write-lock"]);
        Assert.Contains("outside the module destination", result.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task AllowClobberAppliesToEveryPackageInTheRestore()
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "modules");
        const string exports = "FunctionsToExport = '*'";
        WritePackage(feed, "App", "1.0.0", new TestDependency("Leaf", "[1.0.0]", null), manifestExports: exports);
        WritePackage(feed, "Leaf", "1.0.0", manifestExports: exports);
        File.WriteAllText(requirements, """{"App":"1.0.0"}""");
        var command = new[] { "--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed, "--write-lock" };

        using var refused = await RunCliAsync(temp.Path, 1, command);
        Assert.Contains("wildcard export clobber", refused.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(lockPath));

        using var allowed = await RunCliAsync(temp.Path, 0, [.. command, "--allow-clobber"]);
        Assert.True(File.Exists(Path.Combine(destination, "Leaf", "1.0.0", "Leaf.psd1")));
        Assert.True(File.Exists(Path.Combine(destination, "App", "1.0.0", "App.psd1")));
        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public async Task RestoreUsesManifestVersionFolderAndRefusesDifferentPackageInThatFolder()
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "modules");
        WritePackage(feed, "App", "2.0.0-beta", manifestVersion: "2.0",
            manifestExports: "PrivateData = @{ PSData = @{ Prerelease = 'beta' } }");
        WritePackage(feed, "App", "2.0.0", manifestVersion: "2.0");
        var args = new[] { "--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed, "--write-lock" };

        File.WriteAllText(requirements, """{"App":"2.0.0-beta"}""");
        using var beta = await RunCliAsync(temp.Path, 0, args);
        var installed = Path.Combine(destination, "App", "2.0", "App.psm1");
        Assert.Equal("# App 2.0.0-beta", File.ReadAllText(installed));
        var originalLock = File.ReadAllText(lockPath);
        Assert.Contains("2.0.0-beta", originalLock, StringComparison.Ordinal);

        File.WriteAllText(requirements, """{"App":"2.0.0"}""");
        using var stable = await RunCliAsync(temp.Path, 1, args);
        Assert.Contains("different module package", stable.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal("# App 2.0.0-beta", File.ReadAllText(installed));
        Assert.Equal(originalLock, File.ReadAllText(lockPath));
    }

    [Theory]
    [InlineData(false, 2, "must be a file")]
    [InlineData(true, 1, "temporary directory must be outside")]
    public async Task RestoreRejectsInvalidWriteLocationsBeforeTouchingDestination(bool plan, int exitCode, string error)
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "modules");
        WritePackage(feed, "App", "1.0.0");
        File.WriteAllText(requirements, """{"App":"1.0.0"}""");
        var args = new[] { "--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed, "--write-lock" };
        if (plan)
        {
            Directory.CreateDirectory(destination);
            args = [.. args, "--plan"];
        }
        else
        {
            Directory.CreateDirectory(lockPath);
        }

        using var result = await RunCliAsync(temp.Path, exitCode, args, plan ? destination : null);
        Assert.Contains(error, result.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        if (plan)
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        else
            Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task RestoreRejectsBuildMetadataOnTransitivePackageBeforeWriting()
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "modules");
        WritePackage(feed, "App", "1.0.0", new TestDependency("Leaf", "[1.2.3]", null));
        WritePackage(feed, "Leaf", "1.2.3+ci", manifestVersion: "1.2.3");
        File.WriteAllText(requirements, """{"App":"1.0.0"}""");

        using var result = await RunCliAsync(temp.Path, 2,
            ["--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed, "--write-lock"]);
        Assert.Contains("'Leaf' requires an exact package version", result.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public async Task RestoreRequiresDependencyGuidInFreshAndFrozenResolution()
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "modules");
        const string guid = "11111111-1111-1111-1111-111111111111";
        WritePackage(feed, "App", "1.0.0", manifestExports:
            $"RequiredModules = @(@{{ ModuleName = 'Leaf'; RequiredVersion = '1.0.0'; GUID = '{guid}' }})");
        var originalLeaf = WritePackage(feed, "Leaf", "1.0.0", manifestExports: $"GUID = '{guid}'");
        File.WriteAllText(requirements, """{"App":"1.0.0"}""");
        var args = new[] { "--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed, "--plan" };
        var verified = Path.Combine(temp.Path, "verified");
        var install = new[] { "--requirements", requirements, "--lock-file", lockPath, "--destination", verified, "--source", feed };
        using var write = await RunCliAsync(temp.Path, 0, [.. install, "--write-lock"]);
        File.Delete(originalLeaf);
        var wrongPath = WritePackage(feed, "Leaf", "1.0.0", manifestExports: "GUID = '22222222-2222-2222-2222-222222222222'");
        var evidence = JsonNode.Parse(File.ReadAllText(lockPath))!;
        var leafPin = evidence["packages"]!.AsArray().Single(pin => (string?)pin!["name"] == "Leaf")!;
        leafPin["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(wrongPath))).ToLowerInvariant();
        File.WriteAllText(lockPath, evidence.ToJsonString());

        using var fresh = await RunCliAsync(temp.Path, 1, [.. args, "--write-lock"]);
        using var frozen = await RunCliAsync(temp.Path, 1,
            ["--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed]);
        Assert.Contains(guid, fresh.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Contains(guid, frozen.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoreOnlyAllowsPrereleaseWhenDiscoveredConstraintsPermitIt(bool siblingPinsPrerelease)
    {
        using var temp = new TemporaryDirectory();
        var feed = Path.Combine(temp.Path, "feed");
        var requirements = Path.Combine(temp.Path, "modules.json");
        var lockPath = Path.Combine(temp.Path, "modules.lock.json");
        var destination = Path.Combine(temp.Path, "modules");
        WritePackage(feed, "App", "1.0.0", new TestDependency("Alpha", "1.0.0", null),
            additionalDependency: new TestDependency("Zulu", "1.0.0", null));
        WritePackage(feed, "Zulu", "1.0.0", siblingPinsPrerelease ? new TestDependency("Alpha", "[2.0.0-beta]", null) : null);
        WritePackage(feed, "Alpha", "2.0.0-beta", manifestVersion: "2.0.0",
            manifestExports: "PrivateData = @{ PSData = @{ Prerelease = 'beta' } }");
        File.WriteAllText(requirements, """{"App":"1.0.0"}""");

        using var result = await RunCliAsync(temp.Path, siblingPinsPrerelease ? 0 : 1,
            ["--requirements", requirements, "--lock-file", lockPath, "--destination", destination, "--source", feed, "--write-lock"]);
        if (siblingPinsPrerelease)
        {
            Assert.Contains("2.0.0-beta", File.ReadAllText(lockPath), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(destination, "Alpha", "2.0.0", "Alpha.psd1")));
        }
        else
        {
            Assert.Contains("'Alpha'", result.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(destination));
        }
    }

    private static string WritePackage(string feed, string name, string version,
        TestDependency? dependency = null, string? content = null, string? manifestExports = null,
        string? manifestVersion = null, TestDependency? additionalDependency = null)
    {
        var path = Path.Combine(feed, $"{name}.{version}.nupkg");
        var dependencies = new List<TestDependency>();
        if (dependency is not null)
            dependencies.Add(dependency);
        if (additionalDependency is not null)
            dependencies.Add(additionalDependency);
        TestPackageFactory.Create(path, name, version,
            dependencies: dependencies,
            files: new Dictionary<string, string>
            {
                [name + ".psd1"] = $"@{{ ModuleVersion = '{manifestVersion ?? version}'; RootModule = '{name}.psm1'; {manifestExports} }}",
                [name + ".psm1"] = content ?? $"# {name} {version}"
            });
        return path;
    }

    private static async Task<JsonDocument> RunCliAsync(string currentDirectory, int expectedExitCode, string[] args, string? tempDirectory = null)
    {
        var root = PowerForgeCliTestHost.FindRepositoryRoot();
        var assembly = PowerForgeCliTestHost.ResolveAssemblyPath(root,
            Environment.GetEnvironmentVariable(PowerForgeCliTestHost.AssemblyPathEnvironmentVariable));
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = currentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (tempDirectory is not null)
        {
            startInfo.Environment["TMPDIR"] = tempDirectory;
            startInfo.Environment["TEMP"] = tempDirectory;
            startInfo.Environment["TMP"] = tempDirectory;
        }
        foreach (var argument in new[] { assembly, "module", "restore", "--output", "json" }.Concat(args))
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await stdout;
        Assert.True(process.ExitCode == expectedExitCode, $"Exit code {process.ExitCode}: {output}\n{await stderr}");
        return JsonDocument.Parse(output);
    }
}
