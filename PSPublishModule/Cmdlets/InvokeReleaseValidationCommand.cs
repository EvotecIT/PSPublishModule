using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Validates final artifacts and runs isolated product smoke tests without publishing.</summary>
/// <para>Configuration can come from JSON, a typed object, or a settings block using New-ConfigurationReleaseValidation.</para>
/// <example><summary>Validate a local package set</summary><code>Invoke-ReleaseValidation -ConfigPath './Build/validation.json' -Variables @{ PackageRoot = './Artifacts/packages' }</code></example>
/// <example><summary>Export a command contract as JSON</summary><code>Invoke-ReleaseValidation -JsonOnly -Settings { New-ConfigurationReleaseValidation -Commands @{ Name = 'CLI help'; FileName = 'example'; Arguments = @('--help') } }</code></example>
[Cmdlet(VerbsLifecycle.Invoke, "ReleaseValidation", DefaultParameterSetName = "Config")]
[OutputType(typeof(ReleaseValidationReport), typeof(string))]
public sealed class InvokeReleaseValidationCommand : AsyncPSCmdlet
{
    /// <para>Path to a release-validation JSON file.</para>
    [Parameter(Mandatory = true, ParameterSetName = "Config")]
    public string ConfigPath { get; set; } = string.Empty;
    /// <para>Typed validation configuration.</para>
    [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = "Configuration")]
    public ReleaseValidationSpec Configuration { get; set; } = new();
    /// <para>Block that emits one New-ConfigurationReleaseValidation object.</para>
    [Parameter(Mandatory = true, ParameterSetName = "Settings")]
    public ScriptBlock Settings { get; set; } = ScriptBlock.Create(string.Empty);
    /// <para>Optional project-root override.</para>
    [Parameter] public string? ProjectRoot { get; set; }
    /// <para>Expected artifact version; otherwise inferred from the package or module.</para>
    [Parameter] public string? Version { get; set; }
    /// <para>Named path and value substitutions used by the configuration.</para>
    [Parameter] public Hashtable Variables { get; set; } = new();
    /// <para>Return JSON without executing validation.</para>
    [Parameter] public SwitchParameter JsonOnly { get; set; }
    /// <para>Optional configuration export path. Relative paths use the current PowerShell directory.</para>
    [Parameter] public string? JsonPath { get; set; }

    /// <summary>Maps PowerShell inputs to the shared validation engine.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var configPath = ParameterSetName == "Config" ? SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath) : null;
        var spec = configPath is not null ? ReleaseValidationService.Load(configPath) : Configuration;
        if (ParameterSetName == "Settings")
        {
            var output = Settings.Invoke().Select(value => value.BaseObject).ToArray();
            if (output.Length != 1 || output[0] is not ReleaseValidationSpec configured)
                throw new ArgumentException("Settings must emit one release-validation configuration.");
            spec = configured;
        }
        if (JsonOnly || JsonPath is not null)
        {
            var json = ReleaseValidationService.Serialize(spec);
            if (JsonPath is not null) File.WriteAllText(SessionState.Path.GetUnresolvedProviderPathFromPSPath(JsonPath), json);
            if (JsonOnly) { WriteObject(json); return; }
        }
        var request = new ReleaseValidationRequest
        {
            ProjectRoot = ProjectRoot is null ? (configPath is null ? Path.GetFullPath(Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path, spec.ProjectRoot)) : null)
                : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectRoot),
            Version = Version
        };
        foreach (DictionaryEntry entry in Variables) request.Variables[Convert.ToString(entry.Key)!] = Convert.ToString(entry.Value) ?? string.Empty;
        var result = await new ReleaseValidationService().RunAsync(spec, configPath, request, CancelToken);
        if (!result.Success) ThrowTerminatingError(new ErrorRecord(new InvalidOperationException(string.Join(Environment.NewLine, result.Errors)),
            "ReleaseValidationFailed", ErrorCategory.InvalidResult, result));
        WriteObject(result);
    }
}
