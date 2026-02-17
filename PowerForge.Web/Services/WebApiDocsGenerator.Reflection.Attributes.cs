using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Loader;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static List<string> GetAttributeList(MemberInfo member)
    {
        var list = new List<string>();
        foreach (var attr in CustomAttributeData.GetCustomAttributes(member))
        {
            if (!ShouldIncludeAttribute(attr)) continue;
            var formatted = FormatAttribute(attr);
            if (!string.IsNullOrWhiteSpace(formatted))
                list.Add(formatted);
        }
        return list;
    }

    private static bool ShouldIncludeAttribute(CustomAttributeData attr)
    {
        var name = attr.AttributeType.FullName ?? attr.AttributeType.Name;
        if (name.StartsWith("System.Runtime.CompilerServices", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.StartsWith("System.Diagnostics", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.EndsWith(".ExtensionAttribute", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string FormatAttribute(CustomAttributeData attr)
    {
        var name = attr.AttributeType.Name;
        if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 9);

        var args = new List<string>();
        foreach (var arg in attr.ConstructorArguments)
        {
            args.Add(FormatAttributeArgument(arg));
        }
        foreach (var named in attr.NamedArguments)
        {
            var value = FormatAttributeArgument(named.TypedValue);
            args.Add($"{named.MemberName} = {value}");
        }

        if (args.Count == 0)
            return name;

        return $"{name}({string.Join(", ", args)})";
    }

    private static string FormatAttributeArgument(CustomAttributeTypedArgument arg)
    {
        var value = arg.Value;
        if (value is null) return "null";
        if (value is string s) return $"\"{s}\"";
        if (value is char c) return $"'{c}'";
        if (value is bool b) return b ? "true" : "false";
        if (value is Type t) return $"typeof({GetReadableTypeName(t)})";
        if (value is IReadOnlyCollection<CustomAttributeTypedArgument> list)
        {
            var items = list.Select(FormatAttributeArgument);
            return $"[{string.Join(", ", items)}]";
        }
        return value.ToString() ?? string.Empty;
    }

    private static ApiMemberModel CloneMember(ApiMemberModel source, bool isExtension)
    {
        var clone = new ApiMemberModel
        {
            Name = source.Name,
            DisplayName = source.DisplayName,
            Summary = source.Summary,
            Kind = source.Kind,
            Signature = source.Signature,
            ReturnType = source.ReturnType,
            DeclaringType = source.DeclaringType,
            IsInherited = source.IsInherited,
            IsStatic = source.IsStatic,
            Access = source.Access,
            IsExtension = isExtension,
            IsConstructor = source.IsConstructor,
            Returns = source.Returns,
            Value = source.Value,
            ValueSummary = source.ValueSummary,
            Source = source.Source is null
                ? null
                : new ApiSourceLink { Path = source.Source.Path, Line = source.Source.Line, Url = source.Source.Url }
        };
        foreach (var attr in source.Attributes)
            clone.Attributes.Add(attr);
        foreach (var modifier in source.Modifiers)
            clone.Modifiers.Add(modifier);
        foreach (var tp in source.TypeParameters)
            clone.TypeParameters.Add(new ApiTypeParameterModel { Name = tp.Name, Summary = tp.Summary });
        foreach (var ex in source.Examples)
            clone.Examples.Add(new ApiExampleModel { Kind = ex.Kind, Text = ex.Text });
        foreach (var ex in source.Exceptions)
            clone.Exceptions.Add(new ApiExceptionModel { Type = ex.Type, Summary = ex.Summary });
        foreach (var see in source.SeeAlso)
            clone.SeeAlso.Add(see);
        clone.Parameters = source.Parameters
            .Select(p => new ApiParameterModel
            {
                Name = p.Name,
                Type = p.Type,
                Summary = p.Summary,
                IsOptional = p.IsOptional,
                DefaultValue = p.DefaultValue,
                Position = p.Position,
                PipelineInput = p.PipelineInput
            }).ToList();
        for (var i = 0; i < clone.Parameters.Count && i < source.Parameters.Count; i++)
        {
            foreach (var alias in source.Parameters[i].Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    clone.Parameters[i].Aliases.Add(alias);
            }
        }
        return clone;
    }

    private static ApiParameterModel BuildParameterModel(ParameterInfo parameter)
    {
        var model = new ApiParameterModel
        {
            Name = parameter.Name ?? string.Empty,
            Type = GetReadableTypeName(parameter.ParameterType)
        };
        ApplyParameterMetadata(model, parameter);
        return model;
    }

    private static void ApplyParameterMetadata(ApiParameterModel model, ParameterInfo parameter)
    {
        model.IsOptional = parameter.IsOptional;
        if (parameter.Position >= 0)
            model.Position = parameter.Position.ToString();
        if (parameter.HasDefaultValue)
            model.DefaultValue = FormatDefaultValue(parameter.DefaultValue);
    }

    private static string BuildMethodSignature(MethodInfo method)
    {
        var prefix = BuildMethodPrefix(method);
        var name = method.Name;
        if (method.IsGenericMethod)
        {
            var args = method.GetGenericArguments().Select(GetReadableTypeName);
            name += $"<{string.Join(", ", args)}>";
        }
        var returnType = GetReadableTypeName(method.ReturnType);
        var parameters = method.GetParameters()
            .Select(BuildParameterSignature)
            .ToList();
        return $"{prefix}{returnType} {name}({string.Join(", ", parameters)})".Trim();
    }

    private static string BuildParameterSignature(ParameterInfo parameter)
    {
        var prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
        if (parameter.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
            prefix = "params " + prefix;
        var typeName = GetReadableTypeName(parameter.ParameterType);
        var name = parameter.Name ?? "value";
        var value = $"{prefix}{typeName} {name}".Trim();
        if (parameter.IsOptional)
        {
            var def = parameter.HasDefaultValue ? FormatDefaultValue(parameter.DefaultValue) : "null";
            value += $" = {def}";
        }
        return value;
    }

    private static string BuildPropertySignature(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is not null) accessors.Add("get;");
        if (property.SetMethod is not null) accessors.Add("set;");
        var prefix = BuildPropertyPrefix(property);
        return $"{prefix}{GetReadableTypeName(property.PropertyType)} {property.Name} {{ {string.Join(" ", accessors)} }}".Trim();
    }

    private static string BuildFieldSignature(FieldInfo field)
    {
        var prefix = BuildFieldPrefix(field);
        return $"{prefix}{GetReadableTypeName(field.FieldType)} {field.Name}".Trim();
    }

    private static string BuildEventSignature(EventInfo evt)
    {
        var prefix = BuildEventPrefix(evt);
        var handler = evt.EventHandlerType is null ? "EventHandler" : GetReadableTypeName(evt.EventHandlerType);
        return $"{prefix}event {handler} {evt.Name}".Trim();
    }

    private static string BuildConstructorSignature(ConstructorInfo ctor, Type declaringType)
    {
        var prefix = BuildMethodPrefix(ctor);
        var name = GetReadableTypeName(declaringType);
        var parameters = ctor.GetParameters()
            .Select(BuildParameterSignature)
            .ToList();
        return $"{prefix}{name}({string.Join(", ", parameters)})".Trim();
    }

    private static string BuildMethodPrefix(MethodBase method)
    {
        var parts = new List<string>();
        var access = GetAccessModifier(method);
        if (!string.IsNullOrWhiteSpace(access))
            parts.Add(access);
        if (method is MethodInfo mi)
            parts.AddRange(GetMethodModifiers(mi));
        else if (method is ConstructorInfo ci)
            parts.AddRange(GetConstructorModifiers(ci));
        return parts.Count == 0 ? string.Empty : string.Join(" ", parts) + " ";
    }

    private static string BuildPropertyPrefix(PropertyInfo property)
    {
        var accessor = GetMostVisibleAccessor(property.GetMethod, property.SetMethod);
        if (accessor is null) return string.Empty;
        var parts = new List<string> { GetAccessModifier(accessor) };
        parts.AddRange(GetPropertyModifiers(property));
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))) + " ";
    }

    private static string BuildFieldPrefix(FieldInfo field)
    {
        var parts = new List<string> { GetAccessModifier(field) };
        parts.AddRange(GetFieldModifiers(field));
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))) + " ";
    }

    private static string BuildEventPrefix(EventInfo evt)
    {
        var accessor = evt.AddMethod ?? evt.RemoveMethod;
        if (accessor is null) return string.Empty;
        var parts = new List<string> { GetAccessModifier(accessor) };
        parts.AddRange(GetEventModifiers(evt));
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))) + " ";
    }

    private static string FormatDefaultValue(object? value)
    {
        if (value is null) return "null";
        return value switch
        {
            string s => $"\"{s}\"",
            char c => $"'{c}'",
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static Assembly? TryLoadAssembly(string assemblyPath, List<string> warnings)
    {
        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception ex)
        {
            try
            {
                var bytes = File.ReadAllBytes(assemblyPath);
                return Assembly.Load(bytes);
            }
            catch (Exception ex2)
            {
                warnings.Add($"Assembly load failed: {Path.GetFileName(assemblyPath)} ({ex2.GetType().Name}: {ex2.Message})");
                warnings.Add($"Primary load error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }

    private static IEnumerable<Type?> GetExportedTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Trace.TraceWarning($"ReflectionTypeLoadException in GetExportedTypesSafe: {ex.Message}");
            return ex.Types ?? Array.Empty<Type?>();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"GetExportedTypesSafe failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<Type?>();
        }
    }

    private static string GetTypeKind(Type type)
    {
        if (type.IsInterface) return "Interface";
        if (type.IsEnum) return "Enum";
        if (type.IsValueType) return "Struct";
        if (type.BaseType == typeof(MulticastDelegate)) return "Delegate";
        return "Class";
    }

    private static string GetReadableTypeName(Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType() ?? type;

        if (type.IsArray)
            return $"{GetReadableTypeName(type.GetElementType() ?? typeof(object))}[]";

        if (type.IsGenericType)
        {
            var name = StripGenericArity(type.Name);
            var args = type.GetGenericArguments().Select(GetReadableTypeName);
            return $"{name}<{string.Join(", ", args)}>";
        }

        return type.Name;
    }

    private static ApiTypeModel ParseType(
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var lastDot = fullName.LastIndexOf('.');
        var ns = lastDot > 0 ? fullName.Substring(0, lastDot) : string.Empty;
        var name = lastDot > 0 ? fullName.Substring(lastDot + 1) : fullName;

        var model = new ApiTypeModel
        {
            Name = name,
            FullName = fullName,
            Namespace = ns,
            Summary = GetSummary(member, memberKey, memberLookup),
            Remarks = GetElement(member, "remarks", memberKey, memberLookup),
            Kind = InferTypeKind(name),
            Slug = Slugify(fullName)
        };
        model.TypeParameters.AddRange(GetTypeParameters(member, memberKey, memberLookup));
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        return model;
    }

    private static void AddMethod(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        Assembly? assembly,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var parameterTypes = ParseParameterTypes(fullName);
        var parameterNames = TryResolveParameterNames(assembly, typeName, name, parameterTypes);
        var parameters = ParseParameters(member, parameterTypes, parameterNames, memberKey, memberLookup);

        var isCtor = IsConstructorName(name);
        var displayName = isCtor ? GetShortTypeName(typeName) : name;
        var model = new ApiMemberModel
        {
            Name = name,
            DisplayName = displayName,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = isCtor ? "Constructor" : "Method",
            Parameters = parameters,
            Returns = GetElement(member, "returns", memberKey, memberLookup),
            IsConstructor = isCtor
        };
        model.TypeParameters.AddRange(GetTypeParameters(member, memberKey, memberLookup));
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        if (isCtor)
            type.Constructors.Add(model);
        else
            type.Methods.Add(model);
    }

    private static void AddProperty(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var model = new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = "Property",
            ValueSummary = GetElement(member, "value", memberKey, memberLookup)
        };
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        type.Properties.Add(model);
    }

    private static void AddField(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var model = new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = "Field",
            ValueSummary = GetElement(member, "value", memberKey, memberLookup)
        };
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        type.Fields.Add(model);
    }

    private static void AddEvent(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var model = new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = "Event"
        };
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        type.Events.Add(model);
    }

    private static string ExtractTypeName(string fullName)
    {
        var trimmed = fullName;
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx > 0)
            trimmed = trimmed.Substring(0, parenIdx);
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot > 0 ? trimmed.Substring(0, lastDot) : trimmed;
    }

    private static string ExtractMemberName(string fullName)
    {
        var trimmed = fullName;
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx > 0)
            trimmed = trimmed.Substring(0, parenIdx);
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot > 0 ? trimmed.Substring(lastDot + 1) : trimmed;
    }

    private static List<ApiParameterModel> ParseParameters(
        XElement member,
        IReadOnlyList<string> parameterTypes,
        IReadOnlyList<string>? parameterNames,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = new List<ApiParameterModel>();
        var ownParamElements = member.Elements("param").ToList();
        var inheritedParamElements = ownParamElements.Count > 0
            ? new List<XElement>()
            : GetInheritedElements(member, memberKey, "param", memberLookup);
        var count = Math.Max(Math.Max(ownParamElements.Count, inheritedParamElements.Count), parameterTypes.Count);
        for (var i = 0; i < count; i++)
        {
            var paramElement = i < ownParamElements.Count
                ? ownParamElements[i]
                : (i < inheritedParamElements.Count ? inheritedParamElements[i] : null);
            var paramName = paramElement is not null
                ? paramElement.Attribute("name")?.Value ?? $"arg{i + 1}"
                : (parameterNames != null && i < parameterNames.Count && !string.IsNullOrWhiteSpace(parameterNames[i])
                    ? parameterNames[i]
                    : $"arg{i + 1}");
            var summary = paramElement is null ? null : NormalizeXmlText(paramElement);
            var type = i < parameterTypes.Count ? parameterTypes[i] : string.Empty;
            results.Add(new ApiParameterModel
            {
                Name = paramName,
                Type = type,
                Summary = summary
            });
        }
        return results;
    }

    private static bool ShouldIncludeType(ApiTypeModel type, WebApiDocsOptions options)
    {
        var ns = type.Namespace ?? string.Empty;
        if (options.IncludeNamespacePrefixes.Count > 0)
        {
            var matches = options.IncludeNamespacePrefixes.Any(prefix =>
                ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                type.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!matches) return false;
        }

        if (options.ExcludeNamespacePrefixes.Count > 0)
        {
            var excluded = options.ExcludeNamespacePrefixes.Any(prefix =>
                ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                type.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (excluded) return false;
        }

        if (options.IncludeTypeNames.Count > 0)
        {
            var matches = options.IncludeTypeNames.Any(pattern => MatchTypePattern(pattern, type));
            if (!matches) return false;
        }

        if (options.ExcludeTypeNames.Count > 0)
        {
            var excluded = options.ExcludeTypeNames.Any(pattern => MatchTypePattern(pattern, type));
            if (excluded) return false;
        }

        return true;
    }

    private static bool MatchTypePattern(string pattern, ApiTypeModel type)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var fullName = type.FullName ?? string.Empty;
        var name = type.Name ?? string.Empty;
        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = pattern.TrimEnd('*');
            return fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(fullName, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? TryResolveParameterNames(Assembly? assembly, string typeName, string memberName, IReadOnlyList<string> parameterTypes)
    {
        if (assembly is null) return null;
        var type = ResolveType(assembly, typeName);
        if (type is null) return null;

        var lookupName = StripGenericArity(memberName);
        if (IsConstructorName(lookupName))
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            return ResolveParameterNamesFromCandidates(ctors, parameterTypes, assembly);
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, lookupName, StringComparison.Ordinal))
            .ToArray();

        return ResolveParameterNamesFromCandidates(methods, parameterTypes, assembly);
    }

    private static IReadOnlyList<string>? ResolveParameterNamesFromCandidates(MethodBase[] candidates, IReadOnlyList<string> parameterTypes, Assembly assembly)
    {
        foreach (var candidate in candidates)
        {
            var parameters = candidate.GetParameters();
            if (!ParameterTypesMatch(parameters, parameterTypes, assembly)) continue;
            return parameters.Select(p => p.Name ?? string.Empty).ToList();
        }

        var countMatches = candidates
            .Where(m => m.GetParameters().Length == parameterTypes.Count)
            .ToArray();
        if (countMatches.Length == 1)
        {
            return countMatches[0].GetParameters().Select(p => p.Name ?? string.Empty).ToList();
        }

        return null;
    }

    private static bool ParameterTypesMatch(ParameterInfo[] parameters, IReadOnlyList<string> parameterTypes, Assembly assembly)
    {
        if (parameters.Length != parameterTypes.Count) return false;
        for (var i = 0; i < parameterTypes.Count; i++)
        {
            if (!ParameterTypeMatches(parameters[i].ParameterType, parameterTypes[i], assembly))
                return false;
        }
        return true;
    }

    private static bool ParameterTypeMatches(Type parameterType, string xmlType, Assembly assembly)
    {
        if (string.IsNullOrWhiteSpace(xmlType)) return false;
        var typeName = xmlType.Trim();
        var byRef = false;
        if (typeName.EndsWith("@", StringComparison.Ordinal) || typeName.EndsWith("&", StringComparison.Ordinal))
        {
            byRef = true;
            typeName = typeName.TrimEnd('@', '&');
        }

        if (parameterType.IsByRef != byRef)
            return false;
        if (byRef)
            parameterType = parameterType.GetElementType() ?? parameterType;

        var arrayRanks = 0;
        while (typeName.EndsWith("[]", StringComparison.Ordinal))
        {
            arrayRanks++;
            typeName = typeName.Substring(0, typeName.Length - 2);
        }

        if (arrayRanks > 0)
        {
            for (var i = 0; i < arrayRanks; i++)
            {
                if (!parameterType.IsArray) return false;
                parameterType = parameterType.GetElementType() ?? parameterType;
            }
        }
        else if (parameterType.IsArray)
        {
            return false;
        }

        if (TryParseGenericParameterToken(typeName, out var isMethodParameter, out var position))
        {
            if (!parameterType.IsGenericParameter) return false;
            if (parameterType.GenericParameterPosition != position) return false;
            if (isMethodParameter && parameterType.DeclaringMethod is null) return false;
            if (!isMethodParameter && parameterType.DeclaringMethod is not null) return false;
            return true;
        }

        var genericStart = typeName.IndexOf('{');
        if (genericStart >= 0 && typeName.EndsWith("}", StringComparison.Ordinal))
        {
            if (!parameterType.IsGenericType) return false;
            var outer = typeName.Substring(0, genericStart);
            var argsText = typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2);
            var argTokens = SplitTypeArguments(argsText);
            var genericDefName = $"{outer}`{argTokens.Count}";
            var resolvedDef = ResolveType(assembly, genericDefName) ?? ResolveType(assembly, outer);
            if (resolvedDef is null) return false;
            if (parameterType.GetGenericTypeDefinition() != resolvedDef) return false;
            var argTypes = parameterType.GetGenericArguments();
            if (argTypes.Length != argTokens.Count) return false;
            for (var i = 0; i < argTypes.Length; i++)
            {
                if (!ParameterTypeMatches(argTypes[i], argTokens[i], assembly))
                    return false;
            }
            return true;
        }

        var resolved = ResolveType(assembly, typeName);
        return resolved is not null && parameterType == resolved;
    }
}
