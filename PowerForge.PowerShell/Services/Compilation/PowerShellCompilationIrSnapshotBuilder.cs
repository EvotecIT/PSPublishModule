using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Creates deterministic semantic-only snapshots from canonical bound and lowered IR.</summary>
internal static class PowerShellCompilationIrSnapshotBuilder
{
    internal static PowerShellCompilationIrSnapshotBundle Create(PowerShellSemanticCompilationResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var bundle = new PowerShellCompilationIrSnapshotBundle
        {
            Bound = result.Analyzed.Functions
                .OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal)
                .Select(static function => new PowerShellCompilationIrUnitSnapshot
                {
                    UnitId = function.Symbol.StableKey,
                    DocumentId = function.Symbol.DocumentId,
                    Name = function.Symbol.Name,
                    ReturnType = TypeName(function.ReturnType.ClrType),
                    OutputCardinality = function.OutputCardinality.ToString(),
                    ValueStates = Array.Empty<string>(),
                    Capabilities = SplitFlags(function.Capabilities.ToString()),
                    Effects = SplitFlags(function.Effects.ToString()),
                    Disposition = function.Disposition.Kind.ToString(),
                    Nodes = function.Body.Statements.Select(static statement => statement.GetType().Name).ToArray()
                })
                .ToArray(),
            Lowered = result.Lowered.Functions
                .OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal)
                .Select(static function => new PowerShellCompilationIrUnitSnapshot
                {
                    UnitId = function.Symbol.StableKey,
                    DocumentId = function.Symbol.DocumentId,
                    Name = function.Symbol.Name,
                    ReturnType = TypeName(function.ReturnType),
                    OutputCardinality = function.OutputCardinality.ToString(),
                    ValueStates = function.OutputValueStates.Select(static state => state.ToString()).OrderBy(static state => state, StringComparer.Ordinal).ToArray(),
                    Capabilities = SplitFlags(GetLoweredCapabilities(function).ToString()),
                    Effects = Array.Empty<string>(),
                    Disposition = "LoweredClr",
                    Nodes = function.Statements.Select(static statement => statement.GetType().Name).ToArray()
                })
                .ToArray()
        };
        bundle.Sha256 = ComputeSha256(bundle);
        return bundle;
    }

    internal static string Serialize(PowerShellCompilationIrSnapshotBundle bundle, bool indented)
        => JsonSerializer.Serialize(bundle, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented
        });

    private static PowerShellRequiredCapability GetLoweredCapabilities(PowerShellLoweredFunction function)
    {
        var capabilities = PowerShellRequiredCapability.None;
        if (function.RequiresPowerShellBoundParameters) capabilities |= PowerShellRequiredCapability.PowerShellHost;
        if (function.RequiresPowerShellHostStreams) capabilities |= PowerShellRequiredCapability.PowerShellStreams;
        if (function.RequiresRuntimeFreeProviderOperations) capabilities |= PowerShellRequiredCapability.RuntimeFreeProviderOperations;
        if (function.RequiresPowerShellCommandRegions) capabilities |= PowerShellRequiredCapability.CommandRegion;
        if (function.RequiresPowerShellRuntimeState) capabilities |= PowerShellRequiredCapability.RuntimeStateIntrinsics;
        if (function.RequiresPowerShellModuleState)
            capabilities |= PowerShellRequiredCapability.PowerShellModuleState;
        return capabilities;
    }

    private static string[] SplitFlags(string value)
        => value.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();

    private static string TypeName(Type type) => type.FullName ?? type.Name;

    private static string ComputeSha256(PowerShellCompilationIrSnapshotBundle bundle)
    {
        var canonical = new PowerShellCompilationIrSnapshotBundle
        {
            SchemaVersion = bundle.SchemaVersion,
            RedactedSemanticOnly = bundle.RedactedSemanticOnly,
            Bound = bundle.Bound,
            Lowered = bundle.Lowered,
            Sha256 = string.Empty
        };
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(Serialize(canonical, indented: false)))
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
