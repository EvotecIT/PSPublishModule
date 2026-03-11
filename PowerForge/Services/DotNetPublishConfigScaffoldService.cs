using System;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Resolves and scaffolds starter dotnet publish configuration files.
/// </summary>
public sealed class DotNetPublishConfigScaffoldService
{
    private readonly Func<ILogger, DotNetPublishConfigScaffolder> _createScaffolder;

    /// <summary>
    /// Creates a new scaffold service.
    /// </summary>
    public DotNetPublishConfigScaffoldService(Func<ILogger, DotNetPublishConfigScaffolder>? createScaffolder = null)
    {
        _createScaffolder = createScaffolder ?? (logger => new DotNetPublishConfigScaffolder(logger));
    }

    /// <summary>
    /// Resolves the output config path without writing the file.
    /// </summary>
    public string ResolveOutputPath(DotNetPublishConfigScaffoldRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var resolvedRoot = ResolvePath(request.WorkingDirectory, request.ProjectRoot);
        return ResolvePath(resolvedRoot, request.OutputPath);
    }

    /// <summary>
    /// Generates the starter config file.
    /// </summary>
    public DotNetPublishConfigScaffoldResult Generate(
        DotNetPublishConfigScaffoldRequest request,
        ILogger? logger = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var resolvedRoot = ResolvePath(request.WorkingDirectory, request.ProjectRoot);
        var scaffolder = _createScaffolder(logger ?? new NullLogger());
        return scaffolder.Generate(new DotNetPublishConfigScaffoldOptions
        {
            ProjectRoot = resolvedRoot,
            ProjectPath = NormalizeNullable(request.ProjectPath),
            TargetName = NormalizeNullable(request.TargetName),
            Framework = NormalizeNullable(request.Framework),
            Runtimes = NormalizeStrings(request.Runtimes),
            Styles = NormalizeStyles(request.Styles),
            Configuration = string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration.Trim(),
            OutputPath = ResolvePath(resolvedRoot, request.OutputPath),
            Overwrite = request.Force,
            IncludeSchema = request.IncludeSchema
        });
    }

    private static string ResolvePath(string basePath, string value)
    {
        var fallback = string.IsNullOrWhiteSpace(basePath) ? Environment.CurrentDirectory : basePath;
        var raw = NormalizePathValue(value);
        if (raw.Length == 0)
            return Path.GetFullPath(fallback);

        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(fallback, raw));
    }

    private static string NormalizePathValue(string? value)
    {
        var raw = (value ?? string.Empty).Trim().Trim('"');
        if (raw.Length == 0)
            return string.Empty;

        return raw
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace(Path.DirectorySeparatorChar == '/' ? '\\' : '/', Path.DirectorySeparatorChar);
    }

    private static string? NormalizeNullable(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string[]? NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0)
            return null;

        var normalized = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : normalized;
    }

    private static DotNetPublishStyle[]? NormalizeStyles(DotNetPublishStyle[]? values)
    {
        if (values is null || values.Length == 0)
            return null;

        var normalized = values
            .Distinct()
            .ToArray();
        return normalized.Length == 0 ? null : normalized;
    }
}
