using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

internal static class PowerShellCompilationAbiBuilder
{
    internal static PowerShellCompilationAbiManifest Create(
        string namespaceName,
        string typeName,
        IEnumerable<PowerShellCompiledMethod> methods)
    {
        var manifest = new PowerShellCompilationAbiManifest
        {
            NamespaceName = namespaceName ?? string.Empty,
            TypeName = typeName ?? string.Empty,
            Methods = methods.OrderBy(static method => method.SourceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static method => method.GeneratedName, StringComparer.Ordinal)
                .Select(CreateMethod)
                .ToArray()
        };
        manifest.Sha256 = ComputeSha256(GetNormalizedText(manifest));
        return manifest;
    }

    internal static string GetNormalizedText(PowerShellCompilationAbiManifest manifest)
    {
        var builder = new StringBuilder()
            .Append("schema=").Append(manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("namespace=").Append(manifest.NamespaceName).Append('\n')
            .Append("type=").Append(manifest.TypeName).Append('\n');
        foreach (var method in manifest.Methods)
        {
            builder.Append("method=").Append(method.PowerShellName).Append('|').Append(method.ClrName).Append('|')
                .Append(method.ReturnType).Append('|').Append(method.OutputCardinality).Append('|')
                .Append(method.Nullable ? "nullable" : "nonnullable").Append('|')
                .Append(method.StreamContract).Append('|').Append(method.ExceptionContract).Append('\n');
            foreach (var parameter in method.Parameters)
            {
                builder.Append("parameter=").Append(parameter.PowerShellName).Append('|').Append(parameter.ClrName).Append('|')
                    .Append(parameter.TypeName).Append('|').Append(parameter.Nullable ? "nullable" : "nonnullable").Append('|')
                    .Append(parameter.Required ? "required" : "optional").Append('|')
                    .Append(parameter.TracksBoundState ? "bound-state" : "value-only").Append('\n');
            }
        }
        return builder.ToString();
    }

    internal static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return string.Concat(hash.Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static PowerShellCompilationAbiMethod CreateMethod(PowerShellCompiledMethod method)
        => new()
        {
            PowerShellName = method.SourceName,
            ClrName = method.GeneratedName,
            ReturnType = method.ReturnType,
            OutputCardinality = GetCardinality(method.ReturnType),
            Nullable = IsNullableTypeName(method.ReturnType),
            StreamContract = method.RequiresPowerShellStreams ? "SuccessAndNonSuccessStreams" : "SuccessOutputOnly",
            ExceptionContract = "ClrDirect",
            Parameters = method.Parameters.Select(parameter => new PowerShellCompilationAbiParameter
            {
                PowerShellName = parameter.Name,
                ClrName = PowerShellClrSymbolMapper.MapIdentifier(parameter.Name),
                TypeName = parameter.TypeName,
                Nullable = parameter.AllowNull || IsNullableTypeName(parameter.TypeName),
                Required = parameter.IsMandatory,
                TracksBoundState = method.RequiresPowerShellBoundParameters
            }).ToArray()
        };

    private static string GetCardinality(string typeName)
    {
        if (typeName.Equals(typeof(void).FullName, StringComparison.Ordinal) ||
            typeName.Equals("void", StringComparison.Ordinal)) return "None";
        return typeName.EndsWith("[]", StringComparison.Ordinal) ? "Collection" : "Scalar";
    }

    private static bool IsNullableTypeName(string typeName)
        => typeName.EndsWith("?", StringComparison.Ordinal);
}
