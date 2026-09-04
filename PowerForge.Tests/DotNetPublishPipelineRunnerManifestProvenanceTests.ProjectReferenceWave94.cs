using System.IO.Compression;
using System.Reflection;
using NuGet.Packaging;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledMsBuildEvaluation_UsesResponseFileBeyondWindowsCommandLineLimit()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                "<Project><PropertyGroup><PowerForgeReady>true</PowerForgeReady></PropertyGroup></Project>");
            var arguments = new List<string>
            {
                "msbuild",
                projectPath,
                "-nologo",
                "-verbosity:quiet",
                "-getProperty:PowerForgeReady",
                "-p:PowerForgeLongProperty=" + new string('x', 40000)
            };

            var result = DotNetPublishPipelineRunner.RunControlledMsBuildEvaluationProcess(
                root,
                arguments,
                new Dictionary<string, string?>(),
                TimeSpan.FromMinutes(2),
                root);

            Assert.False(result.TimedOut);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("true", result.StdOut, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFiles(root, "controlled-msbuild-*.rsp"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void VerifiedPackageArchive_AcceptsInactiveAnalyzerConfigAndEvaluatedSourceItemFlow()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.csproj");
            string packagePath = Path.Combine(root, "Controlled.Analyzers.1.0.0.nupkg");
            File.WriteAllText(projectPath, "<Project />");
            WriteControlledAnalyzerPackage(packagePath);

            string contentHash;
            using (FileStream packageStream = File.OpenRead(packagePath))
            using (var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: false))
                contentHash = packageReader.GetContentHash(CancellationToken.None);

            Type archiveType = typeof(DotNetPublishPipelineRunner).GetNestedType(
                "VerifiedPackageArchive",
                BindingFlags.NonPublic)!;
            using IDisposable archive = (IDisposable)archiveType.GetMethod(
                    "TryOpen",
                    BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [packagePath, contentHash])!;
            bool accepted = (bool)archiveType.GetMethod(
                    "HasOnlyControlledBuildInputs",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(
                    archive,
                    [
                        new[]
                        {
                            "buildTransitive/Controlled.Analyzers.props",
                            "buildTransitive/Controlled.Analyzers.targets"
                        },
                        root,
                        projectPath,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["_GlobalAnalyzerConfigFile_ControlledAnalyzers"] = string.Empty
                        }
                    ])!;

            Assert.True(accepted);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static void WriteControlledAnalyzerPackage(string packagePath)
    {
        using FileStream stream = File.Open(packagePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(
            "Controlled.Analyzers.nuspec",
            "<package><metadata><id>Controlled.Analyzers</id><version>1.0.0</version><authors>PowerForge</authors><description>Controlled analyzer fixture</description></metadata></package>");
        WriteEntry("buildTransitive/Controlled.Analyzers.props", "<Project />");
        WriteEntry(
            "buildTransitive/Controlled.Analyzers.targets",
            """
            <Project>
              <Target Name="AddControlledAnalyzerInputs" BeforeTargets="CoreCompile">
                <PropertyGroup>
                  <_GlobalAnalyzerConfigFile_ControlledAnalyzers Condition="'$(_GlobalAnalyzerConfigFile_ControlledAnalyzers)' == 'configured'">$(MSBuildThisFileDirectory)config/rules.globalconfig</_GlobalAnalyzerConfigFile_ControlledAnalyzers>
                </PropertyGroup>
                <ItemGroup Condition="Exists('$(_GlobalAnalyzerConfigFile_ControlledAnalyzers)')">
                  <EditorConfigFiles Include="$(_GlobalAnalyzerConfigFile_ControlledAnalyzers)" />
                </ItemGroup>
                <ItemGroup>
                  <EmbeddedResourceWithResxExtension Include="@(EmbeddedResource)" Condition="'%(Extension)' == '.resx'" />
                  <AdditionalFiles Include="%(EmbeddedResourceWithResxExtension.Identity)" />
                </ItemGroup>
              </Target>
            </Project>
            """);

        void WriteEntry(string name, string value)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(value);
        }
    }
}
