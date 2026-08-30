using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Non-secret normalized module and release inputs that must remain stable when compiled staging is reused.</summary>
internal sealed class PowerShellModuleCompilationReleaseContract
{
    private PowerShellModuleCompilationReleaseContract(
        string sha256,
        string moduleName,
        string version,
        string preRelease,
        RequiredModuleReference[] requiredModules,
        string[] externalModuleDependencies)
    {
        Sha256 = sha256;
        ModuleName = moduleName;
        Version = version;
        PreRelease = preRelease;
        RequiredModules = requiredModules;
        ExternalModuleDependencies = externalModuleDependencies;
    }

    internal string Sha256 { get; }
    internal string ModuleName { get; }
    internal string Version { get; }
    internal string PreRelease { get; }
    internal RequiredModuleReference[] RequiredModules { get; }
    internal string[] ExternalModuleDependencies { get; }

    internal static PowerShellModuleCompilationReleaseContract Create(
        ModulePipelinePlan plan,
        IEnumerable<RequiredModuleReference>? manifestRequiredModules,
        IEnumerable<string>? manifestExternalModuleDependencies)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var compilation = plan.BuildSpec.PowerShellCompilation
            ?? throw new InvalidOperationException("PowerShell compilation configuration is unavailable.");
        if (compilation.DependencyLock is not null)
            PowerShellCompilationDependencyLockHasher.EnsureValid(compilation.DependencyLock, "Configured dependency lock");
        var sourceInput = new PowerShellCompilationInputResolver().Resolve(
            plan.BuildSpec.SourcePath,
            PowerShellCompilationArtifactKind.BinaryModule,
            compilation.Mode,
            allowDynamicModuleRuntimeSources: compilation.ResourceMode == PowerShellCompilationResourceMode.CompleteModule &&
                                              compilation.Mode != PowerShellCompilationMode.Strict);
        var sourceInputSha256 = CreateSourceInputSha256(
            sourceInput,
            compilation);
        var sourceTreeSha256 = CreateSourceTreeSha256(plan.BuildSpec);

        var requiredModules = NormalizeRequiredModules(manifestRequiredModules).ToArray();
        var externalModules = NormalizeStrings(manifestExternalModuleDependencies, StringComparer.OrdinalIgnoreCase);
        var compilerAssembly = typeof(PowerShellModuleCompilationReleaseContract).Assembly;
        var coreAssembly = typeof(PowerShellCompilationPlan).Assembly;
        var safeContract = new
        {
            schemaVersion = 3,
            sourceInputSha256,
            sourceTreeSha256,
            compiler = new
            {
                powerShellAssemblyVersion = compilerAssembly.GetName().Version?.ToString() ?? string.Empty,
                powerShellAssemblySha256 = ComputeFileSha256(compilerAssembly.Location),
                coreAssemblyVersion = coreAssembly.GetName().Version?.ToString() ?? string.Empty,
                coreAssemblySha256 = ComputeFileSha256(coreAssembly.Location)
            },
            moduleName = plan.ModuleName,
            version = plan.ResolvedVersion,
            preRelease = Normalize(plan.PreRelease),
            compatiblePSEditions = NormalizeStrings(plan.CompatiblePSEditions, StringComparer.OrdinalIgnoreCase),
            requiredModules = requiredModules.Select(static module => new
            {
                module.ModuleName,
                module.ModuleVersion,
                module.RequiredVersion,
                module.MaximumVersion,
                module.Guid
            }).ToArray(),
            externalModuleDependencies = externalModules,
            embeddedModules = NormalizeRequiredModules(plan.EmbeddedModules).Select(static module => new
            {
                module.ModuleName,
                module.ModuleVersion,
                module.RequiredVersion,
                module.MaximumVersion,
                module.Guid
            }).ToArray(),
            plan.Manifest,
            plan.Information,
            plan.Delivery,
            build = new
            {
                plan.BuildSpec.Name,
                plan.BuildSpec.Version,
                plan.BuildSpec.PreReleaseTag,
                plan.BuildSpec.Configuration,
                frameworks = NormalizeStrings(plan.BuildSpec.Frameworks, StringComparer.OrdinalIgnoreCase),
                nuGetRestoreSources = NormalizeStrings(plan.BuildSpec.NuGetRestoreSources, StringComparer.OrdinalIgnoreCase),
                plan.BuildSpec.Author,
                plan.BuildSpec.CompanyName,
                plan.BuildSpec.Description,
                tags = NormalizeStrings(plan.BuildSpec.Tags, StringComparer.Ordinal),
                plan.BuildSpec.IconUri,
                plan.BuildSpec.ProjectUri,
                exportAssemblies = NormalizeStrings(plan.BuildSpec.ExportAssemblies, StringComparer.OrdinalIgnoreCase),
                plan.BuildSpec.UseAssemblyLoadContext,
                plan.BuildSpec.HandleRuntimes,
                plan.BuildSpec.DisableBinaryCmdletScan,
                excludeDirectories = NormalizeStrings(plan.BuildSpec.ExcludeDirectories, StringComparer.OrdinalIgnoreCase),
                excludeFiles = NormalizeStrings(plan.BuildSpec.ExcludeFiles, StringComparer.OrdinalIgnoreCase),
                excludeLibraryFilter = NormalizeStrings(plan.BuildSpec.ExcludeLibraryFilter, StringComparer.OrdinalIgnoreCase),
                ignoreLibraryOnLoad = NormalizeStrings(plan.BuildSpec.IgnoreLibraryOnLoad, StringComparer.OrdinalIgnoreCase),
                plan.BuildSpec.DoNotCopyLibrariesRecursively,
                plan.BuildSpec.DevelopmentBinariesMode,
                plan.BuildSpec.DevelopmentBinariesPath,
                plan.BuildSpec.DevelopmentBinariesEnvironmentVariable,
                plan.BuildSpec.DevelopmentConfigurationEnvironmentVariable,
                plan.BuildSpec.DevelopmentSourceBootstrapperMode,
                plan.BuildSpec.AssemblyTypeAcceleratorMode,
                assemblyTypeAccelerators = NormalizeStrings(plan.BuildSpec.AssemblyTypeAccelerators, StringComparer.Ordinal),
                assemblyTypeAcceleratorAssemblies = NormalizeStrings(plan.BuildSpec.AssemblyTypeAcceleratorAssemblies, StringComparer.OrdinalIgnoreCase),
                csprojRequiredReasons = NormalizeStrings(plan.BuildSpec.CsprojRequiredReasons, StringComparer.Ordinal),
                plan.BuildSpec.RefreshManifestOnly,
                resolvedCsprojIdentity = CreateOptionalFileIdentity(plan.ResolvedCsprojPath)
            },
            transformations = new
            {
                plan.MergeModule,
                plan.MergeMissing,
                plan.DoNotAttemptToFixRelativePaths,
                approvedModules = NormalizeStrings(plan.ApprovedModules, StringComparer.OrdinalIgnoreCase),
                plan.ModuleSkip,
                plan.Documentation,
                plan.DocumentationBuild,
                plan.Formatting,
                plan.ImportModules,
                placeholders = (plan.PlaceHolders ?? Array.Empty<PlaceHolderReplacement>())
                    .Select(static placeholder => new
                    {
                        findSha256 = ComputeSha256(Encoding.UTF8.GetBytes(placeholder.Find ?? string.Empty)),
                        replaceSha256 = ComputeSha256(Encoding.UTF8.GetBytes(placeholder.Replace ?? string.Empty))
                    }).ToArray(),
                plan.PlaceHolderOption,
                commandModuleDependencies = (plan.CommandModuleDependencies ?? new Dictionary<string, string[]>())
                    .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static pair => new
                    {
                        module = pair.Key,
                        commands = NormalizeStrings(pair.Value, StringComparer.OrdinalIgnoreCase)
                    }).ToArray(),
                actions = (plan.Actions ?? Array.Empty<ConfigurationActionSegment>())
                    .Select(action => CreateActionIdentity(action, plan.ProjectRoot))
                    .ToArray(),
                externalAssets = (plan.ExternalAssets ?? Array.Empty<ConfigurationExternalAssetSegment>())
                    .Select(static asset => new
                    {
                        asset.Configuration.Enabled,
                        asset.Configuration.Name,
                        asset.Configuration.Version,
                        asset.Configuration.OutputPath,
                        asset.Configuration.ManifestPath,
                        sourceSha256 = ComputeSha256(Encoding.UTF8.GetBytes(asset.Configuration.Source ?? string.Empty)),
                        asset.Configuration.License,
                        asset.Configuration.SkipDownload,
                        files = (asset.Configuration.Files ?? Array.Empty<ExternalAssetFileConfiguration>())
                            .Select(static file => new
                            {
                                file.Runtime,
                                file.Architecture,
                                file.FileName,
                                file.Path,
                                uriSha256 = ComputeSha256(Encoding.UTF8.GetBytes(file.Uri ?? string.Empty)),
                                file.Sha256
                            }).ToArray()
                    }).ToArray()
            },
            plan.SignModule,
            signing = plan.Signing is null ? null : new
            {
                plan.Signing.IncludeInternals,
                plan.Signing.IncludeBinaries,
                plan.Signing.IncludeExe,
                include = NormalizeStrings(plan.Signing.Include, StringComparer.OrdinalIgnoreCase),
                excludePaths = NormalizeStrings(plan.Signing.ExcludePaths, StringComparer.OrdinalIgnoreCase),
                plan.Signing.OverwriteSigned,
                certificateThumbprint = Normalize(plan.Signing.CertificateThumbprint).Replace(" ", string.Empty),
                certificatePfxIdentitySha256 = CreatePfxIdentitySha256(plan.Signing)
            },
            compilation = new
            {
                compilation.Enabled,
                compilation.Mode,
                compilation.TargetFramework,
                compilation.ResourceMode,
                includeResource = NormalizeStrings(compilation.IncludeResource, StringComparer.OrdinalIgnoreCase),
                excludeResource = NormalizeStrings(compilation.ExcludeResource, StringComparer.OrdinalIgnoreCase),
                compilation.UseBuildCache,
                buildCacheDirectory = Normalize(compilation.BuildCacheDirectory),
                compilation.EmitIrSnapshots,
                expectedPublicAbiSha256 = Normalize(compilation.ExpectedPublicAbiSha256),
                dependencyLockSha256 = Normalize(compilation.DependencyLock?.LockSha256),
                compilation.AllowUnreviewedDependencies,
                compilation.TimeoutSeconds
            },
            artefacts = (plan.Artefacts ?? Array.Empty<ConfigurationArtefactSegment>())
                .Select(static artefact => new
                {
                    artefact.ArtefactType,
                    configuration = artefact.Configuration is null ? null : new
                    {
                        artefact.Configuration.Enabled,
                        artefact.Configuration.ID,
                        artefact.Configuration.Path,
                        artefact.Configuration.ArtefactName,
                        artefact.Configuration.IncludeTagName,
                        artefact.Configuration.DoNotClear,
                        requiredModules = new
                        {
                            artefact.Configuration.RequiredModules.Enabled,
                            artefact.Configuration.RequiredModules.Path,
                            artefact.Configuration.RequiredModules.Repository,
                            artefact.Configuration.RequiredModules.Tool,
                            artefact.Configuration.RequiredModules.Source,
                            excludeModuleName = NormalizeStrings(
                                artefact.Configuration.RequiredModules.ExcludeModuleName,
                                StringComparer.OrdinalIgnoreCase)
                        }
                    }
                }).ToArray(),
            publishes = (plan.Publishes ?? Array.Empty<ConfigurationPublishSegment>())
                .Select(static publish => new
                {
                    publish.Configuration.Destination,
                    publish.Configuration.Tool,
                    publish.Configuration.ID,
                    publish.Configuration.Enabled,
                    publish.Configuration.UserName,
                    publish.Configuration.RepositoryName,
                    publish.Configuration.Force,
                    publish.Configuration.OverwriteTagName,
                    publish.Configuration.DoNotMarkAsPreRelease,
                    publish.Configuration.GenerateReleaseNotes,
                    publish.Configuration.ReuseExistingRelease,
                    publish.Configuration.ReplaceExistingAssets,
                    publish.Configuration.UseAsDependencyVersionSource,
                    publish.Configuration.PublishRequiredModules,
                    publish.Configuration.RequiredModuleSourceRepository,
                    publish.Configuration.RequiredModuleSourceRepositoryUri,
                    repository = publish.Configuration.Repository is null ? null : new
                    {
                        publish.Configuration.Repository.Name,
                        publish.Configuration.Repository.Uri,
                        publish.Configuration.Repository.SourceUri,
                        publish.Configuration.Repository.PublishUri,
                        publish.Configuration.Repository.Trusted,
                        publish.Configuration.Repository.Priority,
                        publish.Configuration.Repository.ApiVersion,
                        publish.Configuration.Repository.EnsureRegistered,
                        publish.Configuration.Repository.UnregisterAfterUse,
                        credentialUserName = publish.Configuration.Repository.Credential?.UserName,
                        credentialProvider = publish.Configuration.Repository.CredentialProvider is null ? null : new
                        {
                            publish.Configuration.Repository.CredentialProvider.Kind,
                            publish.Configuration.Repository.CredentialProvider.UserName,
                            publish.Configuration.Repository.CredentialProvider.JFrogPlatformUri,
                            publish.Configuration.Repository.CredentialProvider.JFrogOidcProvider,
                            publish.Configuration.Repository.CredentialProvider.JFrogOidcTokenIdEnvironmentVariable,
                            publish.Configuration.Repository.CredentialProvider.JFrogOidcProviderType
                        }
                    }
                }).ToArray(),
            install = new
            {
                plan.InstallEnabled,
                plan.InstallStrategy,
                plan.InstallKeepVersions,
                roots = NormalizeStrings(plan.InstallRoots, StringComparer.OrdinalIgnoreCase),
                plan.InstallLegacyFlatHandling,
                preserveVersions = NormalizeStrings(plan.InstallPreserveVersions, StringComparer.OrdinalIgnoreCase)
            }
        };
        var json = JsonSerializer.Serialize(safeContract);
        return new PowerShellModuleCompilationReleaseContract(
            ComputeSha256(Encoding.UTF8.GetBytes(json)),
            plan.ModuleName,
            plan.ResolvedVersion,
            Normalize(plan.PreRelease),
            requiredModules,
            externalModules);
    }

    internal void ValidateStagedManifest(string manifestPath, string expectedRootModule)
    {
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("Reusable compiled staging is missing its module manifest.");

        var version = Normalize(ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion"));
        var preRelease = Normalize(ModuleManifestValueReader.ReadTopLevelString(manifestPath, "Prerelease") ??
                                   ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Prerelease").FirstOrDefault());
        var rootModule = Normalize(ModuleManifestValueReader.ReadTopLevelString(manifestPath, "RootModule"));
        var requiredModules = NormalizeRequiredModules(ModuleManifestValueReader.ReadRequiredModules(manifestPath)).ToArray();
        var externalModules = NormalizeStrings(
            ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "ExternalModuleDependencies"),
            StringComparer.OrdinalIgnoreCase);

        var mismatches = new List<string>();
        if (!version.Equals(Version, StringComparison.OrdinalIgnoreCase)) mismatches.Add("ModuleVersion");
        if (!preRelease.Equals(PreRelease, StringComparison.Ordinal)) mismatches.Add("Prerelease");
        if (!rootModule.Equals(expectedRootModule, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"RootModule expected '{expectedRootModule}' but found '{rootModule}'");
        if (!RequiredModulesEqual(RequiredModules, requiredModules)) mismatches.Add("RequiredModules");
        if (!ExternalModuleDependencies.SequenceEqual(externalModules, StringComparer.OrdinalIgnoreCase))
            mismatches.Add("ExternalModuleDependencies");
        if (mismatches.Count > 0)
            throw new InvalidOperationException(
                $"Reusable compiled staging manifest does not match the current module and release plan: {string.Join(", ", mismatches)}.");
    }

    private static IEnumerable<RequiredModuleReference> NormalizeRequiredModules(
        IEnumerable<RequiredModuleReference>? modules)
        => (modules ?? Array.Empty<RequiredModuleReference>())
            .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
            .Select(static module => new RequiredModuleReference(
                Normalize(module.ModuleName),
                NormalizeNullable(module.ModuleVersion),
                NormalizeNullable(module.RequiredVersion),
                NormalizeNullable(module.MaximumVersion),
                NormalizeNullable(module.Guid)))
            .OrderBy(static module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.RequiredVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.ModuleVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.MaximumVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static module => module.Guid, StringComparer.OrdinalIgnoreCase);

    private static bool RequiredModulesEqual(
        IReadOnlyList<RequiredModuleReference> expected,
        IReadOnlyList<RequiredModuleReference> actual)
    {
        if (expected.Count != actual.Count) return false;
        for (var index = 0; index < expected.Count; index++)
        {
            if (!expected[index].ModuleName.Equals(actual[index].ModuleName, StringComparison.OrdinalIgnoreCase) ||
                !Normalize(expected[index].ModuleVersion).Equals(Normalize(actual[index].ModuleVersion), StringComparison.OrdinalIgnoreCase) ||
                !Normalize(expected[index].RequiredVersion).Equals(Normalize(actual[index].RequiredVersion), StringComparison.OrdinalIgnoreCase) ||
                !Normalize(expected[index].MaximumVersion).Equals(Normalize(actual[index].MaximumVersion), StringComparison.OrdinalIgnoreCase) ||
                !Normalize(expected[index].Guid).Equals(Normalize(actual[index].Guid), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static string[] NormalizeStrings(IEnumerable<string>? values, StringComparer comparer)
        => (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().Replace('\\', '/'))
            .Distinct(comparer)
            .OrderBy(static value => value, comparer)
            .ToArray();

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string CreateSourceInputSha256(
        PowerShellCompilationResolvedInput input,
        PowerShellModuleCompilationConfiguration compilation)
    {
        var planner = new PowerShellCompilationDependencyPlanner();
        var dependencies = planner.Analyze(
            input,
            compilation.Mode,
            compilation.ResourceMode,
            compilation.IncludeResource,
            compilation.ExcludeResource);
        var files = input.SourceFiles
            .Concat(dependencies
                .Where(static dependency => dependency.SourcePath is not null && dependency.Exists)
                .Where(static dependency => dependency.Kind != PowerShellCompilationDependencyKind.ModuleManifest)
                .Where(static dependency => dependency.Kind != PowerShellCompilationDependencyKind.RequiredModule)
                .Where(static dependency => dependency.Selection is not PowerShellCompilationDependencySelection.Excluded and
                    not PowerShellCompilationDependencySelection.Unclassified)
                .Select(static dependency => dependency.SourcePath!))
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(input.ModuleRoot, path), StringComparer.OrdinalIgnoreCase)
            .Select(path => new
            {
                path = FrameworkCompatibility.GetRelativePath(input.ModuleRoot, path).Replace('\\', '/'),
                sha256 = ComputeFileSha256(path)
            })
            .ToArray();
        var externalDependencies = dependencies
            .Where(static dependency => dependency.SourcePath is null || !dependency.Exists)
            .Where(static dependency => dependency.Kind != PowerShellCompilationDependencyKind.RequiredModule)
            .OrderBy(static dependency => dependency.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(static dependency => new
            {
                dependency.Name,
                dependency.RelativePath,
                dependency.Kind,
                dependency.Discovery,
                dependency.Disposition,
                dependency.Selection,
                dependency.Exists
            })
            .ToArray();
        var manifestSemantics = input.ModuleManifestPath is null
            ? null
            : new
            {
                functionsToExport = ReadManifestArray(input.ModuleManifestPath, "FunctionsToExport"),
                cmdletsToExport = ReadManifestArray(input.ModuleManifestPath, "CmdletsToExport"),
                aliasesToExport = ReadManifestArray(input.ModuleManifestPath, "AliasesToExport"),
                variablesToExport = ReadManifestArray(input.ModuleManifestPath, "VariablesToExport"),
                scriptsToProcess = ReadManifestArray(input.ModuleManifestPath, "ScriptsToProcess"),
                nestedModules = ReadManifestArray(input.ModuleManifestPath, "NestedModules"),
                requiredAssemblies = ReadManifestArray(input.ModuleManifestPath, "RequiredAssemblies"),
                typesToProcess = ReadManifestArray(input.ModuleManifestPath, "TypesToProcess"),
                formatsToProcess = ReadManifestArray(input.ModuleManifestPath, "FormatsToProcess"),
                fileList = ReadManifestArray(input.ModuleManifestPath, "FileList")
            };
        return ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            files,
            externalDependencies,
            manifestSemantics
        })));
    }

    private static string CreateSourceTreeSha256(ModuleBuildSpec build)
    {
        var root = Path.GetFullPath(build.SourcePath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"PowerShell compilation source directory was not found: {root}");
        EnsureNotReparsePoint(root);
        var excludedDirectories = new HashSet<string>(
            NormalizeStrings(build.ExcludeDirectories, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var excludedFiles = new HashSet<string>(
            NormalizeStrings(build.ExcludeFiles, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var files = new List<object>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly))
            {
                EnsureNotReparsePoint(entry);
                if (Directory.Exists(entry))
                {
                    if (!excludedDirectories.Contains(Path.GetFileName(entry))) pending.Push(entry);
                    continue;
                }
                if (excludedFiles.Contains(Path.GetFileName(entry))) continue;
                files.Add(new
                {
                    path = FrameworkCompatibility.GetRelativePath(root, entry).Replace('\\', '/'),
                    sizeBytes = new FileInfo(entry).Length,
                    sha256 = ComputeFileSha256(entry)
                });
            }
        }
        return ComputeSha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            files.OrderBy(file => JsonSerializer.Serialize(file), StringComparer.Ordinal).ToArray())));
    }

    private static object CreateActionIdentity(ConfigurationActionSegment action, string projectRoot)
    {
        var configuration = action?.Configuration ?? new ModulePipelineActionConfiguration();
        return new
        {
            configuration.Enabled,
            configuration.Name,
            configuration.At,
            configuration.FilePath,
            fileIdentity = CreateOptionalFileIdentity(ResolveOptionalPath(projectRoot, configuration.FilePath)),
            inlineSha256 = ComputeSha256(Encoding.UTF8.GetBytes(configuration.InlineScript ?? string.Empty)),
            configuration.WorkingDirectory,
            environment = (configuration.Environment ?? new Dictionary<string, string?>())
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new
                {
                    pair.Key,
                    valueSha256 = ComputeSha256(Encoding.UTF8.GetBytes(pair.Value ?? string.Empty))
                }).ToArray(),
            configuration.TimeoutSeconds,
            configuration.ContinueOnError,
            configuration.PreferWindowsPowerShell
        };
    }

    private static object? CreateOptionalFileIdentity(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fullPath = Path.GetFullPath(path);
        return new
        {
            path = fullPath.Replace('\\', '/'),
            exists = File.Exists(fullPath),
            sha256 = File.Exists(fullPath) ? ComputeFileSha256(fullPath) : string.Empty
        };
    }

    private static string? ResolveOptionalPath(string root, string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"PowerShell compilation release inputs do not permit symbolic links or junctions: '{path}'.");
    }

    private static string CreatePfxIdentitySha256(SigningOptionsConfiguration signing)
    {
        if (!string.IsNullOrWhiteSpace(signing.CertificatePFXBase64))
        {
            try
            {
                return ComputeSha256(Convert.FromBase64String(signing.CertificatePFXBase64));
            }
            catch (FormatException)
            {
                return ComputeSha256(Encoding.UTF8.GetBytes(signing.CertificatePFXBase64));
            }
        }

        var certificatePfxPath = signing.CertificatePFXPath ?? string.Empty;
        return !string.IsNullOrWhiteSpace(certificatePfxPath) && File.Exists(certificatePfxPath)
            ? ComputeFileSha256(certificatePfxPath)
            : string.Empty;
    }

    private static string[] ReadManifestArray(string manifestPath, string key)
        => NormalizeStrings(
            ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(manifestPath, key),
            StringComparer.OrdinalIgnoreCase);

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(stream)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
