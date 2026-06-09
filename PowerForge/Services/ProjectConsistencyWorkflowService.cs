using System.Collections;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Reusable workflow service for project consistency analysis and conversion.
/// </summary>
public sealed class ProjectConsistencyWorkflowService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new workflow service.
    /// </summary>
    public ProjectConsistencyWorkflowService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs consistency analysis only.
    /// </summary>
    public ProjectConsistencyWorkflowResult Analyze(ProjectConsistencyWorkflowRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.RecommendedEncoding == TextEncodingKind.Any)
            throw new ArgumentException("RecommendedEncoding cannot be Any for project consistency analysis.", nameof(request));

        var context = BuildContext(request);
        var analyzer = new ProjectConsistencyAnalyzer(_logger);
        var report = analyzer.Analyze(
            enumeration: context.Enumeration,
            projectType: request.ProjectType,
            recommendedEncoding: request.RecommendedEncoding,
            recommendedLineEnding: request.RecommendedLineEnding,
            includeDetails: request.IncludeDetails,
            exportPath: request.ExportPath,
            encodingOverrides: request.EncodingOverrides,
            lineEndingOverrides: request.LineEndingOverrides);

        return new ProjectConsistencyWorkflowResult(
            context.RootPath,
            context.Patterns,
            context.Kind,
            report,
            null,
            null);
    }

    /// <summary>
    /// Runs conversion and then re-analyzes the project for a combined result.
    /// </summary>
    public ProjectConsistencyWorkflowResult ConvertAndAnalyze(ProjectConsistencyWorkflowRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var context = BuildContext(request);
        var encodingOverrides = request.EncodingOverrides;
        var lineEndingOverrides = request.LineEndingOverrides;
        var applyEncoding = request.FixEncodingSpecified ? request.FixEncoding : !request.FixLineEndingsSpecified;
        var applyLineEndings = request.FixLineEndingsSpecified ? request.FixLineEndings : !request.FixEncodingSpecified;

        ProjectConversionResult? encodingResult = null;
        ProjectConversionResult? lineEndingResult = null;

        if (applyEncoding)
        {
            var encOptions = new EncodingConversionOptions(
                enumeration: context.Enumeration,
                sourceEncoding: request.SourceEncoding,
                targetEncoding: request.RequiredEncoding.ToTextEncodingKind(),
                createBackups: request.CreateBackups,
                backupDirectory: request.BackupDirectory,
                force: request.Force,
                noRollbackOnMismatch: request.NoRollbackOnMismatch,
                preferUtf8BomForPowerShell: request.RequiredEncoding == FileConsistencyEncoding.UTF8BOM);

            if (encodingOverrides is { Count: > 0 })
            {
                encOptions.TargetEncodingResolver = path =>
                {
                    var rel = ProjectTextInspection.ComputeRelativePath(context.Enumeration.RootPath, path);
                    var overrideEncoding = FileConsistencyOverrideResolver.ResolveEncodingOverride(rel, encodingOverrides);
                    return overrideEncoding?.ToTextEncodingKind();
                };
            }

            encodingResult = new EncodingConverter().Convert(encOptions);
        }

        if (applyLineEndings)
        {
            var lineOptions = new LineEndingConversionOptions(
                enumeration: context.Enumeration,
                target: request.RequiredLineEnding.ToLineEnding(),
                createBackups: request.CreateBackups,
                backupDirectory: request.BackupDirectory,
                force: request.Force,
                onlyMixed: request.OnlyMixedLineEndings,
                ensureFinalNewline: request.EnsureFinalNewline,
                onlyMissingNewline: request.OnlyMissingFinalNewline,
                preferUtf8BomForPowerShell: request.RequiredEncoding == FileConsistencyEncoding.UTF8BOM);

            if (lineEndingOverrides is { Count: > 0 })
            {
                lineOptions.TargetResolver = path =>
                {
                    var rel = ProjectTextInspection.ComputeRelativePath(context.Enumeration.RootPath, path);
                    var overrideLineEnding = FileConsistencyOverrideResolver.ResolveLineEndingOverride(rel, lineEndingOverrides);
                    return overrideLineEnding?.ToLineEnding();
                };
            }

            lineEndingResult = new LineEndingConverter().Convert(lineOptions);
        }

        var analyzer = new ProjectConsistencyAnalyzer(_logger);
        var report = analyzer.Analyze(
            enumeration: context.Enumeration,
            projectType: request.ProjectType,
            recommendedEncoding: request.RequiredEncoding.ToTextEncodingKind(),
            recommendedLineEnding: request.RequiredLineEnding,
            includeDetails: request.IncludeDetails,
            exportPath: request.ExportPath,
            encodingOverrides: encodingOverrides,
            lineEndingOverrides: lineEndingOverrides);

        return new ProjectConsistencyWorkflowResult(
            context.RootPath,
            context.Patterns,
            context.Kind,
            report,
            encodingResult,
            lineEndingResult);
    }

    /// <summary>
    /// Parses per-path encoding overrides from a hashtable-like input.
    /// </summary>
    public static Dictionary<string, FileConsistencyEncoding>? ParseEncodingOverrides(IDictionary? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return null;

        var dict = new Dictionary<string, FileConsistencyEncoding>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in overrides)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (TryParseEnum(entry.Value, out FileConsistencyEncoding value))
                dict[key!] = value;
        }

        return dict.Count == 0 ? null : dict;
    }

    /// <summary>
    /// Parses per-path line ending overrides from a hashtable-like input.
    /// </summary>
    public static Dictionary<string, FileConsistencyLineEnding>? ParseLineEndingOverrides(IDictionary? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return null;

        var dict = new Dictionary<string, FileConsistencyLineEnding>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in overrides)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (TryParseEnum(entry.Value, out FileConsistencyLineEnding value))
                dict[key!] = value;
        }

        return dict.Count == 0 ? null : dict;
    }

    /// <summary>
    /// Resolves effective include patterns for the specified project type.
    /// </summary>
    public static string[] ResolvePatterns(string projectType, string[]? custom)
    {
        if (projectType.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            return custom is null || custom.Length == 0 ? Array.Empty<string>() : custom;

        return projectType switch
        {
            "PowerShell" => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml" },
            "CSharp" => new[] { "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml", "*.resx" },
            "Mixed" => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml" },
            "All" => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml", "*.js", "*.ts", "*.py", "*.rb", "*.java", "*.cpp", "*.h", "*.hpp", "*.sql", "*.md", "*.txt", "*.yaml", "*.yml" },
            _ => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml" }
        };
    }

    /// <summary>
    /// Resolves effective project kind for the specified project type.
    /// </summary>
    public static ProjectKind ResolveKind(string projectType)
    {
        return projectType switch
        {
            "PowerShell" => ProjectKind.PowerShell,
            "CSharp" => ProjectKind.CSharp,
            "All" => ProjectKind.All,
            _ => ProjectKind.Mixed
        };
    }

    private static bool TryParseEnum<T>(object? raw, out T value) where T : struct
    {
        value = default;
        if (raw is null)
            return false;

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        var text = raw.ToString();
        return !string.IsNullOrWhiteSpace(text) && Enum.TryParse(text, true, out value);
    }

    private static ProjectConsistencyWorkflowContext BuildContext(ProjectConsistencyWorkflowRequest request)
    {
        var rootPath = System.IO.Path.GetFullPath(request.Path.Trim().Trim('"'));
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Project path '{rootPath}' not found or is not a directory");

        var patterns = ResolvePatterns(request.ProjectType, request.CustomExtensions);
        var kind = ResolveKind(request.ProjectType);
        var enumeration = new ProjectEnumeration(
            rootPath: rootPath,
            kind: kind,
            customExtensions: request.ProjectType.Equals("Custom", StringComparison.OrdinalIgnoreCase) ? patterns : null,
            excludeDirectories: request.ExcludeDirectories,
            excludeFiles: request.ExcludeFiles);

        return new ProjectConsistencyWorkflowContext(rootPath, patterns, kind, enumeration);
    }

    private sealed class ProjectConsistencyWorkflowContext
    {
        internal ProjectConsistencyWorkflowContext(string rootPath, string[] patterns, ProjectKind kind, ProjectEnumeration enumeration)
        {
            RootPath = rootPath;
            Patterns = patterns;
            Kind = kind;
            Enumeration = enumeration;
        }

        internal string RootPath { get; }

        internal string[] Patterns { get; }

        internal ProjectKind Kind { get; }

        internal ProjectEnumeration Enumeration { get; }
    }
}
