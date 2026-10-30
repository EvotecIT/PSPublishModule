using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation.Language;
using Microsoft.PowerShell.Commands;

namespace PowerForge;

/// <summary>Projects declared dependency paths onto the modules selected by a build.</summary>
internal sealed class ModuleDependencyImportGraphReader
{
    private readonly IModuleDependencyMetadataProvider _metadata;
    private readonly ILogger _logger;
    private readonly Dictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SourceDependencies> _files = new(StringComparer.Ordinal);

    internal ModuleDependencyImportGraphReader(IModuleDependencyMetadataProvider metadata, ILogger logger)
    {
        _metadata = metadata;
        _logger = logger;
    }

    internal IReadOnlyDictionary<string, string[]> Read(IReadOnlyList<ApprovedModuleSource> sources)
    {
        var selected = sources.ToDictionary(static source => source.Name, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var active = new HashSet<string>(StringComparer.Ordinal);
            VisitFile(ResolveEntryPoint(source.ModuleBasePath, source.Name), 0);
            result[source.Name] = dependencies.ToArray();

            void VisitFile(string path, int depth)
            {
                path = Path.GetFullPath(path);
                if (active.Contains(path))
                    throw new InvalidOperationException($"Module dependency cycle at '{path}' under '{source.Name}'.");
                if (!visited.Add(path) || string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                    return;
                active.Add(path);
                try
                {
                    if (depth > 128)
                        throw new InvalidOperationException($"Module dependency declarations are too deeply nested under '{source.Name}'.");
                    if (!_files.TryGetValue(path, out var file))
                        _files[path] = file = ReadSource(path);
                    foreach (var required in file.Required)
                        VisitReference(required, nested: false);
                    foreach (var nested in file.Nested)
                        VisitReference(nested, nested: true);
                }
                finally { active.Remove(path); }

                void VisitReference(RequiredModuleReference reference, bool nested)
                {
                    var local = nested || Path.IsPathRooted(reference.ModuleName) ||
                                reference.ModuleName.IndexOfAny(new[] { '/', '\\' }) >= 0;
                    if (local)
                    {
                        var candidate = Path.IsPathRooted(reference.ModuleName)
                            ? reference.ModuleName
                            : Path.Combine(Path.GetDirectoryName(path)!, reference.ModuleName.Replace('\\', Path.DirectorySeparatorChar));
                        var localPath = ResolveLocalFile(candidate);
                        if (localPath is not null)
                        {
                            VisitFile(localPath, depth + 1);
                            return;
                        }
                    }
                    if (selected.ContainsKey(reference.ModuleName))
                    {
                        dependencies.Add(reference.ModuleName);
                        return;
                    }
                    var modulePath = ResolveInstalledReference(reference, source.ModuleSearchRoot);
                    if (modulePath is null)
                    {
                        _logger.Warn($"Cannot inspect indirect module dependency '{reference.ModuleName}' of '{source.Name}' from installed metadata. Import ordering is incomplete until that dependency is available.");
                        return;
                    }
                    VisitFile(modulePath, depth + 1);
                }
            }
        }
        return result;
    }

    private string? ResolveInstalledReference(RequiredModuleReference reference, string? searchRoot)
    {
        var key = string.Join("\0", searchRoot, reference.ModuleName, reference.ModuleVersion,
            reference.RequiredVersion, reference.MaximumVersion, reference.Guid);
        if (_resolved.TryGetValue(key, out var existing))
            return existing;
        var provider = !string.IsNullOrWhiteSpace(searchRoot) && _metadata is PowerShellModuleDependencyMetadataProvider native
            ? native.WithModuleSearchRoot(searchRoot!)
            : _metadata;
        var installed = provider is IModuleDependencyVersionedMetadataProvider versioned
            ? versioned.GetInstalledModules(new[] { reference })
            : provider.GetLatestInstalledModules(new[] { reference.ModuleName });
        var path = installed.TryGetValue(reference.ModuleName, out var module) &&
                   !string.IsNullOrWhiteSpace(module.ModuleBasePath)
            ? ResolveEntryPoint(module.ModuleBasePath!, reference.ModuleName)
            : null;
        _resolved[key] = path;
        return path;
    }

    private static SourceDependencies ReadSource(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".psd1", StringComparison.OrdinalIgnoreCase))
        {
            var text = ModuleManifestValueReader.ReadPowerShellCompatibleText(path);
            ModuleManifestTextParser.TryGetModuleReferences(text, "RequiredModules", out var required);
            ModuleManifestTextParser.TryGetModuleReferences(text, "NestedModules", out var nested);
            var children = new List<RequiredModuleReference>(nested ?? Array.Empty<RequiredModuleReference>());
            if ((ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(text, "RootModule", out var root) ||
                 ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(text, "ModuleToProcess", out root)) &&
                !string.IsNullOrWhiteSpace(root))
                children.Add(new RequiredModuleReference(root!));
            return new SourceDependencies(required ?? Array.Empty<RequiredModuleReference>(), children.ToArray());
        }
        if (!string.Equals(extension, ".psm1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
            return new SourceDependencies(Array.Empty<RequiredModuleReference>(), Array.Empty<RequiredModuleReference>());

        var ast = Parser.ParseFile(path, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException($"Cannot inspect module dependency declarations in '{path}': {errors[0].Message}");
        var references = (ast.ScriptRequirements?.RequiredModules.AsEnumerable() ?? Enumerable.Empty<ModuleSpecification>())
            .Select(ToReference).ToList();
        foreach (var statement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false)
                     .Cast<UsingStatementAst>()
                     .Where(static statement => statement.UsingStatementKind == UsingStatementKind.Module))
        {
            var moduleSpecification = statement.GetType().GetProperty("ModuleSpecification")?.GetValue(statement) as HashtableAst;
            var spec = moduleSpecification is not null
                ? new ModuleSpecification((Hashtable)moduleSpecification.SafeGetValue())
                : new ModuleSpecification(statement.Name.Value);
            references.Add(ToReference(spec));
        }
        return new SourceDependencies(references.ToArray(), Array.Empty<RequiredModuleReference>());
    }

    private static RequiredModuleReference ToReference(ModuleSpecification spec)
        => new(spec.Name, moduleVersion: spec.Version?.ToString(), requiredVersion: spec.RequiredVersion?.ToString(),
            maximumVersion: spec.GetType().GetProperty("MaximumVersion")?.GetValue(spec)?.ToString(), guid: spec.Guid?.ToString());

    private static string? ResolveLocalFile(string path)
    {
        if (File.Exists(path))
            return path;
        foreach (var extension in new[] { ".psd1", ".psm1", ".dll" })
            if (File.Exists(path + extension))
                return path + extension;
        if (Directory.Exists(path))
            return EmbeddedModuleDependencyService.ResolveModuleImportPath(path, Path.GetFileName(path));
        return null;
    }

    private static string ResolveEntryPoint(string moduleBasePath, string name)
        => ResolveLocalFile(Path.Combine(moduleBasePath, name)) ??
           EmbeddedModuleDependencyService.ResolveModuleImportPath(moduleBasePath, name);

    private sealed class SourceDependencies
    {
        internal RequiredModuleReference[] Required { get; }
        internal RequiredModuleReference[] Nested { get; }

        internal SourceDependencies(RequiredModuleReference[] required, RequiredModuleReference[] nested)
        {
            Required = required;
            Nested = nested;
        }
    }
}
