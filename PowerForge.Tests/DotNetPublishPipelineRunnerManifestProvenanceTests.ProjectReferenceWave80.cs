using System.Security.Cryptography;
using System.Xml.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectPropertyAssignmentAfterConsumingTask()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkedPath = Path.Combine(root, "payload-link.txt");
            string safePath = Path.Combine(root, "safe.txt");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(externalPath, "external payload");
            File.WriteAllText(safePath, "safe payload");
            try
            {
                File.CreateSymbolicLink(linkedPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <PropertyGroup><Files>payload-link.txt</Files></PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(Files)" DestinationFolder="output" />
                    <PropertyGroup><Files>safe.txt</Files></PropertyGroup>
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, safePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptPropertyAssignmentBeforeConsumingTask()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string safePath = Path.Combine(root, "safe.txt");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(safePath, "safe payload");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <PropertyGroup><Files>unselected.txt</Files></PropertyGroup>
                  <Target Name="Build">
                    <PropertyGroup><Files>safe.txt</Files></PropertyGroup>
                    <Copy SourceFiles="$(Files)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, safePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Run_UnsignedBuildPublishAllowsDirtyDevelopmentCheckout()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            string sourcePath = Path.Combine(root, "Program.cs");
            string outputPath = Path.Combine(root, "publish");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(sourcePath, "System.Console.WriteLine(\"approved\");");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npublish/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(sourcePath, "System.Console.WriteLine(\"dirty development build\");");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath,
                        Publish = new DotNetPublishPublishOptions
                        {
                            OutputPath = outputPath,
                            Style = DotNetPublishStyle.FrameworkDependent,
                            Sign = new DotNetPublishSignOptions { Enabled = false }
                        },
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Runtime = string.Empty,
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "App",
                        Framework = "net8.0",
                        Runtime = string.Empty,
                        Style = DotNetPublishStyle.FrameworkDependent
                    }
                ]
            };

            DotNetPublishResult result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(File.Exists(Path.Combine(outputPath, "App.dll")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_EscapesMsBuildExpressionsInOriginalPath()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string expressionDirectory = Directory.CreateDirectory(
                Path.Combine(root, "$(Build.SourcesDirectory)@(Items)")).FullName;
            string sourcePath = Path.Combine(expressionDirectory, "Library.dll");
            byte[] bytes = "controlled-library"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "Library.dll",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(bytes)));

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], existingCustomAfterTargets: null);
            string targets = File.ReadAllText(snapshot.TargetsPath);
            string escapedPath = sourcePath
                .Replace("%", "%25")
                .Replace("$", "%24")
                .Replace("@", "%40")
                .Replace("'", "%27");
            string[] conditions = XDocument.Parse(targets).Descendants()
                .Attributes("Condition")
                .Select(attribute => attribute.Value)
                .ToArray();

            Assert.Contains(conditions, condition => condition.Contains(escapedPath, StringComparison.Ordinal));
            Assert.DoesNotContain(conditions, condition => condition.Contains(sourcePath, StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
