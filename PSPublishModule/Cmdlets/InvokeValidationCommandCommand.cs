using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Runs a bounded validation process and returns its captured output.</summary>
/// <para>Use this for product-specific probes that must inspect structured process output. PowerForge owns timeout, cancellation, argument quoting, and expected-exit checks.</para>
/// <example><summary>Read product diagnostics as JSON</summary><code>$result = Invoke-ValidationCommand -Command @{ FileName = './tool.exe'; Arguments = @('diagnostics', '--json'); OutputJsonKind = 'Object' }; $result.StdOut | ConvertFrom-Json</code></example>
[Cmdlet(VerbsLifecycle.Invoke, "ValidationCommand")]
[OutputType(typeof(ProcessRunResult))]
public sealed class InvokeValidationCommandCommand : AsyncPSCmdlet
{
    /// <para>Executable, arguments, timeout, environment, and output expectations.</para>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    public ReleaseCommandValidation Command { get; set; } = new();
    /// <para>Root for relative paths. Defaults to the current PowerShell filesystem location.</para>
    [Parameter] public string? ProjectRoot { get; set; }
    /// <para>Named substitutions for command arguments, environment, and paths.</para>
    [Parameter] public Hashtable Variables { get; set; } = new();

    /// <summary>Runs the shared process contract without duplicating host logic.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Variables) variables[Convert.ToString(entry.Key)!] = Convert.ToString(entry.Value) ?? string.Empty;
        variables["ProjectRoot"] = ProjectRoot is null ? SessionState.Path.CurrentFileSystemLocation.Path : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectRoot);
        var result = await new ReleaseValidationService().RunCommandAsync(Command, variables, CancelToken);
        WriteObject(result);
    }
}
