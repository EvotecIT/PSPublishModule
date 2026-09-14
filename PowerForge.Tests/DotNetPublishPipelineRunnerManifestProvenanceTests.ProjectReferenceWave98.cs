using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_RestoresEverySelectedFrameworkForSharedMultiTargetReference(
        bool distinctRestoreContexts)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string bridgeDirectory = Directory.CreateDirectory(Path.Combine(root, "Bridge")).FullName;
            string sharedDirectory = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
            string leafDirectory = Directory.CreateDirectory(Path.Combine(root, "Leaf")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string bridgeProject = Path.Combine(bridgeDirectory, "Bridge.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            string leafProject = Path.Combine(leafDirectory, "Leaf.csproj");
            string directContext = distinctRestoreContexts
                ? " AdditionalProperties=\"Flavor=Direct\""
                : string.Empty;
            string bridgeContext = distinctRestoreContexts
                ? " AdditionalProperties=\"Flavor=Bridge\""
                : string.Empty;
            File.WriteAllText(appProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Bridge/Bridge.csproj" />
                    <ProjectReference Include="../Shared/Shared.csproj"{directContext} />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(bridgeProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj"{bridgeContext} /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(sharedProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Leaf/Leaf.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(leafProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Bridge.Value + Shared.Value; } }");
            File.WriteAllText(Path.Combine(bridgeDirectory, "Bridge.cs"),
                "public static class Bridge { public static int Value => Shared.Value; }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"),
                "public static class Shared { public static int Value => Leaf.Value; }");
            File.WriteAllText(Path.Combine(leafDirectory, "Leaf.cs"),
                "public static class Leaf { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved multi-framework graph\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
                "-m:1 -p:BuildInParallel=false " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                "/p:ContinuousIntegrationBuild=true /p:DebugType=None /p:DebugSymbols=false");
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
                        ProjectPath = appProject,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net10.0",
                                Runtime = "linux-x64",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("net8.0", false)]
    [InlineData("net8.0;net10.0", true)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledRestore_UsesOnlyDeclaredMultiTargetFrameworkMatrix(
        string? declaredTargetFrameworks,
        bool expectsMatrix)
    {
        var evaluatedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (declaredTargetFrameworks is not null)
            evaluatedProperties["TargetFrameworks"] = declaredTargetFrameworks;

        string[] frameworks = DotNetPublishPipelineRunner.SelectControlledMultiFrameworkRestoreFrameworks(
            evaluatedProperties);

        Assert.Equal(expectsMatrix, frameworks.Length > 1);
        if (expectsMatrix)
        {
            Assert.Equal(2, frameworks.Length);
            Assert.Contains("net8.0", frameworks);
            Assert.Contains("net10.0", frameworks);
        }
    }
}
