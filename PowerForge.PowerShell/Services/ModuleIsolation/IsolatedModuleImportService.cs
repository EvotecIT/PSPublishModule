using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Prepares and imports known PowerShell modules through module-scoped AssemblyLoadContext wrappers.
/// </summary>
public sealed partial class IsolatedModuleImportService
{
    private static readonly StringComparison PathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

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
        var source = ResolveModuleSource(request, profile);
        var validationIssues = ValidateProfileLayout(profile, source);
        if (validationIssues.Any(static issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(BuildValidationFailureMessage(profile, validationIssues));

        var sourceModuleBase = source.ModuleBase;
        var sourceScriptPath = Path.Combine(sourceModuleBase, NormalizeRelativePath(profile.ScriptRelativePath));
        if (!File.Exists(sourceScriptPath))
            throw new FileNotFoundException($"The profile '{profile.Name}' expected script module '{profile.ScriptRelativePath}' below '{sourceModuleBase}'.", sourceScriptPath);

        var workRoot = ResolveWorkRoot(request.WorkRoot, profile);
        var workPath = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        var isolatedModuleBase = Path.Combine(workPath, profile.ModuleName);
        CopyDirectory(sourceModuleBase, isolatedModuleBase);
        PatchCopiedBinaryImportScripts(isolatedModuleBase, profile);

        var isolatedScriptDirectory = Path.Combine(isolatedModuleBase, Path.GetDirectoryName(NormalizeRelativePath(profile.ScriptRelativePath)) ?? string.Empty);
        var isolatedScriptPath = Path.Combine(isolatedScriptDirectory, profile.IsolatedScriptName);
        _patcher.PatchFile(sourceScriptPath, isolatedScriptPath, profile);
        var isolatedManifestPath = PrepareManifest(profile, source.ManifestPath, isolatedModuleBase, isolatedScriptPath);
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
        var moduleResolutionPath = string.Empty;
        if (request.PreferIsolatedModulePath)
        {
            moduleResolutionPath = plan.WorkPath;
            PrependModuleResolutionPath(moduleResolutionPath);
        }

        return new IsolatedModuleImportResult
        {
            ProfileName = plan.Profile.Name,
            ModuleName = plan.Profile.ModuleName,
            SourceModuleBase = plan.SourceModuleBase,
            IsolatedScriptPath = plan.IsolatedScriptPath,
            IsolatedManifestPath = plan.IsolatedManifestPath,
            IsolatedImportPath = plan.IsolatedImportPath,
            WorkPath = plan.WorkPath,
            PreferIsolatedModulePath = request.PreferIsolatedModulePath,
            IsolatedModuleResolutionPath = moduleResolutionPath,
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

    private static ResolvedModuleSource ResolveModuleSource(IsolatedModuleImportRequest request, ModuleIsolationProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            var explicitSource = ResolveExplicitModuleSource(request.Path!, profile);
            EnsureMinimumVersion(explicitSource, profile);
            return explicitSource;
        }

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

        var moduleBase = Path.GetFullPath(module.ModuleBase);
        var manifestPath = !string.IsNullOrWhiteSpace(module.Path) && Path.GetExtension(module.Path).Equals(".psd1", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(module.Path)
            : ResolveProfileManifestPath(moduleBase, profile);

        return new ResolvedModuleSource(moduleBase, manifestPath);
    }

    private static void EnsureMinimumVersion(ResolvedModuleSource source, ModuleIsolationProfile profile)
    {
        if (profile.MinimumVersion is null)
            return;

        if (!File.Exists(source.ManifestPath))
            throw new FileNotFoundException($"Profile '{profile.Name}' requires a module manifest to validate the minimum supported version.", source.ManifestPath);

        var version = ReadManifestVersion(source.ManifestPath);
        if (version < profile.MinimumVersion)
            throw new InvalidOperationException($"Profile '{profile.Name}' requires {profile.ModuleName} {profile.MinimumVersion} or newer. Resolved version {version} at '{source.ModuleBase}'.");
    }

    private static Version ReadManifestVersion(string manifestPath)
    {
        var ast = Parser.ParseFile(manifestPath, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException($"Module manifest '{manifestPath}' could not be parsed. {errors[0].Message}");

        var hashtable = ast.Find(static node => node is HashtableAst, searchNestedScriptBlocks: false) as HashtableAst
            ?? throw new InvalidOperationException($"Module manifest '{manifestPath}' does not contain a root hashtable.");

        foreach (var pair in hashtable.KeyValuePairs)
        {
            var key = GetManifestStringValue(pair.Item1);
            if (!string.Equals(key, "ModuleVersion", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = GetManifestStringValue(pair.Item2);
            if (Version.TryParse(value, out var version))
                return version;

            throw new InvalidOperationException($"Module manifest '{manifestPath}' has an invalid ModuleVersion value '{value}'.");
        }

        throw new InvalidOperationException($"Module manifest '{manifestPath}' does not declare ModuleVersion.");
    }

    private static string GetManifestStringValue(Ast ast)
    {
        if (ast is StringConstantExpressionAst stringAst)
            return stringAst.Value;

        if (ast is ConstantExpressionAst constantAst)
            return Convert.ToString(constantAst.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        return ast.Extent.Text.Trim().Trim('\'', '"');
    }

    private static ResolvedModuleSource ResolveExplicitModuleSource(string path, ModuleIsolationProfile profile)
    {
        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (Directory.Exists(fullPath))
            return new ResolvedModuleSource(fullPath, ResolveExistingManifestPath(fullPath, profile));

        if (File.Exists(fullPath))
        {
            var moduleBase = Path.GetDirectoryName(fullPath) ?? throw new DirectoryNotFoundException($"Could not resolve module base for '{fullPath}'.");
            var manifestPath = Path.GetExtension(fullPath).Equals(".psd1", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : ResolveExistingManifestPath(moduleBase, profile);

            return new ResolvedModuleSource(moduleBase, manifestPath);
        }

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

    private static string PrepareManifest(ModuleIsolationProfile profile, string sourceManifestPath, string isolatedModuleBase, string isolatedScriptPath)
    {
        if (string.IsNullOrWhiteSpace(profile.ManifestRelativePath))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(profile.IsolatedManifestName))
            throw new InvalidOperationException($"Profile '{profile.Name}' declares a manifest path but no generated manifest name.");

        var manifestRelativePath = NormalizeRelativePath(profile.ManifestRelativePath);
        if (!File.Exists(sourceManifestPath))
            throw new FileNotFoundException($"The profile '{profile.Name}' expected module manifest '{profile.ManifestRelativePath}'.", sourceManifestPath);

        var manifestDirectory = Path.Combine(isolatedModuleBase, Path.GetDirectoryName(manifestRelativePath) ?? string.Empty);
        var isolatedManifestPath = Path.Combine(manifestDirectory, profile.IsolatedManifestName);
        var scriptRelativePath = GetRelativePath(manifestDirectory, isolatedScriptPath).Replace('\\', '/');
        if (!scriptRelativePath.StartsWith(".", StringComparison.Ordinal))
            scriptRelativePath = "./" + scriptRelativePath;

        var source = File.ReadAllText(sourceManifestPath, Encoding.UTF8);
        var patched = PatchManifestText(source, sourceManifestPath, scriptRelativePath, profile.RemoveManifestNestedModules);
        Directory.CreateDirectory(Path.GetDirectoryName(isolatedManifestPath) ?? ".");
        File.WriteAllText(isolatedManifestPath, patched, Encoding.UTF8);
        WriteDiscoverableManifest(profile, source, sourceManifestPath, isolatedModuleBase, isolatedManifestPath, isolatedScriptPath);
        return isolatedManifestPath;
    }

    private static void WriteDiscoverableManifest(ModuleIsolationProfile profile, string source, string sourceManifestPath, string isolatedModuleBase, string isolatedManifestPath, string isolatedScriptPath)
    {
        var discoverableManifestPath = Path.Combine(isolatedModuleBase, profile.ModuleName + ".psd1");
        if (PathsEqual(discoverableManifestPath, isolatedManifestPath))
            return;

        var scriptRelativePath = GetRelativePath(isolatedModuleBase, isolatedScriptPath).Replace('\\', '/');
        if (!scriptRelativePath.StartsWith(".", StringComparison.Ordinal))
            scriptRelativePath = "./" + scriptRelativePath;

        var patched = PatchManifestText(source, sourceManifestPath, scriptRelativePath, profile.RemoveManifestNestedModules);
        File.WriteAllText(discoverableManifestPath, patched, Encoding.UTF8);
    }

    private static string ResolveProfileManifestPath(string moduleBase, ModuleIsolationProfile profile)
        => Path.Combine(moduleBase, NormalizeRelativePath(
            string.IsNullOrWhiteSpace(profile.ManifestRelativePath)
                ? profile.ModuleName + ".psd1"
                : profile.ManifestRelativePath));

    private static string ResolveExistingManifestPath(string moduleBase, ModuleIsolationProfile profile)
    {
        var profileManifestPath = ResolveProfileManifestPath(moduleBase, profile);
        if (File.Exists(profileManifestPath))
            return profileManifestPath;

        var manifests = Directory.GetFiles(moduleBase, "*.psd1", SearchOption.TopDirectoryOnly);
        if (manifests.Length == 1)
            return manifests[0];

        var moduleNameManifestPath = Path.Combine(moduleBase, Path.GetFileName(moduleBase) + ".psd1");
        if (File.Exists(moduleNameManifestPath))
            return moduleNameManifestPath;

        return profileManifestPath;
    }

    private static string PatchManifestText(string source, string sourceManifestPath, string rootModule, bool removeNestedModules)
    {
        var replacement = "RootModule = '" + rootModule.Replace("'", "''") + "'";
        var patched = new Regex(@"^\s*RootModule\s*=\s*(['""]).*?\1", RegexOptions.IgnoreCase | RegexOptions.Multiline)
            .Replace(source, replacement, 1);

        if (string.Equals(source, patched, StringComparison.Ordinal))
        {
            patched = new Regex(@"^\s*RootModule\s*=\s*if\s*\([^\r\n]*\)\s*\{.*?^\s*\}\s*else[^\r\n]*\s*\{.*?^\s*\}", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline)
                .Replace(source, replacement, 1);
        }

        if (string.Equals(source, patched, StringComparison.Ordinal))
            throw new InvalidOperationException($"Module manifest '{sourceManifestPath}' does not contain a RootModule entry that can be patched.");

        if (removeNestedModules)
            patched = RemoveManifestNestedModules(patched);

        return patched;
    }

    private static string RemoveManifestNestedModules(string manifest)
    {
        var lines = manifest
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
        var builder = new StringBuilder();
        var skippingNestedModules = false;

        foreach (var line in lines)
        {
            if (!skippingNestedModules &&
                Regex.IsMatch(line, @"^\s*NestedModules\s*=", RegexOptions.IgnoreCase))
            {
                builder.AppendLine("NestedModules = @()");
                skippingNestedModules = line.Contains("@(", StringComparison.Ordinal) && !line.Contains(")", StringComparison.Ordinal);
                continue;
            }

            if (skippingNestedModules)
            {
                if (line.Contains(")", StringComparison.Ordinal))
                    skippingNestedModules = false;
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
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

    internal static string PrependModuleResolutionPath(string moduleResolutionPath)
    {
        if (string.IsNullOrWhiteSpace(moduleResolutionPath))
            throw new ArgumentException("Module resolution path is required.", nameof(moduleResolutionPath));

        var normalizedPath = Path.GetFullPath(moduleResolutionPath.Trim().Trim('"'));
        var current = Environment.GetEnvironmentVariable("PSModulePath", EnvironmentVariableTarget.Process) ?? string.Empty;
        var entries = current
            .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static entry => entry.Trim())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .ToList();

        if (!entries.Any(entry => PathsEqual(entry, normalizedPath)))
        {
            entries.Insert(0, normalizedPath);
            Environment.SetEnvironmentVariable("PSModulePath", string.Join(Path.PathSeparator.ToString(), entries), EnvironmentVariableTarget.Process);
        }

        return normalizedPath;
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), PathComparison);
        }
        catch (Exception) when (
            first.Length > 0 &&
            second.Length > 0)
        {
            return string.Equals(first.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), second.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), PathComparison);
        }
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

    private static void PatchCopiedBinaryImportScripts(string isolatedModuleBase, ModuleIsolationProfile profile)
    {
        if (profile.CopiedScriptBinaryImports.Length == 0)
            return;

        var binaryImports = profile.CopiedScriptBinaryImports
            .Select(relativePath => new
            {
                RelativePath = NormalizeRelativePath(relativePath),
                FileName = GetPortableFileName(relativePath)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.FileName))
            .ToArray();

        foreach (var scriptPath in Directory.EnumerateFiles(isolatedModuleBase, "*.psm1", SearchOption.AllDirectories))
        {
            var script = File.ReadAllText(scriptPath, Encoding.UTF8);
            var lines = script
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
            var changed = false;
            var scriptDirectory = Path.GetDirectoryName(scriptPath) ?? isolatedModuleBase;

            foreach (var binaryImport in binaryImports)
            {
                var binaryPath = Path.Combine(isolatedModuleBase, binaryImport.RelativePath);
                var binaryPathFromScript = GetRelativePath(scriptDirectory, binaryPath).Replace('\\', '/');
                var loadLine = "$null = Import-Module -Assembly ([PowerForge.ModuleIsolation.ModuleLoadContext]::LoadModule([System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, '" + EscapePowerShellSingleQuotedString(binaryPathFromScript) + "')), '" + EscapePowerShellSingleQuotedString(profile.ContextName) + "')) -Force";

                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];
                    if (!line.Contains("Import-Module", StringComparison.OrdinalIgnoreCase) ||
                        !line.Contains(binaryImport.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var indent = new string(line.TakeWhile(char.IsWhiteSpace).ToArray());
                    lines[index] = indent + loadLine;
                    changed = true;
                }
            }

            if (changed)
            {
                var patched = string.Join(Environment.NewLine, lines);
                File.WriteAllText(scriptPath, patched, Encoding.UTF8);
            }
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

    private static string EscapePowerShellSingleQuotedString(string value)
        => value.Replace("'", "''");

    private static string GetPortableFileName(string path)
        => path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;

    private sealed class ResolvedModuleSource
    {
        public ResolvedModuleSource(string moduleBase, string manifestPath)
        {
            ModuleBase = moduleBase;
            ManifestPath = manifestPath;
        }

        public string ModuleBase { get; }

        public string ManifestPath { get; }
    }
}
