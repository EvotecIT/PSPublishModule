using System.Reflection;
using System.Text.Json;

namespace PowerForge;

internal static class ProjectBuildConfigurationAdapter
{
    internal static ProjectBuildConfiguration FromPackageBuild(PackageBuildConfiguration source)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        var json = JsonSerializer.Serialize(source);
        var target = JsonSerializer.Deserialize<ProjectBuildConfiguration>(json)
            ?? throw new InvalidOperationException("Inline package-build configuration could not be converted.");
        ApplyOptions(target, source.Options);
        return target;
    }

    internal static ProjectBuildConfiguration ApplyReference(
        ProjectBuildConfiguration target,
        ProjectBuildConfigurationReference reference)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (reference is null)
            throw new ArgumentNullException(nameof(reference));

        ApplyOptions(target, reference.Options);
        if (UsesDefaultActions(target) && HasActionOverride(reference))
        {
            target.UpdateVersions = true;
            target.Build = true;
            target.PublishNuget = false;
            target.PublishGitHub = false;
        }
        Apply(target, reference.UpdateVersions, static (config, value) => config.UpdateVersions = value);
        Apply(target, reference.Build, static (config, value) => config.Build = value);
        Apply(target, reference.IncludeSymbols, static (config, value) => config.IncludeSymbols = value);
        Apply(target, reference.PublishNuget, static (config, value) => config.PublishNuget = value);
        Apply(target, reference.PublishGitHub, static (config, value) => config.PublishGitHub = value);
        Apply(target, reference.CreateReleaseZip, static (config, value) => config.CreateReleaseZip = value);
        Apply(target, reference.SignAssemblies, static (config, value) => config.SignAssemblies = value);
        Apply(target, reference.SignDependencyAssemblies, static (config, value) => config.SignDependencyAssemblies = value);
        Apply(target, reference.SignPackages, static (config, value) => config.SignPackages = value);
        return target;
    }

    private static bool UsesDefaultActions(ProjectBuildConfiguration target)
        => target.UpdateVersions is null &&
           target.Build is null &&
           target.PublishNuget is null &&
           target.PublishGitHub is null;

    private static bool HasActionOverride(ProjectBuildConfigurationReference reference)
        => reference.UpdateVersions is not null ||
           reference.Build is not null ||
           reference.PublishNuget is not null ||
           reference.PublishGitHub is not null;

    private static void Apply(
        ProjectBuildConfiguration target,
        bool? value,
        Action<ProjectBuildConfiguration, bool> assign)
    {
        if (value.HasValue)
            assign(target, value.Value);
    }

    private static void ApplyOptions(
        ProjectBuildConfiguration target,
        IReadOnlyDictionary<string, object?>? options)
    {
        if (options is null || options.Count == 0)
            return;

        var properties = typeof(ProjectBuildConfiguration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.CanWrite)
            .ToDictionary(static property => property.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Key) ||
                !properties.TryGetValue(option.Key.Trim(), out var property))
                continue;

            var value = ConvertOption(option.Value, property.PropertyType);
            if (value is not null ||
                Nullable.GetUnderlyingType(property.PropertyType) is not null ||
                !property.PropertyType.IsValueType)
                property.SetValue(target, value);
        }
    }

    private static object? ConvertOption(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType.IsInstanceOfType(value))
            return value;
        if (value is JsonElement json)
        {
            if (underlyingType == typeof(string[]))
            {
                if (json.ValueKind == JsonValueKind.Array)
                {
                    return json.EnumerateArray()
                        .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Select(static item => item!.Trim())
                        .ToArray();
                }

                var text = json.ValueKind == JsonValueKind.String ? json.GetString() : json.ToString();
                return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : [text!.Trim()];
            }
            if (underlyingType == typeof(bool) && json.ValueKind == JsonValueKind.String)
                return bool.TryParse(json.GetString(), out var boolean) && boolean;
            return JsonSerializer.Deserialize(json.GetRawText(), underlyingType);
        }
        if (underlyingType == typeof(string))
            return value.ToString();
        if (underlyingType == typeof(bool))
            return value is bool boolean ? boolean : bool.TryParse(value.ToString(), out var parsed) && parsed;
        if (underlyingType == typeof(string[]))
            return ConvertStringArray(value);

        var valueJson = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize(valueJson, underlyingType);
    }

    private static string[] ConvertStringArray(object value)
    {
        if (value is string text)
            return string.IsNullOrWhiteSpace(text) ? [] : [text.Trim()];
        if (value is not System.Collections.IEnumerable enumerable)
            return [value.ToString() ?? string.Empty];

        var values = new List<string>();
        foreach (var item in enumerable)
        {
            var textValue = item?.ToString();
            if (!string.IsNullOrWhiteSpace(textValue))
                values.Add(textValue!.Trim());
        }

        return values.ToArray();
    }
}
