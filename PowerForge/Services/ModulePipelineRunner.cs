using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
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
    public ModulePipelineRunner(ILogger logger) => _logger = logger;

}
