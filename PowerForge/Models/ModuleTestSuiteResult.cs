using System;

namespace PowerForge;

/// <summary>
/// Result of running a module test suite.
/// </summary>
public sealed class ModuleTestSuiteResult
{
    /// <summary>Project directory path.</summary>
    public string ProjectPath { get; }

    /// <summary>Resolved test file or folder path passed to Pester.</summary>
    public string TestPath { get; }

    /// <summary>Module name derived from the manifest file name.</summary>
    public string ModuleName { get; }

    /// <summary>Module version as declared in the manifest (string form), when available.</summary>
    public string? ModuleVersion { get; }

    /// <summary>Full path to the module manifest (.psd1) file.</summary>
    public string ManifestPath { get; }

    /// <summary>Required modules extracted from the manifest.</summary>
    public ManifestEditor.RequiredModule[] RequiredModules { get; }

    /// <summary>Dependency installation results (empty when dependency installation was skipped).</summary>
    public ModuleDependencyInstallResult[] DependencyResults { get; }

    /// <summary>Indicates whether the module was imported before running tests.</summary>
    public bool ModuleImported { get; }

    /// <summary>Number of exported functions detected after import (when available).</summary>
    public int? ExportedFunctionCount { get; }

    /// <summary>Number of exported cmdlets detected after import (when available).</summary>
    public int? ExportedCmdletCount { get; }

    /// <summary>Number of exported aliases detected after import (when available).</summary>
    public int? ExportedAliasCount { get; }

    /// <summary>Pester module version used by the out-of-process runner (string form), when available.</summary>
    public string? PesterVersion { get; }

    /// <summary>Total number of tests discovered.</summary>
    public int TotalCount { get; }

    /// <summary>Number of passed tests.</summary>
    public int PassedCount { get; }

    /// <summary>Number of failed tests.</summary>
    public int FailedCount { get; }

    /// <summary>Number of skipped tests.</summary>
    public int SkippedCount { get; }

    /// <summary>Execution duration when available.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>Code coverage percent when available.</summary>
    public double? CoveragePercent { get; }

    /// <summary>Failure analysis parsed from NUnit XML results when available.</summary>
    public ModuleTestFailureAnalysis? FailureAnalysis { get; }

    /// <summary>Exit code returned by the out-of-process test runner.</summary>
    public int ExitCode { get; }

    /// <summary>Captured standard output from the out-of-process runner (excluding marker lines).</summary>
    public string StdOut { get; }

    /// <summary>Captured standard error from the out-of-process runner.</summary>
    public string StdErr { get; }

    /// <summary>Path to the NUnit XML results file when it was kept; otherwise null.</summary>
    public string? ResultsXmlPath { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public ModuleTestSuiteResult(
        string projectPath,
        string testPath,
        string moduleName,
        string? moduleVersion,
        string manifestPath,
        ManifestEditor.RequiredModule[] requiredModules,
        ModuleDependencyInstallResult[] dependencyResults,
        bool moduleImported,
        int? exportedFunctionCount,
        int? exportedCmdletCount,
        int? exportedAliasCount,
        string? pesterVersion,
        int totalCount,
        int passedCount,
        int failedCount,
        int skippedCount,
        TimeSpan? duration,
        double? coveragePercent,
        ModuleTestFailureAnalysis? failureAnalysis,
        int exitCode,
        string stdOut,
        string stdErr,
        string? resultsXmlPath)
    {
        ProjectPath = projectPath;
        TestPath = testPath;
        ModuleName = moduleName;
        ModuleVersion = moduleVersion;
        ManifestPath = manifestPath;
        RequiredModules = requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>();
        DependencyResults = dependencyResults ?? Array.Empty<ModuleDependencyInstallResult>();
        ModuleImported = moduleImported;
        ExportedFunctionCount = exportedFunctionCount;
        ExportedCmdletCount = exportedCmdletCount;
        ExportedAliasCount = exportedAliasCount;
        PesterVersion = pesterVersion;
        TotalCount = totalCount;
        PassedCount = passedCount;
        FailedCount = failedCount;
        SkippedCount = skippedCount;
        Duration = duration;
        CoveragePercent = coveragePercent;
        FailureAnalysis = failureAnalysis;
        ExitCode = exitCode;
        StdOut = stdOut ?? string.Empty;
        StdErr = stdErr ?? string.Empty;
        ResultsXmlPath = resultsXmlPath;
    }
}

