using System;
using System.Collections;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;

namespace PSPublishModule;

/// <summary>
/// Tells the module to create an artefact of a specified type.
/// </summary>
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
    public ArtefactType Type { get; set; }

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

    /// <summary>Hashtable of directories to copy to artefact (key: source, value: destination).</summary>
    [Parameter]
    public IDictionary? CopyDirectories { get; set; }

    /// <summary>Hashtable of files to copy to artefact (key: source, value: destination).</summary>
    [Parameter]
    public IDictionary? CopyFiles { get; set; }

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
        var typeString = Type.ToString();

        var requiredModules = new OrderedDictionary();
        var configuration = new OrderedDictionary
        {
            ["Type"] = typeString,
            ["RequiredModules"] = requiredModules
        };

        var artefact = new OrderedDictionary
        {
            ["Type"] = typeString,
            ["Configuration"] = configuration
        };

        if (MyInvocation.BoundParameters.ContainsKey(nameof(Enable)))
            configuration["Enabled"] = Enable.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(IncludeTagName)))
            configuration["IncludeTagName"] = IncludeTagName.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(Path)) && Path is not null)
            configuration["Path"] = NormalizePath(Path);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(RequiredModulesPath)) && RequiredModulesPath is not null)
            requiredModules["Path"] = NormalizePath(RequiredModulesPath);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(AddRequiredModules)))
            requiredModules["Enabled"] = true;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ModulesPath)) && ModulesPath is not null)
            requiredModules["ModulesPath"] = NormalizePath(ModulesPath);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyDirectories)) && CopyDirectories is not null)
            configuration["DirectoryOutput"] = NormalizeDictionaryValues(CopyDirectories);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyDirectoriesRelative)))
            configuration["DestinationDirectoriesRelative"] = CopyDirectoriesRelative.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyFiles)) && CopyFiles is not null)
            configuration["FilesOutput"] = NormalizeDictionaryValues(CopyFiles);

        if (MyInvocation.BoundParameters.ContainsKey(nameof(CopyFilesRelative)))
            configuration["DestinationFilesRelative"] = CopyFilesRelative.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(DoNotClear)))
            configuration["DoNotClear"] = DoNotClear.IsPresent;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ArtefactName)))
            configuration["ArtefactName"] = ArtefactName;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ScriptName)))
            configuration["ScriptName"] = ScriptName;

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PreScriptMerge)) && PreScriptMerge is not null)
            configuration["PreScriptMerge"] = TryFormatScript(PreScriptMerge.ToString());

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PostScriptMerge)) && PostScriptMerge is not null)
            configuration["PostScriptMerge"] = TryFormatScript(PostScriptMerge.ToString());

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PreScriptMergePath)) && !string.IsNullOrWhiteSpace(PreScriptMergePath))
        {
            var scriptContent = File.ReadAllText(PreScriptMergePath!);
            if (!string.IsNullOrWhiteSpace(scriptContent))
                configuration["PreScriptMerge"] = TryFormatScript(scriptContent);
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(PostScriptMergePath)) && !string.IsNullOrWhiteSpace(PostScriptMergePath))
        {
            var scriptContent = File.ReadAllText(PostScriptMergePath!);
            if (!string.IsNullOrWhiteSpace(scriptContent))
                configuration["PostScriptMerge"] = TryFormatScript(scriptContent);
        }

        if (MyInvocation.BoundParameters.ContainsKey(nameof(ID)))
            configuration["ID"] = ID;

        WriteObject(artefact);
    }

    private static string NormalizePath(string value)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? value.Replace('/', '\\')
            : value.Replace('\\', '/');
    }

    private static IDictionary NormalizeDictionaryValues(IDictionary input)
    {
        var normalized = new OrderedDictionary();
        foreach (DictionaryEntry entry in input)
        {
            var v = entry.Value;
            if (v is string s) v = NormalizePath(s);
            normalized[entry.Key] = v;
        }

        return normalized;
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
