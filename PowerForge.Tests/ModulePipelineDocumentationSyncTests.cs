using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineDocumentationSyncTests
{
    [Fact]
    public void SyncGeneratedDocumentationToProjectRoot_SkipsCopyWhenAbsoluteDocsPathIsSource()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var docsPath = Path.Combine(root.FullName, "AbsoluteDocs");
            Directory.CreateDirectory(docsPath);
            File.WriteAllText(Path.Combine(docsPath, "Get-Test.md"), "# Get-Test");
            var readmePath = Path.Combine(docsPath, "Readme.md");
            File.WriteAllText(readmePath, "# TestModule");

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = CreatePlan(runner, root.FullName, moduleName, docsPath, readmePath);
            var result = new DocumentationBuildResult(
                enabled: true,
                docsPath: docsPath,
                readmePath: readmePath,
                succeeded: true,
                exitCode: 0,
                markdownFiles: 2,
                externalHelpFilePath: string.Empty,
                errorMessage: null);

            InvokeSync(runner, plan, result);

            Assert.True(File.Exists(Path.Combine(docsPath, "Get-Test.md")));
            Assert.True(File.Exists(readmePath));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SyncGeneratedDocumentationToProjectRoot_PrunesStaleMarkdownAndPreservesAssets()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var projectRoot = Path.Combine(root.FullName, "Project");
            Directory.CreateDirectory(projectRoot);
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot, moduleName, "1.0.0");

            var targetDocs = Path.Combine(projectRoot, "Docs");
            Directory.CreateDirectory(Path.Combine(targetDocs, "assets"));
            File.WriteAllText(Path.Combine(targetDocs, "Old-Command.md"), GeneratedCommandMarkdown("Old-Command"));
            File.WriteAllText(Path.Combine(targetDocs, "usage.md"), "# Usage guide");
            File.WriteAllText(Path.Combine(targetDocs, "assets", "logo.png"), "asset");

            var sourceDocs = Path.Combine(root.FullName, "Staging", "Docs");
            Directory.CreateDirectory(sourceDocs);
            File.WriteAllText(Path.Combine(sourceDocs, "New-Command.md"), "# New");
            var readmePath = Path.Combine(sourceDocs, "Readme.md");
            File.WriteAllText(readmePath, "# TestModule");

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = CreatePlan(runner, projectRoot, moduleName, "Docs", "Docs\\Readme.md");
            var result = new DocumentationBuildResult(
                enabled: true,
                docsPath: sourceDocs,
                readmePath: readmePath,
                succeeded: true,
                exitCode: 0,
                markdownFiles: 2,
                externalHelpFilePath: string.Empty,
                errorMessage: null);

            InvokeSync(runner, plan, result);

            Assert.True(File.Exists(Path.Combine(targetDocs, "New-Command.md")));
            Assert.True(File.Exists(Path.Combine(targetDocs, "Readme.md")));
            Assert.False(File.Exists(Path.Combine(targetDocs, "Old-Command.md")));
            Assert.True(File.Exists(Path.Combine(targetDocs, "usage.md")));
            Assert.True(File.Exists(Path.Combine(targetDocs, "assets", "logo.png")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void NormalizeDocumentationForStaging_MapsProjectAbsoluteDocsPathToStagingRelativePath()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var projectRoot = Path.Combine(root.FullName, "Project");
            var stagingRoot = Path.Combine(root.FullName, "Staging");
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(stagingRoot);
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = CreatePlan(
                runner,
                projectRoot,
                moduleName,
                Path.Combine(projectRoot, "Docs"),
                Path.Combine(projectRoot, "Docs", "Readme.md"));

            var documentation = InvokeNormalizeDocumentationForStaging(plan, stagingRoot);

            Assert.Equal("Docs", documentation.Path);
            Assert.Equal(Path.Combine("Docs", "Readme.md"), documentation.PathReadme);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void NormalizeDocumentationForStaging_RejectsAbsoluteProjectRootDocsPath()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var projectRoot = Path.Combine(root.FullName, "Project");
            var stagingRoot = Path.Combine(root.FullName, "Staging");
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(stagingRoot);
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot, moduleName, "1.0.0");

            var runner = new ModulePipelineRunner(new NullLogger());
            var plan = CreatePlan(
                runner,
                projectRoot,
                moduleName,
                projectRoot,
                Path.Combine(projectRoot, "Readme.md"));

            var exception = Assert.Throws<TargetInvocationException>(() => InvokeNormalizeDocumentationForStaging(plan, stagingRoot));
            Assert.Contains("project root", exception.InnerException?.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static ModulePipelinePlan CreatePlan(ModulePipelineRunner runner, string projectRoot, string moduleName, string docsPath, string readmePath)
    {
        return runner.Plan(new ModulePipelineSpec
        {
            Build = new ModuleBuildSpec
            {
                Name = moduleName,
                SourcePath = projectRoot,
                Version = "1.0.0",
                CsprojPath = null
            },
            Install = new ModulePipelineInstallOptions { Enabled = false },
            Segments = new IConfigurationSegment[]
            {
                new ConfigurationDocumentationSegment
                {
                    Configuration = new DocumentationConfiguration
                    {
                        Path = docsPath,
                        PathReadme = readmePath
                    }
                },
                new ConfigurationBuildDocumentationSegment
                {
                    Configuration = new BuildDocumentationConfiguration
                    {
                        Enable = true
                    }
                }
            }
        });
    }

    private static void InvokeSync(ModulePipelineRunner runner, ModulePipelinePlan plan, DocumentationBuildResult result)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("SyncGeneratedDocumentationToProjectRoot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(runner, new object[] { plan, result });
    }

    private static DocumentationConfiguration InvokeNormalizeDocumentationForStaging(ModulePipelinePlan plan, string stagingPath)
    {
        var method = typeof(ModulePipelineRunner).GetMethod("NormalizeDocumentationForStaging", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method.Invoke(null, new object[] { plan, stagingPath });
        return Assert.IsType<DocumentationConfiguration>(result);
    }

    private static string GeneratedCommandMarkdown(string commandName)
        => string.Join(Environment.NewLine, new[]
        {
            "---",
            "external help file: TestModule-help.xml",
            "Module Name: TestModule",
            "schema: 2.0.0",
            "---",
            $"# {commandName}"
        });
}
