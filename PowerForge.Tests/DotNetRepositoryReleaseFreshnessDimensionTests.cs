namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseFreshnessDimensionTests
{
    [Fact]
    public void FreshnessCleanup_DoesNotWidenTargetFrameworkPrefixedCustomRoots()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.CustomLayout"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.CustomLayout.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
                    <BaseOutputPath>$(MSBuildProjectDirectory)/artifacts/bin/</BaseOutputPath>
                    <BaseIntermediateOutputPath>$(MSBuildProjectDirectory)/artifacts/obj/</BaseIntermediateOutputPath>
                    <OutputPath>$(BaseOutputPath)$(TargetFramework)/$(Configuration)/</OutputPath>
                    <IntermediateOutputPath>$(BaseIntermediateOutputPath)$(TargetFramework)/$(Configuration)/</IntermediateOutputPath>
                  </PropertyGroup>
                </Project>
                """);

            var activePaths = new[]
            {
                Path.Combine(projectDirectory.FullName, "artifacts", "bin", "net8.0", "Release", "Sample.CustomLayout.dll"),
                Path.Combine(projectDirectory.FullName, "artifacts", "obj", "net8.0", "Release", "Sample.CustomLayout.dll")
            };
            var unrelatedPaths = new[]
            {
                Path.Combine(projectDirectory.FullName, "artifacts", "bin", "net8.0", "Debug", "Sample.CustomLayout.dll"),
                Path.Combine(projectDirectory.FullName, "artifacts", "bin", "net10.0", "Release", "Sample.CustomLayout.dll")
            };
            foreach (var path in activePaths.Concat(unrelatedPaths))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, path);
            }

            var success = DotNetRepositoryReleaseService.TryRemoveStalePrimaryPackageOutputs(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.CustomLayout",
                    CsprojPath = projectPath
                },
                "Release",
                new NullLogger(),
                out var removedFileCount,
                out var removedIntermediatePrimaryOutput,
                out _,
                out var error);

            Assert.True(success, error);
            Assert.Equal(activePaths.Length, removedFileCount);
            Assert.True(removedIntermediatePrimaryOutput);
            Assert.All(activePaths, path => Assert.False(File.Exists(path)));
            Assert.All(unrelatedPaths, path => Assert.True(File.Exists(path)));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void FreshnessCleanup_EvaluatesTargetFrameworksForActiveConfiguration()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.ConfigurationFramework"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.ConfigurationFramework.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <AssemblyName>Sample.DebugFramework</AssemblyName>
                    <OutputPath>$(MSBuildProjectDirectory)/artifacts/bin/Debug/net8.0/</OutputPath>
                    <IntermediateOutputPath>$(MSBuildProjectDirectory)/artifacts/obj/Debug/net8.0/</IntermediateOutputPath>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
                    <AssemblyName>Sample.ReleaseFramework</AssemblyName>
                    <OutputPath>$(MSBuildProjectDirectory)/artifacts/bin/Release/net10.0/</OutputPath>
                    <IntermediateOutputPath>$(MSBuildProjectDirectory)/artifacts/obj/Release/net10.0/</IntermediateOutputPath>
                  </PropertyGroup>
                </Project>
                """);

            var releasePaths = new[]
            {
                Path.Combine(projectDirectory.FullName, "artifacts", "bin", "Release", "net10.0", "Sample.ReleaseFramework.dll"),
                Path.Combine(projectDirectory.FullName, "artifacts", "obj", "Release", "net10.0", "Sample.ReleaseFramework.dll")
            };
            var debugOutput = Path.Combine(projectDirectory.FullName, "artifacts", "bin", "Debug", "net8.0", "Sample.DebugFramework.dll");
            foreach (var path in releasePaths.Append(debugOutput))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, path);
            }

            var success = DotNetRepositoryReleaseService.TryRemoveStalePrimaryPackageOutputs(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.ConfigurationFramework",
                    CsprojPath = projectPath
                },
                "Release",
                new NullLogger(),
                out var removedFileCount,
                out var removedIntermediatePrimaryOutput,
                out _,
                out var error);

            Assert.True(success, error);
            Assert.Equal(releasePaths.Length, removedFileCount);
            Assert.True(removedIntermediatePrimaryOutput);
            Assert.All(releasePaths, path => Assert.False(File.Exists(path)));
            Assert.True(File.Exists(debugOutput));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void FreshnessCleanup_EvaluatesConfiguredRuntimeIdentifierMetadata()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Sample.RuntimeMetadata"));
            var projectPath = Path.Combine(projectDirectory.FullName, "Sample.RuntimeMetadata.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
                    <AssemblyName>Sample.Windows</AssemblyName>
                    <OutputPath>$(MSBuildProjectDirectory)/custom/bin/win-x64/</OutputPath>
                    <IntermediateOutputPath>$(MSBuildProjectDirectory)/custom/obj/win-x64/</IntermediateOutputPath>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(RuntimeIdentifier)' == 'linux-x64'">
                    <AssemblyName>Sample.Linux</AssemblyName>
                    <OutputPath>$(MSBuildProjectDirectory)/custom/bin/linux-x64/</OutputPath>
                    <IntermediateOutputPath>$(MSBuildProjectDirectory)/custom/obj/linux-x64/</IntermediateOutputPath>
                  </PropertyGroup>
                </Project>
                """);

            var stalePaths = new[]
            {
                Path.Combine(projectDirectory.FullName, "custom", "bin", "win-x64", "Sample.Windows.dll"),
                Path.Combine(projectDirectory.FullName, "custom", "obj", "win-x64", "Sample.Windows.dll"),
                Path.Combine(projectDirectory.FullName, "custom", "bin", "linux-x64", "Sample.Linux.dll"),
                Path.Combine(projectDirectory.FullName, "custom", "obj", "linux-x64", "Sample.Linux.dll")
            };
            foreach (var path in stalePaths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, path);
            }

            var success = DotNetRepositoryReleaseService.TryRemoveStalePrimaryPackageOutputs(
                new DotNetRepositoryProjectResult
                {
                    ProjectName = "Sample.RuntimeMetadata",
                    CsprojPath = projectPath
                },
                "Release",
                new NullLogger(),
                out var removedFileCount,
                out var removedIntermediatePrimaryOutput,
                out _,
                out var error);

            Assert.True(success, error);
            Assert.Equal(stalePaths.Length, removedFileCount);
            Assert.True(removedIntermediatePrimaryOutput);
            Assert.All(stalePaths, path => Assert.False(File.Exists(path)));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
