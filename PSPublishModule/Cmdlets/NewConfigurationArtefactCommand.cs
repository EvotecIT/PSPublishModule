using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Tells the module to create an artefact of a specified type.
/// </summary>
/// <remarks>
/// <para>
/// Artefacts are created after the module is built into staging. Use <c>Packed</c> (ZIP) for distribution and
/// <c>Unpacked</c> (folder) for inspection or offline installation.
/// </para>
/// <para>
/// When <c>-AddRequiredModules</c> is enabled, required modules are copied from locally available modules (Get-Module -ListAvailable) and,
/// when configured, downloaded (via <c>Save-PSResource</c>/<c>Save-Module</c>) before being copied into the artefact.
/// </para>
/// <para>
/// Only <c>RequiredModule</c> dependencies participate in artefact bundling. <c>ExternalModule</c> dependencies are
/// intentionally excluded because they represent dependencies that should remain separately installed on the target
/// machine.
/// </para>
/// <para>
/// <c>RequiredModulesSource</c> controls the packaging strategy: <c>Installed</c> means local-only copy,
/// <c>Auto</c> means prefer local and download when missing, and <c>Download</c> means always download. When omitted,
/// the default is <c>Installed</c>.
/// </para>
/// <para>
/// Use <c>-ID</c> to link an artefact to a publish step (<c>New-ConfigurationPublish</c>) and publish only a specific artefact.
/// </para>
/// <para>
/// For a broader dependency workflow explanation, see <c>about_ModuleDependencies</c>.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a packed ZIP artefact</summary>
/// <code>New-ConfigurationArtefact -Type Packed -Enable -Path 'Artefacts\Packed' -ID 'Packed'</code>
/// </example>
/// <example>
/// <summary>Create an unpacked artefact including required modules</summary>
/// <code>New-ConfigurationArtefact -Type Unpacked -Enable -AddRequiredModules -Path 'Artefacts\Unpacked' -RequiredModulesRepository 'PSGallery'</code>
/// </example>
/// <example>
/// <summary>Always download required modules into a packed artefact</summary>
/// <code>New-ConfigurationArtefact -Type Packed -Enable -AddRequiredModules -RequiredModulesSource Download -RequiredModulesRepository 'PSGallery'</code>
/// <para>Use this when you want packaging to ignore whatever is already installed on the build machine.</para>
/// </example>
/// <example>
/// <summary>Prefer local modules and download only when a dependency is missing</summary>
/// <code>New-ConfigurationArtefact -Type Unpacked -Enable -AddRequiredModules -RequiredModulesSource Auto -RequiredModulesTool Auto</code>
/// <para>This is a good default for developer machines that may already have some dependencies installed.</para>
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

    /// <summary>
    /// Add <c>RequiredModule</c> dependencies to the artefact by copying or downloading them. This does not include
    /// <c>ExternalModule</c> dependencies.
    /// </summary>
    [Parameter]
    [Alias("RequiredModules")]
    public SwitchParameter AddRequiredModules { get; set; }

    /// <summary>Path where main module (or required module) will be copied to.</summary>
    [Parameter]
    public string? ModulesPath { get; set; }

    /// <summary>
    /// Path where required modules will be copied to. When omitted, PowerForge uses the default module layout under
    /// the artefact output.
    /// </summary>
    [Parameter]
    public string? RequiredModulesPath { get; set; }

    /// <summary>
    /// Repository name used when downloading required modules (<c>Save-PSResource</c> / <c>Save-Module</c>). Set this
    /// when packaging should resolve from a specific gallery or private feed.
    /// </summary>
    [Parameter]
    public string? RequiredModulesRepository { get; set; }

    /// <summary>
    /// Tool used when downloading required modules (<c>Save-PSResource</c> / <c>Save-Module</c>). <c>Auto</c> prefers
    /// PSResourceGet and falls back to PowerShellGet when necessary.
    /// </summary>
    [Parameter]
    public ModuleSaveTool RequiredModulesTool { get; set; }

    /// <summary>
    /// Source used when resolving required modules (<c>Auto</c> / <c>Installed</c> / <c>Download</c>). When omitted,
    /// PowerForge defaults to <c>Installed</c>, which means packaging expects the dependency to already exist on the
    /// machine.
    /// </summary>
    [Parameter]
    public RequiredModulesSource RequiredModulesSource { get; set; }

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
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var factory = new ArtefactConfigurationFactory(logger);
        var request = new ArtefactConfigurationRequest
        {
            Type = Type,
            EnableSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(Enable)),
            Enable = Enable.IsPresent,
            IncludeTagNameSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(IncludeTagName)),
            IncludeTagName = IncludeTagName.IsPresent,
            Path = Path,
            ModulesPath = ModulesPath,
            RequiredModulesPath = RequiredModulesPath,
            RequiredModulesRepository = RequiredModulesRepository,
            RequiredModulesTool = MyInvocation.BoundParameters.ContainsKey(nameof(RequiredModulesTool)) ? RequiredModulesTool : null,
            RequiredModulesSource = MyInvocation.BoundParameters.ContainsKey(nameof(RequiredModulesSource)) ? RequiredModulesSource : null,
            AddRequiredModulesSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(AddRequiredModules)),
            AddRequiredModules = AddRequiredModules.IsPresent,
            RequiredModulesCredentialUserName = RequiredModulesCredentialUserName,
            RequiredModulesCredentialSecret = RequiredModulesCredentialSecret,
            RequiredModulesCredentialSecretFilePath = RequiredModulesCredentialSecretFilePath,
            CopyDirectories = MyInvocation.BoundParameters.ContainsKey(nameof(CopyDirectories)) ? CopyDirectories : null,
            CopyDirectoriesRelativeSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(CopyDirectoriesRelative)),
            CopyDirectoriesRelative = CopyDirectoriesRelative.IsPresent,
            CopyFiles = MyInvocation.BoundParameters.ContainsKey(nameof(CopyFiles)) ? CopyFiles : null,
            CopyFilesRelativeSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(CopyFilesRelative)),
            CopyFilesRelative = CopyFilesRelative.IsPresent,
            DoNotClearSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(DoNotClear)),
            DoNotClear = DoNotClear.IsPresent,
            ArtefactName = MyInvocation.BoundParameters.ContainsKey(nameof(ArtefactName)) ? ArtefactName : null,
            ScriptName = MyInvocation.BoundParameters.ContainsKey(nameof(ScriptName)) ? ScriptName : null,
            ID = MyInvocation.BoundParameters.ContainsKey(nameof(ID)) ? ID : null,
            PreScriptMergeText = MyInvocation.BoundParameters.ContainsKey(nameof(PreScriptMerge)) ? PreScriptMerge?.ToString() : null,
            PostScriptMergeText = MyInvocation.BoundParameters.ContainsKey(nameof(PostScriptMerge)) ? PostScriptMerge?.ToString() : null,
            PreScriptMergePath = MyInvocation.BoundParameters.ContainsKey(nameof(PreScriptMergePath)) ? PreScriptMergePath : null,
            PostScriptMergePath = MyInvocation.BoundParameters.ContainsKey(nameof(PostScriptMergePath)) ? PostScriptMergePath : null
        };

        try
        {
            WriteObject(factory.Create(request));
        }
        catch (ArgumentException ex)
        {
            throw new PSArgumentException(ex.Message, ex);
        }
    }
}
