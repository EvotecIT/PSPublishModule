using System.Reflection;
using NuGet.Packaging;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("CustomAfterMicrosoftCommonTargets", "untrusted.targets")]
    [InlineData("MSBuildSDKsPath", "sdks")]
    [InlineData("MSBuildToolsPath", "tools")]
    public void ControlledBuildInputs_RejectControlledReferenceToolchainOverride(
        string propertyName,
        string propertyValue)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                $"<Project><PropertyGroup><{propertyName}>{propertyValue}</{propertyName}></PropertyGroup></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("CustomAfterMicrosoftCommonTargets")]
    [InlineData("MSBuildSDKsPath")]
    [InlineData("MSBuildToolsPath")]
    [InlineData("RestoreRecursive")]
    public void ControlledBuildInputs_RejectControlledReferenceBoundaryPropertyEscape(
        string propertyName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                $"<Project TreatAsLocalProperty=\"{propertyName}\" />");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("BeforeTargets")]
    [InlineData("AfterTargets")]
    [InlineData("DependsOnTargets")]
    public void ReadSourceProvenance_ExecutesReferenceHooksOnlyInControlledCheckout(
        string schedulingMode)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string schedulingProperty = schedulingMode == "DependsOnTargets"
                ? "<ResolveReferencesDependsOn>ObserveResolveReferences;$(ResolveReferencesDependsOn)</ResolveReferencesDependsOn>"
                : string.Empty;
            string schedulingAttribute = schedulingMode == "DependsOnTargets"
                ? string.Empty
                : $" {schedulingMode}=\"ResolveReferences\"";
            File.WriteAllText(
                appProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    {schedulingProperty}
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  <Target Name="ObserveResolveReferences"{schedulingAttribute}>
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/resolve-marker.txt"
                                      Lines="ran"
                                      Overwrite="true" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.False(File.Exists(Path.Combine(appDirectory, "resolve-marker.txt")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void VerifiedPackageCatalog_SkipsInvalidDuplicateBeforeValidLockedArchive()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            const string packageId = "Package.Duplicate";
            const string packageVersion = "1.0.0";
            string packageName = packageId + "." + packageVersion + ".nupkg";
            string invalidRoot = Directory.CreateDirectory(Path.Combine(root, "invalid")).FullName;
            string validRoot = Directory.CreateDirectory(Path.Combine(root, "valid")).FullName;
            string invalidDirectory = Directory.CreateDirectory(Path.Combine(
                invalidRoot,
                packageId.ToLowerInvariant(),
                packageVersion)).FullName;
            string validDirectory = Directory.CreateDirectory(Path.Combine(
                validRoot,
                packageId.ToLowerInvariant(),
                packageVersion)).FullName;
            File.WriteAllText(Path.Combine(invalidDirectory, packageName), "not a package");
            string validPackagePath = Path.Combine(validDirectory, packageName);
            WriteTestPackage(validPackagePath, "approved");
            string contentHash;
            using (FileStream packageStream = File.OpenRead(validPackagePath))
            using (var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: false))
                contentHash = packageReader.GetContentHash(CancellationToken.None);

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
                MethodInfo prime = catalogType.GetMethod(
                    "TryPrimeLockedPackageArchives",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                object?[] arguments =
                {
                    new[] { invalidRoot, validRoot },
                    new Dictionary<string, string>
                    {
                        [packageId + "|" + packageVersion] = contentHash
                    },
                    cache,
                    null
                };

                Assert.True((bool)prime.Invoke(null, arguments)!);
                string selectedPath = Assert.Single(Assert.IsType<string[]>(arguments[3]));
                Assert.Equal(Path.GetFullPath(validPackagePath), Path.GetFullPath(selectedPath));
            }
            finally
            {
                ((IDisposable)cache).Dispose();
            }
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
