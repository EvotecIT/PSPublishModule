using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class DotNetPublishPreparationService
{
    private readonly ILogger _logger;

    public DotNetPublishPreparationService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DotNetPublishPreparedContext Prepare(DotNetPublishPreparationRequest request, Action<string>? warn = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.CurrentPath))
            throw new ArgumentException("Current path is required.", nameof(request));
        if (request.ResolvePath is null)
            throw new ArgumentException("ResolvePath is required.", nameof(request));

        var sourceLabel = string.Empty;
        var spec = LoadSpec(request, ref sourceLabel, warn);
        if (!string.IsNullOrWhiteSpace(request.Profile))
            spec.Profile = request.Profile!.Trim();
        if (!string.IsNullOrWhiteSpace(request.ProjectRoot))
        {
            spec.DotNet ??= new DotNetPublishDotNetOptions();
            spec.DotNet.ProjectRoot = request.ResolvePath!(request.ProjectRoot!);
        }
        ApplyOverrides(spec, request);

        return new DotNetPublishPreparedContext
        {
            Spec = spec,
            SourceLabel = sourceLabel,
            JsonOutputPath = request.JsonOnly ? ResolveJsonOutputPath(spec, sourceLabel, request) : null,
            JsonOnly = request.JsonOnly,
            PlanOnly = request.Plan,
            ValidateOnly = request.Validate
        };
    }

    private DotNetPublishSpec LoadSpec(DotNetPublishPreparationRequest request, ref string sourceLabel, Action<string>? warn)
    {
        if (string.Equals(request.ParameterSetName, "Config", StringComparison.OrdinalIgnoreCase))
        {
            var full = request.ResolvePath!(request.ConfigPath!);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Config file not found: {full}");

            sourceLabel = full;
            return ParseSpecJson(File.ReadAllText(full), full);
        }

        sourceLabel = Path.Combine(request.CurrentPath, "powerforge.dotnetpublish.dsl.json");
        var spec = DotNetPublishDslComposer.ComposeFromSettings(request.Settings, new DotNetPublishSpec(), warn);
        if ((spec.Targets ?? Array.Empty<DotNetPublishTarget>()).Length == 0)
            _logger.Warn("No DotNet publish targets were defined.");

        return spec;
    }

    private static string ResolveJsonOutputPath(
        DotNetPublishSpec spec,
        string sourceLabel,
        DotNetPublishPreparationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.JsonPath))
            return request.ResolvePath!(request.JsonPath!);

        if (string.Equals(request.ParameterSetName, "Config", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(sourceLabel))
        {
            var baseDir = Path.GetDirectoryName(sourceLabel);
            if (!string.IsNullOrWhiteSpace(baseDir))
                return Path.Combine(baseDir, "powerforge.dotnetpublish.generated.json");
        }

        var projectRoot = spec.DotNet?.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var root = request.ResolvePath!(projectRoot!);
            return Path.Combine(root, "powerforge.dotnetpublish.json");
        }

        return Path.Combine(request.CurrentPath, "powerforge.dotnetpublish.json");
    }

    private static DotNetPublishSpec ParseSpecJson(string json, string pathLabel)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, options);
            if (spec is null)
                throw new InvalidOperationException("Parsed config is null.");
            return spec;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse DotNet publish config '{pathLabel}'. {ex.Message}", ex);
        }
    }

    private static void ApplyOverrides(DotNetPublishSpec spec, DotNetPublishPreparationRequest request)
    {
        if (spec is null) return;

        var overrideTargets = NormalizeStrings(request.Target);
        var overrideRuntimes = NormalizeStrings(request.Runtimes);
        var overrideFrameworks = NormalizeStrings(request.Frameworks);
        var overrideStyles = NormalizeStyles(request.Styles);
        var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
            ? null
            : request.OutputPath!.Trim();

        if (overrideTargets.Length > 0)
        {
            var knownTargets = spec.Targets ?? Array.Empty<DotNetPublishTarget>();
            var selected = new HashSet<string>(overrideTargets, StringComparer.OrdinalIgnoreCase);
            var missing = selected
                .Where(name => knownTargets.All(t => !string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"Unknown target override value(s): {string.Join(", ", missing)}");

            spec.Targets = knownTargets
                .Where(t => selected.Contains(t.Name))
                .ToArray();

            if (spec.Profiles is { Length: > 0 })
            {
                var activeProfileName = ResolveActiveProfileName(spec);
                foreach (var profile in spec.Profiles)
                {
                    if (profile?.Targets is not { Length: > 0 })
                        continue;
                    if (string.IsNullOrWhiteSpace(activeProfileName) ||
                        !string.Equals(profile.Name, activeProfileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var originalTargets = profile.Targets;
                    profile.Targets = profile.Targets
                        .Where(target => !string.IsNullOrWhiteSpace(target) && selected.Contains(target.Trim()))
                        .ToArray();
                    if (profile.Targets.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"Profile '{profile.Name}' does not match target override value(s): {string.Join(", ", overrideTargets)}. " +
                            $"Profile target(s): {string.Join(", ", originalTargets)}.");
                    }
                }
            }

            if (spec.Installers is { Length: > 0 })
            {
                spec.Installers = spec.Installers
                    .Where(i =>
                        i is not null
                        && (string.IsNullOrWhiteSpace(i.PrepareFromTarget)
                            || selected.Contains(i.PrepareFromTarget)))
                    .ToArray();
            }
        }

        if (overrideRuntimes.Length > 0
            || overrideFrameworks.Length > 0
            || overrideStyles.Length > 0
            || !string.IsNullOrWhiteSpace(outputPath))
        {
            foreach (var target in spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            {
                target.Publish ??= new DotNetPublishPublishOptions();

                if (overrideRuntimes.Length > 0)
                    target.Publish.Runtimes = overrideRuntimes;

                if (overrideFrameworks.Length > 0)
                {
                    target.Publish.Framework = overrideFrameworks[0];
                    target.Publish.Frameworks = overrideFrameworks;
                }

                if (overrideStyles.Length > 0)
                {
                    target.Publish.Style = overrideStyles[0];
                    target.Publish.Styles = overrideStyles;
                }

                if (!string.IsNullOrWhiteSpace(outputPath))
                    target.Publish.OutputPath = outputPath;
            }
        }

        spec.DotNet ??= new DotNetPublishDotNetOptions();
        if (request.MsBuildProperties is { Count: > 0 })
        {
            spec.DotNet.MsBuildProperties ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in request.MsBuildProperties)
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                    continue;
                spec.DotNet.MsBuildProperties[property.Key.Trim()] = property.Value;
            }
        }

        if (request.SkipInstallers)
            spec.Installers = Array.Empty<DotNetPublishInstaller>();

        if (request.SkipRestore)
        {
            spec.DotNet.Restore = false;
            spec.DotNet.NoRestoreInPublish = true;
        }

        if (request.SkipBuild)
        {
            spec.DotNet.Build = false;
            spec.DotNet.NoBuildInPublish = true;
        }
    }

    private static string? ResolveActiveProfileName(DotNetPublishSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Profile))
            return spec.Profile!.Trim();

        var defaultProfile = (spec.Profiles ?? Array.Empty<DotNetPublishProfile>())
            .FirstOrDefault(profile => profile is not null && profile.Default);
        return defaultProfile?.Name;
    }

    private static string[] NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0) return Array.Empty<string>();

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DotNetPublishStyle[] NormalizeStyles(DotNetPublishStyle[]? values)
    {
        if (values is null || values.Length == 0) return Array.Empty<DotNetPublishStyle>();
        return values.Distinct().ToArray();
    }
}
