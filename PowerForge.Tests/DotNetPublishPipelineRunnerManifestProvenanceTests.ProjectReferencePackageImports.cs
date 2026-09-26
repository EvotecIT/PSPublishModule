using System.IO.Compression;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_PrGateVerifiedConditionalPackageImport()
        => ReadSourceProvenance_InspectsVerifiedConditionalPackageImports(false, false, false);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ReadSourceProvenance_InspectsVerifiedConditionalPackageImports(
        bool distinctPackageRoots, bool mutatesIntermediatePath, bool useStableAlias)
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
            string packageSource = Directory.CreateDirectory(Path.Combine(root, "package-src")).FullName;
            string feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
            string packageBuild = Directory.CreateDirectory(
                Path.Combine(packageSource, "buildTransitive")).FullName;
            File.WriteAllText(Path.Combine(packageSource, "Context.Package.nuspec"), """
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Context.Package</id>
                    <version>1.0.0</version>
                    <authors>PowerForge Tests</authors>
                    <description>Conditional package import fixture.</description>
                  </metadata>
                </package>
                """);
            string nestedImportPath = useStableAlias
                ? "$(ContextNestedPath)" : "Context.Nested.targets";
            string aliasProperty = useStableAlias
                ? "<PropertyGroup><ContextNestedPath>Context.Nested.targets</ContextNestedPath></PropertyGroup>"
                : string.Empty;
            File.WriteAllText(Path.Combine(packageBuild, "Context.Package.props"), $"""
                <Project>
                  {aliasProperty}
                  <Import Project="{nestedImportPath}"
                          Condition="'$(BuildProjectReferences)' == 'false' and '$(Flavor)' == 'Bridge'" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(packageBuild, "Context.Nested.targets"),
                mutatesIntermediatePath
                    ? "<Project><Target Name=\"MutatePackageContext\" BeforeTargets=\"CoreCompile\"><PropertyGroup><IntermediateOutputPath>$(MSBuildProjectDirectory)/../shared-obj/</IntermediateOutputPath></PropertyGroup></Target></Project>"
                    : "<Project><PropertyGroup><ContextNestedMarker>Loaded</ContextNestedMarker></PropertyGroup></Project>");
            ZipFile.CreateFromDirectory(packageSource,
                Path.Combine(feed, "Context.Package.1.0.0.nupkg"));
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), """
                <configuration>
                  <config><add key="globalPackagesFolder" value=".nuget/packages" /></config>
                  <packageSources>
                    <clear />
                    <add key="local" value="feed" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            string appProject = Path.Combine(appDirectory, "App.csproj");
            string bridgeProject = Path.Combine(bridgeDirectory, "Bridge.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Bridge/Bridge.csproj" />
                    <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="Flavor=Direct" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(bridgeProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="Flavor=Bridge" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(sharedProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <NuGetLockFilePath>packages.$(Flavor).lock.json</NuGetLockFilePath>
                    {(distinctPackageRoots ? "<RestorePackagesPath>$(MSBuildProjectDirectory)/../.nuget/packages.$(Flavor)</RestorePackagesPath>" : string.Empty)}
                  </PropertyGroup>
                  <ItemGroup><PackageReference Include="Context.Package" Version="1.0.0" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Bridge.Value + Shared.Value; } }");
            File.WriteAllText(Path.Combine(bridgeDirectory, "Bridge.cs"),
                "public static class Bridge { public static int Value => Shared.Value; }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"),
                "public static class Shared { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"),
                "bin/\nobj/\n.nuget/\npackage-src/\n");

            RunDotNet(root,
                $"restore \"{sharedProject}\" --use-lock-file --nologo -p:Flavor=Direct -p:TargetFrameworks=net10.0");
            RunDotNet(root,
                $"restore \"{sharedProject}\" --use-lock-file --nologo -p:Flavor=Bridge -p:TargetFrameworks=net8.0");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved package graph\"");
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
            plan.MsBuildProperties["BuildProjectReferences"] = "true";
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);
            if (mutatesIntermediatePath)
            {
                Assert.True(provenance.Dirty);
                Assert.Contains(provenance.DirtyReasons, reason => reason.Contains(
                    "target-time assignment to an isolation-sensitive property",
                    StringComparison.Ordinal));
            }
            else
            {
                Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            }
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
