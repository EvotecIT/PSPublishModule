using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Analyzes a script or scriptblock and reports functions/commands it calls that are not present.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet parses PowerShell code and returns a list of referenced commands that look like missing local helpers.
/// It is useful when building “portable” scripts/modules where you want to detect (and optionally inline) helper functions.
/// </para>
/// <para>
/// When <c>-ApprovedModules</c> is specified, helper definitions are only accepted from those modules.
/// </para>
/// </remarks>
/// <example>
/// <summary>List missing function dependencies for a script</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-MissingFunctions -FilePath '.\Build\Build-Module.ps1' -Summary</code>
/// <para>Returns a list of functions referenced by the script that are not part of the script itself.</para>
/// </example>
/// <example>
/// <summary>Analyze a scriptblock and return inlineable helper definitions</summary>
/// <prefix>PS&gt; </prefix>
/// <code>$sb = { Invoke-ModuleBuild -ModuleName 'MyModule' }; Get-MissingFunctions -Code $sb -SummaryWithCommands -ApprovedModules 'PSSharedGoods','PSPublishModule'</code>
/// <para>Returns a structured report that can include helper function bodies sourced from approved modules.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "MissingFunctions", DefaultParameterSetName = ParameterSetFile)]
public sealed class GetMissingFunctionsCommand : PSCmdlet
{
    private const string ParameterSetFile = "File";
    private const string ParameterSetCode = "Code";

    /// <summary>Path to a script file to analyze for missing function dependencies. Alias: Path.</summary>
    [Parameter(ParameterSetName = ParameterSetFile)]
    [Alias("Path")]
    public string? FilePath { get; set; }

    /// <summary>ScriptBlock to analyze instead of a file. Alias: ScriptBlock.</summary>
    [Parameter(ParameterSetName = ParameterSetCode)]
    [Alias("ScriptBlock")]
    public ScriptBlock? Code { get; set; }

    /// <summary>Known function names to treat as already available (exclude from missing list).</summary>
    [Parameter]
    public string[] Functions { get; set; } = Array.Empty<string>();

    /// <summary>Return only a flattened summary list of functions used (objects with Name/Source), not inlined definitions.</summary>
    [Parameter]
    public SwitchParameter Summary { get; set; }

    /// <summary>Return a typed report with Summary, SummaryFiltered, and Functions.</summary>
    [Parameter]
    public SwitchParameter SummaryWithCommands { get; set; }

    /// <summary>Module names that are allowed sources for pulling inline helper function definitions.</summary>
    [Parameter]
    public string[] ApprovedModules { get; set; } = Array.Empty<string>();

    /// <summary>Function names to ignore when computing the missing set.</summary>
    [Parameter]
    public string[] IgnoreFunctions { get; set; } = Array.Empty<string>();

    /// <summary>Executes the analysis.</summary>
    protected override void ProcessRecord()
    {
        if (string.IsNullOrWhiteSpace(FilePath) && Code is null)
            return;

        var analyzer = new MissingFunctionsAnalyzer();
        var report = analyzer.Analyze(
            filePath: FilePath,
            code: Code?.ToString(),
            options: new MissingFunctionsOptions(
                knownFunctions: Functions,
                approvedModules: ApprovedModules,
                ignoreFunctions: IgnoreFunctions,
                includeFunctionsRecursively: SummaryWithCommands.IsPresent));

        if (SummaryWithCommands.IsPresent)
        {
            WriteObject(report, enumerateCollection: false);
            return;
        }

        if (Summary.IsPresent)
        {
            foreach (var o in report.SummaryFiltered)
                WriteObject(o, enumerateCollection: false);
            return;
        }

        foreach (var line in report.FunctionsTopLevelOnly)
            WriteObject(line, enumerateCollection: false);
    }
}
