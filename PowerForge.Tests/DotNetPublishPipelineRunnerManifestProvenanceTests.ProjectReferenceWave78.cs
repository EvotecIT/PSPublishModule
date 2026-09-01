using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Packaging;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void GitExecution_RejectsUnsignedConfiguredToolchain()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previous = Environment.GetEnvironmentVariable("POWERFORGE_GIT_PATH");
        try
        {
            string configuredPath = Path.Combine(root, OperatingSystem.IsWindows() ? "git.exe" : "git");
            File.WriteAllText(configuredPath, "untrusted executable");
            Environment.SetEnvironmentVariable("POWERFORGE_GIT_PATH", configuredPath);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                DotNetPublishPipelineRunner.ResolveRunGitExecutablePath);

            Assert.Contains("trusted Git executable", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_GIT_PATH", previous);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void GitExecution_RejectsExecutableChangedAfterAdmission()
    {
        string resolved = DotNetPublishPipelineRunner.ResolveRunGitExecutablePath();
        string actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(resolved)));

        DotNetPublishPipelineRunner.ValidateGitExecutableSnapshot(resolved, actualSha256);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DotNetPublishPipelineRunner.ValidateGitExecutableSnapshot(
                resolved,
                new string('0', actualSha256.Length)));

        Assert.Contains("changed after admission", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeAotEnvironment_RejectsExplicitPath()
    {
        var plan = new DotNetPublishPlan
        {
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = "untrusted-native-toolchain"
            },
            Targets =
            [
                new DotNetPublishTargetPlan
                {
                    Combinations =
                    [
                        new DotNetPublishTargetCombination
                        {
                            Style = DotNetPublishStyle.AotSpeed
                        }
                    ]
                }
            ]
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DotNetPublishPipelineRunner.ValidateNativeAotEnvironmentVariables(plan));

        Assert.Contains("PATH", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NativeAOT", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlledBuildInputs_AcceptUnreachableManualExecTarget()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Deploy\"><Exec Command=\"unreachable\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectManualExecTargetReachableBeforeBuild()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Deploy\" BeforeTargets=\"Build\"><Exec Command=\"reachable\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectManualExecTargetReachableFromBuildDependency()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\" DependsOnTargets=\"Deploy\" /><Target Name=\"Deploy\"><Exec Command=\"reachable\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void VerifiedPackageCatalog_FailsWhenLockedArchiveCannotBePrimed()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            string packageRoot = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(
                Path.Combine(projectDirectory, "packages.lock.json"),
                """
                {
                  "version": 1,
                  "dependencies": {
                    "net8.0": {
                      "Missing.Package": {
                        "type": "Direct",
                        "resolved": "1.0.0",
                        "contentHash": "sha512-missing"
                      }
                    }
                  }
                }
                """);
            using JsonDocument propertiesDocument = JsonDocument.Parse("{}");
            Type runnerType = typeof(DotNetPublishPipelineRunner);
            Type catalogType = runnerType.GetNestedType("VerifiedPackageInputCatalog", BindingFlags.NonPublic)!;
            Type cacheType = runnerType.GetNestedType("VerifiedPackageArchiveCache", BindingFlags.NonPublic)!;
            object cache = Activator.CreateInstance(cacheType, nonPublic: true)!;
            try
            {
                MethodInfo create = catalogType.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
                object?[] arguments = [projectPath, propertiesDocument.RootElement, new[] { packageRoot }, cache, null];

                Assert.False((bool)create.Invoke(null, arguments)!);
                Assert.Null(arguments[4]);
            }
            finally
            {
                (cache as IDisposable)?.Dispose();
            }
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void VerifiedPackageCatalog_AllowsMissingAssetsOnlySdkArchiveWithoutTrustingIt()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            string packageRoot = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");
            string objDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "obj")).FullName;
            File.WriteAllText(
                Path.Combine(objDirectory, "project.assets.json"),
                """
                {
                  "project": {
                    "frameworks": {
                      "net8.0": {
                        "dependencies": {
                          "Runtime.Pack": { "autoReferenced": true }
                        }
                      }
                    }
                  },
                  "libraries": {
                    "Runtime.Pack/1.0.0": {
                      "type": "package",
                      "sha512": "sha512-assets-only"
                    }
                  }
                }
                """);
            using JsonDocument propertiesDocument = JsonDocument.Parse("{}");
            Type runnerType = typeof(DotNetPublishPipelineRunner);
            Type catalogType = runnerType.GetNestedType("VerifiedPackageInputCatalog", BindingFlags.NonPublic)!;
            Type cacheType = runnerType.GetNestedType("VerifiedPackageArchiveCache", BindingFlags.NonPublic)!;
            object cache = Activator.CreateInstance(cacheType, nonPublic: true)!;
            try
            {
                MethodInfo create = catalogType.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
                object?[] arguments = [projectPath, propertiesDocument.RootElement, new[] { packageRoot }, cache, null];

                Assert.True((bool)create.Invoke(null, arguments)!);
                Assert.NotNull(arguments[4]);
                object catalog = arguments[4]!;
                var archivesByPackageKey = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                    catalogType.GetField("_archivePathsByPackageKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(catalog));
                Assert.Empty(archivesByPackageKey);
            }
            finally
            {
                (cache as IDisposable)?.Dispose();
            }
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void VerifiedPackageCatalog_IgnoresPresentAssetsOnlyAutoReferencedArchive()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string packageId = "Runtime.Pack";
            const string packageVersion = "1.0.0";
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            string packageRoot = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
            string packageDirectory = Directory.CreateDirectory(Path.Combine(
                packageRoot,
                packageId.ToLowerInvariant(),
                packageVersion)).FullName;
            string packagePath = Path.Combine(
                packageDirectory,
                packageId + "." + packageVersion + ".nupkg");
            WriteTestPackage(packagePath, "assets-only");
            string contentHash;
            using (FileStream stream = File.OpenRead(packagePath))
            using (var reader = new PackageArchiveReader(stream, leaveStreamOpen: false))
                contentHash = reader.GetContentHash(CancellationToken.None);

            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");
            string objDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "obj")).FullName;
            File.WriteAllText(
                Path.Combine(objDirectory, "project.assets.json"),
                $$"""
                {
                  "project": {
                    "frameworks": {
                      "net8.0": {
                        "dependencies": {
                          "{{packageId}}": { "autoReferenced": true }
                        }
                      }
                    }
                  },
                  "libraries": {
                    "{{packageId}}/{{packageVersion}}": {
                      "type": "package",
                      "sha512": "{{contentHash}}"
                    }
                  }
                }
                """);
            using JsonDocument propertiesDocument = JsonDocument.Parse("{}");
            Type runnerType = typeof(DotNetPublishPipelineRunner);
            Type catalogType = runnerType.GetNestedType(
                "VerifiedPackageInputCatalog",
                BindingFlags.NonPublic)!;
            Type cacheType = runnerType.GetNestedType(
                "VerifiedPackageArchiveCache",
                BindingFlags.NonPublic)!;
            object cache = Activator.CreateInstance(cacheType, nonPublic: true)!;
            try
            {
                MethodInfo create = catalogType.GetMethod(
                    "TryCreate",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                object?[] arguments =
                    [projectPath, propertiesDocument.RootElement, new[] { packageRoot }, cache, null];

                Assert.True((bool)create.Invoke(null, arguments)!);
                Assert.NotNull(arguments[4]);
                object catalog = arguments[4]!;
                var archivesByPackageKey = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                    catalogType.GetField(
                            "_archivePathsByPackageKey",
                            BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(catalog));
                Assert.Empty(archivesByPackageKey);
            }
            finally
            {
                (cache as IDisposable)?.Dispose();
            }
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
