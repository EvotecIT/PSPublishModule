using System;
using System.Linq;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports BenchmarkDotNet or normalized benchmark artifacts into the common benchmark schema.
/// </summary>
/// <example>
/// <summary>Import BenchmarkDotNet artifacts</summary>
/// <code>Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -OutputPath .\Build\Benchmarks\normalized.json</code>
/// </example>
/// <example>
/// <summary>Import with explicit library identity and output validation</summary>
/// <code>Import-BenchmarkResult -Path .\BenchmarkDotNet.Artifacts -LibraryMap @{ ReadOfficeImo = 'OfficeIMO'; ReadClosedXml = 'ClosedXML' } -ValidationPath .\Build\validation.json</code>
/// </example>
[Cmdlet(VerbsData.Import, "BenchmarkResult", SupportsShouldProcess = true)]
[OutputType(typeof(BenchmarkRunResult))]
public sealed class ImportBenchmarkResultCommand : PSCmdlet
{
    /// <summary>
    /// Input file or directory.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Optional suite name override.
    /// </summary>
    [Parameter]
    public string? Suite { get; set; }

    /// <summary>
    /// Optional numeric culture used by the producing CSV report, for example <c>de-DE</c>.
    /// Supply it when punctuation such as <c>1,234</c> would otherwise be ambiguous.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Culture { get; set; }

    /// <summary>
    /// Map from benchmark method (case) name to library id. Every row must resolve to a library through this map
    /// or a <c>[BenchmarkCategory("library:&lt;id&gt;")]</c> category; library identity is never guessed from names.
    /// </summary>
    [Parameter]
    public Hashtable? LibraryMap { get; set; }

    /// <summary>
    /// JSON array of output validations recorded before timing: objects with library, case (optional),
    /// status (passed or failed) and detail.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ValidationPath { get; set; }

    /// <summary>
    /// Library ids whose licence acceptance you gave explicitly for this run. Required before
    /// acceptance-required libraries can appear in evidence; it is never assumed.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? AcceptLicense { get; set; }

    /// <summary>
    /// Optional output path for normalized JSON.
    /// </summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Imports and optionally writes normalized JSON.
    /// </summary>
    protected override void ProcessRecord()
    {
        var input = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        CultureInfo? culture = string.IsNullOrWhiteSpace(Culture)
            ? null
            : CultureInfo.GetCultureInfo(Culture!);
        var result = new BenchmarkResultImporter().Import(input, new BenchmarkImportOptions
        {
            Suite = Suite,
            Culture = culture,
            LibraryMap = LibraryMap?.Cast<DictionaryEntry>()
                .ToDictionary(item => item.Key?.ToString() ?? string.Empty, item => item.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            Validations = string.IsNullOrWhiteSpace(ValidationPath)
                ? null
                : BenchmarkJson.Read<BenchmarkOutputValidation[]>(SessionState.Path.GetUnresolvedProviderPathFromPSPath(ValidationPath)),
            AcceptedLibraries = AcceptLicense
        });
        if (!string.IsNullOrWhiteSpace(OutputPath))
        {
            var output = SessionState.Path.GetUnresolvedProviderPathFromPSPath(OutputPath!);
            if (ShouldProcess(output, "Write normalized benchmark result"))
            {
                BenchmarkJson.Write(output, result);
                result.Artifacts["normalized.json"] = output;
            }
        }

        WriteObject(result);
    }
}
