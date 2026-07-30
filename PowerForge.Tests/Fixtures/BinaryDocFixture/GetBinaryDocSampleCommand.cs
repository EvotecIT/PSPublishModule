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
    [SupportsWildcards]
    public string Name { get; set; } = string.Empty;

    /// <summary>Selects the sample rendering mode.</summary>
    [Parameter]
    [PSDefaultValue(Value = BinaryDocMode.Basic)]
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

/// <summary>Returns parameters with explicitly empty defaults.</summary>
[Cmdlet(VerbsCommon.Get, "BinaryDocEmptyDefault")]
public sealed class GetBinaryDocEmptyDefaultCommand : PSCmdlet
{
    /// <summary>An optional label whose empty default has a human-readable description.</summary>
    [Parameter]
    [PSDefaultValue(Value = "", Help = "Empty string")]
    public string Label { get; set; } = string.Empty;

    /// <summary>An optional separator whose empty default is rendered as a PowerShell literal.</summary>
    [Parameter]
    [PSDefaultValue(Value = "")]
    public string Separator { get; set; } = string.Empty;

    /// <summary>An optional delay whose authored help takes precedence over its numeric value.</summary>
    [Parameter]
    [PSDefaultValue(Value = 5, Help = "five seconds")]
    public int DelaySeconds { get; set; } = 5;

    /// <summary>An optional delimiter whose trailing space is part of the default.</summary>
    [Parameter]
    [PSDefaultValue(Value = ", ")]
    public string Delimiter { get; set; } = ", ";

    /// <summary>Optional names whose element boundaries must remain visible.</summary>
    [Parameter]
    [PSDefaultValue(Value = new[] { "a", "b c" })]
    public string[] Names { get; set; } = ["a", "b c"];

    /// <summary>Optional switches whose Boolean values must remain valid PowerShell literals.</summary>
    [Parameter]
    [PSDefaultValue(Value = new[] { true, false })]
    public bool[] Switches { get; set; } = [true, false];

    /// <summary>Optional modes whose enum values must remain valid PowerShell literals.</summary>
    [Parameter]
    [PSDefaultValue(Value = new[] { BinaryDocMode.Basic, BinaryDocMode.Advanced })]
    public BinaryDocMode[] Modes { get; set; } = [BinaryDocMode.Basic, BinaryDocMode.Advanced];

    /// <summary>An optional string containing an XML-invalid control character.</summary>
    [Parameter]
    [PSDefaultValue(Value = "\0")]
    public string ControlText { get; set; } = "\0";

    /// <summary>An optional XML-invalid control character.</summary>
    [Parameter]
    [PSDefaultValue(Value = '\0')]
    public char ControlCharacter { get; set; }

    /// <summary>An optional type whose default must remain a type literal.</summary>
    [Parameter]
    [PSDefaultValue(Value = typeof(string))]
    public Type ValueType { get; set; } = typeof(string);

    /// <summary>Optional types whose element semantics must remain visible.</summary>
    [Parameter]
    [PSDefaultValue(Value = new[] { typeof(string), typeof(int) })]
    public Type[] ValueTypes { get; set; } = [typeof(string), typeof(int)];

    /// <summary>An optional value whose declared default is explicitly null.</summary>
    [Parameter]
    [PSDefaultValue(Value = null)]
    public string? OptionalValue { get; set; }
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
