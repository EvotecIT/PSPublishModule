using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleTestFailureWorkflowServiceTests
{
    [Fact]
    public void ResolvePath_ReturnsExistingCandidateWhenExplicitPathIsNotProvided()
    {
        var root = CreateTempPath();
        try
        {
            var testsRoot = Path.Combine(root, "Tests");
            Directory.CreateDirectory(testsRoot);
            var resultsPath = Path.Combine(testsRoot, "TestResults.xml");
            File.WriteAllText(resultsPath, "<test-results total=\"0\" failures=\"0\" />");

            var service = new ModuleTestFailureWorkflowService();
            var resolution = service.ResolvePath(new ModuleTestFailureWorkflowRequest
            {
                CurrentDirectory = root
            });

            Assert.Equal(root, resolution.ProjectPath);
            Assert.Equal(resultsPath, resolution.ResultsPath);
            Assert.False(resolution.ExplicitPathProvided);
            Assert.Contains(resultsPath, resolution.SearchedPaths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ReturnsWarningMessagesWhenNoResultsFileIsFound()
    {
        var root = CreateTempPath();
        try
        {
            var service = new ModuleTestFailureWorkflowService();
            var result = service.Execute(new ModuleTestFailureWorkflowRequest
            {
                CurrentDirectory = root
            });

            Assert.Null(result.Analysis);
            Assert.NotEmpty(result.WarningMessages);
            Assert.Equal("No test results file found. Searched in:", result.WarningMessages[0]);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
