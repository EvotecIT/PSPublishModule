using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Prepares and imports known PowerShell modules through module-scoped AssemblyLoadContext wrappers.
/// </summary>
public sealed class IsolatedModuleImportService
{
    private readonly ModuleIsolationProfileRegistry _profiles;
    private readonly ModuleIsolationScriptPatcher _patcher;

    /// <summary>Initializes the service with the built-in profile registry.</summary>
    public IsolatedModuleImportService()
        : this(new ModuleIsolationProfileRegistry(), new ModuleIsolationScriptPatcher())
    {
    }

    /// <summary>Initializes the service with explicit dependencies for tests or custom hosts.</summary>
    public IsolatedModuleImportService(ModuleIsolationProfileRegistry profiles, ModuleIsolationScriptPatcher patcher)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _patcher = patcher ?? throw new ArgumentNullException(nameof(patcher));
    }

    /// <summary>Creates a copied and patched module import plan without importing it.</summary>
    public IsolatedModuleImportPlan Prepare(IsolatedModuleImportRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        EnsureSupportedRuntime();

        var profile = _profiles.Resolve(request.ProfileName);
        var sourceModuleBase = ResolveModuleBase(request, profile);
        var sourceScriptPath = Path.Combine(sourceModuleBase, NormalizeRelativePath(profile.ScriptRelativePath));
        if (!File.Exists(sourceScriptPath))
            throw new FileNotFoundException($"The profile '{profile.Name}' expected script module '{profile.ScriptRelativePath}' below '{sourceModuleBase}'.", sourceScriptPath);

        var workRoot = ResolveWorkRoot(request.WorkRoot, profile);
        var workPath = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        var isolatedModuleBase = Path.Combine(workPath, profile.ModuleName);
        CopyDirectory(sourceModuleBase, isolatedModuleBase);

        var isolatedScriptDirectory = Path.Combine(isolatedModuleBase, Path.GetDirectoryName(NormalizeRelativePath(profile.ScriptRelativePath)) ?? string.Empty);
        var isolatedScriptPath = Path.Combine(isolatedScriptDirectory, profile.IsolatedScriptName);
        _patcher.PatchFile(sourceScriptPath, isolatedScriptPath, profile);
        var isolatedManifestPath = PrepareManifest(profile, sourceModuleBase, isolatedModuleBase, isolatedScriptPath);
        var isolatedImportPath = string.IsNullOrWhiteSpace(isolatedManifestPath) ? isolatedScriptPath : isolatedManifestPath;

        return new IsolatedModuleImportPlan
        {
            Profile = profile,
            SourceModuleBase = sourceModuleBase,
            WorkPath = workPath,
            IsolatedModuleBase = isolatedModuleBase,
            IsolatedScriptPath = isolatedScriptPath,
            IsolatedManifestPath = isolatedManifestPath,
            IsolatedImportPath = isolatedImportPath
        };
    }

    /// <summary>Prepares and imports the isolated module into the current PowerShell runspace.</summary>
    public IsolatedModuleImportResult Import(IsolatedModuleImportRequest request)
    {
        var plan = Prepare(request);
        ImportGeneratedModule(plan.IsolatedImportPath);

        return new IsolatedModuleImportResult
        {
            ProfileName = plan.Profile.Name,
            ModuleName = plan.Profile.ModuleName,
            SourceModuleBase = plan.SourceModuleBase,
            IsolatedScriptPath = plan.IsolatedScriptPath,
            IsolatedManifestPath = plan.IsolatedManifestPath,
            IsolatedImportPath = plan.IsolatedImportPath,
            WorkPath = plan.WorkPath,
            ContextName = plan.Profile.ContextName,
            BinaryImportCount = plan.Profile.BinaryImports.Length,
            TypeAcceleratorNamespaceCount = plan.Profile.TypeAcceleratorNamespaces.Length
        };
    }

    private static void EnsureSupportedRuntime()
    {
        if (RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException("Import-IsolatedModule requires PowerShell 7+ on CoreCLR because AssemblyLoadContext is not available in Windows PowerShell.");
    }

    private static string ResolveModuleBase(IsolatedModuleImportRequest request, ModuleIsolationProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(request.Path))
            return ResolveExplicitModuleBase(request.Path!);

        var moduleName = string.IsNullOrWhiteSpace(request.ModuleName) ? profile.ModuleName : request.ModuleName!.Trim();
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddCommand("Get-Module")
            .AddParameter("ListAvailable")
            .AddParameter("Name", moduleName);

        var modules = ps.Invoke<PSModuleInfo>();
        ThrowIfPowerShellFailed(ps.Streams.Error, $"Failed to resolve module '{moduleName}'.");

        var module = modules
            .Where(static item => !string.IsNullOrWhiteSpace(item.ModuleBase))
            .OrderByDescending(static item => item.Version)
            .FirstOrDefault();

        if (module is null)
            throw new InvalidOperationException($"Module '{moduleName}' is not installed or visible on PSModulePath.");

        if (profile.MinimumVersion is not null && module.Version < profile.MinimumVersion)
            throw new InvalidOperationException($"Profile '{profile.Name}' requires {profile.ModuleName} {profile.MinimumVersion} or newer. Resolved version {module.Version} at '{module.ModuleBase}'.");

        return Path.GetFullPath(module.ModuleBase);
    }

    private static string ResolveExplicitModuleBase(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (Directory.Exists(fullPath))
            return fullPath;

        if (File.Exists(fullPath))
            return Path.GetDirectoryName(fullPath) ?? throw new DirectoryNotFoundException($"Could not resolve module base for '{fullPath}'.");

        throw new FileNotFoundException($"Module path was not found: {fullPath}", fullPath);
    }

    private static string ResolveWorkRoot(string? workRoot, ModuleIsolationProfile profile)
    {
        var root = string.IsNullOrWhiteSpace(workRoot)
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "IsolatedModules", SanitizePathSegment(profile.Name))
            : Path.GetFullPath(workRoot!.Trim().Trim('"'));

        Directory.CreateDirectory(root);
        return root;
    }

    private static string PrepareManifest(ModuleIsolationProfile profile, string sourceModuleBase, string isolatedModuleBase, string isolatedScriptPath)
    {
        if (string.IsNullOrWhiteSpace(profile.ManifestRelativePath))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(profile.IsolatedManifestName))
            throw new InvalidOperationException($"Profile '{profile.Name}' declares a manifest path but no generated manifest name.");

        var manifestRelativePath = NormalizeRelativePath(profile.ManifestRelativePath);
        var sourceManifestPath = Path.Combine(sourceModuleBase, manifestRelativePath);
        if (!File.Exists(sourceManifestPath))
            throw new FileNotFoundException($"The profile '{profile.Name}' expected module manifest '{profile.ManifestRelativePath}' below '{sourceModuleBase}'.", sourceManifestPath);

        var manifestDirectory = Path.Combine(isolatedModuleBase, Path.GetDirectoryName(manifestRelativePath) ?? string.Empty);
        var isolatedManifestPath = Path.Combine(manifestDirectory, profile.IsolatedManifestName);
        var scriptRelativePath = GetRelativePath(manifestDirectory, isolatedScriptPath).Replace('\\', '/');
        if (!scriptRelativePath.StartsWith(".", StringComparison.Ordinal))
            scriptRelativePath = "./" + scriptRelativePath;

        PatchManifest(sourceManifestPath, isolatedManifestPath, scriptRelativePath);
        return isolatedManifestPath;
    }

    private static void PatchManifest(string sourceManifestPath, string targetManifestPath, string rootModule)
    {
        var source = File.ReadAllText(sourceManifestPath, Encoding.UTF8);
        var replacement = "RootModule = '" + rootModule.Replace("'", "''") + "'";
        var patched = new Regex(@"^\s*RootModule\s*=\s*(['""]).*?\1", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Replace(source, replacement, 1);

        if (string.Equals(source, patched, StringComparison.Ordinal))
            throw new InvalidOperationException($"Module manifest '{sourceManifestPath}' does not contain a RootModule entry that can be patched.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetManifestPath) ?? ".");
        File.WriteAllText(targetManifestPath, patched, Encoding.UTF8);
    }

    private static void ImportGeneratedModule(string isolatedImportPath)
    {
        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddCommand("Import-Module")
            .AddParameter("Name", isolatedImportPath)
            .AddParameter("Force")
            .AddParameter("PassThru")
            .AddParameter("ErrorAction", ActionPreference.Stop);

        _ = ps.Invoke<PSModuleInfo>();
        ThrowIfPowerShellFailed(ps.Streams.Error, $"Failed to import isolated module '{isolatedImportPath}'.");
    }

    private static void ThrowIfPowerShellFailed(PSDataCollection<ErrorRecord> errors, string message)
    {
        if (errors.Count == 0)
            return;

        var first = errors[0];
        throw new InvalidOperationException(message + " " + first.Exception.Message, first.Exception);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var targetRoot = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(targetRoot);

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(sourceRoot, file);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? targetRoot);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private static string GetRelativePath(string root, string path)
    {
        var rootUri = new Uri(AppendDirectorySeparator(root));
        var pathUri = new Uri(path);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
