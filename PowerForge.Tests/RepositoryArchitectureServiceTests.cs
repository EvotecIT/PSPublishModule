using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed class RepositoryArchitectureServiceTests
{
    [Fact]
    public void Verify_EnforcesDependencyEdgesConsumerDiscoveryEvidenceAndImpact()
    {
        var root = CreateRepository();
        try
        {
            WriteProject(root, "Owner/Owner.csproj");
            WriteProject(root, "Consumer/Consumer.csproj", "../Owner/Owner.csproj", "Allowed.Package");
            WriteProject(root, "Unknown/Unknown.csproj", "../Owner/Owner.csproj");
            WriteProject(root, "Evidence.Tests/Evidence.Tests.csproj", "../Owner/Owner.csproj", "Microsoft.NET.Test.Sdk");
            WriteFile(root, "Owner/Projection.cs", "public static class Projection { public static void ProjectRows() { } }");
            WriteFile(root, "Consumer/Use.cs", "public static class Use { public static void Run() => Projection.ProjectRows(); }");
            WriteFile(root, "Unknown/Use.cs", "public static class Use { public static void Run() => Projection.ProjectRows(); }");
            WriteFile(root, "Evidence.Tests/ProjectionTests.cs", "public sealed class ProjectionTests { }");
            WriteWorkspaceValidation(root, "core-contract", "consumer-artifact", "native-aot");

            var spec = CreateSpec();
            var service = new RepositoryArchitectureService();
            var configPath = Path.Combine(root, ".powerforge", "architecture.json");
            var report = service.Verify(spec, configPath, ["Owner/Projection.cs"]);

            Assert.False(report.Succeeded);
            Assert.Contains(report.Issues, issue => issue.Code == "ARC210" && issue.Path == "Unknown/Unknown.csproj");
            var capability = Assert.Single(report.Capabilities);
            Assert.True(capability.Impacted);
            Assert.Contains("Consumer/Consumer.csproj", capability.ObservedConsumerProjects);
            Assert.Contains("Unknown/Unknown.csproj", capability.ObservedConsumerProjects);
            Assert.Equal(["consumer-artifact", "core-contract", "native-aot"], report.RequiredValidationStepIds);

            File.Delete(Path.Combine(root, "Unknown", "Use.cs"));
            report = service.Verify(spec, configPath, ["Owner/Projection.cs"]);

            Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Issues.Select(issue => issue.Message)));
            capability = Assert.Single(report.Capabilities);
            Assert.Equal(["Consumer/Consumer.csproj"], capability.ObservedConsumerProjects);
            Assert.Contains("Consumer/Consumer.csproj", report.Projects.Single(project => project.Path == "Owner/Owner.csproj").ReverseProjectReferences);

            report = service.Verify(spec, configPath, ["README.md"]);
            Assert.True(report.Succeeded);
            Assert.False(Assert.Single(report.Capabilities).Impacted);
            Assert.Empty(report.RequiredValidationStepIds);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Verify_RejectsUnexpectedAndForbiddenDependencyEdges()
    {
        var root = CreateRepository();
        try
        {
            WriteProject(root, "Owner/Owner.csproj");
            WriteProject(root, "Reader/Reader.csproj");
            WriteProject(root, "Bridge/Bridge.csproj", "../Owner/Owner.csproj;../Reader/Reader.csproj", "Allowed.Package;Forbidden.Package");
            WriteFile(root, "Owner/Projection.cs", "public static class Projection { }");
            WriteWorkspaceValidation(root, "owner-contract");

            var spec = new RepositoryArchitectureSpec
            {
                RepositoryRoot = "..",
                WorkspaceValidationConfig = ".powerforge/workspace.validation.json",
                ProjectRules =
                [
                    new RepositoryArchitectureProjectRule
                    {
                        Id = "bridge",
                        Project = "Bridge/Bridge.csproj",
                        AllowedProjectReferences = ["Owner/Owner.csproj"],
                        RequiredProjectReferences = ["Missing/Missing.csproj"],
                        ForbiddenProjectReferences = ["Reader/Reader.csproj"],
                        AllowedPackageReferences = ["Allowed.Package"],
                        RequiredPackageReferences = ["Missing.Package"],
                        ForbiddenPackageReferences = ["Forbidden.Package"]
                    }
                ]
            };

            var report = new RepositoryArchitectureService().Verify(
                spec,
                Path.Combine(root, ".powerforge", "architecture.json"));

            Assert.False(report.Succeeded);
            Assert.Contains(report.Issues, issue => issue.Code == "ARC110" && issue.Message.Contains("Reader/Reader.csproj", StringComparison.Ordinal));
            Assert.Contains(report.Issues, issue => issue.Code == "ARC111" && issue.Message.Contains("Reader/Reader.csproj", StringComparison.Ordinal));
            Assert.Contains(report.Issues, issue => issue.Code == "ARC112" && issue.Message.Contains("Missing/Missing.csproj", StringComparison.Ordinal));
            Assert.Contains(report.Issues, issue => issue.Code == "ARC120" && issue.Message.Contains("Forbidden.Package", StringComparison.Ordinal));
            Assert.Contains(report.Issues, issue => issue.Code == "ARC121" && issue.Message.Contains("Forbidden.Package", StringComparison.Ordinal));
            Assert.Contains(report.Issues, issue => issue.Code == "ARC122" && issue.Message.Contains("Missing.Package", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Verify_FailsWhenEvidenceDoesNotCoverEveryOwnerAndConsumer()
    {
        var root = CreateRepository();
        try
        {
            WriteProject(root, "Owner/Owner.csproj");
            WriteProject(root, "Consumer/Consumer.csproj", "../Owner/Owner.csproj");
            WriteFile(root, "Owner/Projection.cs", "public static class Projection { public static void ProjectRows() { } }");
            WriteFile(root, "Consumer/Use.cs", "public static class Use { public static void Run() => Projection.ProjectRows(); }");
            WriteWorkspaceValidation(root, "core-contract");

            var spec = CreateSpec();
            spec.Capabilities[0].RequiredEvidenceKinds = ["contract", "nativeAot"];
            spec.Capabilities[0].Evidence =
            [
                new RepositoryArchitectureEvidence
                {
                    Id = "core",
                    Kind = "contract",
                    StepId = "core-contract",
                    Path = "Owner/Projection.cs",
                    CoversProjects = ["Owner/Owner.csproj"]
                }
            ];

            var report = new RepositoryArchitectureService().Verify(
                spec,
                Path.Combine(root, ".powerforge", "architecture.json"));

            Assert.False(report.Succeeded);
            Assert.Contains(report.Issues, issue => issue.Code == "ARC225" && issue.Message.Contains("nativeAot", StringComparison.Ordinal));
            Assert.Contains(report.Issues, issue => issue.Code == "ARC226" && issue.Message.Contains("Consumer/Consumer.csproj", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Verify_RequiresOneSharedWorkspaceRunnerForExecutableEvidence()
    {
        var root = CreateRepository();
        try
        {
            WriteProject(root, "Owner/Owner.csproj");
            WriteProject(root, "Consumer/Consumer.csproj", "../Owner/Owner.csproj");
            WriteFile(root, "Owner/Projection.cs", "public static class Projection { public static void ProjectRows() { } }");
            WriteFile(root, "Consumer/Use.cs", "public static class Use { public static void Run() => Projection.ProjectRows(); }");
            WriteFile(root, "Evidence.Tests/ProjectionTests.cs", "public sealed class ProjectionTests { }");

            var spec = CreateSpec();
            spec.WorkspaceValidationConfig = null;

            var report = new RepositoryArchitectureService().Verify(
                spec,
                Path.Combine(root, ".powerforge", "architecture.json"));

            Assert.False(report.Succeeded);
            Assert.Contains(report.Issues, issue => issue.Code == "ARC227");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Verify_RejectsProjectReferencesThatEscapeTheRepository()
    {
        var root = CreateRepository();
        try
        {
            WriteProject(root, "Consumer/Consumer.csproj", "../../Outside/Outside.csproj");

            var report = new RepositoryArchitectureService().Verify(
                new RepositoryArchitectureSpec { RepositoryRoot = ".." },
                Path.Combine(root, ".powerforge", "architecture.json"));

            Assert.False(report.Succeeded);
            Assert.Contains(report.Issues, issue => issue.Code == "ARC010" && issue.Message.Contains("escapes the repository root", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Load_AcceptsCommentsAndTrailingCommas()
    {
        var root = CreateRepository();
        try
        {
            var path = Path.Combine(root, "architecture.json");
            File.WriteAllText(path, """
                {
                  // Repository policy
                  "schemaVersion": 1,
                  "repositoryRoot": ".",
                }
                """);

            var spec = new RepositoryArchitectureService().Load(path);

            Assert.Equal(1, spec.SchemaVersion);
            Assert.Equal(".", spec.RepositoryRoot);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static RepositoryArchitectureSpec CreateSpec()
        => new()
        {
            RepositoryRoot = "..",
            WorkspaceValidationConfig = ".powerforge/workspace.validation.json",
            WorkspaceValidationProfile = "architecture",
            GlobalImpactPaths = ["Directory.Build.*"],
            ProjectRules =
            [
                new RepositoryArchitectureProjectRule
                {
                    Id = "consumer",
                    Project = "Consumer/Consumer.csproj",
                    AllowedProjectReferences = ["Owner/Owner.csproj"],
                    AllowedPackageReferences = ["Allowed.Package"]
                }
            ],
            Capabilities =
            [
                new RepositoryArchitectureCapability
                {
                    Id = "row-projection",
                    OwnerProjects = ["Owner/Owner.csproj"],
                    OwnerPaths = ["Owner/Projection*.cs"],
                    ConsumerProjects = ["Consumer/Consumer.csproj"],
                    UsagePatterns = ["Projection.ProjectRows"],
                    RequiredEvidenceKinds = ["contract", "artifact", "nativeAot"],
                    Evidence =
                    [
                        new RepositoryArchitectureEvidence
                        {
                            Id = "core",
                            Kind = "contract",
                            StepId = "core-contract",
                            Path = "Evidence.Tests/ProjectionTests.cs",
                            CoversProjects = ["Owner/Owner.csproj"]
                        },
                        new RepositoryArchitectureEvidence
                        {
                            Id = "consumer",
                            Kind = "artifact",
                            StepId = "consumer-artifact",
                            Path = "Consumer/Use.cs",
                            CoversProjects = ["Consumer/Consumer.csproj"]
                        },
                        new RepositoryArchitectureEvidence
                        {
                            Id = "aot",
                            Kind = "nativeAot",
                            StepId = "native-aot",
                            Path = "Consumer/Use.cs",
                            CoversProjects = ["Owner/Owner.csproj", "Consumer/Consumer.csproj"]
                        }
                    ]
                }
            ]
        };

    private static string CreateRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "architecture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".powerforge"));
        return root;
    }

    private static void WriteProject(
        string root,
        string relativePath,
        string projectReferences = "",
        string packageReferences = "")
    {
        var projectItems = string.Join(
            Environment.NewLine,
            projectReferences.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(reference => $"    <ProjectReference Include=\"{reference}\" />"));
        var packageItems = string.Join(
            Environment.NewLine,
            packageReferences.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(reference => $"    <PackageReference Include=\"{reference}\" Version=\"1.0.0\" />"));
        WriteFile(root, relativePath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup>
            {projectItems}
            {packageItems}
              </ItemGroup>
            </Project>
            """);
    }

    private static void WriteWorkspaceValidation(string root, params string[] stepIds)
    {
        var spec = new WorkspaceValidationSpec
        {
            ProjectRoot = "..",
            Profiles = [new WorkspaceValidationProfile { Name = "architecture" }],
            Steps = stepIds.Select(id => new WorkspaceValidationStep
            {
                Id = id,
                Profiles = ["architecture"],
                Arguments = ["--info"]
            }).ToArray()
        };
        File.WriteAllText(
            Path.Combine(root, ".powerforge", "workspace.validation.json"),
            JsonSerializer.Serialize(spec));
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
