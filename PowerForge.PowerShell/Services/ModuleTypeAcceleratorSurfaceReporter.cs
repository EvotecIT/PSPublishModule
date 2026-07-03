using System.IO;
using System.Reflection;
using System.Text;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace PowerForge;

internal sealed class ModuleTypeAcceleratorSurfaceReporter
{
    private readonly ILogger _logger;

    public ModuleTypeAcceleratorSurfaceReporter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ModuleTypeAcceleratorSurfaceReport? WriteReport(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        string reportPath)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));
        if (string.IsNullOrWhiteSpace(reportPath)) throw new ArgumentException("Report path is required.", nameof(reportPath));

        var mode = AssemblyTypeAcceleratorOptions.ResolveMode(
            plan.BuildSpec.AssemblyTypeAcceleratorMode,
            plan.BuildSpec.AssemblyTypeAccelerators,
            plan.BuildSpec.AssemblyTypeAcceleratorAssemblies);
        if (mode == AssemblyTypeAcceleratorExportMode.None)
            return null;

        var requestedTypes = Normalize(plan.BuildSpec.AssemblyTypeAccelerators);
        var requestedAssemblies = Normalize(plan.BuildSpec.AssemblyTypeAcceleratorAssemblies);
        var libraryDirectory = ResolveAssemblyLoadContextLibraryDirectory(buildResult.StagingPath);

        ModuleTypeAcceleratorSurfaceReport report;
        if (string.IsNullOrWhiteSpace(libraryDirectory) || !Directory.Exists(libraryDirectory))
        {
            report = new ModuleTypeAcceleratorSurfaceReport(
                mode,
                Path.GetFullPath(reportPath),
                requestedTypes,
                requestedAssemblies,
                warnings: new[] { $"No compatible AssemblyLoadContext library directory was found under '{Path.Combine(buildResult.StagingPath, "Lib")}'." });
        }
        else
        {
            report = InspectSurface(mode, requestedTypes, requestedAssemblies, libraryDirectory!, Path.GetFullPath(reportPath));
        }

        WriteTextReport(report, libraryDirectory);
        return report;
    }

    private ModuleTypeAcceleratorSurfaceReport InspectSurface(
        AssemblyTypeAcceleratorExportMode mode,
        string[] requestedTypes,
        string[] requestedAssemblies,
        string libraryDirectory,
        string reportPath)
    {
#if NET8_0_OR_GREATER
        var warnings = new List<string>();
        var assemblyReports = new List<ModuleTypeAcceleratorAssemblyReport>();
        var loadedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var alc = new AssemblyLoadContext("PowerForge.TypeAcceleratorSurfaceReport." + Guid.NewGuid().ToString("N"), isCollectible: true);
        alc.Resolving += (_, name) =>
        {
            var resolved = ResolveAssemblyPath(libraryDirectory, name.Name);
            return resolved is null ? null : LoadAssembly(alc, resolved, loadedAssemblies, loadedPaths, warnings);
        };

        try
        {
            if (mode == AssemblyTypeAcceleratorExportMode.Assembly ||
                mode == AssemblyTypeAcceleratorExportMode.Enums)
            {
                foreach (var assemblyName in requestedAssemblies)
                {
                    var assemblyPath = ResolveAssemblyPath(libraryDirectory, assemblyName);
                    if (assemblyPath is null)
                    {
                        assemblyReports.Add(new ModuleTypeAcceleratorAssemblyReport(
                            assemblyName,
                            error: $"Assembly '{assemblyName}' was not found in '{libraryDirectory}'."));
                        continue;
                    }

                    var assembly = LoadAssembly(alc, assemblyPath, loadedAssemblies, loadedPaths, warnings);
                    if (assembly is null)
                    {
                        assemblyReports.Add(new ModuleTypeAcceleratorAssemblyReport(
                            assemblyName,
                            assemblyPath,
                            error: $"Assembly '{assemblyName}' could not be loaded for reporting."));
                        continue;
                    }

                    var exported = GetExportedTypes(assembly, warnings, assemblyName);
                    var registered = exported
                        .Where(type => mode == AssemblyTypeAcceleratorExportMode.Assembly || type.IsEnum)
                        .Where(static type => !string.IsNullOrWhiteSpace(type.FullName))
                        .Select(static type => type.FullName!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var skipped = mode == AssemblyTypeAcceleratorExportMode.Enums
                        ? exported.Count(type => !type.IsEnum)
                        : 0;

                    assemblyReports.Add(new ModuleTypeAcceleratorAssemblyReport(
                        assemblyName,
                        assemblyPath,
                        exported.Length,
                        registered,
                        skipped));
                }
            }

            var explicitFound = new List<string>();
            var explicitMissing = new List<string>();
            foreach (var requestedType in requestedTypes)
            {
                var type = FindType(alc, libraryDirectory, requestedType, loadedAssemblies, loadedPaths, warnings);
                if (type is null)
                    explicitMissing.Add(requestedType);
                else if (!string.IsNullOrWhiteSpace(type.FullName))
                    explicitFound.Add(type.FullName!);
            }

            return new ModuleTypeAcceleratorSurfaceReport(
                mode,
                reportPath,
                requestedTypes,
                requestedAssemblies,
                assemblyReports.ToArray(),
                explicitFound.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
                explicitMissing.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            loadedAssemblies.Clear();
            loadedPaths.Clear();
            alc.Unload();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
#else
        return new ModuleTypeAcceleratorSurfaceReport(
            mode,
            reportPath,
            requestedTypes,
            requestedAssemblies,
            warnings: new[]
            {
                "Type accelerator surface reporting requires the .NET 8+ PowerForge runtime. The build can still generate the PowerShell Core bootstrapper."
            });
#endif
    }

#if NET8_0_OR_GREATER
    private static Assembly? LoadAssembly(
        AssemblyLoadContext alc,
        string assemblyPath,
        Dictionary<string, Assembly> loadedAssemblies,
        HashSet<string> loadedPaths,
        List<string> warnings)
    {
        try
        {
            var fullPath = Path.GetFullPath(assemblyPath);
            if (loadedPaths.Contains(fullPath))
            {
                var simpleName = AssemblyName.GetAssemblyName(fullPath).Name;
                return simpleName is not null && loadedAssemblies.TryGetValue(simpleName, out var existing) ? existing : null;
            }

            var assembly = alc.LoadFromAssemblyPath(fullPath);
            loadedPaths.Add(fullPath);
            var name = assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(name))
                loadedAssemblies[name!] = assembly;
            return assembly;
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not load '{assemblyPath}': {ex.Message}");
            return null;
        }
    }

    private static Type? FindType(
        AssemblyLoadContext alc,
        string libraryDirectory,
        string typeName,
        Dictionary<string, Assembly> loadedAssemblies,
        HashSet<string> loadedPaths,
        List<string> warnings)
    {
        foreach (var assembly in loadedAssemblies.Values)
        {
            var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null)
                return type;
        }

        foreach (var file in Directory.EnumerateFiles(libraryDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var assembly = LoadAssembly(alc, file, loadedAssemblies, loadedPaths, warnings);
            if (assembly is null)
                continue;

            var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is not null)
                return type;
        }

        return null;
    }

    private static Type[] GetExportedTypes(Assembly assembly, List<string> warnings, string assemblyName)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            warnings.Add($"Could not fully enumerate exported types from '{assemblyName}': {ex.Message}");
            return ex.Types
                .Where(static type => type is not null && (type.IsPublic || type.IsNestedPublic))
                .Cast<Type>()
                .ToArray();
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not enumerate exported types from '{assemblyName}': {ex.Message}");
            return Array.Empty<Type>();
        }
    }
#endif

    private void WriteTextReport(ModuleTypeAcceleratorSurfaceReport report, string? libraryDirectory)
    {
        var directory = Path.GetDirectoryName(report.ReportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory!);

        var builder = new StringBuilder();
        builder.AppendLine("ALC Type Accelerator Surface");
        builder.AppendLine("============================");
        builder.AppendLine($"Mode: {report.Mode}");
        builder.AppendLine($"Library directory: {(string.IsNullOrWhiteSpace(libraryDirectory) ? "(not found)" : libraryDirectory)}");
        builder.AppendLine($"Registered accelerator names: {report.TotalRegisteredTypeCount}");
        builder.AppendLine($"Assembly-contributed names: {report.AssemblyRegisteredTypeCount}");
        builder.AppendLine($"Explicit requested types found: {report.ExplicitTypesFound.Length}");
        builder.AppendLine($"Explicit requested types missing: {report.ExplicitTypesMissing.Length}");
        builder.AppendLine($"Public non-enum types skipped: {report.SkippedNonEnumTypeCount}");
        builder.AppendLine();

        AppendList(builder, "Requested assemblies", report.RequestedAssemblies);
        AppendList(builder, "Requested explicit types", report.RequestedTypes);

        builder.AppendLine("Assemblies");
        builder.AppendLine("----------");
        if (report.Assemblies.Length == 0)
        {
            builder.AppendLine("- (none)");
        }
        else
        {
            foreach (var assembly in report.Assemblies.OrderBy(static item => item.AssemblyName, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- {assembly.AssemblyName}");
                if (!string.IsNullOrWhiteSpace(assembly.AssemblyPath))
                    builder.AppendLine($"  Path: {assembly.AssemblyPath}");
                if (!string.IsNullOrWhiteSpace(assembly.Error))
                    builder.AppendLine($"  Error: {assembly.Error}");
                builder.AppendLine($"  Exported public types: {assembly.ExportedTypeCount}");
                builder.AppendLine($"  Registered types: {assembly.RegisteredTypes.Length}");
                if (report.Mode == AssemblyTypeAcceleratorExportMode.Enums)
                    builder.AppendLine($"  Skipped public non-enum types: {assembly.SkippedNonEnumTypeCount}");
                foreach (var typeName in assembly.RegisteredTypes.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
                    builder.AppendLine($"    - {typeName}");
            }
        }

        builder.AppendLine();
        AppendList(builder, "Explicit types found", report.ExplicitTypesFound);
        AppendList(builder, "Explicit types missing", report.ExplicitTypesMissing);
        AppendList(builder, "Warnings", report.Warnings);

        File.WriteAllText(report.ReportPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _logger.Verbose($"Wrote type accelerator surface report to '{report.ReportPath}'.");
    }

    private static void AppendList(StringBuilder builder, string title, string[] values)
    {
        builder.AppendLine(title);
        builder.AppendLine(new string('-', title.Length));
        if (values.Length == 0)
        {
            builder.AppendLine("- (none)");
        }
        else
        {
            foreach (var value in values.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
                builder.AppendLine($"- {value}");
        }
        builder.AppendLine();
    }

    private static string? ResolveAssemblyLoadContextLibraryDirectory(string stagingPath)
    {
        var libRoot = Path.Combine(stagingPath, "Lib");
        if (!Directory.Exists(libRoot))
            return null;

        return ModuleBootstrapperGenerator.ResolveAssemblyLoadContextTargetDirectories(libRoot).FirstOrDefault();
    }

    private static string? ResolveAssemblyPath(string libraryDirectory, string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return null;

        var trimmed = assemblyName!.Trim();
        var direct = Path.Combine(libraryDirectory, trimmed + ".dll");
        if (File.Exists(direct))
            return Path.GetFullPath(direct);

        return Directory.EnumerateFiles(libraryDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Normalize(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
