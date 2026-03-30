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
/// Applies reusable bundle post-process rules from a dotnet publish config to an existing bundle directory.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "PowerForgeBundlePostProcess", SupportsShouldProcess = true)]
public sealed class InvokePowerForgeBundlePostProcessCommand : PSCmdlet
{
    /// <summary>
    /// Path to the dotnet publish configuration file. When omitted, the cmdlet searches the current
    /// directory and its parents for standard PowerForge dotnet publish config names.
    /// </summary>
    [Parameter]
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Bundle identifier whose post-process rules should be applied.
    /// </summary>
    [Parameter(Mandatory = true)]
    [Alias("Bundle")]
    public string BundleId { get; set; } = string.Empty;

    /// <summary>
    /// Existing bundle directory that will be post-processed.
    /// </summary>
    [Parameter(Mandatory = true)]
    public string BundleRoot { get; set; } = string.Empty;

    /// <summary>
    /// Optional project root override used for path-safety checks.
    /// </summary>
    [Parameter]
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Optional target name used for metadata and template tokens.
    /// </summary>
    [Parameter]
    [Alias("Target")]
    public string? TargetName { get; set; }

    /// <summary>
    /// Optional runtime identifier used for metadata and template tokens.
    /// </summary>
    [Parameter]
    [Alias("Rid", "Runtime")]
    public string? Runtime { get; set; }

    /// <summary>
    /// Optional framework used for metadata and template tokens.
    /// </summary>
    [Parameter]
    [Alias("Framework")]
    public string? Framework { get; set; }

    /// <summary>
    /// Optional publish style label used for metadata and template tokens.
    /// </summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>
    /// Optional build configuration label.
    /// </summary>
    [Parameter]
    [ValidateSet("Release", "Debug")]
    public string? Configuration { get; set; }

    /// <summary>
    /// Optional zip path token value.
    /// </summary>
    [Parameter]
    public string? ZipPath { get; set; }

    /// <summary>
    /// Optional source output token value.
    /// </summary>
    [Parameter]
    public string? SourceOutputPath { get; set; }

    /// <summary>
    /// Optional additional template tokens in <c>name=value</c> form.
    /// </summary>
    [Parameter]
    [Alias("Tokens")]
    public string[]? Token { get; set; }

    /// <summary>
    /// Optional additional delete patterns appended to config-defined post-process rules.
    /// </summary>
    [Parameter]
    public string[]? DeletePattern { get; set; }

    /// <summary>
    /// Skips archive-directory rules from the config.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipArchive { get; set; }

    /// <summary>
    /// Skips metadata emission from the config.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipMetadata { get; set; }

    /// <summary>
    /// Emits the resolved request without applying the post-process rules.
    /// </summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>
    /// Sets host exit code: 0 on success, 1 on failure.
    /// </summary>
    [Parameter]
    public SwitchParameter ExitCode { get; set; }

    /// <summary>
    /// Plans or executes bundle post-processing based on the provided parameters.
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
                spec.DotNet.ProjectRoot = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectRoot);

            var runner = new DotNetPublishPipelineRunner(logger);
            var plan = runner.Plan(spec, fullConfigPath);
            var bundle = (spec.Bundles ?? Array.Empty<DotNetPublishBundle>())
                .FirstOrDefault(entry => string.Equals(entry.Id, BundleId, StringComparison.OrdinalIgnoreCase));
            if (bundle is null)
                throw new InvalidOperationException($"Bundle '{BundleId}' was not found in the dotnet publish config.");
            if (bundle.PostProcess is null)
                throw new InvalidOperationException($"Bundle '{BundleId}' does not define PostProcess rules.");

            var request = new PowerForgeBundlePostProcessRequest
            {
                ProjectRoot = plan.ProjectRoot,
                AllowOutputOutsideProjectRoot = plan.AllowOutputOutsideProjectRoot,
                BundleRoot = SessionState.Path.GetUnresolvedProviderPathFromPSPath(BundleRoot),
                BundleId = bundle.Id,
                TargetName = NormalizeNullable(TargetName) ?? bundle.PrepareFromTarget,
                Runtime = NormalizeNullable(Runtime),
                Framework = NormalizeNullable(Framework),
                Style = NormalizeNullable(Style),
                Configuration = NormalizeNullable(Configuration) ?? plan.Configuration,
                ZipPath = NormalizeNullable(ZipPath),
                SourceOutputPath = NormalizeNullable(SourceOutputPath),
                SkipArchiveDirectories = SkipArchive.IsPresent,
                SkipMetadata = SkipMetadata.IsPresent,
                AdditionalDeletePatterns = NormalizeStrings(DeletePattern),
                Tokens = NormalizeKeyValue(Token),
                PostProcess = bundle.PostProcess
            };

            if (Plan.IsPresent)
            {
                WriteObject(request);
                if (exitCodeMode)
                    Host.SetShouldExit(0);
                return;
            }

            if (!ShouldProcess(request.BundleRoot, $"Apply bundle post-process '{request.BundleId}'"))
                return;

            var result = new PowerForgeBundlePostProcessService(logger).Run(request);
            WriteObject(result);

            if (exitCodeMode)
                Host.SetShouldExit(0);
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "InvokePowerForgeBundlePostProcessFailed", ErrorCategory.NotSpecified, BundleRoot));
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
            "ConfigPath is required when no default dotnet publish config could be found. Searched: powerforge.dotnetpublish.json, .powerforge/dotnetpublish.json.");
    }

    private static string? FindDefaultConfigPath(string baseDirectory)
    {
        var candidates = new[]
        {
            "powerforge.dotnetpublish.json",
            Path.Combine(".powerforge", "dotnetpublish.json")
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
                }
                catch (UnauthorizedAccessException)
                {
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

    private static DotNetPublishSpec LoadConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, options);
        if (spec is null)
            throw new InvalidOperationException($"Unable to deserialize dotnet publish config: {configPath}");

        return spec;
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string> NormalizeKeyValue(string[]? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is null || values.Length == 0)
            return result;

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var separator = value.IndexOf('=');
            if (separator < 0)
            {
                result[value.Trim()] = string.Empty;
                continue;
            }

            var key = value.Substring(0, separator).Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key] = value[(separator + 1)..].Trim();
        }

        return result;
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
}
