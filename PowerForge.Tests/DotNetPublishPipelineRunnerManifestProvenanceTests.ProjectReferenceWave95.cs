using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_AcceptsCrossTargetDiamondWithMultiTargetLeaf()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string coreDirectory = Directory.CreateDirectory(Path.Combine(root, "Core")).FullName;
            string signingDirectory = Directory.CreateDirectory(Path.Combine(root, "Signing")).FullName;
            string verificationDirectory = Directory.CreateDirectory(Path.Combine(root, "Verification")).FullName;
            string inputsDirectory = Directory.CreateDirectory(Path.Combine(root, "Inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string coreProject = Path.Combine(coreDirectory, "Core.csproj");
            string signingProject = Path.Combine(signingDirectory, "Signing.csproj");
            string verificationProject = Path.Combine(verificationDirectory, "Verification.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <EnableWindowsTargeting>true</EnableWindowsTargeting>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Core/Core.csproj" />
                    <ProjectReference Include="../Signing/Signing.csproj" />
                    <ProjectReference Include="../Verification/Verification.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(coreProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Verification/Verification.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(signingProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Core/Core.csproj" />
                    <ProjectReference Include="../Verification/Verification.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(verificationProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="Frameworks.props" />
                  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
                    <Compile Include="../Inputs/NetStandardOnly.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <Compile Include="../Inputs/NetEightOnly.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
                    <Compile Include="../Inputs/NetTenOnly.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
                    <Compile Include="../Inputs/ForcedNetNineOnly.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(verificationDirectory, "Frameworks.props"), """
                <Project>
                  <PropertyGroup>
                    <ProductTargetFrameworks>netstandard2.0;net8.0;net10.0</ProductTargetFrameworks>
                    <TargetFrameworks>$(ProductTargetFrameworks)</TargetFrameworks>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(coreDirectory, "Core.cs"), "public static class Core { }");
            File.WriteAllText(Path.Combine(signingDirectory, "Signing.cs"), "public static class Signing { }");
            File.WriteAllText(Path.Combine(verificationDirectory, "Verification.cs"), "public static class Verification { }");
            string netStandardInput = Path.Combine(inputsDirectory, "NetStandardOnly.cs");
            string netEightInput = Path.Combine(inputsDirectory, "NetEightOnly.cs");
            string netTenInput = Path.Combine(inputsDirectory, "NetTenOnly.cs");
            string forcedNetNineInput = Path.Combine(inputsDirectory, "ForcedNetNineOnly.cs");
            File.WriteAllText(netStandardInput, "public static class NetStandardOnly { }");
            File.WriteAllText(netEightInput, "public static class NetEightOnly { }");
            File.WriteAllText(netTenInput, "public static class NetTenOnly { }");
            File.WriteAllText(forcedNetNineInput, "public static class ForcedNetNineOnly { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo -r win-x64");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved cross-target diamond\"");
            File.WriteAllText(netStandardInput, "public static class NetStandardOnly { public const int Changed = 1; }");
            File.WriteAllText(netEightInput, "public static class NetEightOnly { public const int Changed = 1; }");
            File.WriteAllText(netTenInput, "public static class NetTenOnly { public const int Changed = 1; }");
            File.WriteAllText(forcedNetNineInput, "public static class ForcedNetNineOnly { public const int Changed = 1; }");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
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
                                Framework = "net10.0-windows",
                                Runtime = "win-x64",
                                Style = DotNetPublishStyle.PortableCompat
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(provenance.Dirty);
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/NetEightOnly.cs", StringComparison.Ordinal));
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/NetTenOnly.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/NetStandardOnly.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/ForcedNetNineOnly.cs", StringComparison.Ordinal));

            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <EnableWindowsTargeting>true</EnableWindowsTargeting>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Core/Core.csproj" />
                    <ProjectReference Include="../Signing/Signing.csproj" />
                    <ProjectReference Include="../Verification/Verification.csproj" />
                    <ProjectReference Include="../Verification/Verification.csproj"
                                      Properties="TargetFramework=net9.0"
                                      ReferenceOutputAssembly="false"
                                      BuildReference="false" />
                  </ItemGroup>
                </Project>
                """);
            RunGit(root, "add App/App.csproj");
            RunGit(root, "commit -m \"add non-output forced framework edge\"");

            DotNetPublishPipelineRunner.SourceProvenance withForcedSibling =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(withForcedSibling.Dirty);
            Assert.DoesNotContain(withForcedSibling.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/ForcedNetNineOnly.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
