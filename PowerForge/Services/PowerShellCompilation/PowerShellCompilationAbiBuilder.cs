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
        IEnumerable<PowerShellCompiledMethod> methods,
        PowerShellRuntimeFreeModuleContract? moduleLifetime = null)
    {
        var manifest = new PowerShellCompilationAbiManifest
        {
            NamespaceName = namespaceName ?? string.Empty,
            TypeName = typeName ?? string.Empty,
            SchemaVersion = moduleLifetime is null ? 4 : 5,
            ModuleLifetime = moduleLifetime,
            Methods = methods.Where(static method => !method.IsModuleInitializer)
                .OrderBy(static method => method.SourceName, StringComparer.OrdinalIgnoreCase)
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
        if (manifest.ModuleLifetime is { } lifetime)
        {
            AppendRecord(builder, "module-lifetime", lifetime.ConcurrencyPolicy, lifetime.ResetPolicy, lifetime.DisposalPolicy, lifetime.ReentrancyPolicy);
            AppendRecord(builder, "module-constructor-arities", string.Join(",", lifetime.SupportedParameterCounts
                .OrderBy(static count => count).Select(static count => count.ToString(CultureInfo.InvariantCulture))));
            foreach (var field in lifetime.Fields.OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase))
                AppendRecord(builder, "module-field", field.Name, field.TypeName);
            // Reuse the complete parameter contract normalization, including defaults and validation.
            var constructor = new PowerShellCompiledMethod(".ctor", ".ctor", "System.Void", lifetime.Parameters, 0);
            AppendRecord(builder, "module-constructor", GetNormalizedText(new PowerShellCompilationAbiManifest
            {
                Methods = new[] { CreateMethod(constructor) }
            }));
        }
        foreach (var method in manifest.Methods)
        {
            if (manifest.SchemaVersion >= 5)
                AppendRecord(builder, "instance-method", method.ClrName, Boolean(method.IsInstanceMethod));
            AppendRecord(builder, "method",
                method.PowerShellName,
                method.ClrName,
                method.ReturnType,
                method.OutputCardinality,
                string.Join("\0", method.OutputValueStates.OrderBy(static value => value, StringComparer.Ordinal)),
                method.CollectionElementType,
                method.OutputScalarization,
                Boolean(method.CanProduceNoOutput),
                Boolean(method.CanProduceNull),
                Boolean(method.NoOutputDistinctFromNull),
                Boolean(method.Nullable),
                method.StreamContract,
                method.ExceptionContract,
                Boolean(method.IsAdvancedFunction),
                Boolean(method.PositionalBinding),
                method.DefaultParameterSetName,
                Boolean(method.SupportsShouldProcess),
                method.ConfirmImpact,
                string.Join("\0", method.Aliases.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)));
            foreach (var provider in method.CommandProviders.OrderBy(static item => item.ProviderId, StringComparer.Ordinal)
                         .ThenBy(static item => item.ProviderVersion, StringComparer.Ordinal)
                         .ThenBy(static item => item.CommandName, StringComparer.Ordinal))
            {
                AppendRecord(builder, "command-provider",
                    provider.ProviderId,
                    provider.ProviderVersion,
                    provider.FeatureId,
                    provider.Family.ToString(),
                    provider.CommandName,
                    provider.Output.ToString(),
                    provider.Cardinality.ToString(),
                    provider.Stream,
                    provider.Errors.ToString(),
                    provider.Adapter.Operation,
                    provider.Adapter.SemanticProfile,
                    Boolean(provider.Adapter.RuntimeFree),
                    Boolean(provider.Adapter.AotCompatible),
                    provider.Adapter.Cancellation.ToString(),
                    provider.Adapter.ProcessIsolationTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    provider.Adapter.Cleanup.ToString(),
                    string.Join("\0", provider.Adapter.Dependencies.OrderBy(static value => value, StringComparer.Ordinal)),
                    provider.Adapter.EntryPoint?.AssemblyPath ?? string.Empty,
                    provider.Adapter.EntryPoint?.TypeName ?? string.Empty,
                    provider.Adapter.EntryPoint?.MethodName ?? string.Empty,
                    provider.Adapter.EntryPoint?.ResultType.ToString() ?? string.Empty);
            }
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
        var parameters = (method.NativeFunctionBinding is null ? method.Parameters : Array.Empty<PowerShellCompilationParameter>())
            .Select(parameter => new PowerShellCompilationAbiParameter
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
        if (method.NativeFunctionBinding is not null)
            AddCompilerParameter(parameters, "__nativeFunction", "PowerForge.Generated.Runtime.PowerShellNativeFunctionContext", "NativeFunctionContext");
        AddCompilerParameters(method, parameters);
        var outputCardinality = string.IsNullOrWhiteSpace(method.OutputCardinality)
            ? GetLegacyCardinality(method.ReturnType)
            : method.OutputCardinality;
        var outputValueStates = method.OutputValueStates.Length == 0
            ? GetLegacyValueStates(outputCardinality, method.ReturnType)
            : method.OutputValueStates.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var resolvedReturnType = Type.GetType(method.ReturnType, throwOnError: false);
        var unknownCanBeNull = outputValueStates.Contains("Unknown", StringComparer.Ordinal) &&
                               (resolvedReturnType is null || !resolvedReturnType.IsValueType || Nullable.GetUnderlyingType(resolvedReturnType) is not null);
        var canProduceNull = outputValueStates.Contains("Null", StringComparer.Ordinal) ||
                             outputValueStates.Contains("AutomationNull", StringComparer.Ordinal) ||
                             unknownCanBeNull;
        return new PowerShellCompilationAbiMethod
        {
            PowerShellName = method.SourceName,
            IsInstanceMethod = method.IsInstanceMethod,
            ClrName = method.GeneratedName,
            ReturnType = method.ReturnType,
            OutputCardinality = outputCardinality,
            OutputValueStates = outputValueStates,
            CollectionElementType = method.CollectionElementType,
            OutputScalarization = string.IsNullOrWhiteSpace(method.OutputScalarization)
                ? GetLegacyScalarization(outputCardinality)
                : method.OutputScalarization,
            CanProduceNoOutput = outputCardinality.Equals("None", StringComparison.Ordinal),
            CanProduceNull = canProduceNull,
            NoOutputDistinctFromNull = true,
            Nullable = canProduceNull || IsNullableTypeName(method.ReturnType),
            StreamContract = method.RequiresPowerShellStreams ? "SuccessAndNonSuccessStreams" : "SuccessOutputOnly",
            ExceptionContract = method.RequiresPowerShellStatementErrors ? "PowerShellStatementErrors" : "ClrDirect",
            Aliases = method.Aliases.ToArray(),
            IsAdvancedFunction = method.CommandBinding.IsAdvancedFunction,
            PositionalBinding = method.CommandBinding.PositionalBinding,
            DefaultParameterSetName = method.CommandBinding.DefaultParameterSetName,
            SupportsShouldProcess = method.CommandBinding.SupportsShouldProcess,
            ConfirmImpact = method.CommandBinding.ConfirmImpact,
            CommandProviders = method.CommandProviders.ToArray(),
            Parameters = parameters.ToArray()
        };
    }

    private static void AddCompilerParameters(
        PowerShellCompiledMethod method,
        ICollection<PowerShellCompilationAbiParameter> parameters)
    {
        if (method.RequiresPowerShellStatementErrors)
            AddCompilerParameter(parameters, "__statementErrors", "PowerForge.Generated.Runtime.PowerShellStatementErrorContext", "PowerShellStatementErrors");
        if (method.RequiresPowerShellStopping)
            AddCompilerParameter(parameters, "__checkLoopInterrupts", "System.Action", "LoopInterruption");
        if (method.RequiresPowerShellStreams)
        {
            AddCompilerParameter(parameters, "__writeOutput", "System.Action<System.Object>", "SuccessStream");
            AddCompilerParameter(parameters, "__writeVerbose", "System.Action<System.String>", "VerboseStream");
            AddCompilerParameter(parameters, "__writeDebug", "System.Action<System.String>", "DebugStream");
            AddCompilerParameter(parameters, "__writeWarning", "System.Action<System.String>", "WarningStream");
            AddCompilerParameter(parameters, "__writeInformation", "System.Action<System.String>", "InformationStream");
            AddCompilerParameter(parameters, "__writeHost", "System.Action<System.String>", "HostStream");
            AddCompilerParameter(parameters, "__writeError", "System.Action<System.String>", "ErrorStream");
        }
        if (method.RequiresProviderCancellation)
            AddCompilerParameter(parameters, "__providerCancellationToken", "System.Threading.CancellationToken", "ProviderCancellation");
        if (method.RequiresPowerShellCommandRegions && method.NativeFunctionBinding is null)
        {
            AddCompilerParameter(parameters, "__invokePowerShellRegion", "System.Action<System.String,System.Object[]>", "HostedCommandRegion");
            AddCompilerParameter(parameters, "__invokePowerShellCapture", "System.Func<System.String,System.Object[],System.Object>", "HostedCommandCapture");
        }
        if (method.RequiresPowerShellRuntimeState)
        {
            AddCompilerParameter(parameters, "__shouldProcessTarget", "System.Func<System.String,System.Boolean>", "ShouldProcessTarget");
            AddCompilerParameter(parameters, "__shouldProcessAction", "System.Func<System.String,System.String,System.Boolean>", "ShouldProcessAction");
            AddCompilerParameter(parameters, "__psVersion", "System.Object", "PowerShellVersionState");
            AddCompilerParameter(parameters, "__whatIfPreference", "System.Object", "WhatIfPreference", nullable: true);
            AddCompilerParameter(parameters, "__runtimeState", "System.Collections.Generic.IReadOnlyDictionary<System.String,System.Object>", "PowerShellRuntimeState");
        }
        if (method.RequiresPowerShellModuleStateRead)
            AddCompilerParameter(parameters, "__readPowerShellModuleVariable", "System.Func<System.String,System.Object>", "PowerShellModuleStateReader");
        if (method.RequiresPowerShellModuleStateWrite)
            AddCompilerParameter(parameters, "__writePowerShellModuleVariable", "System.Action<System.String,System.Object>", "PowerShellModuleStateWriter");
        if (method.RequiresPowerShellBoundParameters && method.NativeFunctionBinding is null)
            AddCompilerParameter(parameters, "__boundParameters", "System.Collections.Generic.ISet<System.String>", "BoundParameterNames");
    }

    private static void AddCompilerParameter(
        ICollection<PowerShellCompilationAbiParameter> parameters,
        string name,
        string typeName,
        string purpose,
        bool nullable = false)
        => parameters.Add(new PowerShellCompilationAbiParameter
        {
            ClrName = name,
            TypeName = typeName,
            CompilerAdded = true,
            CompilerPurpose = purpose,
            Nullable = nullable,
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

    private static string GetLegacyCardinality(string typeName)
    {
        if (typeName.Equals(typeof(void).FullName, StringComparison.Ordinal) ||
            typeName.Equals("void", StringComparison.Ordinal)) return "None";
        return typeName.EndsWith("[]", StringComparison.Ordinal) ? "Collection" : "Scalar";
    }

    private static string[] GetLegacyValueStates(string cardinality, string returnType)
    {
        if (cardinality.Equals("None", StringComparison.Ordinal)) return Array.Empty<string>();
        return IsNullableTypeName(returnType) ? new[] { "Known", "Null" } : new[] { "Unknown" };
    }

    private static string GetLegacyScalarization(string cardinality)
        => cardinality switch
        {
            "None" => "NoOutput",
            "Collection" => "EnumerateCollection",
            "Scalar" => "PreserveScalar",
            _ => "RuntimeDependent"
        };

    private static bool IsNullableTypeName(string typeName)
        => typeName.EndsWith("?", StringComparison.Ordinal);
}
