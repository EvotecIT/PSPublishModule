using System.Security.Cryptography;
using System.Xml.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void NativeAotEnvironment_PreservesOnlyTrustedNativeToolchainDirectories()
    {
        string untrustedDirectory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string dotNetPath = DotNetPublishPipelineRunner.ResolveRunDotNetExecutablePath();
            string dotNetDirectory = Path.GetDirectoryName(dotNetPath)!;
            string trustedDirectory = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.System)
                : "/usr/bin";
            string inheritedPath = string.Join(
                Path.PathSeparator.ToString(),
                new[] { untrustedDirectory, trustedDirectory });

            string actual = DotNetPublishPipelineRunner.BuildTrustedNativeAotPath(
                dotNetPath,
                inheritedPath);
            string[] entries = actual.Split(Path.PathSeparator);

            Assert.Contains(entries, entry => PathsEqual(entry, dotNetDirectory));
            Assert.Contains(entries, entry => PathsEqual(entry, trustedDirectory));
            Assert.DoesNotContain(entries, entry => PathsEqual(entry, untrustedDirectory));
        }
        finally
        {
            DeleteTestRepository(untrustedDirectory);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_PreservesEvaluatedCustomAfterTargets()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Library.dll");
            string customTargetsPath = Path.Combine(root, "Custom.After.targets");
            byte[] bytes = "controlled-library"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            File.WriteAllText(customTargetsPath, "<Project />");
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "Library.dll",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(bytes)),
                customTargetsPath);

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create(
                    [input],
                    input.CustomAfterMicrosoftCommonTargets);
            XDocument targets = XDocument.Load(snapshot.TargetsPath);

            Assert.Equal(
                customTargetsPath,
                targets.Root?.Element("Import")?.Attribute("Project")?.Value);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_UsesUnpredictableBindingTargetName()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Library.dll");
            byte[] bytes = "controlled-library"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "Library.dll",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(bytes)));

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot first =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null);
            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot second =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null);
            string firstName = XDocument.Load(first.TargetsPath).Root!
                .Element("Target")!
                .Attribute("Name")!
                .Value;
            string secondName = XDocument.Load(second.TargetsPath).Root!
                .Element("Target")!
                .Attribute("Name")!
                .Value;

            Assert.StartsWith("_PowerForgeBindNoBuildPublishInputs_", firstName, StringComparison.Ordinal);
            Assert.NotEqual("_PowerForgeBindNoBuildPublishInputs", firstName);
            Assert.NotEqual(firstName, secondName);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_CapturesDuplicateSourceOnceAndPreservesDestinations()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Library.dll");
            byte[] bytes = "controlled-library"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var first = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "first/Library.dll",
                new Dictionary<string, string>(),
                sha256);
            var second = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "second/Library.dll",
                new Dictionary<string, string>(),
                sha256);

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([first, second], null);
            string[] snapshotFiles = Directory.GetFiles(
                Path.Combine(Path.GetDirectoryName(snapshot.TargetsPath)!, "inputs"),
                "*",
                SearchOption.AllDirectories);
            XDocument targets = XDocument.Load(snapshot.TargetsPath);
            XElement[] replacements = targets.Descendants("ResolvedFileToPublish")
                .Where(element => element.Attribute("Include") is not null)
                .ToArray();

            Assert.Single(snapshotFiles);
            Assert.Equal(2, replacements.Length);
            Assert.All(replacements, replacement =>
                Assert.Equal(snapshotFiles[0], replacement.Attribute("Include")?.Value));
            Assert.Equal(
                new[] { "first/Library.dll", "second/Library.dll" },
                replacements.Select(replacement => replacement.Element("RelativePath")?.Value).ToArray());
            Assert.Single(
                targets.Descendants("ResolvedFileToPublish"),
                element => element.Attribute("Remove") is not null);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void Run_NoBuildPublishPreservesProjectEvaluatedCustomAfterTargets()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string customTargetsPath = Path.Combine(root, "Custom.After.targets");
            string markerPath = Path.Combine(root, "marker.txt");
            File.WriteAllText(markerPath, "custom-after-marker");
            File.WriteAllText(
                customTargetsPath,
                """
                <Project>
                  <Target Name="AddCustomMarker" AfterTargets="ComputeFilesToPublish">
                    <ItemGroup>
                      <ResolvedFileToPublish Include="$(MSBuildProjectDirectory)/marker.txt" RelativePath="marker.txt" />
                    </ItemGroup>
                  </Target>
                </Project>
                """);
            DotNetPublishResult result = RunNoBuildSnapshotScenario(
                root,
                "<CustomAfterMicrosoftCommonTargets>$(MSBuildProjectDirectory)/Custom.After.targets</CustomAfterMicrosoftCommonTargets>",
                targetXml: null,
                out string outputDirectory,
                out _);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(
                "custom-after-marker",
                File.ReadAllText(Path.Combine(outputDirectory, "marker.txt")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void Run_NoBuildPublishCannotHookReservedSnapshotTarget()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string replacementPath = Path.Combine(root, "replacement.dll");
            File.WriteAllText(replacementPath, "unproven replacement");
            DotNetPublishResult result = RunNoBuildSnapshotScenario(
                root,
                propertyXml: null,
                """
                <Target Name="ReplaceBoundApp" AfterTargets="_PowerForgeBindNoBuildPublishInputs">
                  <ItemGroup>
                    <ResolvedFileToPublish Remove="@(ResolvedFileToPublish)" Condition="'%(ResolvedFileToPublish.RelativePath)' == 'App.dll'" />
                    <ResolvedFileToPublish Include="$(MSBuildProjectDirectory)/replacement.dll" RelativePath="App.dll" />
                  </ItemGroup>
                </Target>
                """,
                out string outputDirectory,
                out byte[] provenAppBytes);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(provenAppBytes, File.ReadAllBytes(Path.Combine(outputDirectory, "App.dll")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static DotNetPublishResult RunNoBuildSnapshotScenario(
        string root,
        string? propertyXml,
        string? targetXml,
        out string outputDirectory,
        out byte[] provenAppBytes,
        Func<string, string, byte[], IProcessRunner>? processRunnerFactory = null)
    {
        RunGit(root, "init");
        RunGit(root, "config user.name \"PowerForge Tests\"");
        RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
        string projectPath = Path.Combine(root, "App.csproj");
        outputDirectory = Path.Combine(root, "publish");
        File.WriteAllText(
            projectPath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                {{propertyXml}}
              </PropertyGroup>
              {{targetXml}}
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.cs"), "System.Console.WriteLine(\"approved\");");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npublish/\n");
        RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
        RunGit(root, "add .");
        RunGit(root, "commit -m \"approved source\"");
        string revision = RunGit(root, "rev-parse HEAD").Trim();
        RunDotNet(
            root,
            $"build \"{projectPath}\" -c Release -f net8.0 --no-restore --nologo " +
            $"/p:SourceRevisionId={revision} " +
            "/p:IncludeSourceRevisionInInformationalVersion=true");
        provenAppBytes = File.ReadAllBytes(Path.Combine(root, "bin", "Release", "net8.0", "App.dll"));
        var plan = new DotNetPublishPlan
        {
            ProjectRoot = root,
            Configuration = "Release",
            SourceRevision = revision,
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
                        OutputPath = outputDirectory,
                        Style = DotNetPublishStyle.FrameworkDependent
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
        IProcessRunner? processRunner = processRunnerFactory?.Invoke(
            projectPath,
            Path.Combine(root, "bin", "Release", "net8.0", "App.dll"),
            provenAppBytes);
        DotNetPublishPipelineRunner runner = processRunner is null
            ? new DotNetPublishPipelineRunner(new NullLogger())
            : new DotNetPublishPipelineRunner(new NullLogger(), processRunner);
        return runner.Run(plan, progress: null);
    }

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
