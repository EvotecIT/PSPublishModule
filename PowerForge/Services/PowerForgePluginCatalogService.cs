using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

internal sealed class PowerForgePluginCatalogService
{
    private readonly ILogger _logger;
    private readonly Func<ProcessStartInfo, PluginCatalogProcessResult> _runProcess;
    private readonly Func<DotNetNuGetPushRequest, DotNetNuGetPushResult> _pushPackage;

    public PowerForgePluginCatalogService(ILogger logger)
        : this(logger, RunProcess, PushPackage)
    {
    }

    internal PowerForgePluginCatalogService(
        ILogger logger,
        Func<ProcessStartInfo, PluginCatalogProcessResult> runProcess)
        : this(logger, runProcess, PushPackage)
    {
    }

    internal PowerForgePluginCatalogService(
        ILogger logger,
        Func<ProcessStartInfo, PluginCatalogProcessResult> runProcess,
        Func<DotNetNuGetPushRequest, DotNetNuGetPushResult> pushPackage)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runProcess = runProcess ?? throw new ArgumentNullException(nameof(runProcess));
        _pushPackage = pushPackage ?? throw new ArgumentNullException(nameof(pushPackage));
    }

    public PowerForgePluginFolderExportPlan PlanFolderExport(
        PowerForgePluginCatalogSpec spec,
        string? configPath,
        PowerForgePluginCatalogRequest? request = null)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var configDir = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

        var projectRoot = string.IsNullOrWhiteSpace(spec.ProjectRoot)
            ? configDir
            : ResolvePath(configDir, spec.ProjectRoot!);

        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"Plugin catalog ProjectRoot not found: {projectRoot}");

        var configuration = string.IsNullOrWhiteSpace(request?.Configuration)
            ? (string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim())
            : request!.Configuration!.Trim();
        var preferredFramework = NormalizeNullable(request?.PreferredFramework);
        var includeSymbols = request?.IncludeSymbols ?? false;
        var selectedGroups = NormalizeStrings(request?.Groups);

        var catalog = (spec.Catalog ?? Array.Empty<PowerForgePluginCatalogEntry>())
            .Where(entry => entry is not null)
            .ToArray();
        if (catalog.Length == 0)
            throw new InvalidOperationException("Plugin catalog does not contain any entries.");

        var knownGroups = new HashSet<string>(
            catalog
                .SelectMany(entry => entry.Groups ?? Array.Empty<string>())
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Select(group => group.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (selectedGroups.Length > 0)
        {
            var missing = selectedGroups
                .Where(group => !knownGroups.Contains(group))
                .ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown plugin group(s): {string.Join(", ", missing)}", nameof(request));
        }

        var selectedEntries = selectedGroups.Length == 0
            ? catalog
            : catalog.Where(entry => EntryMatchesAnyGroup(entry, selectedGroups)).ToArray();

        if (selectedEntries.Length == 0)
            throw new InvalidOperationException("No plugin catalog entries were selected.");

        var outputRoot = string.IsNullOrWhiteSpace(request?.OutputRoot)
            ? ResolvePath(projectRoot, Path.Combine("Artifacts", "Plugins"))
            : ResolvePath(projectRoot, request!.OutputRoot!);

        var plans = new List<PowerForgePluginFolderExportEntryPlan>();
        foreach (var entry in selectedEntries)
        {
            var id = NormalizeRequired(entry.Id, "Catalog entry Id");
            var projectPath = ResolvePath(projectRoot, NormalizeRequired(entry.ProjectPath, $"Plugin catalog entry '{id}' ProjectPath"));
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Plugin project not found for entry '{id}': {projectPath}", projectPath);

            var metadata = ReadProjectMetadata(projectPath);
            var resolvedFramework = ResolveFramework(
                metadata.Frameworks,
                NormalizeNullable(entry.Framework),
                preferredFramework);

            var assemblyName = NormalizeNullable(entry.AssemblyName)
                ?? metadata.AssemblyName
                ?? Path.GetFileNameWithoutExtension(projectPath)
                ?? id;
            var packageId = NormalizeNullable(entry.PackageId)
                ?? metadata.PackageId
                ?? assemblyName;

            var msbuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in entry.MsBuildProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                    continue;

                msbuildProperties[property.Key.Trim()] = property.Value ?? string.Empty;
            }

            plans.Add(new PowerForgePluginFolderExportEntryPlan
            {
                Id = id,
                ProjectPath = projectPath,
                Groups = NormalizeStrings(entry.Groups),
                Framework = resolvedFramework,
                PackageId = packageId,
                AssemblyName = assemblyName,
                OutputPath = Path.Combine(outputRoot, packageId),
                MsBuildProperties = msbuildProperties,
                Manifest = entry.Manifest
            });
        }

        return new PowerForgePluginFolderExportPlan
        {
            ProjectRoot = projectRoot,
            Configuration = configuration,
            OutputRoot = outputRoot,
            IncludeSymbols = includeSymbols,
            SelectedGroups = selectedGroups,
            Entries = plans
                .OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public PowerForgePluginFolderExportResult ExportFolders(PowerForgePluginFolderExportPlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var results = new List<PowerForgePluginFolderExportEntryResult>();
        try
        {
            foreach (var entry in plan.Entries ?? Array.Empty<PowerForgePluginFolderExportEntryPlan>())
                results.Add(ExportOne(plan, entry));

            return new PowerForgePluginFolderExportResult
            {
                Success = true,
                Entries = results.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
            if (_logger.IsVerbose)
                _logger.Verbose(ex.ToString());

            return new PowerForgePluginFolderExportResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Entries = results.ToArray()
            };
        }
    }

    public PowerForgePluginPackagePlan PlanPackages(
        PowerForgePluginCatalogSpec spec,
        string? configPath,
        PowerForgePluginPackageRequest? request = null)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var configDir = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

        var projectRoot = string.IsNullOrWhiteSpace(spec.ProjectRoot)
            ? configDir
            : ResolvePath(configDir, spec.ProjectRoot!);

        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"Plugin catalog ProjectRoot not found: {projectRoot}");

        var configuration = string.IsNullOrWhiteSpace(request?.Configuration)
            ? (string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim())
            : request!.Configuration!.Trim();
        var selectedGroups = NormalizeStrings(request?.Groups);
        var packageVersion = NormalizeNullable(request?.PackageVersion);
        var versionSuffix = NormalizeNullable(request?.VersionSuffix);
        if (!string.IsNullOrWhiteSpace(packageVersion) && !string.IsNullOrWhiteSpace(versionSuffix))
            throw new ArgumentException("PackageVersion and VersionSuffix cannot be used together.", nameof(request));

        var catalog = (spec.Catalog ?? Array.Empty<PowerForgePluginCatalogEntry>())
            .Where(entry => entry is not null)
            .ToArray();
        if (catalog.Length == 0)
            throw new InvalidOperationException("Plugin catalog does not contain any entries.");

        var knownGroups = new HashSet<string>(
            catalog
                .SelectMany(entry => entry.Groups ?? Array.Empty<string>())
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Select(group => group.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (selectedGroups.Length > 0)
        {
            var missing = selectedGroups
                .Where(group => !knownGroups.Contains(group))
                .ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown plugin group(s): {string.Join(", ", missing)}", nameof(request));
        }

        var selectedEntries = selectedGroups.Length == 0
            ? catalog
            : catalog.Where(entry => EntryMatchesAnyGroup(entry, selectedGroups)).ToArray();

        if (selectedEntries.Length == 0)
            throw new InvalidOperationException("No plugin catalog entries were selected.");

        if (request?.PushPackages == true)
        {
            if (string.IsNullOrWhiteSpace(request.PushSource))
                throw new ArgumentException("PushSource is required when PushPackages is enabled.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.ApiKey))
                throw new ArgumentException("ApiKey is required when PushPackages is enabled.", nameof(request));
        }

        var outputRoot = string.IsNullOrWhiteSpace(request?.OutputRoot)
            ? ResolvePath(projectRoot, Path.Combine("Artifacts", "NuGet"))
            : ResolvePath(projectRoot, request!.OutputRoot!);

        var plans = new List<PowerForgePluginPackageEntryPlan>();
        foreach (var entry in selectedEntries)
        {
            var id = NormalizeRequired(entry.Id, "Catalog entry Id");
            var projectPath = ResolvePath(projectRoot, NormalizeRequired(entry.ProjectPath, $"Plugin catalog entry '{id}' ProjectPath"));
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Plugin project not found for entry '{id}': {projectPath}", projectPath);

            var metadata = ReadProjectMetadata(projectPath);
            var packageId = NormalizeNullable(entry.PackageId)
                ?? metadata.PackageId
                ?? metadata.AssemblyName
                ?? Path.GetFileNameWithoutExtension(projectPath)
                ?? id;

            var msbuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in entry.MsBuildProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                    continue;

                msbuildProperties[property.Key.Trim()] = property.Value ?? string.Empty;
            }

            plans.Add(new PowerForgePluginPackageEntryPlan
            {
                Id = id,
                ProjectPath = projectPath,
                Groups = NormalizeStrings(entry.Groups),
                PackageId = packageId,
                OutputRoot = outputRoot,
                MsBuildProperties = msbuildProperties
            });
        }

        return new PowerForgePluginPackagePlan
        {
            ProjectRoot = projectRoot,
            Configuration = configuration,
            OutputRoot = outputRoot,
            NoBuild = request?.NoBuild ?? false,
            IncludeSymbols = request?.IncludeSymbols ?? false,
            PackageVersion = packageVersion,
            VersionSuffix = versionSuffix,
            PushPackages = request?.PushPackages ?? false,
            PushSource = NormalizeNullable(request?.PushSource),
            SkipDuplicate = request?.SkipDuplicate ?? true,
            SelectedGroups = selectedGroups,
            Entries = plans
                .OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    public PowerForgePluginPackageResult PackPackages(PowerForgePluginPackagePlan plan, string? apiKey = null)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var results = new List<PowerForgePluginPackageEntryResult>();
        try
        {
            foreach (var entry in plan.Entries ?? Array.Empty<PowerForgePluginPackageEntryPlan>())
                results.Add(PackOne(plan, entry));

            if (plan.PushPackages)
            {
                var normalizedApiKey = NormalizeRequired(apiKey, "ApiKey");
                foreach (var entry in results)
                {
                    var pushResults = new List<DotNetNuGetPushResult>();
                    foreach (var packagePath in entry.PackagePaths ?? Array.Empty<string>())
                    {
                        _logger.Info($"Pushing plugin package '{Path.GetFileName(packagePath)}' -> {plan.PushSource}");
                        pushResults.Add(_pushPackage(new DotNetNuGetPushRequest(
                            packagePath,
                            normalizedApiKey,
                            plan.PushSource!,
                            skipDuplicate: plan.SkipDuplicate,
                            workingDirectory: plan.ProjectRoot)));
                    }

                    entry.PushResults = pushResults.ToArray();
                    var failedPush = entry.PushResults.FirstOrDefault(push => push.ExitCode != 0 || push.TimedOut);
                    if (failedPush is not null)
                    {
                        throw new InvalidOperationException(
                            $"dotnet nuget push failed for package '{entry.PackageId}'. {TrimForMessage(failedPush.StdErr, failedPush.StdOut)}");
                    }
                }
            }

            return new PowerForgePluginPackageResult
            {
                Success = true,
                Entries = results.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
            if (_logger.IsVerbose)
                _logger.Verbose(ex.ToString());

            return new PowerForgePluginPackageResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Entries = results.ToArray()
            };
        }
    }

    private PowerForgePluginFolderExportEntryResult ExportOne(
        PowerForgePluginFolderExportPlan plan,
        PowerForgePluginFolderExportEntryPlan entry)
    {
        ClearDirectory(entry.OutputPath);
        Directory.CreateDirectory(entry.OutputPath);

        _logger.Info($"Exporting plugin '{entry.Id}' ({entry.Framework}) -> {entry.OutputPath}");
        ExecutePublish(plan, entry);

        if (!plan.IncludeSymbols)
            RemoveFiles(entry.OutputPath, "*.pdb");

        var manifestPath = WriteManifest(plan, entry);
        var summary = SummarizeDirectory(entry.OutputPath);
        return new PowerForgePluginFolderExportEntryResult
        {
            Id = entry.Id,
            ProjectPath = entry.ProjectPath,
            Framework = entry.Framework,
            PackageId = entry.PackageId,
            AssemblyName = entry.AssemblyName,
            OutputPath = entry.OutputPath,
            ManifestPath = manifestPath,
            Files = summary.Files,
            TotalBytes = summary.TotalBytes
        };
    }

    private PowerForgePluginPackageEntryResult PackOne(
        PowerForgePluginPackagePlan plan,
        PowerForgePluginPackageEntryPlan entry)
    {
        Directory.CreateDirectory(plan.OutputRoot);

        _logger.Info($"Packing plugin '{entry.Id}' -> {plan.OutputRoot}");
        ExecutePack(plan, entry);

        var allPackageFiles = ListMatchingPackageFiles(plan.OutputRoot, entry.PackageId);
        var packagePaths = allPackageFiles
            .Where(path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var symbolPackagePaths = allPackageFiles
            .Where(path => path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (packagePaths.Length == 0)
            throw new InvalidOperationException($"dotnet pack did not produce a .nupkg for plugin '{entry.Id}' in '{plan.OutputRoot}'.");

        return new PowerForgePluginPackageEntryResult
        {
            Id = entry.Id,
            ProjectPath = entry.ProjectPath,
            PackageId = entry.PackageId,
            OutputRoot = plan.OutputRoot,
            PackagePaths = packagePaths,
            SymbolPackagePaths = symbolPackagePaths
        };
    }

    private void ExecutePublish(PowerForgePluginFolderExportPlan plan, PowerForgePluginFolderExportEntryPlan entry)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(entry.ProjectPath) ?? plan.ProjectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        var args = new List<string>
        {
            "publish",
            Quote(entry.ProjectPath),
            "-c",
            Quote(plan.Configuration)
        };

        if (!string.IsNullOrWhiteSpace(entry.Framework))
        {
            args.Add("-f");
            args.Add(Quote(entry.Framework));
        }

        args.Add("-o");
        args.Add(Quote(entry.OutputPath));
        args.Add("/p:UseAppHost=false");

        foreach (var property in entry.MsBuildProperties)
            args.Add($"/p:{property.Key}={property.Value}");

        psi.Arguments = string.Join(" ", args);

        var result = _runProcess(psi);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet publish failed for plugin '{entry.Id}' ({entry.Framework}). " +
                $"{TrimForMessage(result.StdErr, result.StdOut)}");
        }
    }

    private void ExecutePack(PowerForgePluginPackagePlan plan, PowerForgePluginPackageEntryPlan entry)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(entry.ProjectPath) ?? plan.ProjectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        var args = new List<string>
        {
            "pack",
            Quote(entry.ProjectPath),
            "-c",
            Quote(plan.Configuration),
            "-o",
            Quote(plan.OutputRoot),
            "/p:ContinuousIntegrationBuild=true"
        };

        if (plan.NoBuild)
            args.Add("--no-build");
        if (!string.IsNullOrWhiteSpace(plan.PackageVersion))
            args.Add("/p:PackageVersion=" + plan.PackageVersion);
        else if (!string.IsNullOrWhiteSpace(plan.VersionSuffix))
            args.Add("/p:VersionSuffix=" + plan.VersionSuffix);
        if (plan.IncludeSymbols)
        {
            args.Add("/p:IncludeSymbols=true");
            args.Add("/p:SymbolPackageFormat=snupkg");
        }

        foreach (var property in entry.MsBuildProperties)
            args.Add($"/p:{property.Key}={property.Value}");

        psi.Arguments = string.Join(" ", args);

        var result = _runProcess(psi);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet pack failed for plugin '{entry.Id}'. {TrimForMessage(result.StdErr, result.StdOut)}");
        }
    }

    private string? WriteManifest(PowerForgePluginFolderExportPlan plan, PowerForgePluginFolderExportEntryPlan entry)
    {
        var manifest = entry.Manifest;
        if (manifest is null || !manifest.Enabled)
            return null;

        var manifestFileName = string.IsNullOrWhiteSpace(manifest.FileName)
            ? "plugin.manifest.json"
            : manifest.FileName.Trim();
        var manifestPath = Path.Combine(entry.OutputPath, manifestFileName);

        var entryAssembly = NormalizeNullable(manifest.EntryAssembly)
            ?? (entry.AssemblyName + ".dll");
        var entryType = NormalizeNullable(manifest.EntryType)
            ?? ResolveEntryType(entry.ProjectPath, manifest.EntryTypeMatchBaseType);

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = entry.Id,
            ["packageId"] = entry.PackageId,
            ["assemblyName"] = entry.AssemblyName,
            ["entryAssembly"] = entryAssembly,
            ["entryType"] = entryType ?? string.Empty,
            ["framework"] = entry.Framework,
            ["configuration"] = plan.Configuration,
            ["projectRoot"] = plan.ProjectRoot,
            ["projectPath"] = entry.ProjectPath,
            ["outputPath"] = entry.OutputPath
        };

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (manifest.IncludeStandardProperties)
        {
            payload["schemaVersion"] = 1;
            payload["id"] = entry.Id;
            payload["packageId"] = entry.PackageId;
            payload["assemblyName"] = entry.AssemblyName;
            payload["entryAssembly"] = entryAssembly;
            payload["entryType"] = entryType;
            payload["framework"] = entry.Framework;
            payload["configuration"] = plan.Configuration;
            payload["groups"] = entry.Groups;
        }

        foreach (var property in manifest.Properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(property.Key))
                continue;

            payload[property.Key.Trim()] = ApplyTemplate(property.Value ?? string.Empty, tokens);
        }

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
        return manifestPath;
    }

    internal static string? ResolveEntryType(string projectPath, string? baseTypeSymbol)
    {
        var matchSymbol = NormalizeNullable(baseTypeSymbol);
        if (string.IsNullOrWhiteSpace(matchSymbol))
            return null;

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            return null;

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceFile in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = FrameworkCompatibility.GetRelativePath(projectDirectory, sourceFile)
                .Replace('/', '\\');
            if (relative.StartsWith("bin\\", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("obj\\", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = File.ReadAllText(sourceFile);
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var namespaceMatch = Regex.Match(content, @"(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]");
            var namespaceName = namespaceMatch.Success ? namespaceMatch.Groups[1].Value.Trim() : string.Empty;

            var typeMatches = Regex.Matches(
                content,
                @"(?m)^\s*(?:public|internal|protected|private|sealed|abstract|partial|static|\s)*class\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([^\r\n{]+)");
            foreach (Match typeMatch in typeMatches)
            {
                var className = typeMatch.Groups[1].Value.Trim();
                var inheritanceList = typeMatch.Groups[2].Value;
                if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(inheritanceList))
                    continue;

                var parts = inheritanceList
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim());
                if (!parts.Any(part => TypeReferenceMatchesSymbol(part, matchSymbol!)))
                    continue;

                var fullTypeName = string.IsNullOrWhiteSpace(namespaceName)
                    ? className
                    : namespaceName + "." + className;
                candidates.Add(fullTypeName);
            }
        }

        if (candidates.Count == 0)
            return null;
        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one type assignable to '{matchSymbol}' in '{projectPath}', found {candidates.Count}: {string.Join(", ", candidates)}");
        }

        return candidates.First();
    }

    private static bool TypeReferenceMatchesSymbol(string typeReference, string symbol)
    {
        var normalizedReference = StripGenericArity(typeReference);
        var normalizedSymbol = StripGenericArity(symbol);
        if (string.Equals(normalizedReference, normalizedSymbol, StringComparison.Ordinal))
            return true;

        var lastDot = normalizedReference.LastIndexOf('.');
        if (lastDot >= 0)
            normalizedReference = normalizedReference.Substring(lastDot + 1);

        var symbolLastDot = normalizedSymbol.LastIndexOf('.');
        if (symbolLastDot >= 0)
            normalizedSymbol = normalizedSymbol.Substring(symbolLastDot + 1);

        return string.Equals(normalizedReference, normalizedSymbol, StringComparison.Ordinal);
    }

    private static string StripGenericArity(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        var genericIndex = trimmed.IndexOf('<');
        if (genericIndex >= 0)
            trimmed = trimmed.Substring(0, genericIndex);
        return trimmed;
    }

    private static (int Files, long TotalBytes) SummarizeDirectory(string path)
    {
        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file))
            .ToArray();

        long totalBytes = 0;
        foreach (var file in files)
            totalBytes += file.Length;

        return (files.Length, totalBytes);
    }

    private static void RemoveFiles(string root, string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static string[] ListMatchingPackageFiles(string outputRoot, string packageId)
    {
        if (!Directory.Exists(outputRoot))
            return Array.Empty<string>();

        var pattern = "^" + Regex.Escape(packageId) + "\\..+\\.(?:nupkg|snupkg)$";
        return Directory.EnumerateFiles(outputRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Regex.IsMatch(Path.GetFileName(path), pattern, RegexOptions.IgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool EntryMatchesAnyGroup(PowerForgePluginCatalogEntry entry, string[] selectedGroups)
    {
        var groups = NormalizeStrings(entry.Groups);
        return groups.Any(group => selectedGroups.Contains(group, StringComparer.OrdinalIgnoreCase));
    }

    private static string ResolveFramework(
        string[] projectFrameworks,
        string? entryFramework,
        string? preferredFramework)
    {
        var explicitFramework = NormalizeNullable(entryFramework);
        if (!string.IsNullOrWhiteSpace(explicitFramework))
            return explicitFramework!;

        var preferred = NormalizeNullable(preferredFramework);
        if (projectFrameworks.Length == 0)
            return preferred ?? string.Empty;
        if (string.IsNullOrWhiteSpace(preferred))
            return projectFrameworks[0];
        if (projectFrameworks.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            return preferred!;

        var preferredCore = preferred!;
        if (preferredCore.EndsWith("-windows", StringComparison.OrdinalIgnoreCase))
            preferredCore = preferredCore.Substring(0, preferredCore.Length - "-windows".Length);
        var coreMatch = projectFrameworks.FirstOrDefault(framework =>
            string.Equals(framework, preferredCore, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(coreMatch))
            return coreMatch!;

        if (preferred!.EndsWith("-windows", StringComparison.OrdinalIgnoreCase))
        {
            var windowsMatch = projectFrameworks.FirstOrDefault(framework =>
                framework.EndsWith("-windows", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(windowsMatch))
                return windowsMatch!;
        }

        return projectFrameworks[0];
    }

    private static ProjectMetadata ReadProjectMetadata(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var properties = document.Root?
            .Elements()
            .Where(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.OrdinalIgnoreCase))
            .SelectMany(element => element.Elements())
            .ToArray() ?? Array.Empty<XElement>();

        var targetFramework = properties
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        var targetFrameworks = properties
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        var assemblyName = properties
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "AssemblyName", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        var packageId = properties
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "PackageId", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        var frameworks = !string.IsNullOrWhiteSpace(targetFramework)
            ? new[] { (targetFramework ?? string.Empty).Trim() }
            : (targetFrameworks ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

        return new ProjectMetadata(
            frameworks,
            NormalizeNullable(assemblyName),
            NormalizeNullable(packageId));
    }

    private static string[] NormalizeStrings(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeRequired(string? value, string label)
    {
        var normalized = NormalizeNullable(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException($"{label} is required.");
        return normalized!;
    }

    private static string ResolvePath(string basePath, string value)
    {
        var trimmed = (value ?? string.Empty).Trim().Trim('"');
        if (trimmed.Length == 0)
            throw new ArgumentException("Path value is required.", nameof(value));

        return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(basePath, trimmed));
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var value = template ?? string.Empty;
        foreach (var token in tokens)
            value = value.Replace("{" + token.Key + "}", token.Value ?? string.Empty);
        return value;
    }

    private static string Quote(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";

    private static void ClearDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            try
            {
                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static PluginCatalogProcessResult RunProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
            return new PluginCatalogProcessResult(1, string.Empty, "Failed to start process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new PluginCatalogProcessResult(
            process.ExitCode,
            stdOutTask.GetAwaiter().GetResult(),
            stdErrTask.GetAwaiter().GetResult());
    }

    private static DotNetNuGetPushResult PushPackage(DotNetNuGetPushRequest request)
        => new DotNetNuGetClient().PushPackageAsync(request).GetAwaiter().GetResult();

    private static string TrimForMessage(string? stdErr, string? stdOut)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { stdErr, stdOut }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        if (combined.Length <= 500)
            return combined;

        return combined.Substring(0, 500).TrimEnd() + "...";
    }

    private sealed class ProjectMetadata
    {
        public ProjectMetadata(string[] frameworks, string? assemblyName, string? packageId)
        {
            Frameworks = frameworks ?? Array.Empty<string>();
            AssemblyName = assemblyName;
            PackageId = packageId;
        }

        public string[] Frameworks { get; }

        public string? AssemblyName { get; }

        public string? PackageId { get; }
    }
}

internal sealed class PluginCatalogProcessResult
{
    public PluginCatalogProcessResult(int exitCode, string stdOut, string stdErr)
    {
        ExitCode = exitCode;
        StdOut = stdOut ?? string.Empty;
        StdErr = stdErr ?? string.Empty;
    }

    public int ExitCode { get; }

    public string StdOut { get; }

    public string StdErr { get; }
}
