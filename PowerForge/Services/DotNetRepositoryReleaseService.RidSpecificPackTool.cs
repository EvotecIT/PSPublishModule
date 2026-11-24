using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    /// <summary>Traversal item metadata carrying the properties one project's pack needs beyond the shared ones.</summary>
    private const string PackAdditionalPropertiesMetadata = "PackAdditionalProperties";

    /// <summary>MSBuild properties that decide whether a project packs one package per runtime identifier.</summary>
    private const string ToolPackageRuntimeIdentifierProperties =
        "PackAsTool,CreateRidSpecificToolPackages,ToolPackageRuntimeIdentifiers,RuntimeIdentifiers,RuntimeIdentifier,PublishAot";

    /// <summary>
    /// Returns the runtime identifiers <c>dotnet pack</c> creates <c>{PackageId}.{rid}</c> packages for, mirroring the
    /// SDK's RID-specific tool packaging: the tool lists <c>ToolPackageRuntimeIdentifiers</c> (or <c>RuntimeIdentifiers</c>),
    /// is not fixed to one <c>RuntimeIdentifier</c> and is not NativeAOT, which the SDK packs one runtime at a time.
    /// </summary>
    internal static IReadOnlyList<string> ResolveToolPackageRuntimeIdentifiers(Func<string, string?> readProperty)
    {
        static bool IsTrue(string? value) => bool.TryParse(value?.Trim(), out var enabled) && enabled;

        if (!IsTrue(readProperty("PackAsTool")) ||
            !IsTrue(readProperty("CreateRidSpecificToolPackages")) ||
            !string.IsNullOrWhiteSpace(readProperty("RuntimeIdentifier")) ||
            IsTrue(readProperty("PublishAot")))
        {
            return Array.Empty<string>();
        }

        var runtimeIdentifiers = readProperty("ToolPackageRuntimeIdentifiers");
        if (string.IsNullOrWhiteSpace(runtimeIdentifiers))
            runtimeIdentifiers = readProperty("RuntimeIdentifiers");

        return (runtimeIdentifiers ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static runtimeIdentifier => runtimeIdentifier.Trim())
            .Where(static runtimeIdentifier => runtimeIdentifier.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// A RID-specific tool package ships its runtime's whole publish output: every dependency assembly is copied there
    /// and, with ReadyToRun, recompiled, which drops its original signature. Signing covers all of those assemblies,
    /// whether or not <see cref="DotNetRepositoryReleaseSpec.SignDependencyAssemblies"/> is set for build outputs.
    /// </summary>
    private static readonly string[] PreparedRuntimeOutputSigningPatterns = { "*.dll", "*.exe" };

    private static IReadOnlyDictionary<string, string> CreatePackToolRuntimeProperties(
        IReadOnlyDictionary<string, string> packProperties,
        string? runtimeIdentifier)
    {
        if (runtimeIdentifier is null)
            return packProperties;

        // The SDK packs each runtime's package with RuntimeIdentifier as a global property; publish with the same one.
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in packProperties)
            properties[property.Key] = property.Value;
        properties["RuntimeIdentifier"] = runtimeIdentifier;
        return properties;
    }

    private static IReadOnlyDictionary<string, string> CreatePackToolRuntimeBuildProperties(string runtimeIdentifier)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RuntimeIdentifier"] = runtimeIdentifier,
            ["BuildProjectReferences"] = "false"
        };

    private static string FormatRuntimeIdentifierSuffix(string? runtimeIdentifier)
        => runtimeIdentifier is null ? string.Empty : $" ({runtimeIdentifier})";
}
