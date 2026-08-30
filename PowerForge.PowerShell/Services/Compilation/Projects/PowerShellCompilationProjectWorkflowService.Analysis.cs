using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Runs the coherent project workflow through the canonical compiler owners.</summary>
public sealed partial class PowerShellCompilationProjectWorkflowService
{
    /// <summary>Analyzes every selected target and writes portable evidence.</summary>
    public PowerShellCompilationProjectResult Analyze(string projectPath, IEnumerable<string>? targetNames = null)
        => Inspect(projectPath, targetNames, explain: false);

    /// <summary>Shapes final target-aware decisions without building artifacts.</summary>
    public PowerShellCompilationProjectResult Explain(string projectPath, IEnumerable<string>? targetNames = null)
        => Inspect(projectPath, targetNames, explain: true);

    /// <summary>Produces opt-in measured profile advice without changing project source or target selection.</summary>
    public PowerShellCompilationProjectResult Recommend(
        string projectPath,
        string? boundaryProfilePath = null,
        IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var providers = ResolveProviders(context);
        var profile = string.IsNullOrWhiteSpace(boundaryProfilePath)
            ? null
            : ReadJson<PowerShellCompilationBoundaryRuntimeProfile>(Path.GetFullPath(boundaryProfilePath!.Trim().Trim('"')));
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var input = ResolveInput(context, artifact);
                var plan = CreatePlan(context, artifact, input, providers.Providers);
                var recommendation = new PowerShellCompilationProfileRecommendationService().Create(plan, artifact.Target, profile);
                var evidencePath = context.Resolve($".powerforge/recommend/{artifact.Name}.json");
                WriteJson(evidencePath, recommendation);
                results.Add(Pass(artifact, $"Opt-in profile advice: {recommendation.Action}. The project was not modified.", evidencePath, plan.DependencyGraph?.LockSha256));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete("recommend", context.ProjectPath, results);
    }

    /// <summary>Writes separately reviewable dependency and provider locks for every selected target.</summary>
    public PowerShellCompilationProjectResult Lock(string projectPath, IEnumerable<string>? targetNames = null)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var providers = ResolveProviders(context);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var input = ResolveInput(context, artifact);
                var plan = CreatePlan(context, artifact, input, providers.Providers);
                if (!plan.CanProceed)
                    throw new InvalidOperationException("The target cannot be locked because analysis rejected or could not fully shape the selected input.");
                var lockPath = context.Resolve(artifact.DependencyLock);
                WriteJson(lockPath, plan.DependencyGraph ?? throw new InvalidOperationException("Project analysis did not produce a dependency graph."));
                if (context.Manifest.ProviderPackages.Length > 0)
                {
                    if (string.IsNullOrWhiteSpace(artifact.ProviderLock))
                        throw new InvalidDataException($"Project target '{artifact.Name}' must declare providerLock when provider packages are selected.");
                    WriteJson(context.Resolve(artifact.ProviderLock!), providers.Lock);
                }
                results.Add(Pass(artifact, "Dependency and provider identities were resolved without executing source.", lockPath, plan.DependencyGraph!.LockSha256));
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete("lock", context.ProjectPath, results);
    }

    private static PowerShellCompilationProjectResult Inspect(
        string projectPath,
        IEnumerable<string>? targetNames,
        bool explain)
    {
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var providers = ResolveProviders(context);
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            try
            {
                var input = ResolveInput(context, artifact);
                var plan = CreatePlan(context, artifact, input, providers.Providers);
                object evidence = explain
                    ? PowerShellCompilationExplainShaper.CreateFinalExplanation(input, plan, artifact.Target.TargetFramework)
                    : plan;
                var evidencePath = context.Resolve($".powerforge/{(explain ? "explain" : "analysis")}/{artifact.Name}.json");
                WriteJson(evidencePath, evidence);
                results.Add(plan.CanProceed
                    ? Pass(artifact, explain ? "Final target-aware decisions were shaped." : "Analysis can proceed.", evidencePath, plan.DependencyGraph?.LockSha256)
                    : new PowerShellCompilationProjectTargetResult
                    {
                        Name = artifact.Name,
                        TargetContractSha256 = artifact.Target.ContractSha256,
                        Succeeded = false,
                        Message = "Analysis retained rejection, fallback, or missing-dependency causes; inspect the evidence.",
                        Path = evidencePath,
                        DependencyLockSha256 = plan.DependencyGraph?.LockSha256
                    });
            }
            catch (Exception exception)
            {
                results.Add(Fail(artifact, exception));
            }
        }
        return Complete(explain ? "explain" : "analyze", context.ProjectPath, results);
    }

    private static PowerShellCompilationPlan CreatePlan(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact,
        PowerShellCompilationResolvedInput input,
        IEnumerable<PowerShellCompilationCommandProviderContract> providers,
        string? nuGetPackageRoot = null)
    {
        return new PowerShellCompilationAnalyzer(providers, context.Manifest.SemanticProfileId).Analyze(
            input,
            artifact.Target.Mode,
            artifact.Target.TargetFramework,
            context.Manifest.Resources.Mode,
            context.Manifest.Resources.Include,
            context.Manifest.Resources.Exclude,
            context.Resolve(artifact.OutputDirectory),
            artifact.Target,
            GetGeneratedOutputDirectories(context),
            nuGetPackageRoot);
    }

    private static string[] GetGeneratedOutputDirectories(
        PowerShellCompilationProjectManifestService.ProjectContext context)
        => new[] { context.Resolve(".powerforge") }
            .Concat(context.Manifest.Artifacts.Select(artifact => context.Resolve(artifact.OutputDirectory)))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();

    private static PowerShellCompilationResolvedInput ResolveInput(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact)
        => new PowerShellCompilationInputResolver().Resolve(
            context.Sources,
            artifact.Target.ArtifactKind,
            artifact.Target.Mode,
            context.EntryPoint,
            allowDynamicModuleRuntimeSources: context.Manifest.Resources.Mode == PowerShellCompilationResourceMode.CompleteModule &&
                                              artifact.Target.Mode != PowerShellCompilationMode.Strict);

    private static PowerShellCompilationProviderResolution ResolveProviders(
        PowerShellCompilationProjectManifestService.ProjectContext context)
        => new PowerShellCompilationProviderPackageReader().Resolve(
            context.Manifest.ProviderPackages.Select(path => new PowerShellCompilationProviderPackageReference(context.Resolve(path))),
            context.Manifest.ProviderTrust,
            context.Manifest.SemanticProfileId);

    private static PowerShellCompilationProjectArtifact[] SelectArtifacts(
        PowerShellCompilationProjectManifestService.ProjectContext context,
        IEnumerable<string>? targetNames)
    {
        var selected = (targetNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var artifacts = context.Manifest.Artifacts
            .Where(artifact => selected.Count == 0 || selected.Contains(artifact.Name))
            .OrderBy(static artifact => artifact.Name, StringComparer.Ordinal)
            .ToArray();
        if (artifacts.Length == 0) throw new ArgumentException("No project targets matched the selection.", nameof(targetNames));
        if (selected.Count > artifacts.Length)
        {
            var missing = selected.Where(name => artifacts.All(artifact => !artifact.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            throw new ArgumentException("Unknown project target(s): " + string.Join(", ", missing), nameof(targetNames));
        }
        return artifacts;
    }

    private static void WriteJson<T>(string path, T value)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(path, JsonSerializer.Serialize(value, PowerShellCompilationProjectManifestService.JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static T ReadJson<T>(string path)
        => JsonSerializer.Deserialize<T>(File.ReadAllText(path), PowerShellCompilationProjectManifestService.JsonOptions)
           ?? throw new InvalidDataException($"Project evidence '{path}' is empty.");

    private static PowerShellCompilationProjectTargetResult Pass(
        PowerShellCompilationProjectArtifact artifact,
        string message,
        string? path = null,
        string? dependencyLock = null,
        string? artifactSha256 = null)
        => new()
        {
            Name = artifact.Name,
            TargetContractSha256 = artifact.Target.ContractSha256,
            Succeeded = true,
            Message = message,
            Path = path,
            DependencyLockSha256 = dependencyLock,
            ArtifactSha256 = artifactSha256
        };

    private static PowerShellCompilationProjectTargetResult Fail(PowerShellCompilationProjectArtifact artifact, Exception exception)
        => new()
        {
            Name = artifact.Name,
            TargetContractSha256 = artifact.Target.ContractSha256,
            Succeeded = false,
            Message = exception.Message
        };

    private static PowerShellCompilationProjectResult Complete(
        string operation,
        string projectPath,
        IEnumerable<PowerShellCompilationProjectTargetResult> results)
    {
        var array = results.ToArray();
        return new PowerShellCompilationProjectResult
        {
            Operation = operation,
            ProjectPath = projectPath,
            Succeeded = array.Length > 0 && array.All(static result => result.Succeeded),
            Targets = array
        };
    }
}
