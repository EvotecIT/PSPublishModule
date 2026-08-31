using System;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Identifies the source construct represented by a compilation unit.
/// </summary>
public enum PowerShellCompilationUnitKind
{
    /// <summary>Executable statements at script or module scope.</summary>
    Script,

    /// <summary>A named PowerShell function.</summary>
    Function
}

/// <summary>Optional host capabilities available to genuinely compiled methods.</summary>
[Flags]
public enum PowerShellCompilationCapability
{
    /// <summary>Runtime-independent CLR compilation only.</summary>
    None = 0,

    /// <summary>Binary-module methods may route supported Write-* stream commands through their generated PSCmdlet.</summary>
    PowerShellStreams = 1,

    /// <summary>Strict executable analysis may defer direct local-function binding to the executable emitter.</summary>
    LocalFunctionCalls = 2,

    /// <summary>Generated hosts can preserve which parameters were explicitly bound.</summary>
    BoundParameters = 4,

    /// <summary>Binary-module methods may construct genuine PowerShell objects.</summary>
    PowerShellObjects = 8,

    /// <summary>Strict executable entry-point parameters must be bindable from process arguments.</summary>
    ExecutableParameterBinding = 16,

    /// <summary>Generated binary cmdlets can preserve PowerShell pipeline parameter binding.</summary>
    PipelineParameterBinding = 32,

    /// <summary>Generated methods may reference a conservative set of PowerShell host types.</summary>
    PowerShellHostTypes = 64,

    /// <summary>Generated methods may use public PowerShell language conversion primitives.</summary>
    PowerShellLanguageConversions = 128,

    /// <summary>Generated methods may use public PowerShell comparison, wildcard, and membership primitives.</summary>
    PowerShellLanguageOperators = 256,

    /// <summary>Generated methods may lower a bounded set of read-only automatic state and host interactions.</summary>
    RuntimeStateIntrinsics = 512,

    /// <summary>Runtime-free targets may invoke separately packaged executable provider operations through generated stream delegates.</summary>
    RuntimeFreeProviderOperations = 1024
}

/// <summary>
/// A whole script body or function considered as one atomic compilation unit.
/// </summary>
public sealed class PowerShellCompilationUnitPlan
{
    /// <summary>Creates a compilation-unit plan.</summary>
    public PowerShellCompilationUnitPlan(
        string name,
        PowerShellCompilationUnitKind kind,
        int startLine,
        string returnType,
        PowerShellCompilationParameter[] parameters,
        PowerShellCompilationDiagnostic[] diagnostics)
    {
        Name = name ?? string.Empty;
        Kind = kind;
        StartLine = startLine;
        ReturnType = returnType ?? typeof(object).FullName!;
        Parameters = parameters ?? Array.Empty<PowerShellCompilationParameter>();
        Diagnostics = diagnostics ?? Array.Empty<PowerShellCompilationDiagnostic>();
    }

    /// <summary>Function name or the synthetic name <c>&lt;script&gt;</c>.</summary>
    public string Name { get; }

    /// <summary>Kind of source unit.</summary>
    public PowerShellCompilationUnitKind Kind { get; }

    /// <summary>One-based starting source line.</summary>
    public int StartLine { get; }

    /// <summary>Resolved CLR return type name, or <see cref="object"/> until inference completes.</summary>
    public string ReturnType { get; }

    /// <summary>Declared parameters in source order.</summary>
    public PowerShellCompilationParameter[] Parameters { get; }

    /// <summary>Compilation blockers. An empty array means the unit is structurally eligible.</summary>
    public PowerShellCompilationDiagnostic[] Diagnostics { get; }

    /// <summary>Whether the complete unit is structurally eligible for typed compilation.</summary>
    public bool IsCompilable => Diagnostics.Length == 0;
}

/// <summary>
/// A statically typed parameter discovered in PowerShell source.
/// </summary>
public sealed class PowerShellCompilationParameter
{
    /// <summary>Creates a parameter description.</summary>
    public PowerShellCompilationParameter(string name, string typeName, bool hasDefaultValue)
        : this(name, typeName, hasDefaultValue, false)
    {
    }

    /// <summary>Creates a parameter description including preserved binding metadata.</summary>
    public PowerShellCompilationParameter(string name, string typeName, bool hasDefaultValue, bool isMandatory)
        : this(name, typeName, hasDefaultValue, isMandatory, false, null, false, null)
    {
    }

    /// <summary>Creates a parameter description using the original validation contract.</summary>
    public PowerShellCompilationParameter(
        string name,
        string typeName,
        bool hasDefaultValue,
        bool isMandatory,
        bool isSwitch,
        string[]? aliases,
        bool allowNull,
        PowerShellCompilationValidation[]? validations)
        : this(name, typeName, hasDefaultValue, isMandatory, isSwitch, aliases, allowNull, validations,
            PowerShellCompilationParameterTypeCapability.ClrMethod, null, false, false, false, null)
    {
    }

    /// <summary>Creates a parameter description using the pre-default-literal public contract.</summary>
    public PowerShellCompilationParameter(
        string name,
        string typeName,
        bool hasDefaultValue,
        bool isMandatory,
        bool isSwitch,
        string[]? aliases,
        bool allowNull,
        PowerShellCompilationValidation[]? validations,
        PowerShellCompilationParameterTypeCapability typeCapabilities,
        PowerShellCompilationParameterBinding[]? bindings,
        bool allowEmptyString,
        bool allowEmptyCollection,
        bool supportsWildcards)
        : this(name, typeName, hasDefaultValue, isMandatory, isSwitch, aliases, allowNull, validations,
            typeCapabilities, bindings, allowEmptyString, allowEmptyCollection, supportsWildcards, null)
    {
    }

    /// <summary>Creates a parameter description including PowerShell binding and validation metadata.</summary>
    public PowerShellCompilationParameter(
        string name,
        string typeName,
        bool hasDefaultValue,
        bool isMandatory,
        bool isSwitch,
        string[]? aliases,
        bool allowNull,
        PowerShellCompilationValidation[]? validations,
        PowerShellCompilationParameterTypeCapability typeCapabilities = PowerShellCompilationParameterTypeCapability.ClrMethod,
        PowerShellCompilationParameterBinding[]? bindings = null,
        bool allowEmptyString = false,
        bool allowEmptyCollection = false,
        bool supportsWildcards = false,
        PowerShellCompilationLiteral? defaultValue = null)
    {
        Name = name ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        HasDefaultValue = hasDefaultValue;
        IsMandatory = isMandatory;
        IsSwitch = isSwitch;
        Aliases = aliases ?? Array.Empty<string>();
        AllowNull = allowNull;
        Validations = validations ?? Array.Empty<PowerShellCompilationValidation>();
        TypeCapabilities = typeCapabilities;
        Bindings = bindings ?? new[] { new PowerShellCompilationParameterBinding(mandatory: isMandatory) };
        AllowEmptyString = allowEmptyString;
        AllowEmptyCollection = allowEmptyCollection;
        SupportsWildcards = supportsWildcards;
        DefaultValue = defaultValue;
    }

    /// <summary>PowerShell parameter name without the dollar prefix.</summary>
    public string Name { get; }

    /// <summary>Resolved CLR type name.</summary>
    public string TypeName { get; }

    /// <summary>Whether PowerShell source declares a default value.</summary>
    public bool HasDefaultValue { get; }

    /// <summary>Whether source parameter metadata requires a bound value.</summary>
    public bool IsMandatory { get; }

    /// <summary>Whether the source declares a PowerShell <c>[switch]</c> parameter.</summary>
    public bool IsSwitch { get; }

    /// <summary>Alternative PowerShell binding names.</summary>
    public string[] Aliases { get; }

    /// <summary>Whether <c>[AllowNull]</c> is declared.</summary>
    public bool AllowNull { get; }

    /// <summary>Validation metadata preserved for generated executable and cmdlet binders.</summary>
    public PowerShellCompilationValidation[] Validations { get; }

    /// <summary>Typed surfaces that can represent this resolved parameter type.</summary>
    public PowerShellCompilationParameterTypeCapability TypeCapabilities { get; }

    /// <summary>Authored parameter-set bindings. An implicit all-sets binding is used when no attribute is present.</summary>
    public PowerShellCompilationParameterBinding[] Bindings { get; }

    /// <summary>Whether an explicitly bound empty string is allowed.</summary>
    public bool AllowEmptyString { get; }

    /// <summary>Whether an explicitly bound empty collection is allowed.</summary>
    public bool AllowEmptyCollection { get; }

    /// <summary>Whether tooling should interpret the value as supporting wildcard syntax.</summary>
    public bool SupportsWildcards { get; }

    /// <summary>Safely resolved target-typed default, or null when no supported default was declared.</summary>
    public PowerShellCompilationLiteral? DefaultValue { get; }

    /// <summary>Whether any parameter-set binding accepts pipeline input.</summary>
    public bool AcceptsPipelineInput => Bindings.Any(static binding =>
        binding.ValueFromPipeline || binding.ValueFromPipelineByPropertyName);
}

/// <summary>Supported PowerShell validation metadata preserved by typed compilation.</summary>
public enum PowerShellCompilationValidationKind
{
    /// <summary>Reject null values.</summary>
    NotNull,

    /// <summary>Reject null and empty values.</summary>
    NotNullOrEmpty,

    /// <summary>Require one value from a literal set.</summary>
    Set,

    /// <summary>Require a numeric value within an inclusive literal range.</summary>
    Range,

    /// <summary>Require a string matching a regular-expression pattern.</summary>
    Pattern
}

/// <summary>One safely resolved validation attribute.</summary>
public sealed class PowerShellCompilationValidation
{
    /// <summary>Creates validation metadata.</summary>
    public PowerShellCompilationValidation(PowerShellCompilationValidationKind kind, string[]? arguments = null)
    {
        if (!Enum.IsDefined(typeof(PowerShellCompilationValidationKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
        Arguments = arguments ?? Array.Empty<string>();
    }

    /// <summary>Validation behavior.</summary>
    public PowerShellCompilationValidationKind Kind { get; }

    /// <summary>Invariant literal arguments used by the validation behavior.</summary>
    public string[] Arguments { get; }
}

/// <summary>
/// Compilation plan for one PowerShell source file.
/// </summary>
public sealed class PowerShellCompilationFilePlan
{
    /// <summary>Creates a file plan.</summary>
    public PowerShellCompilationFilePlan(string fullPath, string relativePath, PowerShellCompilationUnitPlan[] units, PowerShellCompilationDiagnostic[] diagnostics)
    {
        FullPath = fullPath ?? string.Empty;
        RelativePath = relativePath ?? string.Empty;
        Units = units ?? Array.Empty<PowerShellCompilationUnitPlan>();
        Diagnostics = diagnostics ?? Array.Empty<PowerShellCompilationDiagnostic>();
    }

    /// <summary>Full source path.</summary>
    public string FullPath { get; }

    /// <summary>Path relative to the analyzed root when available.</summary>
    public string RelativePath { get; }

    /// <summary>Executable units discovered in the file.</summary>
    public PowerShellCompilationUnitPlan[] Units { get; }

    /// <summary>File-level diagnostics such as parse errors.</summary>
    public PowerShellCompilationDiagnostic[] Diagnostics { get; }
}

/// <summary>
/// Aggregate result of PowerShell compilation planning.
/// </summary>
public sealed class PowerShellCompilationPlan
{
    /// <summary>Creates an aggregate compilation plan.</summary>
    public PowerShellCompilationPlan(
        PowerShellCompilationMode mode,
        PowerShellCompilationFilePlan[] files,
        string? targetFramework = null,
        PowerShellCompilationDependency[]? dependencies = null,
        PowerShellCompilationDependencyGraph? dependencyGraph = null)
        : this(mode, files, targetFramework, dependencies, dependencyGraph, null)
    {
    }

    /// <summary>Creates an aggregate compilation plan for an explicit semantic and deployment target.</summary>
    public PowerShellCompilationPlan(
        PowerShellCompilationMode mode,
        PowerShellCompilationFilePlan[] files,
        string? targetFramework,
        PowerShellCompilationDependency[]? dependencies,
        PowerShellCompilationDependencyGraph? dependencyGraph,
        PowerShellCompilationTargetContract? targetContract)
    {
        if (!Enum.IsDefined(typeof(PowerShellCompilationMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        TargetFramework = string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework;
        Files = files ?? Array.Empty<PowerShellCompilationFilePlan>();
        Dependencies = dependencies ?? Array.Empty<PowerShellCompilationDependency>();
        DependencyGraph = dependencyGraph;
        TargetContract = targetContract;
        ResourceSummary = PowerShellCompilationResourceSummary.Create(Dependencies);
        TotalUnits = Files.Sum(static file => file.Units.Length);
        CompilableUnits = Files.Sum(static file => file.Units.Count(static unit => unit.IsCompilable));
        RuntimeFallbackUnits = TotalUnits - CompilableUnits;
        ParseErrorFiles = Files.Count(static file => file.Diagnostics.Any(static diagnostic => diagnostic.Code == PowerShellCompilationDiagnosticCode.ParseError));
    }

    /// <summary>Requested operating mode.</summary>
    public PowerShellCompilationMode Mode { get; }

    /// <summary>Generated-project target framework used for CLR eligibility, or null for host-runtime analysis.</summary>
    public string? TargetFramework { get; }

    /// <summary>Per-file plans.</summary>
    public PowerShellCompilationFilePlan[] Files { get; }

    /// <summary>Deterministic runtime dependency and resource decisions for the selected artifact shape.</summary>
    public PowerShellCompilationDependency[] Dependencies { get; }

    /// <summary>Locked dependency graph used for analysis and deployment evidence when input resolution supplied one.</summary>
    public PowerShellCompilationDependencyGraph? DependencyGraph { get; }

    /// <summary>Normalized explicit target whose deployment inputs shaped this plan, when supplied.</summary>
    public PowerShellCompilationTargetContract? TargetContract { get; }

    /// <summary>Included, excluded, required, inferred, and unclassified resource totals.</summary>
    public PowerShellCompilationResourceSummary ResourceSummary { get; }

    /// <summary>Total executable units discovered.</summary>
    public int TotalUnits { get; }

    /// <summary>Units eligible for typed compilation.</summary>
    public int CompilableUnits { get; }

    /// <summary>Units that require an explicit PowerShell fallback.</summary>
    public int RuntimeFallbackUnits { get; }

    /// <summary>Files rejected by the PowerShell parser.</summary>
    public int ParseErrorFiles { get; }

    /// <summary>Percentage of executable units eligible for typed compilation.</summary>
    public double CompilationCoveragePercentage => TotalUnits == 0 ? 0 : CompilableUnits * 100d / TotalUnits;

    /// <summary>Whether artifact generation is allowed under the requested mode.</summary>
    public bool CanProceed => ParseErrorFiles == 0 &&
                              Files.All(static file => file.Diagnostics.All(static diagnostic => diagnostic.Code != PowerShellCompilationDiagnosticCode.InputError)) &&
                              Dependencies.All(static dependency => dependency.Disposition != PowerShellCompilationDependencyDisposition.Missing) &&
                              (Mode != PowerShellCompilationMode.Strict || RuntimeFallbackUnits == 0);
}

/// <summary>
/// Input specification for PowerShell compilation planning.
/// </summary>
public sealed class PowerShellCompilationSpec
{
    /// <summary>Creates a compilation specification.</summary>
    public PowerShellCompilationSpec(
        string path,
        PowerShellCompilationMode mode = PowerShellCompilationMode.Analyze,
        bool recurse = true,
        string[]? excludeDirectories = null,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An input path is required.", nameof(path));
        if (!Enum.IsDefined(typeof(PowerShellCompilationMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if ((capabilities & ~(PowerShellCompilationCapability.PowerShellStreams |
                              PowerShellCompilationCapability.LocalFunctionCalls |
                              PowerShellCompilationCapability.BoundParameters |
                              PowerShellCompilationCapability.PowerShellObjects |
                              PowerShellCompilationCapability.ExecutableParameterBinding |
                              PowerShellCompilationCapability.PipelineParameterBinding |
                              PowerShellCompilationCapability.PowerShellHostTypes |
                              PowerShellCompilationCapability.PowerShellLanguageConversions |
                              PowerShellCompilationCapability.PowerShellLanguageOperators |
                              PowerShellCompilationCapability.RuntimeStateIntrinsics |
                              PowerShellCompilationCapability.RuntimeFreeProviderOperations)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        var normalizedTargetFramework = targetFramework?.Trim();
        if (normalizedTargetFramework is not null && normalizedTargetFramework.Length > 0)
        {
            if (!normalizedTargetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase) &&
                !normalizedTargetFramework.Equals("net8.0", StringComparison.OrdinalIgnoreCase) &&
                !normalizedTargetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("PowerShell compilation analysis currently targets net472, net8.0, or net10.0.", nameof(targetFramework));
        }

        Path = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
        Mode = mode;
        Recurse = recurse;
        TargetFramework = string.IsNullOrEmpty(normalizedTargetFramework) ? null : normalizedTargetFramework;
        Capabilities = capabilities;
        ExcludeDirectories = (excludeDirectories ?? new[] { ".git", ".vs", ".vscode", "bin", "obj", "packages", "node_modules", "artifacts", "Artefacts", "Ignore" })
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    /// <summary>PowerShell file or directory to analyze.</summary>
    public string Path { get; }

    /// <summary>Requested fallback policy.</summary>
    public PowerShellCompilationMode Mode { get; }

    /// <summary>Whether directory analysis recurses.</summary>
    public bool Recurse { get; }

    /// <summary>Optional generated-project target framework used for CLR type and member eligibility.</summary>
    public string? TargetFramework { get; }

    /// <summary>Optional generated-host capabilities available to compiled methods.</summary>
    public PowerShellCompilationCapability Capabilities { get; }

    /// <summary>Directory-name fragments excluded from recursive discovery.</summary>
    public string[] ExcludeDirectories { get; }
}
