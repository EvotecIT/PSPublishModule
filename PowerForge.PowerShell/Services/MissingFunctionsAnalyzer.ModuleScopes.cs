using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.ExceptionServices;

namespace PowerForge;

public sealed partial class MissingFunctionsAnalyzer
{
    private Runspace? _selectedRuntimeRunspace;
    private readonly Dictionary<string, ExceptionDispatchInfo> _moduleScopeFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ApprovedModuleSource> _selectedRuntimeSources = new(StringComparer.OrdinalIgnoreCase);

    private void InitializeSelectedRuntimeScope(IReadOnlyList<ApprovedModuleSource> sources)
    {
        if (sources.Count == 0)
            return;

        // A real consumer imports its selected dependencies before its helper modules.
        // Keep that native PowerShell context for module-scoped lookups: a separate
        // donor runspace could otherwise satisfy a minimum version with a newer copy.
        _selectedRuntimeRunspace = CreateModuleScopeRunspace();
        foreach (var source in sources)
            _selectedRuntimeSources[source.Name] = source;
        var sourcesByName = sources.ToDictionary(static source => source.Name, StringComparer.OrdinalIgnoreCase);
        var order = ModuleDependencyImportOrder.Resolve(
            sources.Select(static source => source.Name).ToArray(),
            sources.ToDictionary(static source => source.Name, static source => source.ModuleBasePath, StringComparer.OrdinalIgnoreCase));
        foreach (var name in order)
        {
            try { GetOrCreateModuleScopeSession(sourcesByName[name]); }
            catch
            {
                // The cached failure is reported when command resolution reaches this
                // source. Do not turn a failed import into a successful empty surface.
            }
        }
    }

    private static Runspace CreateModuleScopeRunspace()
    {
        var state = InitialSessionState.CreateDefault();
        state.AuthorizationManager = new AuthorizationManager("PowerForge");
        var runspace = RunspaceFactory.CreateRunspace(state);
        try { runspace.Open(); return runspace; }
        catch { runspace.Dispose(); throw; }
    }

    private ModuleScopeSession GetOrCreateModuleScopeSession(ApprovedModuleSource source)
    {
        var key = source.Name.Trim() + "|" + (source.Version ?? string.Empty) + "|" + source.ModuleBasePath + "|" + (source.ModuleSearchRoot ?? string.Empty);
        if (_moduleScopeSessions.TryGetValue(key, out var existing))
            return existing;
        if (_moduleScopeFailures.TryGetValue(key, out var failure))
            failure.Throw();

        var runspace = _selectedRuntimeRunspace ?? CreateModuleScopeRunspace();
        try
        {
            using var ps = PowerShell.Create();
            ps.Runspace = runspace;
            var moduleReference = ResolveModuleReference(source);
            System.Collections.ObjectModel.Collection<PSObject> importResults;
            if (string.IsNullOrWhiteSpace(source.ModuleSearchRoot))
            {
                importResults = ps.AddCommand("Import-Module")
                    .AddParameter("Name", moduleReference)
                    .AddParameter("PassThru", true)
                    .AddParameter("Force", true)
                    .AddParameter("ErrorAction", "Stop")
                    .AddParameter("Verbose", false)
                    .Invoke();
            }
            else
            {
                ps.AddScript(@"
param($ModuleReference, $ModuleSearchRoot)
$previousModulePath = [Environment]::GetEnvironmentVariable('PSModulePath', 'Process')
try {
    [Environment]::SetEnvironmentVariable(
        'PSModulePath',
        $ModuleSearchRoot + [IO.Path]::PathSeparator + $previousModulePath,
        'Process')
    Import-Module -Name $ModuleReference -PassThru -Force -ErrorAction Stop -Verbose:$false
} finally {
    [Environment]::SetEnvironmentVariable('PSModulePath', $previousModulePath, 'Process')
}
")
                    .AddArgument(moduleReference)
                    .AddArgument(source.ModuleSearchRoot);
                lock (ModulePathImportLock)
                    importResults = ps.Invoke();
            }

            var imported = importResults
                .Select(static result => result.BaseObject)
                .OfType<PSModuleInfo>()
                .LastOrDefault();
            // PowerShell.HadErrors is sticky across nested pipelines, including failures that
            // a module deliberately catches for optional dependencies. Treat the import as
            // failed only when the outer Import-Module pipeline emitted an error or did not
            // return a module; otherwise healthy donors such as ADEssentials are discarded.
            if (ps.Streams.Error.Count > 0 || imported is null)
            {
                var reason = ps.Streams.Error.Count > 0
                    ? ps.Streams.Error[0].ToString()
                    : "Import-Module returned no module.";
                throw new InvalidOperationException($"Selected module '{source.Name}' could not be imported from '{moduleReference}'. {reason}");
            }
            ValidateImportedModuleIdentity(imported, source);
            foreach (var dependency in EnumerateLoadedModuleGraph(imported).Skip(1))
                if (_selectedRuntimeSources.TryGetValue(dependency.Name, out var selected))
                    ValidateImportedModuleIdentity(dependency, selected);

            var created = new ModuleScopeSession(
                runspace,
                imported.ModuleBase,
                imported.ExportedCommands.Keys,
                HasNonFunctionRuntimeSurface(imported));
            _moduleScopeSessions[key] = created;
            return created;
        }
        catch (Exception ex)
        {
            ex.Data[SelectedModuleFailureKey] = source.Name;
            _moduleScopeFailures[key] = ExceptionDispatchInfo.Capture(ex);
            if (_selectedRuntimeRunspace is null)
                runspace.Dispose();
            throw;
        }
    }

    private static void ValidateImportedModuleIdentity(PSModuleInfo imported, ApprovedModuleSource source)
    {
        if (!string.Equals(imported.Name, source.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Imported module '{imported.Name}' does not match expected module '{source.Name}'.");
        if (!string.IsNullOrWhiteSpace(source.Guid) &&
            (!System.Guid.TryParse(source.Guid, out var expectedGuid) || imported.Guid != expectedGuid))
            throw new InvalidOperationException($"Imported module '{source.Name}' GUID '{imported.Guid}' does not match expected GUID '{source.Guid}'.");
        if (!string.IsNullOrWhiteSpace(source.Version) && !ModuleVersionMatches(imported, source.Version!))
            throw new InvalidOperationException($"Imported module '{source.Name}' version '{FormatImportedModuleVersion(imported)}' does not match expected version '{source.Version}'.");
    }

    private static bool ModuleVersionMatches(PSModuleInfo module, string expectedVersion)
    {
        var expected = (expectedVersion ?? string.Empty).Trim();
        var metadataSeparator = expected.IndexOf('+');
        if (metadataSeparator >= 0)
            expected = expected.Substring(0, metadataSeparator);

        var prereleaseSeparator = expected.IndexOf('-');
        var expectedBase = prereleaseSeparator >= 0 ? expected.Substring(0, prereleaseSeparator) : expected;
        var expectedPrerelease = prereleaseSeparator >= 0 ? expected.Substring(prereleaseSeparator + 1) : string.Empty;
        if (!Version.TryParse(expectedBase, out var parsedExpected) || module.Version is null || module.Version != parsedExpected)
            return false;

        return string.Equals(GetModulePrerelease(module), expectedPrerelease, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatImportedModuleVersion(PSModuleInfo module)
    {
        var baseVersion = module.Version?.ToString() ?? string.Empty;
        var prerelease = GetModulePrerelease(module);
        return string.IsNullOrWhiteSpace(prerelease) ? baseVersion : $"{baseVersion}-{prerelease}";
    }

    private static string GetModulePrerelease(PSModuleInfo module)
    {
        if (module.PrivateData is not IDictionary privateData)
            return string.Empty;
        var psData = GetDictionaryValue(privateData, "PSData") as IDictionary;
        var prerelease = psData is null ? null : GetDictionaryValue(psData, "Prerelease")?.ToString();
        return (prerelease ?? string.Empty).Trim().TrimStart('-');
    }

    private static object? GetDictionaryValue(IDictionary dictionary, string key)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }

    private static string ResolveModuleReference(ApprovedModuleSource source)
    {
        if (string.IsNullOrWhiteSpace(source.ModuleBasePath))
            return source.Name;

        foreach (var extension in new[] { ".psd1", ".psm1", ".dll" })
        {
            var modulePath = Path.Combine(source.ModuleBasePath, source.Name + extension);
            if (File.Exists(modulePath))
                return modulePath;
        }

        throw new FileNotFoundException(
            $"The selected approved module '{source.Name}' entry point was not found under '{source.ModuleBasePath}'. Expected {source.Name}.psd1, {source.Name}.psm1, or {source.Name}.dll.",
            source.ModuleBasePath);
    }

    private void DisposeModuleScopeSessions()
    {
        var runspaces = new HashSet<Runspace>(_moduleScopeSessions.Values.Select(static session => session.Runspace));
        if (_selectedRuntimeRunspace is not null)
            runspaces.Add(_selectedRuntimeRunspace);
        foreach (var runspace in runspaces)
            runspace.Dispose();
        _selectedRuntimeRunspace = null;
        _moduleScopeSessions.Clear();
        _moduleScopeFailures.Clear();
        _selectedRuntimeSources.Clear();
        _moduleScopeCommandCache.Clear();
        _currentSessionCommandCache.Clear();
    }

    private sealed class ModuleScopeSession
    {
        internal Runspace Runspace { get; }
        internal string ModuleBase { get; }
        internal HashSet<string> ExportedCommandNames { get; }
        internal bool HasNonFunctionRuntimeSurface { get; }

        internal ModuleScopeSession(
            Runspace runspace,
            string moduleBase,
            IEnumerable<string> exportedCommandNames,
            bool hasNonFunctionRuntimeSurface)
        {
            Runspace = runspace;
            ModuleBase = moduleBase;
            HasNonFunctionRuntimeSurface = hasNonFunctionRuntimeSurface;
            ExportedCommandNames = new HashSet<string>(
                exportedCommandNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

    }

}
