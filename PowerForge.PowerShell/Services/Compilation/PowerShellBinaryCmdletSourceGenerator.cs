using System.Management.Automation;
using System.Reflection;
using System.Text;

namespace PowerForge;

internal static class PowerShellBinaryCmdletSourceGenerator
{
    private static readonly HashSet<string> CommonParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction", "ProgressAction",
        "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable", "OutBuffer", "PipelineVariable",
        "WhatIf", "Confirm", "UseTransaction"
    };

    private static readonly HashSet<string> ReservedMemberNames = typeof(PSCmdlet)
        .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Select(static member => member.Name)
        .Append("ProcessRecord")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static PowerShellTypedCompilationResult PrepareForBinaryModule(
        PowerShellTypedCompilationResult typed,
        string[]? exportedFunctions,
        string? targetFramework)
    {
        var selected = exportedFunctions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<PowerShellCompilationDiagnostic>();
        var descriptors = new List<CmdletDescriptor>();
        foreach (var method in typed.Methods.Where(method => selected is null || selected.Contains(method.SourceName)))
        {
            try
            {
                var descriptor = CreateDescriptor(method);
                ValidateDescriptor(descriptor);
                descriptors.Add(descriptor);
            }
            catch (InvalidOperationException ex)
            {
                invalid.Add(GetMethodKey(method));
                diagnostics.Add(CreateDiagnostic(typed, method, ex.Message));
            }
        }

        foreach (var duplicateClass in descriptors
                     .Where(descriptor => !invalid.Contains(GetMethodKey(descriptor.Method)))
                     .GroupBy(static descriptor => descriptor.ClassName, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            var message = $"Functions {string.Join(", ", duplicateClass.Select(static cmdlet => $"'{cmdlet.Method.SourceName}'"))} generate duplicate binary-cmdlet class '{duplicateClass.Key}'.";
            foreach (var descriptor in duplicateClass)
            {
                invalid.Add(GetMethodKey(descriptor.Method));
                diagnostics.Add(CreateDiagnostic(typed, descriptor.Method, message));
            }
        }

        foreach (var descriptor in descriptors.Where(descriptor =>
                     !invalid.Contains(GetMethodKey(descriptor.Method)) &&
                     descriptor.ClassName.Equals(typed.TypeName, StringComparison.OrdinalIgnoreCase)))
        {
            invalid.Add(GetMethodKey(descriptor.Method));
            diagnostics.Add(CreateDiagnostic(
                typed,
                descriptor.Method,
                $"Function '{descriptor.Method.SourceName}' generates binary-cmdlet class '{descriptor.ClassName}', which collides with compiled method container '{typed.TypeName}'."));
        }

        if (invalid.Count == 0)
            return typed;
        var filtered = new PowerShellTypedCompilationTranspiler().TranspileExcluding(
            typed.SourcePath,
            typed.NamespaceName,
            typed.TypeName,
            targetFramework,
            invalid);
        return new PowerShellTypedCompilationResult(
            filtered.SourcePath,
            filtered.NamespaceName,
            filtered.TypeName,
            filtered.SourceCode,
            filtered.Methods,
            filtered.Diagnostics.Concat(diagnostics)
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray());
    }

    internal static string Generate(PowerShellTypedCompilationResult typed, string[]? exportedFunctions = null)
    {
        var selected = exportedFunctions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cmdlets = typed.Methods
            .Where(method => selected is null || selected.Contains(method.SourceName))
            .Select(CreateDescriptor)
            .ToArray();

        var duplicateClass = cmdlets
            .GroupBy(static cmdlet => cmdlet.ClassName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateClass is not null)
            throw new InvalidOperationException($"Functions {string.Join(", ", duplicateClass.Select(static cmdlet => $"'{cmdlet.Method.SourceName}'"))} generate duplicate binary-cmdlet class '{duplicateClass.Key}'.");
        var typeCollision = cmdlets.FirstOrDefault(cmdlet => cmdlet.ClassName.Equals(typed.TypeName, StringComparison.OrdinalIgnoreCase));
        if (typeCollision is not null)
            throw new InvalidOperationException($"Function '{typeCollision.Method.SourceName}' generates binary-cmdlet class '{typeCollision.ClassName}', which collides with compiled method container '{typed.TypeName}'.");

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System.Management.Automation;");
        builder.AppendLine();
        builder.AppendLine($"namespace {typed.NamespaceName};");
        builder.AppendLine();
        foreach (var cmdlet in cmdlets)
            AppendCmdlet(builder, typed, cmdlet);
        return builder.ToString();
    }

    private static CmdletDescriptor CreateDescriptor(PowerShellCompiledMethod method)
    {
        var separator = method.SourceName.IndexOf('-');
        if (separator < 1 || separator == method.SourceName.Length - 1)
            throw new InvalidOperationException($"Function '{method.SourceName}' cannot be exported as a binary cmdlet because it does not use Verb-Noun naming.");
        var verb = method.SourceName.Substring(0, separator);
        var noun = method.SourceName.Substring(separator + 1);
        return new CmdletDescriptor(method, verb, noun, PowerShellCSharpMethodEmitter.SanitizeIdentifier(verb + noun + "Command"));
    }

    private static void ValidateDescriptor(CmdletDescriptor cmdlet)
    {
        var renamedParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter =>
        {
            var memberName = PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name);
            return !memberName.TrimStart('@').Equals(parameter.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (renamedParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${renamedParameter.Name}' cannot preserve its PowerShell name as binary-cmdlet metadata after CLR identifier normalization.");
        var commonParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter => CommonParameterNames.Contains(parameter.Name));
        if (commonParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${commonParameter.Name}' collides with a PowerShell common parameter and cannot be exported as a binary cmdlet.");
        var reservedParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter =>
        {
            var memberName = PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name);
            return ReservedMemberNames.Contains(memberName) || memberName.Equals(cmdlet.ClassName, StringComparison.OrdinalIgnoreCase);
        });
        if (reservedParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${reservedParameter.Name}' collides with generated or inherited binary-cmdlet member '{PowerShellCSharpMethodEmitter.SanitizeIdentifier(reservedParameter.Name)}'.");
    }

    private static void AppendCmdlet(StringBuilder builder, PowerShellTypedCompilationResult typed, CmdletDescriptor cmdlet)
    {
        ValidateDescriptor(cmdlet);

        builder.AppendLine($"[Cmdlet(\"{EscapeCSharpString(cmdlet.Verb)}\", \"{EscapeCSharpString(cmdlet.Noun)}\")]");
        var outputType = GetCmdletOutputTypeName(cmdlet.Method.ReturnType);
        if (outputType is not null)
            builder.AppendLine($"[OutputType(typeof({GetGeneratedTypeName(outputType)}))]");
        builder.AppendLine($"public sealed class {cmdlet.ClassName} : PSCmdlet");
        builder.AppendLine("{");
        for (var index = 0; index < cmdlet.Method.Parameters.Length; index++)
        {
            var parameter = cmdlet.Method.Parameters[index];
            builder.AppendLine($"    [Parameter(Position = {index}{(parameter.IsMandatory ? ", Mandatory = true" : string.Empty)})]");
            builder.AppendLine($"    public {GetGeneratedTypeName(parameter.TypeName)} {PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name)} {{ get; set; }}{(parameter.TypeName == typeof(string).FullName ? " = string.Empty;" : string.Empty)}");
            builder.AppendLine();
        }
        builder.AppendLine("    protected override void ProcessRecord()");
        builder.AppendLine("    {");
        var arguments = string.Join(", ", cmdlet.Method.Parameters.Select(parameter => PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name)));
        var invocation = $"{typed.TypeName}.{cmdlet.Method.GeneratedName}({arguments})";
        if (cmdlet.Method.ReturnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
            builder.AppendLine($"        {invocation};");
        else
            builder.AppendLine($"        WriteObject({invocation}, enumerateCollection: true);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static string GetGeneratedTypeName(string fullName)
    {
        var resolved = Type.GetType(fullName, throwOnError: false);
        if (resolved is not null)
            return PowerShellCSharpMethodEmitter.GetTypeName(resolved);
        if (fullName.EndsWith("[]", StringComparison.Ordinal))
            return GetGeneratedTypeName(fullName.Substring(0, fullName.Length - 2)) + "[]";
        if (fullName == typeof(void).FullName) return "void";
        if (fullName == typeof(bool).FullName) return "bool";
        if (fullName == typeof(byte).FullName) return "byte";
        if (fullName == typeof(sbyte).FullName) return "sbyte";
        if (fullName == typeof(short).FullName) return "short";
        if (fullName == typeof(ushort).FullName) return "ushort";
        if (fullName == typeof(int).FullName) return "int";
        if (fullName == typeof(uint).FullName) return "uint";
        if (fullName == typeof(long).FullName) return "long";
        if (fullName == typeof(ulong).FullName) return "ulong";
        if (fullName == typeof(float).FullName) return "float";
        if (fullName == typeof(double).FullName) return "double";
        if (fullName == typeof(decimal).FullName) return "decimal";
        if (fullName == typeof(char).FullName) return "char";
        if (fullName == typeof(string).FullName) return "string";
        return "global::" + fullName.Replace('+', '.');
    }

    private static string? GetCmdletOutputTypeName(string returnType)
    {
        if (returnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
            return null;
        if (returnType.EndsWith("[]", StringComparison.Ordinal))
            return returnType.Substring(0, returnType.Length - 2);
        var type = Type.GetType(returnType, throwOnError: false);
        if (type is null)
            return typeof(object).FullName;
        if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
            return returnType;
        return type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            ? typeof(object).FullName
            : returnType;
    }

    private static string EscapeCSharpString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static PowerShellCompilationDiagnostic CreateDiagnostic(
        PowerShellTypedCompilationResult typed,
        PowerShellCompiledMethod method,
        string message)
        => new(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            message,
            typed.SourcePath,
            method.SourceLine,
            1);

    private static string GetMethodKey(PowerShellCompiledMethod method)
        => method.SourceName + "\0" + method.SourceLine;

    private sealed class CmdletDescriptor
    {
        internal CmdletDescriptor(PowerShellCompiledMethod method, string verb, string noun, string className)
        {
            Method = method;
            Verb = verb;
            Noun = noun;
            ClassName = className;
        }

        internal PowerShellCompiledMethod Method { get; }
        internal string Verb { get; }
        internal string Noun { get; }
        internal string ClassName { get; }
    }
}
