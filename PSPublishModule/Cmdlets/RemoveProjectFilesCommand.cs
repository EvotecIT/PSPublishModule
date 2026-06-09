using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Removes specific files and folders from a project directory with safety features.
/// </summary>
/// <remarks>
/// <para>
/// Designed for build/CI cleanup scenarios where removing generated artifacts (bin/obj, packed outputs, temporary files)
/// should be predictable and safe. Supports <c>-WhatIf</c>, retries and optional backups.
/// </para>
/// </remarks>
/// <example>
/// <summary>Preview cleanup of build artifacts</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Remove-ProjectFiles -ProjectPath '.' -ProjectType Build -WhatIf</code>
/// <para>Shows what would be removed for the selected cleanup type.</para>
/// </example>
/// <example>
/// <summary>Remove custom patterns with backups enabled</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Remove-ProjectFiles -ProjectPath '.' -IncludePatterns 'bin','obj','*.nupkg' -CreateBackups -BackupDirectory 'C:\Backups\MyRepo'</code>
/// <para>Creates backups before deletion and stores them under the backup directory.</para>
/// </example>
[Cmdlet(VerbsCommon.Remove, "ProjectFiles", SupportsShouldProcess = true, DefaultParameterSetName = ParameterSetProjectType)]
public sealed class RemoveProjectFilesCommand : PSCmdlet
{
    private const string ParameterSetProjectType = "ProjectType";
    private const string ParameterSetCustom = "Custom";

    /// <summary>Path to the project directory to clean.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Type of project cleanup to perform.</summary>
    [Parameter(ParameterSetName = ParameterSetProjectType)]
    public ProjectCleanupType ProjectType { get; set; } = ProjectCleanupType.Build;

    /// <summary>File/folder patterns to include for deletion when using the Custom parameter set.</summary>
    [Parameter(ParameterSetName = ParameterSetCustom, Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] IncludePatterns { get; set; } = Array.Empty<string>();

    /// <summary>Patterns to exclude from deletion.</summary>
    [Parameter]
    public string[]? ExcludePatterns { get; set; }

    /// <summary>Directory names to completely exclude from processing.</summary>
    [Parameter]
    public string[] ExcludeDirectories { get; set; } = { ".git", ".svn", ".hg", "node_modules" };

    /// <summary>Method to use for deletion.</summary>
    [Parameter]
    public ProjectDeleteMethod DeleteMethod { get; set; } = ProjectDeleteMethod.RemoveItem;

    /// <summary>Create backup copies of items before deletion.</summary>
    [Parameter]
    public SwitchParameter CreateBackups { get; set; }

    /// <summary>Directory where backups should be stored (optional).</summary>
    [Parameter]
    public string? BackupDirectory { get; set; }

    /// <summary>Number of retry attempts for each deletion.</summary>
    [Parameter]
    public int Retries { get; set; } = 3;

    /// <summary>Process subdirectories recursively. Defaults to true unless explicitly specified.</summary>
    [Parameter]
    public SwitchParameter Recurse { get; set; }

    /// <summary>Maximum recursion depth. Default is unlimited (-1).</summary>
    [Parameter]
    public int MaxDepth { get; set; } = -1;

    /// <summary>Display progress information during cleanup.</summary>
    [Parameter]
    public SwitchParameter ShowProgress { get; set; }

    /// <summary>Return detailed results.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Suppress console output and use verbose/warning streams instead.</summary>
    [Parameter]
    public SwitchParameter Internal { get; set; }

    /// <summary>Executes the cleanup.</summary>
    protected override void ProcessRecord()
    {
        var display = new ProjectCleanupDisplayService();
        var root = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectPath);
        if (!Directory.Exists(root))
            throw new PSArgumentException($"Project path '{ProjectPath}' not found or is not a directory");

        var recurse = MyInvocation.BoundParameters.ContainsKey(nameof(Recurse)) ? Recurse.IsPresent : true;
        var includePatterns = ParameterSetName == ParameterSetCustom ? IncludePatterns : Array.Empty<string>();
        var isWhatIf = MyInvocation.BoundParameters.ContainsKey("WhatIf") &&
                       MyInvocation.BoundParameters["WhatIf"] is SwitchParameter sp &&
                       sp.IsPresent;

        var spec = new ProjectCleanupSpec
        {
            ProjectPath = root,
            ProjectType = ProjectType,
            IncludePatterns = includePatterns ?? Array.Empty<string>(),
            ExcludePatterns = (ExcludePatterns ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray(),
            ExcludeDirectories = (ExcludeDirectories ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray(),
            DeleteMethod = DeleteMethod,
            CreateBackups = CreateBackups.IsPresent,
            BackupDirectory = BackupDirectory,
            Retries = Retries,
            Recurse = recurse,
            MaxDepth = MaxDepth,
            WhatIf = isWhatIf
        };

        WriteDisplayLines(display.CreateHeader(Path.GetFullPath(root)));

        var service = new ProjectCleanupService();
        var output = service.Clean(
            spec: spec,
            shouldProcess: isWhatIf ? null : (target, action) => ShouldProcess(target, action),
            onItemProcessed: ShowProgress.IsPresent ? (cur, total, item) => OnItemProcessed(cur, total, item) : null);

        if (output.Summary.TotalItems == 0)
        {
            WriteDisplayLines(display.CreateNoMatchesLines(Internal.IsPresent));
            return;
        }

        WriteDisplayLines(display.CreateSummaryLines(output, isWhatIf, Internal.IsPresent));

        if (PassThru.IsPresent)
            WriteObject(output, enumerateCollection: false);
    }

    private void OnItemProcessed(int current, int total, ProjectCleanupItemResult item)
    {
        WriteDisplayLines(new ProjectCleanupDisplayService().CreateItemLines(item, current, total, Internal.IsPresent));
    }

    private void WriteDisplayLines(IReadOnlyList<ProjectCleanupDisplayLine> lines)
    {
        foreach (var line in lines)
        {
            if (Internal.IsPresent)
            {
                if (line.IsWarning)
                    WriteWarning(line.Text);
                else
                    WriteVerbose(line.Text);
                continue;
            }

            WriteHostLine(line.Text, line.Color ?? ConsoleColor.White);
        }
    }

    private void WriteHostLine(string message, ConsoleColor color)
    {
        try
        {
            if (Host?.UI?.RawUI is not null)
            {
                Host.UI.Write(color, Host.UI.RawUI.BackgroundColor, message + Environment.NewLine);
            }
        }
        catch
        {
            WriteVerbose(message);
        }
    }
}
