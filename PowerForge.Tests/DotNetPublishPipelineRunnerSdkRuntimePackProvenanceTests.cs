using System.Collections;
using System.Reflection;
using System.Text.Json;
using NuGet.Packaging;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_AcceptsVerifiedSdkRuntimePacksForSingleFilePublish()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string runtime = OperatingSystem.IsWindows()
                ? "win-x64"
                : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
            string targetFramework = OperatingSystem.IsWindows() ? "net10.0-windows" : "net10.0";
            string platformProperty = OperatingSystem.IsWindows()
                ? "<UseWindowsForms>true</UseWindowsForms>"
                : string.Empty;
            string privateFeed = Directory.CreateDirectory(Path.Combine(externalRoot, "feed")).FullName;
            string packageRoot = Directory.CreateDirectory(Path.Combine(externalRoot, "packages")).FullName;
            WriteTestPackage(Path.Combine(privateFeed, "Package.Snapshot.1.0.0.nupkg"), "private-feed");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>{{targetFramework}}</TargetFramework>
                    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                    {{platformProperty}}
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Package.Snapshot" Version="1.0.0" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(root, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            string nuGetConfigPath = Path.Combine(root, "NuGet.Config");
            File.WriteAllText(
                nuGetConfigPath,
                $$"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="private" value="{{privateFeed}}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                  </packageSources>
                </configuration>
                """);
            RunDotNet(
                root,
                $"restore \"{projectPath}\" -r {runtime} --use-lock-file --nologo " +
                $"--packages \"{packageRoot}\" --configfile \"{nuGetConfigPath}\"");
            string unavailableSource = Path.Combine(externalRoot, "unavailable-after-lock");
            File.WriteAllText(
                nuGetConfigPath,
                $$"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="unavailable" value="{{unavailableSource}}" />
                  </packageSources>
                </configuration>
                """);
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(
                root,
                $"build \"{projectPath}\" -c Release -f {targetFramework} -r {runtime} --no-restore --nologo " +
                "-p:SelfContained=true -p:PublishSingleFile=true");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Style = DotNetPublishStyle.Portable
                        },
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = targetFramework,
                                Runtime = runtime,
                                Style = DotNetPublishStyle.Portable
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SdkEvidenceProperties_ReplaySafePlanPropertiesWithoutSourceOverrides()
    {
        using JsonDocument properties = JsonDocument.Parse("{\"TargetFramework\":\"net10.0\"}");
        var arguments = new List<string>();
        var effectiveProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EnableWindowsTargeting"] = "true",
            ["TargetLatestRuntimePatch"] = "false",
            ["RestoreSources"] = "https://untrusted.example.invalid/v3/index.json"
        };
        MethodInfo append = typeof(DotNetPublishPipelineRunner).GetMethod(
            "AppendSdkEvidenceProperties",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo appendOwned = typeof(DotNetPublishPipelineRunner).GetMethod(
            "AppendSdkEvidenceOwnedProperties",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo appendSyntheticIsolation = typeof(DotNetPublishPipelineRunner).GetMethod(
            "AppendSyntheticSdkEvidenceProjectIsolationProperties",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        append.Invoke(null, [arguments, properties.RootElement, effectiveProperties]);
        appendOwned.Invoke(
            null,
            [arguments, "C:\\isolated\\obj\\", "C:\\isolated\\NuGet.Config", "C:\\isolated\\verified"]);
        appendSyntheticIsolation.Invoke(null, [arguments]);

        Assert.Contains("-p:EnableWindowsTargeting=true", arguments);
        Assert.Contains("-p:TargetLatestRuntimePatch=false", arguments);
        Assert.Contains("-p:TargetFramework=net10.0", arguments);
        Assert.Single(arguments, value => value.StartsWith("-p:RestoreSources=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("-p:RestoreSources=C:\\isolated\\verified", arguments);
        Assert.Contains("-p:ImportDirectoryBuildProps=false", arguments);
        Assert.Contains("-p:ImportDirectoryBuildTargets=false", arguments);
        Assert.Contains("-p:ImportDirectoryPackagesProps=false", arguments);
    }

    [Theory]
    [InlineData("Microsoft.NET.ILLink.Tasks", true)]
    [InlineData("Package.Snapshot", false)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SdkEvidence_AutoReferencedBypassRequiresSdkOwnedPackageIdentity(
        string packageId,
        bool expected)
    {
        MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
            "IsTrustedSdkAutoReferencedPackageId",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Equal(expected, Assert.IsType<bool>(method.Invoke(null, [packageId])));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void VerifiedPackageArchiveCache_DeduplicatesIdenticalEvidenceArchivesByContentHash()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string firstPath = Path.Combine(root, "first.nupkg");
            string secondPath = Path.Combine(root, "second.nupkg");
            WriteTestPackage(firstPath, "deduplicated");
            File.Copy(firstPath, secondPath);
            string contentHash;
            using (FileStream stream = File.OpenRead(firstPath))
            using (var reader = new PackageArchiveReader(stream, leaveStreamOpen: false))
                contentHash = reader.GetContentHash(CancellationToken.None);

            Type cacheType = typeof(DotNetPublishPipelineRunner).GetNestedType(
                "VerifiedPackageArchiveCache",
                BindingFlags.NonPublic)!;
            object cache = Activator.CreateInstance(cacheType, nonPublic: true)!;
            try
            {
                MethodInfo open = cacheType.GetMethod(
                    "TryGetOrOpen",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.NotNull(open.Invoke(cache, [firstPath, contentHash]));
                Assert.NotNull(open.Invoke(cache, [secondPath, contentHash]));

                var paths = Assert.IsAssignableFrom<IDictionary>(cacheType
                    .GetField("_archives", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(cache));
                var hashes = Assert.IsAssignableFrom<IDictionary>(cacheType
                    .GetField("_archivesByContentHash", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(cache));
                Assert.Equal(2, paths.Count);
                Assert.Single(hashes.Keys.Cast<object>());
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
