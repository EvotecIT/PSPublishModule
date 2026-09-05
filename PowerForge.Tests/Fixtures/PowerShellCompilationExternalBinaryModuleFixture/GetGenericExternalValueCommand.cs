using System.Management.Automation;
using System.Runtime.InteropServices;
using Generic.External.Binary.Dependency;

namespace Generic.External.Binary.Module;

/// <summary>Portable value returned by the external binary-module fixture.</summary>
public sealed class GenericExternalRecord
{
    /// <summary>Requested numeric value.</summary>
    public int Value { get; init; }

    /// <summary>Value returned by the separately built managed dependency.</summary>
    public string DependencyValue { get; init; } = string.Empty;

    /// <summary>Architecture on which the command executed.</summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>Assembly load-context identity observed by the command.</summary>
    public string ModuleLoadContext { get; init; } = string.Empty;

    /// <summary>Whether the module assembly uses the default CLR load context.</summary>
    public bool ModuleUsesDefaultLoadContext { get; init; }

    /// <summary>Physical module assembly location selected by the host.</summary>
    public string ModuleAssemblyLocation { get; init; } = string.Empty;

    /// <summary>Physical dependency assembly location selected by the host.</summary>
    public string DependencyAssemblyLocation { get; init; } = string.Empty;

    /// <summary>Transitive dependency assembly version.</summary>
    public string DependencyAssemblyVersion { get; init; } = string.Empty;

    /// <summary>Dependency assembly load-context identity observed by the command.</summary>
    public string DependencyLoadContext { get; init; } = string.Empty;

    /// <summary>Whether the dependency assembly uses the default CLR load context.</summary>
    public bool DependencyUsesDefaultLoadContext { get; init; }
}

/// <summary>External binary cmdlet used to qualify the hosted module boundary.</summary>
[Cmdlet(VerbsCommon.Get, "GenericExternalValue")]
[OutputType(typeof(GenericExternalRecord))]
public sealed class GetGenericExternalValueCommand : PSCmdlet
{
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Value passed through the transitive managed dependency.</summary>
    [Parameter]
    public int Value { get; set; } = 42;

    /// <summary>Observable behavior selected by the acceptance harness.</summary>
    [Parameter]
    [ValidateSet("Normal", "Error", "Terminating", "Wait")]
    public string Behavior { get; set; } = "Normal";

    /// <summary>Optional path held exclusively until normal, failed, or cancelled completion.</summary>
    [Parameter]
    public string LeasePath { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        FileStream? lease = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(LeasePath))
                lease = new FileStream(LeasePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            if (Behavior.Equals("Wait", StringComparison.OrdinalIgnoreCase))
            {
                _stopping.Token.WaitHandle.WaitOne();
                return;
            }
            WriteInformation("external-information", new[] { "PowerForge.ExternalBinary" });
            WriteWarning("external-warning");
            WriteVerbose("external-verbose");
            WriteDebug("external-debug");
            if (Behavior.Equals("Error", StringComparison.OrdinalIgnoreCase))
                WriteError(new ErrorRecord(
                    new InvalidOperationException("external-nonterminating-error"),
                    "Generic.External.NonTerminating",
                    ErrorCategory.InvalidOperation,
                    Value));
            if (Behavior.Equals("Terminating", StringComparison.OrdinalIgnoreCase))
                ThrowTerminatingError(new ErrorRecord(
                    new InvalidOperationException("external-terminating-error"),
                    "Generic.External.Terminating",
                    ErrorCategory.OperationStopped,
                    Value));
            var moduleAssembly = GetType().Assembly;
            var dependencyAssembly = typeof(GenericValueSource).Assembly;
            var moduleLoadContext = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(moduleAssembly);
            var dependencyLoadContext = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(dependencyAssembly);
            WriteObject(new GenericExternalRecord
            {
                Value = Value,
                DependencyValue = GenericValueSource.Resolve(Value),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ModuleLoadContext = moduleLoadContext?.Name ?? string.Empty,
                ModuleUsesDefaultLoadContext = ReferenceEquals(moduleLoadContext, System.Runtime.Loader.AssemblyLoadContext.Default),
                ModuleAssemblyLocation = moduleAssembly.Location,
                DependencyAssemblyLocation = dependencyAssembly.Location,
                DependencyAssemblyVersion = dependencyAssembly.GetName().Version?.ToString() ?? string.Empty,
                DependencyLoadContext = dependencyLoadContext?.Name ?? string.Empty,
                DependencyUsesDefaultLoadContext = ReferenceEquals(dependencyLoadContext, System.Runtime.Loader.AssemblyLoadContext.Default)
            });
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <inheritdoc />
    protected override void StopProcessing() => _stopping.Cancel();

    /// <inheritdoc />
    protected override void EndProcessing() => _stopping.Dispose();
}
