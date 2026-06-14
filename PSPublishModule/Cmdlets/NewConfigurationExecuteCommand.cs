using System.Collections;
using System;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a module pipeline lifecycle action.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle actions run PowerShell at a named module pipeline context point such as <c>AfterStaging</c>,
/// <c>AfterManifest</c>, or <c>BeforePublish</c>. PowerForge writes a stable JSON context file before each
/// action and exposes its path through the <c>POWERFORGE_CONTEXT</c> environment variable.
/// </para>
/// <para>
/// Use lifecycle actions for project-specific preparation, generated-file adjustments, artifact checks, and
/// release guardrails that need a precise pipeline context. Configuration segment order does not control
/// execution order; the <c>At</c> value does.
/// </para>
/// </remarks>
/// <example>
/// <summary>Run an inline action after staging</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationExecute -Name 'Inspect staged module' -At AfterStaging -InlineScript '$ctx = Get-Content $env:POWERFORGE_CONTEXT | ConvertFrom-Json; Get-ChildItem $ctx.ModuleRoot'</code>
/// <para>Runs the inline PowerShell after the module staging context exists.</para>
/// </example>
/// <example>
/// <summary>Run a script file before publishing</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationExecute -Name 'Release guard' -At BeforePublish -FilePath '.\Build\Test-ReleaseReady.ps1' -TimeoutSeconds 120</code>
/// <para>Runs a repository script before publish steps execute.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationExecute", DefaultParameterSetName = ParameterSetInline)]
[OutputType(typeof(ConfigurationActionSegment))]
public sealed class NewConfigurationExecuteCommand : PSCmdlet
{
    private const string ParameterSetFile = "File";
    private const string ParameterSetInline = "Inline";
    private const string ParameterSetScriptBlock = "ScriptBlock";

    /// <summary>Friendly action name shown in progress and result output.</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Stable module pipeline lifecycle point where the action runs.</summary>
    [Parameter]
    public ModulePipelineActionStage At { get; set; } = ModulePipelineActionStage.AfterBuild;

    /// <summary>Path to a PowerShell script file. Relative paths resolve from the project root.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetFile)]
    public string? FilePath { get; set; }

    /// <summary>Inline PowerShell script text.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetInline)]
    public string? InlineScript { get; set; }

    /// <summary>Inline PowerShell script block. The script block text is executed out-of-process.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetScriptBlock)]
    public ScriptBlock? ScriptBlock { get; set; }

    /// <summary>Optional working directory. Relative paths resolve from the project root.</summary>
    [Parameter]
    public string? WorkingDirectory { get; set; }

    /// <summary>Environment variable overrides passed to the action process.</summary>
    [Parameter]
    public Hashtable? Environment { get; set; }

    /// <summary>Action timeout in seconds. Defaults to five minutes.</summary>
    [Parameter]
    public int? TimeoutSeconds { get; set; }

    /// <summary>When set, logs a failed action and lets the pipeline continue.</summary>
    [Parameter]
    public SwitchParameter ContinueOnError { get; set; }

    /// <summary>When set, disables the action while keeping it in configuration.</summary>
    [Parameter]
    public SwitchParameter Disabled { get; set; }

    /// <summary>When set, prefer Windows PowerShell before pwsh on Windows.</summary>
    [Parameter]
    public SwitchParameter PreferWindowsPowerShell { get; set; }

    /// <summary>Writes the lifecycle action configuration segment.</summary>
    protected override void ProcessRecord()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (Environment is not null)
        {
            foreach (DictionaryEntry entry in Environment)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key)) continue;
                environment[key!.Trim()] = entry.Value?.ToString();
            }
        }

        WriteObject(new ConfigurationActionSegment
        {
            Configuration = new ModulePipelineActionConfiguration
            {
                Enabled = !Disabled.IsPresent,
                Name = Name,
                At = At,
                FilePath = FilePath,
                InlineScript = ScriptBlock is not null ? ScriptBlock.ToString() : InlineScript,
                WorkingDirectory = WorkingDirectory,
                Environment = environment.Count == 0 ? null : environment,
                TimeoutSeconds = TimeoutSeconds,
                ContinueOnError = ContinueOnError.IsPresent,
                PreferWindowsPowerShell = PreferWindowsPowerShell.IsPresent
            }
        });
    }
}
