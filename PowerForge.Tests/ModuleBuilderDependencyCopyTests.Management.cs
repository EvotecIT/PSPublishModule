using System.Reflection;

namespace PowerForge.Tests;

public sealed partial class ModuleBuilderDependencyCopyTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("net8.0", true, true, false, true)]
    [InlineData("net8.0", true, false, false, true)]
    [InlineData("net8.0", false, true, false, false)]
    [InlineData("netstandard2.0", true, true, false, false)]
    [InlineData("net8.0", true, true, true, false)]
    public void BuildInPlace_PreservesPrivateManagementOnlyForIsolatedCorePayloads(
        string framework, bool isolated, bool deps, bool excludeManagement, bool expectedManagement)
        => AssertManagementCopy(framework, isolated, deps, excludeManagement, expectedManagement);

    [WindowsFact]
    [Trait("Category", "Integration")]
    public void BuildInPlace_ExcludesPublishedModernManagementAssetsFromDesktopPayload()
        => AssertManagementCopy("net472", isolated: true, deps: true, excludeManagement: false, expectedManagement: false);

    private static void AssertManagementCopy(
        string framework, bool isolated, bool deps, bool excludeManagement, bool expectedManagement)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(root, "Module")).FullName;
        var projectRoot = Directory.CreateDirectory(Path.Combine(root, "Project")).FullName;
        var projectPath = Path.Combine(projectRoot, "ManagementProbe.csproj");
        try
        {
            File.WriteAllText(Path.Combine(moduleRoot, "ManagementProbe.psm1"), string.Empty);
            File.WriteAllText(Path.Combine(moduleRoot, "ManagementProbe.psd1"),
                "@{ RootModule = 'ManagementProbe.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }");
            File.WriteAllText(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>{{framework}}</TargetFramework>
                    <GenerateDependencyFile>{{deps.ToString().ToLowerInvariant()}}</GenerateDependencyFile>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="System.Management" Version="9.0.6" GeneratePathProperty="true" />
                    <Content Include="$(PkgSystem_Management)/lib/net8.0/System.Management.dll" Condition="'$(TargetFramework)' == 'net472'" TargetPath="System.Management.dll" CopyToPublishDirectory="Always" />
                    <Content Include="$(PkgSystem_Management)/runtimes/win/lib/net8.0/System.Management.dll" Condition="'$(TargetFramework)' == 'net472'" TargetPath="runtimes/win/lib/net8.0/System.Management.dll" CopyToPublishDirectory="Always" />
                    <Content Include="host-root.marker" TargetPath="System.Management.Automation.dll" CopyToPublishDirectory="Always" />
                    <Content Include="host-runtime.marker" TargetPath="runtimes/win/lib/net8.0/System.Management.Automation.dll" CopyToPublishDirectory="Always" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(projectRoot, "Probe.cs"), framework == "net472"
                ? "public static class ManagementProbe { public static int Value() => 1; }"
                : "public static class ManagementProbe { public static System.Type DependencyType() => typeof(System.Management.ManagementObject); }");
            File.WriteAllText(Path.Combine(projectRoot, "host-root.marker"), "Host assembly must not be packaged.");
            File.WriteAllText(Path.Combine(projectRoot, "host-runtime.marker"), "Host runtime assembly must not be packaged.");

            ModuleBuilderTestDependencies.Create().BuildInPlace(new ModuleBuilder.Options
            {
                ProjectRoot = moduleRoot,
                ModuleName = "ManagementProbe",
                CsprojPath = projectPath,
                Frameworks = [framework],
                UseAssemblyLoadContext = isolated,
                HandleRuntimes = true,
                DisableBinaryCmdletScan = true,
                EmitBinaryConflictOwnerNotes = false,
                ExcludeLibraryFilter = excludeManagement ? ["System.Management*"] : []
            });

            var payload = Path.Combine(moduleRoot, "Lib", framework == "net472" ? "Default" : framework == "netstandard2.0" ? "Standard" : "Core");
            Assert.True(File.Exists(Path.Combine(payload, "ManagementProbe.dll")));
            Assert.DoesNotContain(Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories),
                file => Path.GetFileName(file).Equals("System.Management.Automation.dll", StringComparison.OrdinalIgnoreCase));
            var management = Path.Combine(payload, "System.Management.dll");
            var runtimeManagement = Path.Combine(payload, "runtimes", "win", "lib", "net8.0", "System.Management.dll");
            if (expectedManagement)
            {
                Assert.Equal(9, AssemblyName.GetAssemblyName(management).Version!.Major);
                Assert.Equal(9, AssemblyName.GetAssemblyName(runtimeManagement).Version!.Major);
            }
            else
            {
                Assert.DoesNotContain(Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories),
                    file => Path.GetFileName(file).Equals("System.Management.dll", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
