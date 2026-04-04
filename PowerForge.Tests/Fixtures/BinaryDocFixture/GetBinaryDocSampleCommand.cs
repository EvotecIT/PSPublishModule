using System.Management.Automation;

namespace BinaryDocFixture;

/// <summary>Returns a sample binary help object.</summary>
/// <para>First legacy paragraph for the long command description.</para>
/// <para>Second legacy paragraph for the long command description.</para>
/// <list type="alertSet">
///   <item>
///     <term>Important</term>
///     <description>
///       <para>Only use this command with fixture input.</para>
///       <para>It exists to validate generated help fidelity.</para>
///     </description>
///   </item>
/// </list>
/// <example>
///   <summary>Render a sample object</summary>
///   <prefix>PS&gt; </prefix>
///   <code>
///     Get-BinaryDocSample `
///       -Name 'Alpha' `
///       -Mode Advanced
///   </code>
///   <para>Returns a sample output object for documentation tests.</para>
///   <para>Preserves example formatting and prompt handling.</para>
/// </example>
/// <seealso href="https://example.invalid/binary-doc-sample">Binary fixture reference</seealso>
[Cmdlet(VerbsCommon.Get, "BinaryDocSample")]
[OutputType(typeof(BinaryDocOutput))]
public sealed class GetBinaryDocSampleCommand : PSCmdlet
{
    /// <summary>Name of the requested sample object.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("SampleName")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Selects the sample rendering mode.</summary>
    [Parameter]
    public BinaryDocMode Mode { get; set; } = BinaryDocMode.Basic;

    /// <summary>Writes the sample output object.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new BinaryDocOutput
        {
            Name = Name,
            Mode = Mode
        });
    }
}

/// <summary>Rendering mode for the binary documentation fixture.</summary>
public enum BinaryDocMode
{
    /// <summary>Basic fixture output.</summary>
    Basic,

    /// <summary>Advanced fixture output.</summary>
    Advanced
}

/// <summary>Represents the output returned by the binary documentation fixture command.</summary>
public sealed class BinaryDocOutput
{
    /// <summary>Gets or sets the sample name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the rendering mode.</summary>
    public BinaryDocMode Mode { get; set; }
}
