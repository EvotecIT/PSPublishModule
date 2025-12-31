using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Tells the module to create an artefact of a specified type.
/// </summary>
/// <example>
/// <summary>Create a packed ZIP artefact</summary>
/// <code>New-ConfigurationArtefact -Type Packed -Enable -Path 'Artefacts\Packed' -ID 'Packed'</code>
/// </example>
/// <example>
/// <summary>Create an unpacked artefact including required modules</summary>
/// <code>New-ConfigurationArtefact -Type Unpacked -Enable -AddRequiredModules -Path 'Artefacts\Unpacked' -RequiredModulesRepository 'PSGallery'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationArtefact")]
public sealed class NewConfigurationArtefactCommand : PSCmdlet
{
    /// <summary>ScriptBlock that will be added at the end of the script (Script / ScriptPacked).</summary>
    [Parameter(Position = 0)]
    public ScriptBlock? PostScriptMerge { get; set; }

    /// <summary>ScriptBlock that will be added at the beginning of the script (Script / ScriptPacked).</summary>
    [Parameter(Position = 1)]
    public ScriptBlock? PreScriptMerge { get; set; }

    /// <summary>Artefact type to generate.</summary>
    [Parameter(Mandatory = true)]
    public PowerForge.ArtefactType Type { get; set; }

    /// <summary>Enable artefact creation. By default artefact creation is disabled.</summary>
    [Parameter]
    public SwitchParameter Enable { get; set; }

    /// <summary>Include tag name in artefact name. By default tag name is not included.</summary>
    [Parameter]
    public SwitchParameter IncludeTagName { get; set; }

    /// <summary>Path where artefact will be created.</summary>
    [Parameter]
    public string? Path { get; set; }

    /// <summary>Add required modules to artefact by copying them over.</summary>
    [Parameter]
    [Alias("RequiredModules")]
    public SwitchParameter AddRequiredModules { get; set; }

    /// <summary>Path where main module (or required module) will be copied to.</summary>
    [Parameter]
    public string? ModulesPath { get; set; }

    /// <summary>Path where required modules will be copied to.</summary>
    [Parameter]
    public string? RequiredModulesPath { get; set; }

    /// <summary>Repository name used when downloading required modules (Save-PSResource / Save-Module).</summary>
    [Parameter]
    public string? RequiredModulesRepository { get; set; }

    /// <summary>Repository credential username (basic auth) used when downloading required modules.</summary>
    [Parameter]
    public string? RequiredModulesCredentialUserName { get; set; }

    /// <summary>Repository credential secret (password/token) in clear text used when downloading required modules.</summary>
    [Parameter]
    public string? RequiredModulesCredentialSecret { get; set; }

    /// <summary>Repository credential secret (password/token) in a clear-text file used when downloading required modules.</summary>
    [Parameter]
    public string? RequiredModulesCredentialSecretFilePath { get; set; }

    /// <summary>Directories to copy to artefact (Source/Destination). Accepts legacy hashtable (source=&gt;destination) or <see cref="ArtefactCopyMapping"/>[]</summary>
    [Parameter]
    [ArtefactCopyMappingsTransformation]
    public ArtefactCopyMapping[]? CopyDirectories { get; set; }

    /// <summary>Files to copy to artefact (Source/Destination). Accepts legacy hashtable (source=&gt;destination) or <see cref="ArtefactCopyMapping"/>[]</summary>
    [Parameter]
    [ArtefactCopyMappingsTransformation]
    public ArtefactCopyMapping[]? CopyFiles { get; set; }

    /// <summary>Define if destination directories should be relative to artefact root.</summary>
    [Parameter]
    public SwitchParameter CopyDirectoriesRelative { get; set; }

    /// <summary>Define if destination files should be relative to artefact root.</summary>
    [Parameter]
    public SwitchParameter CopyFilesRelative { get; set; }

    /// <summary>Do not clear artefact output directory before creating artefact.</summary>
    [Parameter]
    public SwitchParameter DoNotClear { get; set; }

    /// <summary>The name of the artefact. If not specified, the default name will be used.</summary>
    [Parameter]
    public string? ArtefactName { get; set; }

    /// <summary>The name of the script artefact (alias: FileName).</summary>
    [Parameter]
    [Alias("FileName")]
    public string? ScriptName { get; set; }

    /// <summary>Optional ID of the artefact (to be used by New-ConfigurationPublish).</summary>
    [Parameter]
    public string? ID { get; set; }

    /// <summary>Path to file that will be added at the end of the script (Script / ScriptPacked).</summary>
    [Parameter]
    public string? PostScriptMergePath { get; set; }

    /// <summary>Path to file that will be added at the beginning of the script (Script / ScriptPacked).</summary>
    [Parameter]
    public string? PreScriptMergePath { get; set; }

    /// <summary>Emits an artefact configuration object for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var artefact = new ConfigurationArtefactSegment
        {
            ArtefactType = Type,
            Configuration = new ArtefactConfiguration
            {
                RequiredModules = new ArtefactRequiredModulesConfiguration()
            }
        };

        if (MyInvocation.BoundParameters.ContainsKey(nameof(Enable)))
            artefact.Configuration.Enabled = Enable.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(IncludeTagName)))
            artefact.Configuration.IncludeTagName = IncludeTagName.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(Path)) && Path is not null)
            artefact.Configuration.Path = NormalizePath(Path);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(RequiredModulesPath)) && RequiredModulesPath is not null)
            artefact.Configuration.RequiredModules.Path = NormalizePath(RequiredModulesPath);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(RequiredModulesRepository)) &&
            !string.IsNullOrWhiteSpace(RequiredModulesRepository))
        {
            artefact.Configuration.RequiredModules.Repository = RequiredModulesRepository!.Trim();
        }

        var requiredModulesSecret = string.Empty;
        if (MyInvocation.BoundParameters.ContainsKey(nameof(RequiredModulesCredentialSecretFilePath)) &&
            !string.IsNullOrWhiteSpace(RequiredModulesCredentialSecretFilePath))
        {
            requiredModulesSecret = File.ReadAllText(RequiredModulesCredentialSecretFilePath!).Trim();
        }
        else if (MyInvocation.BoundParameters.ContainsKey(nameof(RequiredModulesCredentialSecret)) &&
                 !string.IsNullOrWhiteSpace(RequiredModulesCredentialSecret))
        {
            requiredModulesSecret = RequiredModulesCredentialSecret!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(requiredModulesSecret) &&
            string.IsNullOrWhiteSpace(RequiredModulesCredentialUserName))
        {
            throw new PSArgumentException(
                "RequiredModulesCredentialUserName is required when RequiredModulesCredentialSecret/RequiredModulesCredentialSecretFilePath is provided.");
        }

        var hasRequiredModulesCredential =
            !string.IsNullOrWhiteSpace(RequiredModulesCredentialUserName) &&
            !string.IsNullOrWhiteSpace(requiredModulesSecret);

        if (hasRequiredModulesCredential)
        {
            artefact.Configuration.RequiredModules.Credential = new RepositoryCredential
            {
                UserName = RequiredModulesCredentialUserName!.Trim(),
                Secret = requiredModulesSecret
            };
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(AddRequiredModules)))
            artefact.Configuration.RequiredModules.Enabled = true;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ModulesPath)) && ModulesPath is not null)
            artefact.Configuration.RequiredModules.ModulesPath = NormalizePath(ModulesPath);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyDirectories)) && CopyDirectories is not null)
            artefact.Configuration.DirectoryOutput = NormalizeMappings(CopyDirectories);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyDirectoriesRelative)))
            artefact.Configuration.DestinationDirectoriesRelative = CopyDirectoriesRelative.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyFiles)) && CopyFiles is not null)
            artefact.Configuration.FilesOutput = NormalizeMappings(CopyFiles);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyFilesRelative)))
            artefact.Configuration.DestinationFilesRelative = CopyFilesRelative.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(DoNotClear)))
            artefact.Configuration.DoNotClear = DoNotClear.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ArtefactName)))
            artefact.Configuration.ArtefactName = ArtefactName;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ScriptName)))
            artefact.Configuration.ScriptName = ScriptName;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PreScriptMerge)) && PreScriptMerge is not null)
            artefact.Configuration.PreScriptMerge = TryFormatScript(PreScriptMerge.ToString());

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PostScriptMerge)) && PostScriptMerge is not null)
            artefact.Configuration.PostScriptMerge = TryFormatScript(PostScriptMerge.ToString());

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PreScriptMergePath)) && !string.IsNullOrWhiteSpace(PreScriptMergePath))
        {
            var scriptContent = File.ReadAllText(PreScriptMergePath!);
            if (!string.IsNullOrWhiteSpace(scriptContent))
                artefact.Configuration.PreScriptMerge = TryFormatScript(scriptContent);
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PostScriptMergePath)) && !string.IsNullOrWhiteSpace(PostScriptMergePath))
        {
            var scriptContent = File.ReadAllText(PostScriptMergePath!);
            if (!string.IsNullOrWhiteSpace(scriptContent))
                artefact.Configuration.PostScriptMerge = TryFormatScript(scriptContent);
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ID)))
            artefact.Configuration.ID = ID;

        WriteObject(artefact);
    }

    private static string NormalizePath(string value)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? value.Replace('/', '\\')
            : value.Replace('\\', '/');
    }

    private static ArtefactCopyMapping[]? NormalizeMappings(ArtefactCopyMapping[]? input)
    {
        if (input is null || input.Length == 0) return null;

        var mappings = new List<ArtefactCopyMapping>();
        foreach (var entry in input)
        {
            if (entry is null) continue;
            if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Destination))
                continue;

            mappings.Add(new ArtefactCopyMapping
            {
                Source = NormalizePath(entry.Source),
                Destination = NormalizePath(entry.Destination)
            });
        }

        return mappings.Count == 0 ? null : mappings.ToArray();
    }

    private string TryFormatScript(string scriptDefinition)
    {
        if (string.IsNullOrWhiteSpace(scriptDefinition)) return scriptDefinition;

        try
        {
            using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
            ps.AddCommand("Invoke-Formatter")
                .AddParameter("ScriptDefinition", scriptDefinition);

            var results = ps.Invoke();
            if (ps.HadErrors)
                throw ps.Streams.Error.FirstOrDefault()?.Exception ?? new InvalidOperationException("Invoke-Formatter failed.");

            var formatted = results.FirstOrDefault()?.BaseObject?.ToString();
            return string.IsNullOrWhiteSpace(formatted) ? scriptDefinition : formatted!;
        }
        catch (Exception ex)
        {
            WriteWarning($"Unable to format merge script provided by user. Error: {ex.Message}. Using original script.");
            return scriptDefinition;
        }
    }
}
