using System.Text;

namespace PowerForge;

internal static class PowerForgeProjectDslMapper
{
    internal static (PowerForgeReleaseSpec Spec, PowerForgeReleaseRequest Request) CreateRelease(ConfigurationProject project, string configPath, string projectRoot)
    {
        if (project is null)
            throw new ArgumentNullException(nameof(project));
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path is required.", nameof(configPath));
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Project root is required.", nameof(projectRoot));

        var release = project.Release ?? new ConfigurationProjectRelease();
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        var targets = (project.Targets ?? Array.Empty<ConfigurationProjectTarget>())
            .Where(target => target is not null)
            .ToArray();
        if (targets.Length == 0)
            throw new InvalidOperationException("At least one project target is required.");

        var installers = (project.Installers ?? Array.Empty<ConfigurationProjectInstaller>())
            .Where(installer => installer is not null)
            .ToArray();

        var signing = project.Signing;
        var bundleTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (IncludesPortableOutput(release, target))
                bundleTargets.Add(target.Name);
        }

        foreach (var installer in installers.Where(entry => entry.PrepareFromPortableBundle))
            bundleTargets.Add(installer.Target);

        var bundles = bundleTargets
            .Select(targetName => CreateBundle(targetName, IncludesPortableOutput(release, targets, targetName)))
            .ToArray();
        var dotNetTargets = targets
            .Select(target => MapTarget(target, signing, fullProjectRoot))
            .ToArray();

        var spec = new PowerForgeReleaseSpec
        {
            Tools = new PowerForgeToolReleaseSpec
            {
                ProjectRoot = fullProjectRoot,
                Configuration = string.IsNullOrWhiteSpace(release.Configuration) ? "Release" : release.Configuration.Trim(),
                DotNetPublish = new DotNetPublishSpec
                {
                    DotNet = new DotNetPublishDotNetOptions
                    {
                        ProjectRoot = fullProjectRoot,
                        Configuration = string.IsNullOrWhiteSpace(release.Configuration) ? "Release" : release.Configuration.Trim()
                    },
                    Targets = dotNetTargets,
                    Bundles = bundles,
                    Installers = installers.Select(installer => MapInstaller(installer, bundles, signing, fullProjectRoot)).ToArray()
                }
            }
        };

        if (project.Workspace is not null)
        {
            spec.WorkspaceValidation = new PowerForgeWorkspaceValidationOptions
            {
                ConfigPath = ResolveOptionalPath(fullProjectRoot, project.Workspace.ConfigPath),
                Profile = NormalizeNullable(project.Workspace.Profile),
                EnableFeatures = NormalizeStrings(project.Workspace.EnableFeatures),
                DisableFeatures = NormalizeStrings(project.Workspace.DisableFeatures)
            };
        }

        var request = new PowerForgeReleaseRequest
        {
            ConfigPath = configPath,
            ToolsOnly = true,
            Configuration = string.IsNullOrWhiteSpace(release.Configuration) ? null : release.Configuration.Trim(),
            PublishToolGitHub = release.PublishToolGitHub,
            SkipRestore = release.SkipRestore,
            SkipBuild = release.SkipBuild,
            SkipWorkspaceValidation = project.Workspace?.SkipValidation == true,
            WorkspaceConfigPath = ResolveOptionalPath(fullProjectRoot, project.Workspace?.ConfigPath),
            WorkspaceProfile = NormalizeNullable(project.Workspace?.Profile),
            WorkspaceEnableFeatures = NormalizeStrings(project.Workspace?.EnableFeatures),
            WorkspaceDisableFeatures = NormalizeStrings(project.Workspace?.DisableFeatures),
            OutputRoot = NormalizeNullable(project.Output?.OutputRoot),
            StageRoot = NormalizeNullable(project.Output?.StageRoot),
            ManifestJsonPath = NormalizeNullable(project.Output?.ManifestJsonPath),
            ChecksumsPath = NormalizeNullable(project.Output?.ChecksumsPath),
            SkipReleaseChecksums = project.Output is not null && !project.Output.IncludeChecksums,
            ToolOutputs = release.ToolOutput.Length > 0
                ? MapReleaseOutputs(release.ToolOutput)
                : ComputeRequestedOutputs(targets, installers),
            SkipToolOutputs = MapReleaseOutputs(release.SkipToolOutput)
        };

        return (spec, request);
    }

    private static DotNetPublishTarget MapTarget(ConfigurationProjectTarget target, ConfigurationProjectSigning? signing, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(target.Name))
            throw new InvalidOperationException("Project targets must have a name.");
        if (string.IsNullOrWhiteSpace(target.ProjectPath))
            throw new InvalidOperationException($"Project target '{target.Name}' must define ProjectPath.");

        var frameworks = NormalizeStrings(target.Frameworks);
        var framework = !string.IsNullOrWhiteSpace(target.Framework)
            ? target.Framework.Trim()
            : frameworks.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(framework))
            throw new InvalidOperationException($"Project target '{target.Name}' must define Framework or Frameworks.");

        var styles = target.Styles?.Distinct().ToArray() ?? Array.Empty<DotNetPublishStyle>();

        return new DotNetPublishTarget
        {
            Name = target.Name.Trim(),
            ProjectPath = ResolveRequiredPath(projectRoot, target.ProjectPath),
            Kind = target.Kind,
            Publish = new DotNetPublishPublishOptions
            {
                Framework = framework,
                Frameworks = frameworks,
                Runtimes = NormalizeStrings(target.Runtimes),
                Style = target.Style,
                Styles = styles,
                OutputPath = NormalizeNullable(target.OutputPath),
                UseStaging = target.UseStaging,
                ClearOutput = target.ClearOutput,
                KeepSymbols = target.KeepSymbols,
                KeepDocs = target.KeepDocs,
                Zip = target.Zip,
                ReadyToRun = target.ReadyToRun,
                Sign = BuildSignOptions(signing)
            }
        };
    }

    private static DotNetPublishBundle CreateBundle(string targetName, bool includePortableOutput)
    {
        var bundleId = CreatePortableBundleId(targetName);
        return new DotNetPublishBundle
        {
            Id = bundleId,
            PrepareFromTarget = targetName.Trim(),
            Zip = includePortableOutput
        };
    }

    private static DotNetPublishInstaller MapInstaller(
        ConfigurationProjectInstaller installer,
        IReadOnlyList<DotNetPublishBundle> bundles,
        ConfigurationProjectSigning? signing,
        string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(installer.Id))
            throw new InvalidOperationException("Project installers must have an Id.");
        if (string.IsNullOrWhiteSpace(installer.Target))
            throw new InvalidOperationException($"Installer '{installer.Id}' must define Target.");
        if (string.IsNullOrWhiteSpace(installer.InstallerProjectPath))
            throw new InvalidOperationException($"Installer '{installer.Id}' must define InstallerProjectPath.");

        var mapped = new DotNetPublishInstaller
        {
            Id = installer.Id.Trim(),
            PrepareFromTarget = installer.Target.Trim(),
            InstallerProjectPath = ResolveRequiredPath(projectRoot, installer.InstallerProjectPath),
            Harvest = installer.Harvest,
            HarvestDirectoryRefId = NormalizeNullable(installer.HarvestDirectoryRefId),
            Runtimes = NormalizeStrings(installer.Runtimes),
            Frameworks = NormalizeStrings(installer.Frameworks),
            Styles = installer.Styles?.Distinct().ToArray() ?? Array.Empty<DotNetPublishStyle>(),
            MsBuildProperties = CloneDictionary(installer.MsBuildProperties),
            Sign = BuildSignOptions(signing)
        };

        if (installer.PrepareFromPortableBundle)
        {
            var bundleId = CreatePortableBundleId(installer.Target);
            if (!bundles.Any(bundle => string.Equals(bundle.Id, bundleId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Installer '{installer.Id}' requested bundle preparation but no bundle was generated for target '{installer.Target}'.");
            mapped.PrepareFromBundleId = bundleId;
        }

        return mapped;
    }

    private static DotNetPublishSignOptions? BuildSignOptions(ConfigurationProjectSigning? signing)
    {
        if (signing is null || signing.Mode == ConfigurationProjectSigningMode.Disabled)
            return null;

        return new DotNetPublishSignOptions
        {
            Enabled = signing.Mode == ConfigurationProjectSigningMode.Enabled,
            ToolPath = NormalizeNullable(signing.ToolPath) ?? "signtool.exe",
            Thumbprint = NormalizeNullable(signing.Thumbprint),
            SubjectName = NormalizeNullable(signing.SubjectName),
            OnMissingTool = signing.OnMissingTool,
            OnSignFailure = signing.OnFailure,
            TimestampUrl = NormalizeNullable(signing.TimestampUrl),
            Description = NormalizeNullable(signing.Description),
            Url = NormalizeNullable(signing.Url),
            Csp = NormalizeNullable(signing.Csp),
            KeyContainer = NormalizeNullable(signing.KeyContainer)
        };
    }

    private static PowerForgeReleaseToolOutputKind[] ComputeRequestedOutputs(
        IReadOnlyList<ConfigurationProjectTarget> targets,
        IReadOnlyList<ConfigurationProjectInstaller> installers)
    {
        var outputs = new HashSet<PowerForgeReleaseToolOutputKind>();
        foreach (var target in targets)
        {
            if (IncludesOutput(target, ConfigurationProjectTargetOutputType.Tool))
                outputs.Add(PowerForgeReleaseToolOutputKind.Tool);
            if (IncludesOutput(target, ConfigurationProjectTargetOutputType.Portable))
                outputs.Add(PowerForgeReleaseToolOutputKind.Portable);
        }

        if (installers.Count > 0)
            outputs.Add(PowerForgeReleaseToolOutputKind.Installer);

        if (outputs.Count == 0)
            outputs.Add(PowerForgeReleaseToolOutputKind.Tool);

        return outputs.ToArray();
    }

    private static PowerForgeReleaseToolOutputKind[] MapReleaseOutputs(ConfigurationProjectReleaseOutputType[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<PowerForgeReleaseToolOutputKind>();

        return values
            .Distinct()
            .Select(value => value switch
            {
                ConfigurationProjectReleaseOutputType.Tool => PowerForgeReleaseToolOutputKind.Tool,
                ConfigurationProjectReleaseOutputType.Portable => PowerForgeReleaseToolOutputKind.Portable,
                ConfigurationProjectReleaseOutputType.Installer => PowerForgeReleaseToolOutputKind.Installer,
                ConfigurationProjectReleaseOutputType.Store => PowerForgeReleaseToolOutputKind.Store,
                _ => throw new InvalidOperationException($"Unsupported release output type '{value}'.")
            })
            .ToArray();
    }

    private static bool IncludesPortableOutput(
        ConfigurationProjectRelease release,
        IReadOnlyList<ConfigurationProjectTarget> targets,
        string targetName)
    {
        return targets.Any(target =>
            string.Equals(target.Name, targetName, StringComparison.OrdinalIgnoreCase)
            && IncludesPortableOutput(release, target));
    }

    private static bool IncludesPortableOutput(ConfigurationProjectRelease release, ConfigurationProjectTarget target)
    {
        if (release.ToolOutput.Length > 0)
            return release.ToolOutput.Contains(ConfigurationProjectReleaseOutputType.Portable);

        return IncludesOutput(target, ConfigurationProjectTargetOutputType.Portable);
    }

    private static bool IncludesOutput(ConfigurationProjectTarget target, ConfigurationProjectTargetOutputType outputType)
    {
        return (target.OutputType ?? Array.Empty<ConfigurationProjectTargetOutputType>()).Contains(outputType);
    }

    private static string CreatePortableBundleId(string targetName)
    {
        var value = string.IsNullOrWhiteSpace(targetName) ? "portable" : targetName.Trim();
        var builder = new StringBuilder(value.Length);
        var previousDash = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
                continue;
            }

            if (previousDash)
                continue;

            builder.Append('-');
            previousDash = true;
        }

        var normalized = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "portable";

        return $"portable-{normalized}";
    }

    private static string[] NormalizeStrings(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<string>();

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
            return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string ResolveRequiredPath(string projectRoot, string value)
    {
        var normalized = NormalizeNullable(value)
            ?? throw new InvalidOperationException("A required path value was empty.");

        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(projectRoot, normalized));
    }

    private static string? ResolveOptionalPath(string projectRoot, string? value)
    {
        var normalized = NormalizeNullable(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(projectRoot, normalized));
    }

    private static Dictionary<string, string>? CloneDictionary(Dictionary<string, string>? value)
    {
        if (value is null || value.Count == 0)
            return null;

        return new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
    }
}
