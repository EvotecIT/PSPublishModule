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
    private static void PopulateFromAssembly(ApiDocModel doc, Assembly assembly)
    {
        foreach (var type in GetExportedTypesSafe(assembly))
        {
            if (type is null) continue;
            var rawFullName = type.FullName ?? type.Name;
            if (string.IsNullOrWhiteSpace(rawFullName)) continue;
            var fullName = rawFullName.Replace('+', '.');
            if (doc.Types.ContainsKey(fullName)) continue;

            var model = new ApiTypeModel
            {
                Name = StripGenericArity(type.Name),
                FullName = fullName,
                Namespace = type.Namespace ?? string.Empty,
                Kind = GetTypeKind(type),
                Slug = Slugify(fullName)
            };
            if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    if (!string.IsNullOrWhiteSpace(arg.Name))
                        model.TypeParameters.Add(new ApiTypeParameterModel { Name = arg.Name });
                }
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName) continue;
                model.Methods.Add(new ApiMemberModel
                {
                    Name = method.Name,
                    DisplayName = method.Name,
                    Parameters = method.GetParameters().Select(p => new ApiParameterModel
                    {
                        Name = p.Name ?? string.Empty,
                        Type = GetReadableTypeName(p.ParameterType)
                    }).ToList()
                });
            }

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                model.Constructors.Add(new ApiMemberModel
                {
                    Name = "#ctor",
                    DisplayName = model.Name,
                    Kind = "Constructor",
                    IsConstructor = true,
                    Parameters = ctor.GetParameters().Select(p => new ApiParameterModel
                    {
                        Name = p.Name ?? string.Empty,
                        Type = GetReadableTypeName(p.ParameterType)
                    }).ToList()
                });
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                model.Properties.Add(new ApiMemberModel
                {
                    Name = property.Name,
                    DisplayName = property.Name
                });
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (field.IsSpecialName) continue;
                model.Fields.Add(new ApiMemberModel
                {
                    Name = field.Name,
                    DisplayName = field.Name
                });
            }

            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                model.Events.Add(new ApiMemberModel
                {
                    Name = evt.Name,
                    DisplayName = evt.Name
                });
            }

            doc.Types[fullName] = model;
        }
    }

    private static void EnrichFromAssembly(ApiDocModel doc, Assembly assembly, WebApiDocsOptions options, List<string> warnings)
    {
        using var sourceLinks = SourceLinkContext.Create(options, assembly, warnings);
        var extensionTargets = new Dictionary<string, List<ApiMemberModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in GetExportedTypesSafe(assembly))
        {
            if (type is null) continue;
            var rawFullName = type.FullName ?? type.Name;
            if (string.IsNullOrWhiteSpace(rawFullName)) continue;
            var fullName = rawFullName.Replace('+', '.');
            if (!doc.Types.TryGetValue(fullName, out var model)) continue;

            model.Kind = GetTypeKind(type);
            model.Assembly = type.Assembly.GetName().Name;
            model.IsAbstract = type.IsAbstract;
            model.IsSealed = type.IsSealed;
            model.IsStatic = type.IsAbstract && type.IsSealed;
            model.Attributes.Clear();
            model.Attributes.AddRange(GetAttributeList(type));
            if (sourceLinks is not null)
                model.Source = sourceLinks.TryGetSource(type);
            model.BaseType = type.BaseType != null && type.BaseType != typeof(object)
                ? GetReadableTypeName(type.BaseType)
                : null;
            model.Interfaces.Clear();
            foreach (var iface in type.GetInterfaces())
            {
                model.Interfaces.Add(GetReadableTypeName(iface));
            }
            if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            {
                MergeTypeParameters(model.TypeParameters, type.GetGenericArguments().Select(a => a.Name));
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName) continue;
                var member = FindMethodModel(model.Methods, method);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = method.Name,
                        Kind = "Method"
                    };
                    model.Methods.Add(member);
                }
                FillMethodMember(member, method, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(method));
                member.IsExtension = IsExtensionMethod(method);
                if (string.IsNullOrWhiteSpace(member.DisplayName))
                    member.DisplayName = member.Name;
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(method);

                if (member.IsExtension)
                {
                    var targetType = method.GetParameters().FirstOrDefault()?.ParameterType;
                    var targetName = targetType?.FullName?.Replace('+', '.');
                    if (!string.IsNullOrWhiteSpace(targetName))
                    {
                        if (!extensionTargets.TryGetValue(targetName, out var list))
                        {
                            list = new List<ApiMemberModel>();
                            extensionTargets[targetName] = list;
                        }
                        list.Add(CloneMember(member, isExtension: true));
                    }
                }
            }

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var member = FindConstructorModel(model.Constructors, ctor);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = "#ctor",
                        Kind = "Constructor",
                        IsConstructor = true,
                        DisplayName = model.Name
                    };
                    model.Constructors.Add(member);
                }
                FillConstructorMember(member, ctor, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(ctor));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(ctor);
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var member = FindNamedMember(model.Properties, property.Name);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = property.Name,
                        Kind = "Property"
                    };
                    model.Properties.Add(member);
                }
                FillPropertyMember(member, property, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(property));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(property);
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (field.IsSpecialName || field.Name == "value__") continue;
                var member = FindNamedMember(model.Fields, field.Name);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = field.Name,
                        Kind = "Field"
                    };
                    model.Fields.Add(member);
                }
                FillFieldMember(member, field, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(field));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(field);
            }

            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var member = FindNamedMember(model.Events, evt.Name);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = evt.Name,
                        Kind = "Event"
                    };
                    model.Events.Add(member);
                }
                FillEventMember(member, evt, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(evt));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(evt);
            }
        }

        foreach (var kvp in extensionTargets)
        {
            if (!doc.Types.TryGetValue(kvp.Key, out var targetModel)) continue;
            foreach (var extension in kvp.Value)
            {
                if (!targetModel.ExtensionMethods.Any(m => string.Equals(m.Signature, extension.Signature, StringComparison.OrdinalIgnoreCase)))
                    targetModel.ExtensionMethods.Add(extension);
            }
        }
    }

    private static ApiMemberModel? FindNamedMember(List<ApiMemberModel> members, string name)
    {
        return members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static ApiMemberModel? FindMethodModel(List<ApiMemberModel> members, MethodInfo method)
    {
        var candidates = members
            .Where(m => string.Equals(m.Name, method.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        var parameters = method.GetParameters();
        foreach (var candidate in candidates)
        {
            if (candidate.Parameters.Count != parameters.Length) continue;
            if (ParamsMatch(candidate.Parameters, parameters)) return candidate;
        }

        return candidates.FirstOrDefault(c => c.Parameters.Count == parameters.Length) ?? candidates.First();
    }

    private static ApiMemberModel? FindConstructorModel(List<ApiMemberModel> members, ConstructorInfo ctor)
    {
        var candidates = members
            .Where(m => m.IsConstructor || string.Equals(m.Name, "#ctor", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        var parameters = ctor.GetParameters();
        foreach (var candidate in candidates)
        {
            if (candidate.Parameters.Count != parameters.Length) continue;
            if (ParamsMatch(candidate.Parameters, parameters)) return candidate;
        }

        return candidates.FirstOrDefault(c => c.Parameters.Count == parameters.Length) ?? candidates.First();
    }

    private static bool ParamsMatch(List<ApiParameterModel> parameters, ParameterInfo[] infos)
    {
        if (parameters.Count != infos.Length) return false;
        for (var i = 0; i < parameters.Count; i++)
        {
            var left = NormalizeTypeName(parameters[i].Type);
            var right = NormalizeTypeName(GetReadableTypeName(infos[i].ParameterType));
            if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static string NormalizeTypeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var name = value.Trim();
        if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(7);
        name = name.Replace("+", ".");
        name = name.Replace("{", "<").Replace("}", ">");
        name = GenericArityRegex.Replace(name, string.Empty);
        return name.Replace(" ", string.Empty);
    }

    private static void MergeTypeParameters(List<ApiTypeParameterModel> target, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (target.Any(tp => string.Equals(tp.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            target.Add(new ApiTypeParameterModel { Name = name });
        }
    }

    private static string GetAccessModifier(MethodBase method)
    {
        if (method.IsPublic) return "public";
        if (method.IsFamily && method.IsAssembly) return "private protected";
        if (method.IsFamilyOrAssembly) return "protected internal";
        if (method.IsFamily) return "protected";
        if (method.IsAssembly) return "internal";
        return "private";
    }

    private static string GetAccessModifier(FieldInfo field)
    {
        if (field.IsPublic) return "public";
        if (field.IsFamily && field.IsAssembly) return "private protected";
        if (field.IsFamilyOrAssembly) return "protected internal";
        if (field.IsFamily) return "protected";
        if (field.IsAssembly) return "internal";
        return "private";
    }

    private static MethodInfo? GetMostVisibleAccessor(MethodInfo? first, MethodInfo? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return GetAccessRank(first) >= GetAccessRank(second) ? first : second;
    }

    private static int GetAccessRank(MethodBase method)
    {
        if (method.IsPublic) return 5;
        if (method.IsFamilyOrAssembly) return 4;
        if (method.IsFamily) return 3;
        if (method.IsAssembly) return 2;
        if (method.IsFamily && method.IsAssembly) return 1;
        return 0;
    }

    private static List<string> GetMethodModifiers(MethodInfo method)
    {
        var modifiers = new List<string>();
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsAbstract) modifiers.Add("abstract");
        else if (method.IsVirtual && method.GetBaseDefinition() != method) modifiers.Add("override");
        else if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsFinal && method.IsVirtual && method.GetBaseDefinition() != method) modifiers.Add("sealed");
        if (IsAsync(method)) modifiers.Add("async");
        return modifiers;
    }

    private static List<string> GetConstructorModifiers(ConstructorInfo ctor)
    {
        var modifiers = new List<string>();
        if (ctor.IsStatic) modifiers.Add("static");
        return modifiers;
    }

    private static List<string> GetPropertyModifiers(PropertyInfo property)
    {
        var accessor = GetMostVisibleAccessor(property.GetMethod, property.SetMethod);
        if (accessor is null) return new List<string>();
        var modifiers = GetMethodModifiers(accessor);
        modifiers.Remove("async");
        return modifiers;
    }

    private static List<string> GetEventModifiers(EventInfo evt)
    {
        var accessor = evt.AddMethod ?? evt.RemoveMethod;
        if (accessor is null) return new List<string>();
        var modifiers = GetMethodModifiers(accessor);
        modifiers.Remove("async");
        return modifiers;
    }

    private static List<string> GetFieldModifiers(FieldInfo field)
    {
        var modifiers = new List<string>();
        if (field.IsStatic && !field.IsLiteral) modifiers.Add("static");
        if (field.IsLiteral) modifiers.Add("const");
        else if (field.IsInitOnly) modifiers.Add("readonly");
        return modifiers;
    }

    private static bool IsAsync(MethodInfo method)
        => method.GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false).Length > 0;

    private static void FillMethodMember(ApiMemberModel member, MethodInfo method, Type declaring)
    {
        member.Kind = "Method";
        member.ReturnType = GetReadableTypeName(method.ReturnType);
        member.Signature = BuildMethodSignature(method);
        member.IsStatic = method.IsStatic;
        member.DeclaringType = method.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = method.DeclaringType != declaring;
        member.Access = GetAccessModifier(method);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetMethodModifiers(method));
        if (method.IsGenericMethodDefinition || method.IsGenericMethod)
            MergeTypeParameters(member.TypeParameters, method.GetGenericArguments().Select(a => a.Name));

        var parameters = method.GetParameters();
        if (member.Parameters.Count == 0)
        {
            member.Parameters = parameters.Select(BuildParameterModel).ToList();
        }
        else
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i >= member.Parameters.Count) break;
                ApplyParameterMetadata(member.Parameters[i], parameters[i]);
            }
        }
    }

    private static void FillConstructorMember(ApiMemberModel member, ConstructorInfo ctor, Type declaring)
    {
        member.Kind = "Constructor";
        member.IsConstructor = true;
        member.ReturnType = null;
        member.Signature = BuildConstructorSignature(ctor, declaring);
        member.IsStatic = ctor.IsStatic;
        member.DeclaringType = ctor.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = false;
        member.Access = GetAccessModifier(ctor);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetConstructorModifiers(ctor));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = StripGenericArity(declaring.Name);

        var parameters = ctor.GetParameters();
        if (member.Parameters.Count == 0)
        {
            member.Parameters = parameters.Select(BuildParameterModel).ToList();
        }
        else
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i >= member.Parameters.Count) break;
                ApplyParameterMetadata(member.Parameters[i], parameters[i]);
            }
        }
    }

    private static void FillPropertyMember(ApiMemberModel member, PropertyInfo property, Type declaring)
    {
        member.Kind = "Property";
        member.ReturnType = GetReadableTypeName(property.PropertyType);
        member.Signature = BuildPropertySignature(property);
        member.IsStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic == true;
        member.DeclaringType = property.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = property.DeclaringType != declaring;
        var accessor = GetMostVisibleAccessor(property.GetMethod, property.SetMethod);
        if (accessor is not null)
            member.Access = GetAccessModifier(accessor);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetPropertyModifiers(property));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = property.Name;
    }

    private static void FillFieldMember(ApiMemberModel member, FieldInfo field, Type declaring)
    {
        member.Kind = "Field";
        member.ReturnType = GetReadableTypeName(field.FieldType);
        member.Signature = BuildFieldSignature(field);
        member.IsStatic = field.IsStatic;
        member.DeclaringType = field.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = field.DeclaringType != declaring;
        member.Access = GetAccessModifier(field);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetFieldModifiers(field));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = field.Name;
        if (field.IsLiteral && field.GetRawConstantValue() is { } value)
            member.Value = value.ToString();
    }

    private static void FillEventMember(ApiMemberModel member, EventInfo evt, Type declaring)
    {
        member.Kind = "Event";
        member.ReturnType = evt.EventHandlerType is null ? null : GetReadableTypeName(evt.EventHandlerType);
        member.Signature = BuildEventSignature(evt);
        member.IsStatic = evt.AddMethod?.IsStatic == true;
        member.DeclaringType = evt.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = evt.DeclaringType != declaring;
        var accessor = evt.AddMethod ?? evt.RemoveMethod;
        if (accessor is not null)
            member.Access = GetAccessModifier(accessor);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetEventModifiers(evt));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = evt.Name;
    }

    private static bool IsExtensionMethod(MethodInfo method)
        => method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
}
