using System;
using System.IO;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Plans and executes a configuration-driven module build workflow using <see cref="ModuleBuildPipeline"/>.
/// </summary>
/// <remarks>
/// <para>
/// The runner works in two phases:
/// </para>
/// <list type="number">
/// <item><description><see cref="Plan"/> merges the base build settings with configuration segments (last-wins) into a deterministic plan.</description></item>
/// <item><description><see cref="Run(ModulePipelineSpec)"/> executes the plan step-by-step and returns a structured result.</description></item>
/// </list>
/// <para>
/// This split enables "plan-only" scenarios such as generating a JSON configuration without performing the build.
/// </para>
/// </remarks>
/// <example>
/// <summary>Plan and execute a build</summary>
/// <code>
/// var logger = new ConsoleLogger { IsVerbose = true };
/// var runner = new ModulePipelineRunner(logger);
/// var json = File.ReadAllText(@"C:\Git\MyModule\powerforge.json");
/// var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
/// options.Converters.Add(new ConfigurationSegmentJsonConverter());
/// var spec = JsonSerializer.Deserialize&lt;ModulePipelineSpec&gt;(json, options)!;
/// var result = runner.Run(spec);
/// </code>
/// </example>
public sealed partial class ModulePipelineRunner
{
    private readonly ILogger _logger;
    private readonly IPowerShellRunner _powerShellRunner;
    private readonly IModuleDependencyMetadataProvider _moduleDependencyMetadataProvider;
    private readonly IModulePipelineHostedOperations _hostedOperations;
    private readonly IModuleManifestMutator _manifestMutator;
    private readonly IMissingFunctionAnalysisService _missingFunctionAnalysisService;
    private readonly IScriptFunctionExportDetector _scriptFunctionExportDetector;

    private sealed class RequiredModuleDraft
    {
        public string ModuleName { get; }
        public string? ModuleVersion { get; }
        public string? MinimumVersion { get; }
        public string? RequiredVersion { get; }
        public string? Guid { get; }

        public RequiredModuleDraft(string moduleName, string? moduleVersion, string? minimumVersion, string? requiredVersion, string? guid)
        {
            ModuleName = moduleName;
            ModuleVersion = moduleVersion;
            MinimumVersion = minimumVersion;
            RequiredVersion = requiredVersion;
            Guid = guid;
        }
    }

    /// <summary>
    /// Creates a new instance using the provided logger.
    /// </summary>
    public ModulePipelineRunner(ILogger logger, IPowerShellRunner? powerShellRunner = null)
        : this(logger, ModulePipelineRunnerDefaults.Create(logger, powerShellRunner, moduleDependencyMetadataProvider: null, hostedOperations: null, manifestMutator: null, missingFunctionAnalysisService: null, scriptFunctionExportDetector: null))
    {
    }

    internal ModulePipelineRunner(
        ILogger logger,
        IPowerShellRunner? powerShellRunner,
        IModuleDependencyMetadataProvider? moduleDependencyMetadataProvider,
        IModulePipelineHostedOperations? hostedOperations = null,
        IModuleManifestMutator? manifestMutator = null,
        IMissingFunctionAnalysisService? missingFunctionAnalysisService = null,
        IScriptFunctionExportDetector? scriptFunctionExportDetector = null)
        : this(logger, ModulePipelineRunnerDefaults.Create(logger, powerShellRunner, moduleDependencyMetadataProvider, hostedOperations, manifestMutator, missingFunctionAnalysisService, scriptFunctionExportDetector))
    {
    }

    private ModulePipelineRunner(ILogger logger, ModulePipelineRunnerServices services)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        _powerShellRunner = services.PowerShellRunner;
        _moduleDependencyMetadataProvider = services.ModuleDependencyMetadataProvider;
        _hostedOperations = services.HostedOperations;
        _manifestMutator = services.ManifestMutator;
        _missingFunctionAnalysisService = services.MissingFunctionAnalysisService;
        _scriptFunctionExportDetector = services.ScriptFunctionExportDetector;
    }

}
