using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Packs plugin-related NuGet packages from a reusable PowerForge plugin catalog configuration.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet is the PowerShell entry point for the same generic plugin package workflow used by
/// <c>powerforge plugin pack</c>. It selects package groups from the shared catalog, runs
/// <c>dotnet pack</c>, and can optionally push only the packages produced by the current run.
/// </para>
/// </remarks>
/// <example>
/// <summary>Plan package output without running dotnet pack</summary>
/// <code>Invoke-PowerForgePluginPack -Plan</code>
/// </example>
/// <example>
/// <summary>Pack and push public plugin packages</summary>
/// <code>Invoke-PowerForgePluginPack -ConfigPath '.\Build\powerforge.plugins.json' -Group pack-public -Push -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY -ExitCode</code>
/// </example>
[Cmdlet(VerbsLifecycle.Invoke, "PowerForgePluginPack", SupportsShouldProcess = true)]
public sealed class InvokePowerForgePluginPackCommand : PSCmdlet
{
    /// <summary>
    /// Path to the plugin catalog configuration file. When omitted, the cmdlet searches the current
    /// directory and its parents for standard PowerForge plugin config names.
    /// </summary>
    [Parameter]
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Optional project root override for resolving plugin project paths.
    /// </summary>
    [Parameter]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Optional package group filter. When omitted, all catalog entries are selected.
    /// </summary>
    [Parameter]
    [Alias("Groups")]
    public string[]? Group { get; set; }

    /// <summary>
    /// Optional configuration override.
    /// </summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string? Configuration { get; set; }

    /// <summary>
    /// Optional output root override for packed NuGet packages.
    /// </summary>
    [Parameter]
    public string? OutputRoot { get; set; }

    /// <summary>
    /// Skips the build step when running <c>dotnet pack</c>.
    /// </summary>
    [Parameter]
    public SwitchParameter NoBuild { get; set; }

    /// <summary>
    /// Includes symbol packages in the pack output.
    /// </summary>
    [Parameter]
    public SwitchParameter IncludeSymbols { get; set; }

    /// <summary>
    /// Optional package version override.
    /// </summary>
    [Parameter]
    public string? PackageVersion { get; set; }

    /// <summary>
    /// Optional package version suffix override.
    /// </summary>
    [Parameter]
    public string? VersionSuffix { get; set; }

    /// <summary>
    /// Pushes packages after a successful pack run.
    /// </summary>
    [Parameter]
    public SwitchParameter Push { get; set; }

    /// <summary>
    /// Package source URL or name used with <see cref="Push"/>.
    /// </summary>
    [Parameter]
    public string? Source { get; set; }

    /// <summary>
    /// API key used with <see cref="Push"/>.
    /// </summary>
    [Parameter]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Builds and emits the resolved package plan without executing <c>dotnet pack</c>.
    /// </summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>
    /// Sets host exit code: 0 on success, 1 on failure.
    /// </summary>
    [Parameter]
    public SwitchParameter ExitCode { get; set; }

    /// <summary>
    /// Plans or executes plugin package creation based on the provided parameters.
    /// </summary>
    protected override void ProcessRecord()
    {
        var boundParameters = MyInvocation?.BoundParameters;
        var isVerbose = boundParameters?.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);
        var exitCodeMode = ExitCode.IsPresent;

        try
        {
            var fullConfigPath = ResolveConfigPath(ConfigPath);
            var spec = LoadConfig(fullConfigPath);
            if (!string.IsNullOrWhiteSpace(ProjectRoot))
                spec.ProjectRoot = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectRoot);

            var request = new PowerForgePluginPackageRequest
            {
                Groups = NormalizeStrings(Group),
                Configuration = NormalizeNullable(Configuration),
                OutputRoot = NormalizeNullable(OutputRoot),
                NoBuild = NoBuild.IsPresent,
                IncludeSymbols = IncludeSymbols.IsPresent,
                PackageVersion = NormalizeNullable(PackageVersion),
                VersionSuffix = NormalizeNullable(VersionSuffix),
                PushPackages = Push.IsPresent,
                PushSource = NormalizeNullable(Source),
                ApiKey = NormalizeNullable(ApiKey),
                SkipDuplicate = true
            };

            var service = new PowerForgePluginCatalogService(logger);
            var plan = service.PlanPackages(spec, fullConfigPath, request);
            if (Plan.IsPresent)
            {
                WriteObject(plan);
                if (exitCodeMode)
                    Host.SetShouldExit(0);
                return;
            }

            var target = string.IsNullOrWhiteSpace(plan.OutputRoot) ? fullConfigPath : plan.OutputRoot;
            if (!ShouldProcess(target, "Pack plugin packages"))
                return;

            var result = service.PackPackages(plan, request.ApiKey);
            WriteObject(result);
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "Plugin pack failed.");

            if (exitCodeMode)
                Host.SetShouldExit(0);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokePowerForgePluginPackFailed", ErrorCategory.NotSpecified, ConfigPath));
            if (exitCodeMode)
                Host.SetShouldExit(1);
        }
    }

    private string ResolveConfigPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            return SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);

        var currentDirectory = SessionState?.Path?.CurrentFileSystemLocation?.Path ?? Environment.CurrentDirectory;
        var found = FindDefaultConfigPath(currentDirectory);
        if (!string.IsNullOrWhiteSpace(found))
            return found!;

        throw new PSArgumentException(
            "ConfigPath is required when no default plugin config could be found. Searched: powerforge.plugins.json, .powerforge/plugins.json, Build/plugins.json, plugins.json.");
    }

    private static string? FindDefaultConfigPath(string baseDirectory)
    {
        var candidates = new[]
        {
            "powerforge.plugins.json",
            Path.Combine(".powerforge", "plugins.json"),
            Path.Combine("Build", "plugins.json"),
            "plugins.json"
        };

        foreach (var directory in EnumerateSelfAndParents(baseDirectory))
        {
            foreach (var relativePath in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch (IOException)
                {
                    // best effort only
                }
                catch (UnauthorizedAccessException)
                {
                    // best effort only
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string startDirectory)
    {
        var current = string.IsNullOrWhiteSpace(startDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(startDirectory);

        while (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
            var parent = Directory.GetParent(current);
            if (parent is null)
                yield break;

            current = parent.FullName;
        }
    }

    private static PowerForgePluginCatalogSpec LoadConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<PowerForgePluginCatalogSpec>(json, options);
        if (spec is null)
            throw new InvalidOperationException($"Unable to deserialize plugin catalog config: {configPath}");

        return spec;
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
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
