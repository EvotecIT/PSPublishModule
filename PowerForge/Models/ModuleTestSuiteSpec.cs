namespace PowerForge;

/// <summary>
/// Typed specification for running a module test suite (dependency install + optional import + Pester execution).
/// </summary>
public sealed class ModuleTestSuiteSpec
{
    /// <summary>Path to the module project directory.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Optional path to a test file/folder. When empty, defaults to <c>Tests</c> under <see cref="ProjectPath"/>.</summary>
    public string? TestPath { get; set; }

    /// <summary>Additional module names to ensure are installed (for example: Pester, PSWriteColor).</summary>
    public string[] AdditionalModules { get; set; } = { "Pester", "PSWriteColor" };

    /// <summary>Module names to skip during dependency installation.</summary>
    public string[] SkipModules { get; set; } = System.Array.Empty<string>();

    /// <summary>Modules to import before running tests (legacy ImportModules segment support).</summary>
    public ModuleDependency[] ImportModules { get; set; } = System.Array.Empty<ModuleDependency>();

    /// <summary>When true, enables verbose output while importing modules.</summary>
    public bool ImportModulesVerbose { get; set; }

    /// <summary>Test output verbosity.</summary>
    public ModuleTestSuiteOutputFormat OutputFormat { get; set; } = ModuleTestSuiteOutputFormat.Detailed;

    /// <summary>Enable code coverage when supported by Pester.</summary>
    public bool EnableCodeCoverage { get; set; }

    /// <summary>Force reinstall of dependencies and force module import.</summary>
    public bool Force { get; set; }

    /// <summary>Skip dependency checking and installation.</summary>
    public bool SkipDependencies { get; set; }

    /// <summary>Skip importing the module under test before executing tests.</summary>
    public bool SkipImport { get; set; }

    /// <summary>When true, keeps the generated NUnit XML test results file on disk.</summary>
    public bool KeepResultsXml { get; set; }

    /// <summary>When true, prefers <c>pwsh</c> for out-of-process execution.</summary>
    public bool PreferPwsh { get; set; } = true;

    /// <summary>Timeout for the test execution process, in seconds. Default is 600 (10 minutes).</summary>
    public int TimeoutSeconds { get; set; } = 600;
}
