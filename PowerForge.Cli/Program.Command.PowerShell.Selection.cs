using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private static readonly string[] PowerShellSelectionOptions =
    {
        "--path", "--entry-point", "--kind", "--target", "--out", "--output-directory", "--name",
        "--mode", "--target-contract", "--semantic-profile", "--framework", "--resource-mode",
        "--include-resource", "--exclude-resource", "--rid", "--optimization", "--output"
    };

    private static readonly string[] PowerShellSelectionFlags =
        { "--self-contained", "--no-single-file", "--json", "--output-json" };

    private static bool TryParsePowerShellSelection(string[] args, string? positionalPath,
        out PowerShellCompilationRequest? request, out string error, bool allowAnalyzeAlias = false)
    {
        request = null;
        var paths = GetOptionValues(args, "--path").ToList();
        if (positionalPath is not null)
            paths.Insert(0, positionalPath);
        if (paths.Count == 0)
        {
            error = "A PowerShell source file is required.";
            return false;
        }

        PowerShellCompilationArtifactKind? kind = null;
        var kindValue = TryGetOptionValue(args, "--kind") ?? TryGetOptionValue(args, "--target");
        if (kindValue is not null)
        {
            if (!TryParseArtifactKind(kindValue, out var parsedKind))
            {
                error = "Artifact kind must be 'exe', 'dll', or 'library'.";
                return false;
            }
            kind = parsedKind;
        }

        var modeValue = TryGetOptionValue(args, "--mode");
        PowerShellCompilationMode? mode = null;
        if (modeValue is not null)
        {
            if (!TryParseSelectionEnum<PowerShellCompilationMode>(modeValue, out var parsedMode) ||
                (!allowAnalyzeAlias && parsedMode == PowerShellCompilationMode.Analyze))
            {
                error = $"Unknown artifact compilation mode '{modeValue}'.";
                return false;
            }
            mode = parsedMode == PowerShellCompilationMode.Analyze ? null : parsedMode;
        }

        var optimizationValue = TryGetOptionValue(args, "--optimization");
        PowerShellCompilationExecutableOptimization? optimization = null;
        if (optimizationValue is not null)
        {
            if (!TryParseSelectionEnum<PowerShellCompilationExecutableOptimization>(optimizationValue, out var parsedOptimization))
            {
                error = $"Unknown executable optimization '{optimizationValue}'. Use None, Trimmed, or NativeAot.";
                return false;
            }
            optimization = parsedOptimization;
        }

        var resourceValue = TryGetOptionValue(args, "--resource-mode") ?? nameof(PowerShellCompilationResourceMode.Declared);
        if (!TryParseSelectionEnum<PowerShellCompilationResourceMode>(resourceValue, out var resourceMode))
        {
            error = $"Unknown resource mode '{resourceValue}'. Use Declared, CompleteModule, or None.";
            return false;
        }

        var targetPath = TryGetOptionValue(args, "--target-contract");
        request = new PowerShellCompilationRequest(paths)
        {
            EntryPoint = TryGetOptionValue(args, "--entry-point"),
            Kind = kind,
            Mode = mode,
            ArtifactName = TryGetOptionValue(args, "--name"),
            OutputDirectory = TryGetOptionValue(args, "--out") ?? TryGetOptionValue(args, "--output-directory"),
            Target = targetPath is null ? null : ReadPowerShellTargetContract(targetPath),
            TargetFramework = TryGetOptionValue(args, "--framework"),
            SemanticProfileId = TryGetOptionValue(args, "--semantic-profile"),
            RuntimeIdentifier = TryGetOptionValue(args, "--rid"),
            SelfContained = args.Contains("--self-contained", StringComparer.OrdinalIgnoreCase) ? true : null,
            SingleFile = args.Contains("--no-single-file", StringComparer.OrdinalIgnoreCase) ? false : null,
            Optimization = optimization,
            ResourceMode = resourceMode,
            IncludeResource = GetOptionValues(args, "--include-resource").ToArray(),
            ExcludeResource = GetOptionValues(args, "--exclude-resource").ToArray()
        };
        error = string.Empty;
        return true;
    }

    private static bool TryParseSelectionEnum<T>(string value, out T selection) where T : struct, Enum
        => Enum.TryParse(value, ignoreCase: true, out selection) && Enum.IsDefined(selection);

    private static PowerShellCompilationTargetContract ReadPowerShellTargetContract(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        return JsonSerializer.Deserialize(File.ReadAllText(fullPath), CliJson.Context.PowerShellCompilationTargetContract)
            ?? throw new InvalidDataException($"Target contract '{fullPath}' was empty.");
    }
}
