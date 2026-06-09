using System.Collections;
using System.Management.Automation;

namespace PowerForge;

internal static class DotNetPublishDslComposer
{
    internal static DotNetPublishSpec ComposeFromSettings(ScriptBlock? settings, DotNetPublishSpec? seed, Action<string>? warn = null)
    {
        var spec = seed ?? new DotNetPublishSpec();
        if (settings is null) return spec;

        foreach (var item in settings.Invoke())
        {
            foreach (var obj in EnumerateBaseObjects(item?.BaseObject))
            {
                MergeObject(spec, obj, warn);
            }
        }

        return spec;
    }

    private static IEnumerable<object> EnumerateBaseObjects(object? value)
    {
        if (value is null) yield break;

        if (value is PSObject pso)
        {
            foreach (var nested in EnumerateBaseObjects(pso.BaseObject))
                yield return nested;
            yield break;
        }

        if (value is string)
        {
            yield return value;
            yield break;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                foreach (var nested in EnumerateBaseObjects(item))
                    yield return nested;
            }

            yield break;
        }

        yield return value;
    }

    private static void MergeObject(DotNetPublishSpec target, object value, Action<string>? warn)
    {
        switch (value)
        {
            case DotNetPublishSpec nestedSpec:
                MergeSpec(target, nestedSpec);
                break;
            case DotNetPublishTarget nestedTarget:
                AddTarget(target, nestedTarget);
                break;
            case DotNetPublishInstaller nestedInstaller:
                AddInstaller(target, nestedInstaller);
                break;
            case DotNetPublishBenchmarkGate nestedGate:
                AddBenchmarkGate(target, nestedGate);
                break;
            case DotNetPublishProfile nestedProfile:
                AddProfile(target, nestedProfile);
                break;
            case DotNetPublishProject nestedProject:
                AddProject(target, nestedProject);
                break;
            case DotNetPublishMatrix nestedMatrix:
                target.Matrix = nestedMatrix;
                break;
            case DotNetPublishDotNetOptions nestedDotNet:
                target.DotNet = nestedDotNet;
                break;
            case DotNetPublishOutputs nestedOutputs:
                target.Outputs = nestedOutputs;
                break;
            default:
                warn?.Invoke($"Ignoring unsupported DotNetPublish DSL object: {value.GetType().FullName}");
                break;
        }
    }

    private static void MergeSpec(DotNetPublishSpec target, DotNetPublishSpec source)
    {
        if (!string.IsNullOrWhiteSpace(source.Schema))
            target.Schema = source.Schema;

        if (source.SchemaVersion > 0)
            target.SchemaVersion = source.SchemaVersion;

        if (!string.IsNullOrWhiteSpace(source.Profile))
            target.Profile = source.Profile;

        if (source.DotNet is not null)
            target.DotNet = source.DotNet;

        if (source.Matrix is not null)
            target.Matrix = source.Matrix;

        if (source.Outputs is not null)
            target.Outputs = source.Outputs;

        foreach (var entry in source.Targets ?? Array.Empty<DotNetPublishTarget>())
            AddTarget(target, entry);

        foreach (var entry in source.Installers ?? Array.Empty<DotNetPublishInstaller>())
            AddInstaller(target, entry);

        foreach (var entry in source.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGate>())
            AddBenchmarkGate(target, entry);

        foreach (var entry in source.Profiles ?? Array.Empty<DotNetPublishProfile>())
            AddProfile(target, entry);

        foreach (var entry in source.Projects ?? Array.Empty<DotNetPublishProject>())
            AddProject(target, entry);
    }

    private static void AddTarget(DotNetPublishSpec spec, DotNetPublishTarget value)
        => spec.Targets = (spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            .Concat(new[] { value })
            .ToArray();

    private static void AddInstaller(DotNetPublishSpec spec, DotNetPublishInstaller value)
        => spec.Installers = (spec.Installers ?? Array.Empty<DotNetPublishInstaller>())
            .Concat(new[] { value })
            .ToArray();

    private static void AddBenchmarkGate(DotNetPublishSpec spec, DotNetPublishBenchmarkGate value)
        => spec.BenchmarkGates = (spec.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGate>())
            .Concat(new[] { value })
            .ToArray();

    private static void AddProfile(DotNetPublishSpec spec, DotNetPublishProfile value)
        => spec.Profiles = (spec.Profiles ?? Array.Empty<DotNetPublishProfile>())
            .Concat(new[] { value })
            .ToArray();

    private static void AddProject(DotNetPublishSpec spec, DotNetPublishProject value)
        => spec.Projects = (spec.Projects ?? Array.Empty<DotNetPublishProject>())
            .Concat(new[] { value })
            .ToArray();
}
