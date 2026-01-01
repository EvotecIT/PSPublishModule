using System;
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

        if (Internal.IsPresent)
        {
            WriteVerbose($"Processing project cleanup for: {Path.GetFullPath(root)}");
        }
        else
        {
            WriteHostLine($"Processing project cleanup for: {Path.GetFullPath(root)}", ConsoleColor.Cyan);
        }

        var service = new ProjectCleanupService();
        var output = service.Clean(
            spec: spec,
            shouldProcess: isWhatIf ? null : (target, action) => ShouldProcess(target, action),
            onItemProcessed: ShowProgress.IsPresent ? (cur, total, item) => OnItemProcessed(cur, total, item) : null);

        if (output.Summary.TotalItems == 0)
        {
            if (Internal.IsPresent)
                WriteVerbose("No files or folders found matching the specified criteria.");
            else
                WriteHostLine("No files or folders found matching the specified criteria.", ConsoleColor.Yellow);
            return;
        }

        WriteSummary(output, isWhatIf);

        if (PassThru.IsPresent)
            WriteObject(output, enumerateCollection: false);
    }

    private void OnItemProcessed(int current, int total, ProjectCleanupItemResult item)
    {
        if (Internal.IsPresent)
        {
            switch (item.Status)
            {
                case ProjectCleanupStatus.WhatIf:
                    WriteVerbose($"Would remove: {item.RelativePath}");
                    break;
                case ProjectCleanupStatus.Removed:
                    WriteVerbose($"Removed: {item.RelativePath}");
                    break;
                case ProjectCleanupStatus.Failed:
                case ProjectCleanupStatus.Error:
                    WriteWarning($"Failed to remove: {item.RelativePath}");
                    break;
            }
            return;
        }

        switch (item.Status)
        {
            case ProjectCleanupStatus.WhatIf:
                WriteHostLine($"  [WOULD REMOVE] {item.RelativePath}", ConsoleColor.Yellow);
                break;
            case ProjectCleanupStatus.Removed:
                WriteHostLine($"  [{current}/{total}] [REMOVED] {item.RelativePath}", ConsoleColor.Red);
                break;
            case ProjectCleanupStatus.Failed:
                WriteHostLine($"  [{current}/{total}] [FAILED] {item.RelativePath}", ConsoleColor.Red);
                break;
            case ProjectCleanupStatus.Error:
                WriteHostLine($"  [{current}/{total}] [ERROR] {item.RelativePath}: {item.Error}", ConsoleColor.Red);
                break;
        }
    }

    private void WriteSummary(ProjectCleanupOutput output, bool isWhatIf)
    {
        if (Internal.IsPresent)
        {
            WriteVerbose($"Cleanup Summary: Project path: {output.Summary.ProjectPath}");
            WriteVerbose($"Cleanup type: {output.Summary.ProjectType}");
            WriteVerbose($"Total items processed: {output.Summary.TotalItems}");

            if (isWhatIf)
            {
                WriteVerbose($"Would remove: {output.Summary.TotalItems} items");
                WriteVerbose($"Would free: {Math.Round(output.Results.Where(r => r.Type == ProjectCleanupItemType.File).Sum(r => r.Size) / (1024d * 1024d), 2)} MB");
            }
            else
            {
                WriteVerbose($"Successfully removed: {output.Summary.Removed}");
                WriteVerbose($"Errors: {output.Summary.Errors}");
                WriteVerbose($"Space freed: {output.Summary.SpaceFreedMB} MB");
                if (!string.IsNullOrWhiteSpace(output.Summary.BackupDirectory))
                    WriteVerbose($"Backups created in: {output.Summary.BackupDirectory}");
            }
            return;
        }

        WriteHostLine(string.Empty, ConsoleColor.White);
        WriteHostLine("Cleanup Summary:", ConsoleColor.Cyan);
        WriteHostLine($"  Project path: {output.Summary.ProjectPath}", ConsoleColor.White);
        WriteHostLine($"  Cleanup type: {output.Summary.ProjectType}", ConsoleColor.White);
        WriteHostLine($"  Total items processed: {output.Summary.TotalItems}", ConsoleColor.White);

        if (isWhatIf)
        {
            var totalSizeMb = Math.Round(output.Results.Where(r => r.Type == ProjectCleanupItemType.File).Sum(r => r.Size) / (1024d * 1024d), 2);
            WriteHostLine($"  Would remove: {output.Summary.TotalItems} items", ConsoleColor.Yellow);
            WriteHostLine($"  Would free: {totalSizeMb} MB", ConsoleColor.Yellow);
            WriteHostLine("Run without -WhatIf to actually remove these items.", ConsoleColor.Cyan);
        }
        else
        {
            WriteHostLine($"  Successfully removed: {output.Summary.Removed}", ConsoleColor.Green);
            WriteHostLine($"  Errors: {output.Summary.Errors}", ConsoleColor.Red);
            WriteHostLine($"  Space freed: {output.Summary.SpaceFreedMB} MB", ConsoleColor.Green);
            if (!string.IsNullOrWhiteSpace(output.Summary.BackupDirectory))
                WriteHostLine($"  Backups created in: {output.Summary.BackupDirectory}", ConsoleColor.Blue);
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
