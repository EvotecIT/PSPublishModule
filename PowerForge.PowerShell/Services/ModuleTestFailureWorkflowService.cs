using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class ModuleTestFailureWorkflowService
{
    private readonly ModuleTestFailureAnalyzer _analyzer;

    public ModuleTestFailureWorkflowService(ModuleTestFailureAnalyzer? analyzer = null)
    {
        _analyzer = analyzer ?? new ModuleTestFailureAnalyzer();
    }

    public ModuleTestFailureWorkflowResult Execute(ModuleTestFailureWorkflowRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.UseTestResultsInput)
        {
            return new ModuleTestFailureWorkflowResult
            {
                Analysis = AnalyzeTestResults(request.TestResults),
                WarningMessages = Array.Empty<string>()
            };
        }

        var resolution = ResolvePath(request);
        if (string.IsNullOrWhiteSpace(resolution.ResultsPath))
        {
            return new ModuleTestFailureWorkflowResult
            {
                WarningMessages = resolution.ExplicitPathProvided
                    ? new[] { $"Test results file not found: {resolution.SearchedPaths[0]}" }
                    : new[] { "No test results file found. Searched in:" }.Concat(resolution.SearchedPaths.Select(path => $"  {path}")).ToArray()
            };
        }

        if (!File.Exists(resolution.ResultsPath))
        {
            return new ModuleTestFailureWorkflowResult
            {
                WarningMessages = new[] { $"Test results file not found: {resolution.ResultsPath}" }
            };
        }

        return new ModuleTestFailureWorkflowResult
        {
            Analysis = _analyzer.AnalyzeFromXmlFile(resolution.ResultsPath!)
        };
    }

    internal ModuleTestFailurePathResolution ResolvePath(ModuleTestFailureWorkflowRequest request)
    {
        var projectPath = ResolveProjectPath(request.ProjectPath, request.ModuleBasePath, request.CurrentDirectory);
        if (!string.IsNullOrWhiteSpace(request.ExplicitPath))
        {
            var explicitPath = Path.GetFullPath(request.ExplicitPath!.Trim().Trim('"'));
            return new ModuleTestFailurePathResolution
            {
                ProjectPath = projectPath,
                ResultsPath = explicitPath,
                SearchedPaths = new[] { explicitPath },
                ExplicitPathProvided = true
            };
        }

        var candidates = new[]
        {
            Path.Combine(projectPath, "TestResults.xml"),
            Path.Combine(projectPath, "Tests", "TestResults.xml"),
            Path.Combine(projectPath, "Test", "TestResults.xml"),
            Path.Combine(projectPath, "Tests", "Results", "TestResults.xml")
        };

        return new ModuleTestFailurePathResolution
        {
            ProjectPath = projectPath,
            ResultsPath = candidates.FirstOrDefault(File.Exists),
            SearchedPaths = candidates,
            ExplicitPathProvided = false
        };
    }

    internal ModuleTestFailureAnalysis AnalyzeTestResults(object? testResults)
    {
        if (testResults is ModuleTestFailureAnalysis analysis)
            return analysis;

        if (testResults is ModuleTestSuiteResult suite)
        {
            if (suite.FailureAnalysis is not null)
                return suite.FailureAnalysis;

            var xmlPath = suite.ResultsXmlPath;
            if (xmlPath is not null && File.Exists(xmlPath))
                return _analyzer.AnalyzeFromXmlFile(xmlPath);

            return new ModuleTestFailureAnalysis
            {
                Source = "ModuleTestSuiteResult",
                Timestamp = DateTime.Now,
                TotalCount = suite.TotalCount,
                PassedCount = suite.PassedCount,
                FailedCount = suite.FailedCount,
                SkippedCount = suite.SkippedCount,
                FailedTests = Array.Empty<ModuleTestFailureInfo>()
            };
        }

        return _analyzer.AnalyzeFromPesterResults(testResults);
    }

    internal static string ResolveProjectPath(string? explicitProjectPath, string? moduleBasePath, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitProjectPath))
            return Path.GetFullPath(explicitProjectPath!.Trim().Trim('"'));

        if (!string.IsNullOrWhiteSpace(moduleBasePath))
            return moduleBasePath!;

        return string.IsNullOrWhiteSpace(currentDirectory)
            ? Directory.GetCurrentDirectory()
            : currentDirectory;
    }
}
