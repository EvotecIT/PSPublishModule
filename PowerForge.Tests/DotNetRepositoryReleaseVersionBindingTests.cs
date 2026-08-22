using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseVersionBindingTests
{
    [Fact]
    public void Execute_updates_project_and_bound_file_to_the_same_version()
    {
        var root = CreateRepository();

        try
        {
            var result = Execute(root, pattern: @"(?<=Example\.Tool@)\d+\.\d+\.\d+");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains("<VersionPrefix>1.2.4</VersionPrefix>", Read(root, "Example.Tool", "Example.Tool.csproj"), StringComparison.Ordinal);
            Assert.Equal("Example.Tool@1.2.4", Read(root, "tool.txt"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_invalid_binding_does_not_partially_update_the_project()
    {
        var root = CreateRepository();

        try
        {
            var result = Execute(root, pattern: @"Missing@\d+\.\d+\.\d+");

            Assert.False(result.Success);
            Assert.Contains("found 0 matches", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Contains("<VersionPrefix>1.2.3</VersionPrefix>", Read(root, "Example.Tool", "Example.Tool.csproj"), StringComparison.Ordinal);
            Assert.Equal("Example.Tool@1.2.3", Read(root, "tool.txt"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_composes_a_binding_that_targets_the_versioned_project_file()
    {
        var root = CreateRepository();

        try
        {
            var projectPath = Path.Combine(root.FullName, "Example.Tool", "Example.Tool.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionPrefix>1.2.3</VersionPrefix><ToolVersion>1.2.3</ToolVersion></PropertyGroup></Project>");

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                IncludeProjects = new[] { "Example.Tool" },
                ExpectedVersion = "1.2.4",
                UpdateVersions = true,
                Pack = false,
                VersionBindings = new[]
                {
                    new ProjectVersionBinding
                    {
                        Path = "Example.Tool/Example.Tool.csproj",
                        Project = "Example.Tool",
                        Pattern = @"(?<=<ToolVersion>)\d+\.\d+\.\d+(?=</ToolVersion>)"
                    }
                }
            });

            Assert.True(result.Success, result.ErrorMessage);
            var projectContent = File.ReadAllText(projectPath);
            Assert.Contains("<VersionPrefix>1.2.4</VersionPrefix>", projectContent, StringComparison.Ordinal);
            Assert.Contains("<ToolVersion>1.2.4</ToolVersion>", projectContent, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_plan_composes_project_version_updates_before_validating_bindings()
    {
        var root = CreateRepository();

        try
        {
            var projectPath = Path.Combine(root.FullName, "Example.Tool", "Example.Tool.csproj");
            var originalContent =
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionPrefix>1.2.3</VersionPrefix><ToolVersion>1.2.3</ToolVersion></PropertyGroup></Project>";
            File.WriteAllText(projectPath, originalContent);

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                IncludeProjects = new[] { "Example.Tool" },
                ExpectedVersion = "1.2.4",
                UpdateVersions = true,
                WhatIf = true,
                Pack = false,
                VersionBindings = new[]
                {
                    new ProjectVersionBinding
                    {
                        Path = "Example.Tool/Example.Tool.csproj",
                        Project = "Example.Tool",
                        Pattern = @"\b1\.2\.3\b"
                    }
                }
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(originalContent, File.ReadAllText(projectPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_refreshes_inherited_package_versions_after_binding_updates()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-release-binding-" + Guid.NewGuid().ToString("N")));
        try
        {
            var propsPath = Path.Combine(root.FullName, "Directory.Build.props");
            File.WriteAllText(propsPath, """
                <Project>
                  <PropertyGroup>
                    <VersionPrefix>1.0.0</VersionPrefix>
                  </PropertyGroup>
                </Project>
                """);

            var primaryDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Example.Primary"));
            File.WriteAllText(Path.Combine(primaryDirectory.FullName, "Example.Primary.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <VersionPrefix>1.0.0</VersionPrefix>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            var inheritedDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Example.Inherited"));
            var inheritedPath = Path.Combine(inheritedDirectory.FullName, "Example.Inherited.csproj");
            var inheritedSource = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """;
            File.WriteAllText(inheritedPath, inheritedSource);

            var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
            {
                RootPath = root.FullName,
                ExpectedVersionsByProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Example.Primary"] = "1.1.0"
                },
                UpdateVersions = true,
                Pack = false,
                VersionBindings = new[]
                {
                    new ProjectVersionBinding
                    {
                        Path = "Directory.Build.props",
                        Project = "Example.Primary",
                        Pattern = @"(?<=<VersionPrefix>)\d+\.\d+\.\d+(?=</VersionPrefix>)"
                    }
                }
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("1.1.0", result.ResolvedVersionsByProject["Example.Primary"]);
            Assert.Equal("1.1.0", result.ResolvedVersionsByProject["Example.Inherited"]);
            Assert.Contains("<VersionPrefix>1.1.0</VersionPrefix>", File.ReadAllText(propsPath), StringComparison.Ordinal);
            Assert.Equal(inheritedSource, File.ReadAllText(inheritedPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetRepositoryReleaseResult Execute(DirectoryInfo root, string pattern)
        => new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
        {
            RootPath = root.FullName,
            IncludeProjects = new[] { "Example.Tool" },
            ExpectedVersion = "1.2.4",
            UpdateVersions = true,
            Pack = false,
            VersionBindings = new[]
            {
                new ProjectVersionBinding
                {
                    Path = "tool.txt",
                    Project = "Example.Tool",
                    Pattern = pattern
                }
            }
        });

    private static DirectoryInfo CreateRepository()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-release-binding-" + Guid.NewGuid().ToString("N")));
        var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Example.Tool"));
        File.WriteAllText(
            Path.Combine(project.FullName, "Example.Tool.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root.FullName, "tool.txt"), "Example.Tool@1.2.3");
        return root;
    }

    private static string Read(DirectoryInfo root, params string[] segments)
        => File.ReadAllText(segments.Aggregate(root.FullName, Path.Combine));

    private static void TryDelete(DirectoryInfo directory)
    {
        try { directory.Delete(recursive: true); } catch { }
    }
}
