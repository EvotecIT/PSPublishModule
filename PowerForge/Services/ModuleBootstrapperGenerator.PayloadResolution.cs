using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    private static readonly Regex NamedCorePayloadFolder = new(@"^Core-(?:net|netcoreapp)\d+\.\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CoreOnlyPayloadFolder = new(@"^Core(?:-(?:net|netcoreapp)\d+\.\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A configured export assembly resolved for one host: a path relative to the module root, or the reason it cannot load.</summary>
    private sealed record BinaryModulePayload(string Name, string? Path, string? Error);

    /// <summary>What one host loads: export assemblies for the psm1 and the dependency preload list for Libraries.ps1.</summary>
    private sealed record BinaryPayloadScenario(Version? MinimumRuntimeVersion, IReadOnlyList<BinaryModulePayload> Modules, IReadOnlyList<string> Libraries);

    /// <summary>
    /// Everything the generated scripts load, resolved from the staged Lib layout. Windows PowerShell has one scenario;
    /// PowerShell 7 has one per runtime band (highest first) when named Core payloads or a Core baseline make the
    /// selection depend on the running .NET version.
    /// </summary>
    private sealed record BinaryPayloadPlan(BinaryPayloadScenario Desktop, IReadOnlyList<BinaryPayloadScenario> Core);

    private static BinaryPayloadPlan ResolveBinaryPayloadPlan(
        string moduleRoot,
        IReadOnlyList<string> libraryReferences,
        IReadOnlyDictionary<string, List<string>> librariesByFolder,
        bool desktopMayDropCoreOnlyHelper,
        IReadOnlyList<string>? targetFrameworks)
    {
        // An ambiguous layout fails the build for every host the module is built for. A module without a
        // Windows PowerShell target keeps that host's ambiguity as an import error, as it always failed there.
        var frameworks = (targetFrameworks ?? Array.Empty<string>()).Where(static framework => !string.IsNullOrWhiteSpace(framework)).ToArray();
        var targetsDesktop = frameworks.Length == 0 || frameworks.Any(static framework =>
            !ModuleBinaryPayloadLayout.IsCoreFramework(framework) ||
            framework.Trim().StartsWith("netstandard", StringComparison.OrdinalIgnoreCase));

        var libRoot = Path.Combine(moduleRoot, "Lib");
        // A script module may carry a Core-only helper without exporting binary cmdlets or types.
        // Windows PowerShell keeps its functions without importing the incompatible helper.
        var desktopReferences = desktopMayDropCoreOnlyHelper && HasOnlyCorePayload(libRoot)
            ? Array.Empty<string>()
            : libraryReferences;
        var desktop = ResolveBinaryPayloadScenario(moduleRoot, libRoot, desktopReferences, librariesByFolder, isCore: false, runtimeVersion: null, failOnAmbiguity: targetsDesktop);

        var core = new List<BinaryPayloadScenario>();
        foreach (var version in ModuleBinaryPayloadLayout.ResolveRuntimeSelectionVersions(libRoot))
            core.Add(ResolveBinaryPayloadScenario(moduleRoot, libRoot, libraryReferences, librariesByFolder, isCore: true, runtimeVersion: version, failOnAmbiguity: true));
        core.Add(ResolveBinaryPayloadScenario(moduleRoot, libRoot, libraryReferences, librariesByFolder, isCore: true, runtimeVersion: new Version(0, 0), failOnAmbiguity: true) with
        {
            MinimumRuntimeVersion = null
        });

        return new BinaryPayloadPlan(desktop, core);
    }

    private static BinaryPayloadScenario ResolveBinaryPayloadScenario(
        string moduleRoot,
        string libRoot,
        IReadOnlyList<string> libraryReferences,
        IReadOnlyDictionary<string, List<string>> librariesByFolder,
        bool isCore,
        Version? runtimeVersion,
        bool failOnAmbiguity)
    {
        var selectedFolder = ModuleBinaryPayloadLayout.ResolveRuntimePayloadFolder(libRoot, isCore ? "Core" : "Desktop", runtimeVersion);
        var topLevelFolders = Directory.Exists(libRoot)
            ? Directory.EnumerateDirectories(libRoot).Select(Path.GetFileName).OfType<string>().ToArray()
            : Array.Empty<string>();
        var noCompatibleNamedCorePayload = isCore &&
                                           string.IsNullOrEmpty(selectedFolder) &&
                                           topLevelFolders.Any(static folder => NamedCorePayloadFolder.IsMatch(folder));
        var preferredFolders = (isCore
                ? new[] { selectedFolder, "Standard", "Core", string.Empty, "Default" }
                : new[] { selectedFolder, "Default", "Standard", string.Empty, "Core" })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var modules = new List<BinaryModulePayload>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in libraryReferences)
        {
            var payload = ResolveBinaryModulePayload(moduleRoot, libRoot, reference, preferredFolders, noCompatibleNamedCorePayload, isCore, failOnAmbiguity);
            if (payload.Path is not null && !seenPaths.Add(Path.GetFullPath(Path.Combine(moduleRoot, payload.Path))))
                continue;
            modules.Add(payload);
        }

        var preloadFolders = new List<string> { selectedFolder };
        var fullLibRoot = Path.GetFullPath(libRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var module in modules.Where(static module => module.Path is not null))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(moduleRoot, module.Path!)))!
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? folder = string.Equals(directory, fullLibRoot, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : directory.StartsWith(fullLibRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? directory.Substring(fullLibRoot.Length + 1).Replace('\\', '/')
                    : null;
            if (folder is not null && !preloadFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                preloadFolders.Add(folder);
        }

        var libraries = preloadFolders
            .SelectMany(folder => librariesByFolder.TryGetValue(folder, out var list) ? list : Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new BinaryPayloadScenario(runtimeVersion, modules, libraries);
    }

    private static BinaryModulePayload ResolveBinaryModulePayload(
        string moduleRoot,
        string libRoot,
        string reference,
        IReadOnlyList<string> preferredFolders,
        bool noCompatibleNamedCorePayload,
        bool isCore,
        bool failOnAmbiguity)
    {
        if (Path.IsPathRooted(reference))
        {
            if (File.Exists(reference))
                return new BinaryModulePayload(reference, Path.GetFullPath(reference), null);
        }
        else if (reference.IndexOfAny(new[] { '/', '\\' }) >= 0)
        {
            var qualified = ResolveQualifiedAssemblyPath(libRoot, reference);
            if (qualified is not null)
                return new BinaryModulePayload(reference, ToModuleRelativePath(moduleRoot, qualified), null);
        }
        else
        {
            foreach (var folder in preferredFolders)
            {
                var directory = folder.Length == 0 ? libRoot : Path.Combine(libRoot, folder);
                if (!Directory.Exists(directory))
                    continue;

                var match = Directory.EnumerateFiles(directory)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), reference, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return new BinaryModulePayload(reference, ToModuleRelativePath(moduleRoot, match), null);
            }

            var nestedMatches = Directory.Exists(libRoot)
                ? Directory.EnumerateFiles(libRoot, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(Path.GetFileName(path), reference, StringComparison.OrdinalIgnoreCase))
                    .Where(path => !noCompatibleNamedCorePayload ||
                                   !NamedCorePayloadFolder.IsMatch(GetTopLevelLibFolder(libRoot, path)))
                    .ToArray()
                : Array.Empty<string>();
            if (nestedMatches.Length > 1)
            {
                var message = $"Configured binary module '{reference}' matched multiple nested Lib payloads for {(isCore ? "PowerShell 7" : "Windows PowerShell")}: " +
                              string.Join(", ", nestedMatches.Select(path => ToModuleRelativePath(moduleRoot, path)).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)) +
                              ". Use a path-qualified ExportAssemblies entry.";
                if (failOnAmbiguity)
                    throw new InvalidOperationException(message);
                return new BinaryModulePayload(reference, null, message);
            }

            if (nestedMatches.Length == 1)
                return new BinaryModulePayload(reference, ToModuleRelativePath(moduleRoot, nestedMatches[0]), null);
        }

        return new BinaryModulePayload(reference, null, noCompatibleNamedCorePayload
            ? "No compatible PowerShell Core assemblies found"
            : $"Configured binary module '{reference}' was not found in a compatible Lib layout.");
    }

    private static bool HasOnlyCorePayload(string libRoot)
    {
        var folders = Directory.Exists(libRoot)
            ? Directory.EnumerateDirectories(libRoot).Select(Path.GetFileName).OfType<string>().ToArray()
            : Array.Empty<string>();
        var hasCore = folders.Any(static folder => CoreOnlyPayloadFolder.IsMatch(folder));
        var hasDesktopFolder = folders.Any(static folder =>
            string.Equals(folder, "Default", StringComparison.OrdinalIgnoreCase) || string.Equals(folder, "Standard", StringComparison.OrdinalIgnoreCase));
        return hasCore && !hasDesktopFolder && !HasDesktopPayload(libRoot);
    }

    private static bool HasDesktopPayload(string libRoot)
        => ModuleBinaryFileLocator.Enumerate(libRoot, SearchOption.AllDirectories).Any(path =>
        {
            var relative = path.Substring(libRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var isNativeRuntime = segments.Length > 3 &&
                                  string.Equals(segments[0], "runtimes", StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(segments[2], "native", StringComparison.OrdinalIgnoreCase);
            return !isNativeRuntime && !CoreOnlyPayloadFolder.IsMatch(segments[0]);
        });

    private static string GetTopLevelLibFolder(string libRoot, string path)
        => path.Substring(libRoot.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

    private static string ToModuleRelativePath(string moduleRoot, string path)
        => FrameworkCompatibility.GetRelativePath(Path.GetFullPath(moduleRoot), Path.GetFullPath(path)).Replace('\\', '/');

    /// <summary>Lib folders (relative to the module root) that ship native runtimes for any architecture.</summary>
    private static string[] ResolveNativeRuntimeRoots(string moduleRoot)
    {
        var libRoot = Path.Combine(moduleRoot, "Lib");
        if (!Directory.Exists(libRoot))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(libRoot)
            .OrderBy(static directory => directory, StringComparer.OrdinalIgnoreCase)
            .Append(libRoot)
            .Where(static directory => Directory.Exists(Path.Combine(directory, "runtimes")))
            .Select(directory => ToModuleRelativePath(moduleRoot, directory))
            .ToArray();
    }

    private static string BuildBinaryModulesLiteral(IReadOnlyList<BinaryModulePayload> modules)
    {
        if (modules.Count == 0)
            return "@()";

        var builder = new StringBuilder();
        builder.Append("@(");
        foreach (var module in modules)
        {
            builder.Append("\r\n    @{ Name = '").Append(EscapePsSingleQuoted(module.Name)).Append("'; ");
            builder.Append(module.Path is null
                ? "Error = '" + EscapePsSingleQuoted(module.Error ?? string.Empty) + "'"
                : "Path = '" + EscapePsSingleQuoted(module.Path) + "'");
            builder.Append(" }");
        }

        return builder.Append("\r\n)").ToString();
    }

    private static string BuildMultilineArrayLiteral(IReadOnlyList<string> values)
        => values.Count == 0
            ? "@()"
            : "@(\r\n" + string.Join("\r\n", values.Select(static value => "    '" + EscapePsSingleQuoted(value) + "'")) + "\r\n)";

    /// <summary>
    /// Renders a value that differs by PowerShell edition and, on PowerShell 7, by .NET runtime band.
    /// Identical neighbouring bands collapse, so a layout without runtime-dependent payloads renders one literal.
    /// </summary>
    private static string RenderPerHostValue(BinaryPayloadPlan plan, Func<BinaryPayloadScenario, string> render)
    {
        var desktop = render(plan.Desktop);
        var bands = new List<(Version? Minimum, string Value)>();
        foreach (var scenario in plan.Core)
        {
            var value = render(scenario);
            if (bands.Count > 0 && string.Equals(bands[bands.Count - 1].Value, value, StringComparison.Ordinal))
                bands[bands.Count - 1] = (scenario.MinimumRuntimeVersion, value);
            else
                bands.Add((scenario.MinimumRuntimeVersion, value));
        }

        if (bands.Count == 1 && string.Equals(bands[0].Value, desktop, StringComparison.Ordinal))
            return desktop;

        string core;
        if (bands.Count == 1)
        {
            core = bands[0].Value;
        }
        else
        {
            var builder = new StringBuilder();
            for (var index = 0; index < bands.Count; index++)
            {
                var (minimum, value) = bands[index];
                builder.Append(index == 0 ? "if " : minimum is null ? " else " : " elseif ");
                if (minimum is not null)
                    builder.Append("([Environment]::Version -ge [version]'").Append(minimum.ToString()).Append("') ");
                builder.Append("{\r\n").Append(IndentPowerShell(value, 4)).Append("\r\n}");
            }

            core = builder.ToString();
        }

        return "if ($PSEdition -eq 'Core') {\r\n" + IndentPowerShell(core, 4) + "\r\n} else {\r\n" + IndentPowerShell(desktop, 4) + "\r\n}";
    }
}
