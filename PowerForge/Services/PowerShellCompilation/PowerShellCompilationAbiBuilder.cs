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
        var builder = new StringBuilder();
        AppendRecord(builder, "schema", manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        AppendRecord(builder, "namespace", manifest.NamespaceName);
        AppendRecord(builder, "type", manifest.TypeName);
        foreach (var method in manifest.Methods)
        {
            AppendRecord(builder, "method",
                method.PowerShellName,
                method.ClrName,
                method.ReturnType,
                method.OutputCardinality,
                Boolean(method.Nullable),
                method.StreamContract,
                method.ExceptionContract,
                Boolean(method.IsAdvancedFunction),
                Boolean(method.PositionalBinding),
                method.DefaultParameterSetName,
                Boolean(method.SupportsShouldProcess),
                method.ConfirmImpact,
                string.Join("\0", method.Aliases.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
            foreach (var parameter in method.Parameters)
            {
                AppendRecord(builder, "parameter",
                    parameter.PowerShellName,
                    parameter.ClrName,
                    parameter.TypeName,
                    Boolean(parameter.Nullable),
                    Boolean(parameter.Required),
                    Boolean(parameter.TracksBoundState),
                    Boolean(parameter.CompilerAdded),
                    parameter.CompilerPurpose,
                    Boolean(parameter.IsSwitch),
                    Boolean(parameter.HasDefaultValue),
                    NormalizeLiteral(parameter.DefaultValue),
                    Boolean(parameter.AllowEmptyString),
                    Boolean(parameter.AllowEmptyCollection),
                    Boolean(parameter.SupportsWildcards),
                    string.Join("\0", parameter.Aliases.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
                foreach (var binding in parameter.Bindings.OrderBy(static item => item.ParameterSetName, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(static item => item.Position ?? int.MaxValue))
                {
                    AppendRecord(builder, "binding",
                        binding.ParameterSetName,
                        Boolean(binding.Mandatory),
                        binding.Position?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        Boolean(binding.ValueFromPipeline),
                        Boolean(binding.ValueFromPipelineByPropertyName),
                        Boolean(binding.ValueFromRemainingArguments),
                        Boolean(binding.DontShow),
                        binding.HelpMessage);
                }
                foreach (var validation in parameter.Validations.OrderBy(static item => item.Kind)
                             .ThenBy(static item => string.Join("\0", item.Arguments), StringComparer.Ordinal))
                {
                    AppendRecord(builder, "validation",
                        validation.Kind.ToString(),
                        string.Join("\0", validation.Arguments));
                }
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
    {
        var parameters = method.Parameters.Select(parameter => new PowerShellCompilationAbiParameter
        {
            PowerShellName = parameter.Name,
            ClrName = PowerShellClrSymbolMapper.MapIdentifier(parameter.Name),
            TypeName = parameter.TypeName,
            Nullable = parameter.AllowNull || IsNullableTypeName(parameter.TypeName),
            Required = parameter.IsMandatory,
            TracksBoundState = method.RequiresPowerShellBoundParameters,
            IsSwitch = parameter.IsSwitch,
            Aliases = parameter.Aliases.ToArray(),
            HasDefaultValue = parameter.HasDefaultValue,
            DefaultValue = parameter.DefaultValue,
            Bindings = parameter.Bindings.ToArray(),
            Validations = parameter.Validations.ToArray(),
            AllowEmptyString = parameter.AllowEmptyString,
            AllowEmptyCollection = parameter.AllowEmptyCollection,
            SupportsWildcards = parameter.SupportsWildcards
        }).ToList();
        AddCompilerParameters(method, parameters);
        return new PowerShellCompilationAbiMethod
        {
            PowerShellName = method.SourceName,
            ClrName = method.GeneratedName,
            ReturnType = method.ReturnType,
            OutputCardinality = GetCardinality(method.ReturnType),
            Nullable = IsNullableTypeName(method.ReturnType),
            StreamContract = method.RequiresPowerShellStreams ? "SuccessAndNonSuccessStreams" : "SuccessOutputOnly",
            ExceptionContract = "ClrDirect",
            Aliases = method.Aliases.ToArray(),
            IsAdvancedFunction = method.CommandBinding.IsAdvancedFunction,
            PositionalBinding = method.CommandBinding.PositionalBinding,
            DefaultParameterSetName = method.CommandBinding.DefaultParameterSetName,
            SupportsShouldProcess = method.CommandBinding.SupportsShouldProcess,
            ConfirmImpact = method.CommandBinding.ConfirmImpact,
            Parameters = parameters.ToArray()
        };
    }

    private static void AddCompilerParameters(
        PowerShellCompiledMethod method,
        ICollection<PowerShellCompilationAbiParameter> parameters)
    {
        if (method.RequiresPowerShellStreams)
        {
            AddCompilerParameter(parameters, "__writeVerbose", "System.Action<System.String>", "VerboseStream");
            AddCompilerParameter(parameters, "__writeDebug", "System.Action<System.String>", "DebugStream");
            AddCompilerParameter(parameters, "__writeWarning", "System.Action<System.String>", "WarningStream");
        }
        if (method.RequiresPowerShellCommandRegions)
        {
            AddCompilerParameter(parameters, "__invokePowerShellRegion", "System.Action<System.String,System.Object[]>", "HostedCommandRegion");
            AddCompilerParameter(parameters, "__invokePowerShellCapture", "System.Func<System.String,System.Object[],System.Object>", "HostedCommandCapture");
        }
        if (method.RequiresPowerShellRuntimeState)
        {
            AddCompilerParameter(parameters, "__shouldProcessTarget", "System.Func<System.String,System.Boolean>", "ShouldProcessTarget");
            AddCompilerParameter(parameters, "__shouldProcessAction", "System.Func<System.String,System.String,System.Boolean>", "ShouldProcessAction");
            AddCompilerParameter(parameters, "__psVersion", "System.Object", "PowerShellVersionState");
            AddCompilerParameter(parameters, "__whatIfPreference", "System.Boolean", "WhatIfPreference");
        }
        if (method.RequiresPowerShellBoundParameters)
            AddCompilerParameter(parameters, "__boundParameters", "System.Collections.Generic.ISet<System.String>", "BoundParameterNames");
    }

    private static void AddCompilerParameter(
        ICollection<PowerShellCompilationAbiParameter> parameters,
        string name,
        string typeName,
        string purpose)
        => parameters.Add(new PowerShellCompilationAbiParameter
        {
            ClrName = name,
            TypeName = typeName,
            CompilerAdded = true,
            CompilerPurpose = purpose,
            Required = true
        });

    private static void AppendRecord(StringBuilder builder, string kind, params string[] values)
    {
        builder.Append(kind);
        foreach (var value in values)
        {
            var normalized = value ?? string.Empty;
            builder.Append('|').Append(normalized.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(normalized);
        }
        builder.Append('\n');
    }

    private static string Boolean(bool value) => value ? "true" : "false";

    private static string NormalizeLiteral(PowerShellCompilationLiteral? literal)
    {
        if (literal is null) return string.Empty;
        var builder = new StringBuilder();
        AppendRecord(builder, "literal", literal.Kind.ToString(), literal.TypeName, literal.Value);
        foreach (var element in literal.Elements)
            AppendRecord(builder, "element", NormalizeLiteral(element));
        return builder.ToString();
    }

    private static string GetCardinality(string typeName)
    {
        if (typeName.Equals(typeof(void).FullName, StringComparison.Ordinal) ||
            typeName.Equals("void", StringComparison.Ordinal)) return "None";
        return typeName.EndsWith("[]", StringComparison.Ordinal) ? "Collection" : "Scalar";
    }

    private static bool IsNullableTypeName(string typeName)
        => typeName.EndsWith("?", StringComparison.Ordinal);
}
