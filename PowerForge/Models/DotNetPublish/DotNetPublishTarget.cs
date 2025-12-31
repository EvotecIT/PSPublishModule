namespace PowerForge;

/// <summary>
/// A dotnet publish target entry (project + publish settings).
/// </summary>
public sealed class DotNetPublishTarget
{
    /// <summary>Friendly name used in output folders and summaries.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Path to the project file (*.csproj) to publish.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Target kind (optional; affects heuristics like executable discovery).</summary>
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>Publish options for this target.</summary>
    public DotNetPublishPublishOptions Publish { get; set; } = new();
}

/// <summary>
/// Publish options for a single target.
/// </summary>
public sealed class DotNetPublishPublishOptions
{
    /// <summary>
    /// Publish style (Portable/AOT etc).
    /// </summary>
    public DotNetPublishStyle Style { get; set; } = DotNetPublishStyle.Portable;

    /// <summary>
    /// Target framework to publish (e.g. net10.0, net10.0-windows).      
    /// </summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>
    /// Optional target frameworks to publish (e.g. net10.0, net10.0-windows).
    /// When provided and non-empty, this takes precedence over <see cref="Framework"/>.
    /// </summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Runtime identifiers to publish for. When omitted/empty, uses <see cref="DotNetPublishDotNetOptions.Runtimes"/>.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional output path template. Supports tokens: {target}, {rid}, {framework}, {style}, {configuration}.
    /// When omitted, defaults to <c>Artifacts/DotNetPublish/{target}/{rid}/{framework}/{style}</c>.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// When true, publishes into a temporary staging directory and then copies the output to the final directory.
    /// </summary>
    public bool UseStaging { get; set; } = true;

    /// <summary>
    /// When true, clears the final output directory before copying new files.
    /// </summary>
    public bool ClearOutput { get; set; } = true;

    /// <summary>
    /// When true, applies aggressive cleanup (recursive removals) to reduce output size.
    /// </summary>
    public bool Slim { get; set; } = true;

    /// <summary>
    /// When true, keeps debug symbols (*.pdb). Default: false.
    /// </summary>
    public bool KeepSymbols { get; set; }

    /// <summary>
    /// When true, keeps documentation files (*.xml, *.pdf). Default: false.
    /// </summary>
    public bool KeepDocs { get; set; }

    /// <summary>
    /// When true, prunes the <c>ref/</c> folder from publish output (where applicable).
    /// </summary>
    public bool PruneReferences { get; set; } = true;

    /// <summary>
    /// When true, creates a zip file next to the output directory.
    /// </summary>
    public bool Zip { get; set; }

    /// <summary>
    /// Optional zip output path. Supports the same tokens as <see cref="OutputPath"/>.
    /// When omitted, a zip is created in the parent directory of the output folder.
    /// </summary>
    public string? ZipPath { get; set; }

    /// <summary>
    /// Optional zip name template (when <see cref="ZipPath"/> is not provided). Supports tokens: {target}, {rid}, {framework}, {style}, {configuration}.
    /// Default: {target}-{framework}-{rid}-{style}.zip
    /// </summary>
    public string? ZipNameTemplate { get; set; }

    /// <summary>
    /// Optional executable rename (applied after publish). For Windows runtimes, <c>.exe</c> is appended when missing.
    /// </summary>
    public string? RenameTo { get; set; }

    /// <summary>
    /// Optional ReadyToRun toggle for non-AOT publish styles. When null, the project default is used.
    /// </summary>
    public bool? ReadyToRun { get; set; }

    /// <summary>
    /// Optional signing configuration (Windows only).
    /// </summary>
    public DotNetPublishSignOptions? Sign { get; set; }
}

/// <summary>
/// Windows code-signing options for published outputs.
/// </summary>
public sealed class DotNetPublishSignOptions
{
    /// <summary>Enables Authenticode signing of *.exe and *.dll under the output folder.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional path to signtool.exe (defaults to "signtool.exe").</summary>
    public string? ToolPath { get; set; } = "signtool.exe";

    /// <summary>Optional certificate thumbprint (SHA1) used for signing (signtool /sha1).</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Optional certificate subject name used for signing (signtool /n).</summary>
    public string? SubjectName { get; set; }

    /// <summary>Optional timestamp URL (signtool /tr). Default: http://timestamp.digicert.com</summary>
    public string? TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Optional signature description (signtool /d).</summary>
    public string? Description { get; set; }

    /// <summary>Optional URL displayed in signature (signtool /du).</summary>
    public string? Url { get; set; }

    /// <summary>Optional CSP name (signtool /csp).</summary>
    public string? Csp { get; set; }

    /// <summary>Optional key container name (signtool /kc).</summary>
    public string? KeyContainer { get; set; }
}
