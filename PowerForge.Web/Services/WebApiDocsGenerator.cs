using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Options for API documentation generation.</summary>
public sealed class WebApiDocsOptions
{
    /// <summary>Path to the XML documentation file.</summary>
    public string XmlPath { get; set; } = string.Empty;
    /// <summary>Optional assembly path for version metadata.</summary>
    public string? AssemblyPath { get; set; }
    /// <summary>Output directory for generated docs.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Documentation title.</summary>
    public string Title { get; set; } = "API Reference";
    /// <summary>Base URL for API documentation routes.</summary>
    public string BaseUrl { get; set; } = "/api";
    /// <summary>Output format hint (json, html, hybrid, both).</summary>
    public string? Format { get; set; }
    /// <summary>Optional stylesheet href for HTML output.</summary>
    public string? CssHref { get; set; }
    /// <summary>Optional path to header HTML fragment.</summary>
    public string? HeaderHtmlPath { get; set; }
    /// <summary>Optional path to footer HTML fragment.</summary>
    public string? FooterHtmlPath { get; set; }
    /// <summary>Optional nav config path (site.json or site-nav.json) for header/footer tokens.</summary>
    public string? NavJsonPath { get; set; }
    /// <summary>Optional site display name override.</summary>
    public string? SiteName { get; set; }
    /// <summary>Optional brand URL override.</summary>
    public string? BrandUrl { get; set; }
    /// <summary>Optional brand icon URL override.</summary>
    public string? BrandIcon { get; set; }
    /// <summary>Optional HTML template name (simple, docs).</summary>
    public string? Template { get; set; }
    /// <summary>Optional list of namespace prefixes to include.</summary>
    public List<string> IncludeNamespacePrefixes { get; } = new();
    /// <summary>Optional list of namespace prefixes to exclude.</summary>
    public List<string> ExcludeNamespacePrefixes { get; } = new();
    /// <summary>Optional list of type full names to include.</summary>
    public List<string> IncludeTypeNames { get; } = new();
    /// <summary>Optional list of type full names to exclude.</summary>
    public List<string> ExcludeTypeNames { get; } = new();
}

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static class WebApiDocsGenerator
{
    /// <summary>Generates API documentation output.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Result payload describing generated artifacts.</returns>
    public static WebApiDocsResult Generate(WebApiDocsOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.XmlPath))
            throw new ArgumentException("XmlPath is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var xmlPath = Path.GetFullPath(options.XmlPath);
        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(outputPath);

        Assembly? assembly = null;
        if (!string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(options.AssemblyPath);
                assembly = Assembly.Load(bytes);
            }
            catch
            {
                assembly = null;
            }
        }

        var apiDoc = ParseXml(xmlPath, assembly, options);
        if (apiDoc.Types.Count == 0 && assembly is not null)
        {
            PopulateFromAssembly(apiDoc, assembly);
        }
        var assemblyName = apiDoc.AssemblyName;
        var assemblyVersion = apiDoc.AssemblyVersion;

        if (!string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
        {
            try
            {
                var assemblyNameInfo = System.Reflection.AssemblyName.GetAssemblyName(options.AssemblyPath);
                if (!string.IsNullOrWhiteSpace(assemblyNameInfo.Name))
                    assemblyName = assemblyNameInfo.Name;
                if (assemblyNameInfo.Version is not null)
                    assemblyVersion = assemblyNameInfo.Version.ToString();
            }
            catch
            {
                // ignore assembly inspection errors
            }
        }

        var types = apiDoc.Types.Values
            .Where(t => ShouldIncludeType(t, options))
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var index = new Dictionary<string, object?>
        {
            ["title"] = options.Title,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["assembly"] = new Dictionary<string, object?>
            {
                ["assemblyName"] = assemblyName ?? string.Empty,
                ["assemblyVersion"] = assemblyVersion
            },
            ["typeCount"] = types.Count,
            ["types"] = types.Select(t => new Dictionary<string, object?>
            {
                ["name"] = t.Name,
                ["fullName"] = t.FullName,
                ["namespace"] = t.Namespace,
                ["kind"] = t.Kind,
                ["slug"] = t.Slug,
                ["summary"] = t.Summary
            }).ToList()
        };

        var indexPath = Path.Combine(outputPath, "index.json");
        WriteJson(indexPath, index);

        var search = types.Select(t => new Dictionary<string, object?>
        {
            ["title"] = t.FullName,
            ["summary"] = t.Summary ?? string.Empty,
            ["kind"] = t.Kind,
            ["namespace"] = t.Namespace,
            ["slug"] = t.Slug,
            ["url"] = $"{options.BaseUrl.TrimEnd('/')}/types/{t.Slug}.json"
        }).ToList();

        var searchPath = Path.Combine(outputPath, "search.json");
        WriteJson(searchPath, search);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var typeModel = new Dictionary<string, object?>
            {
                ["name"] = type.Name,
                ["fullName"] = type.FullName,
                ["namespace"] = type.Namespace,
                ["kind"] = type.Kind,
                ["slug"] = type.Slug,
                ["summary"] = type.Summary,
                ["remarks"] = type.Remarks,
                ["methods"] = type.Methods.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["summary"] = m.Summary,
                    ["returns"] = m.Returns,
                    ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["summary"] = p.Summary
                    }).ToList()
                }).ToList(),
                ["properties"] = type.Properties.Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["summary"] = p.Summary
                }).ToList(),
                ["fields"] = type.Fields.Select(f => new Dictionary<string, object?>
                {
                    ["name"] = f.Name,
                    ["summary"] = f.Summary
                }).ToList(),
                ["events"] = type.Events.Select(e => new Dictionary<string, object?>
                {
                    ["name"] = e.Name,
                    ["summary"] = e.Summary
                }).ToList()
            };

            var typePath = Path.Combine(typesDir, $"{type.Slug}.json");
            WriteJson(typePath, typeModel);
        }

        var format = (options.Format ?? "json").Trim().ToLowerInvariant();
        if (format is "hybrid" or "html" or "both")
        {
            GenerateHtml(outputPath, options, types);
        }

        return new WebApiDocsResult
        {
            OutputPath = outputPath,
            IndexPath = indexPath,
            SearchPath = searchPath,
            TypesPath = typesDir,
            TypeCount = types.Count
        };
    }

    private static ApiDocModel ParseXml(string xmlPath, Assembly? assembly, WebApiDocsOptions options)
    {
        var apiDoc = new ApiDocModel();
        if (!File.Exists(xmlPath))
            return apiDoc;

        using var stream = File.OpenRead(xmlPath);
        var doc = XDocument.Load(stream);
        var docElement = doc.Element("doc");
        if (docElement is null) return apiDoc;

        var assemblyElement = docElement.Element("assembly");
        if (assemblyElement is not null)
        {
            apiDoc.AssemblyName = assemblyElement.Element("name")?.Value ?? string.Empty;
        }

        var members = docElement.Element("members");
        if (members is null) return apiDoc;

        foreach (var member in members.Elements("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;

            var prefix = name[0];
            var fullName = name.Substring(2);

            switch (prefix)
            {
                case 'T':
                    var type = ParseType(member, fullName);
                    apiDoc.Types[type.FullName] = type;
                    break;
                case 'M':
                    AddMethod(apiDoc, member, fullName, assembly);
                    break;
                case 'P':
                    AddProperty(apiDoc, member, fullName);
                    break;
                case 'F':
                    AddField(apiDoc, member, fullName);
                    break;
                case 'E':
                    AddEvent(apiDoc, member, fullName);
                    break;
            }
        }

        return apiDoc;
    }

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

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName) continue;
                model.Methods.Add(new ApiMemberModel
                {
                    Name = method.Name,
                    Parameters = method.GetParameters().Select(p => new ApiParameterModel
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
                    Name = property.Name
                });
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (field.IsSpecialName) continue;
                model.Fields.Add(new ApiMemberModel
                {
                    Name = field.Name
                });
            }

            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                model.Events.Add(new ApiMemberModel
                {
                    Name = evt.Name
                });
            }

            doc.Types[fullName] = model;
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
            return ex.Types ?? Array.Empty<Type?>();
        }
        catch
        {
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

    private static ApiTypeModel ParseType(XElement member, string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        var ns = lastDot > 0 ? fullName.Substring(0, lastDot) : string.Empty;
        var name = lastDot > 0 ? fullName.Substring(lastDot + 1) : fullName;

        return new ApiTypeModel
        {
            Name = name,
            FullName = fullName,
            Namespace = ns,
            Summary = GetSummary(member),
            Remarks = GetElement(member, "remarks"),
            Kind = InferTypeKind(name),
            Slug = Slugify(fullName)
        };
    }

    private static void AddMethod(ApiDocModel doc, XElement member, string fullName, Assembly? assembly)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var parameterTypes = ParseParameterTypes(fullName);
        var parameterNames = TryResolveParameterNames(assembly, typeName, name, parameterTypes);
        var parameters = ParseParameters(member, parameterTypes, parameterNames);

        type.Methods.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member),
            Parameters = parameters,
            Returns = GetElement(member, "returns")
        });
    }

    private static void AddProperty(ApiDocModel doc, XElement member, string fullName)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        type.Properties.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member)
        });
    }

    private static void AddField(ApiDocModel doc, XElement member, string fullName)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        type.Fields.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member)
        });
    }

    private static void AddEvent(ApiDocModel doc, XElement member, string fullName)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        type.Events.Add(new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member)
        });
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

    private static List<ApiParameterModel> ParseParameters(XElement member, IReadOnlyList<string> parameterTypes, IReadOnlyList<string>? parameterNames)
    {
        var results = new List<ApiParameterModel>();
        var paramElements = member.Elements("param").ToList();
        var count = Math.Max(paramElements.Count, parameterTypes.Count);
        for (var i = 0; i < count; i++)
        {
            var paramName = i < paramElements.Count
                ? paramElements[i].Attribute("name")?.Value ?? $"arg{i + 1}"
                : (parameterNames != null && i < parameterNames.Count && !string.IsNullOrWhiteSpace(parameterNames[i])
                    ? parameterNames[i]
                    : $"arg{i + 1}");
            var summary = i < paramElements.Count ? Normalize(paramElements[i].Value) : null;
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
        if (lookupName == "#ctor")
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
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

    private static bool TryParseGenericParameterToken(string typeName, out bool isMethodParameter, out int position)
    {
        isMethodParameter = false;
        position = -1;
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (typeName.StartsWith("``", StringComparison.Ordinal))
        {
            isMethodParameter = true;
            return int.TryParse(typeName.Substring(2), out position);
        }
        if (typeName.StartsWith("`", StringComparison.Ordinal))
        {
            isMethodParameter = false;
            return int.TryParse(typeName.Substring(1), out position);
        }
        return false;
    }

    private static List<string> SplitTypeArguments(string argsText)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(argsText)) return results;
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in argsText)
        {
            if (ch == '{' || ch == '[')
                depth++;
            if (ch == '}' || ch == ']')
                depth = Math.Max(0, depth - 1);

            if (ch == ',' && depth == 0)
            {
                results.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) results.Add(sb.ToString().Trim());
        return results;
    }

    private static Type? ResolveType(Assembly assembly, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var candidate = fullName;
        var type = assembly.GetType(candidate) ?? Type.GetType(candidate);
        if (type is not null) return type;

        while (true)
        {
            var lastDot = candidate.LastIndexOf('.');
            if (lastDot <= 0) break;
            candidate = candidate.Substring(0, lastDot) + "+" + candidate.Substring(lastDot + 1);
            type = assembly.GetType(candidate) ?? Type.GetType(candidate);
            if (type is not null) return type;
        }

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = loaded.GetType(fullName);
            if (type is not null) return type;
        }

        return null;
    }

    private static string StripGenericArity(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        return Regex.Replace(name, "`{1,2}\\d+", string.Empty);
    }

    private static List<string> ParseParameterTypes(string fullName)
    {
        var start = fullName.IndexOf('(');
        if (start < 0) return new List<string>();
        var end = fullName.LastIndexOf(')');
        if (end <= start) return new List<string>();
        var segment = fullName.Substring(start + 1, end - start - 1);
        if (string.IsNullOrWhiteSpace(segment)) return new List<string>();

        var results = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in segment)
        {
            if (ch == '{' || ch == '[')
                depth++;
            if (ch == '}' || ch == ']')
                depth = Math.Max(0, depth - 1);

            if (ch == ',' && depth == 0)
            {
                results.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) results.Add(sb.ToString().Trim());
        return results;
    }

    private static string? GetSummary(XElement member)
    {
        var summary = member.Element("summary");
        return summary is null ? null : NormalizeXmlText(summary);
    }

    private static string? GetElement(XElement member, string name)
    {
        var element = member.Element(name);
        return element is null ? null : NormalizeXmlText(element);
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static string? NormalizeXmlText(XElement element)
    {
        var text = string.Concat(element.Nodes().Select(n =>
        {
            if (n is XText txt) return txt.Value;
            if (n is XElement el)
            {
                return el.Name.LocalName switch
                {
                    "see" => RenderCref(el),
                    "seealso" => RenderCref(el),
                    "paramref" => el.Attribute("name")?.Value ?? el.Value,
                    "typeparamref" => el.Attribute("name")?.Value ?? el.Value,
                    "c" => el.Value,
                    "code" => $" {el.Value} ",
                    "para" => $" {el.Value} ",
                    _ => el.Value
                };
            }
            return string.Empty;
        }));

        return string.IsNullOrWhiteSpace(text) ? null : Normalize(text);
    }

    private static string RenderCref(XElement el)
    {
        var cref = el.Attribute("cref")?.Value;
        if (!string.IsNullOrWhiteSpace(cref))
        {
            var cleaned = cref;
            var colonIdx = cleaned.IndexOf(':');
            if (colonIdx >= 0 && colonIdx + 1 < cleaned.Length)
                cleaned = cleaned.Substring(colonIdx + 1);
            return cleaned;
        }

        var langword = el.Attribute("langword")?.Value;
        if (!string.IsNullOrWhiteSpace(langword))
            return langword;

        return el.Value;
    }

    private static string InferTypeKind(string name)
    {
        if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
            return "Interface";
        return "Class";
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var slug = value.ToLowerInvariant();
        slug = slug.Replace('+', '-');
        slug = Regex.Replace(slug, "`\\d+", string.Empty);
        slug = Regex.Replace(slug, "[^a-z0-9]+", "-");
        slug = Regex.Replace(slug, "-{2,}", "-").Trim('-');
        return slug;
    }

    private static void WriteJson(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static void GenerateHtml(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types)
    {
        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        if (template is "docs" or "sidebar")
        {
            GenerateDocsHtml(outputPath, options, types);
            return;
        }

        var header = LoadOptionalHtml(options.HeaderHtmlPath);
        var footer = LoadOptionalHtml(options.FooterHtmlPath);
        ApplyNavTokens(options, ref header, ref footer);
        var cssLink = string.IsNullOrWhiteSpace(options.CssHref) ? string.Empty : $"<link rel=\"stylesheet\" href=\"{options.CssHref}\" />";
        var fallbackCss = LoadEmbeddedRaw("fallback.css");
        var cssBlock = string.IsNullOrWhiteSpace(cssLink)
            ? WrapStyle(fallbackCss)
            : cssLink;

        var indexTemplate = LoadEmbeddedRaw("index.html");
        var typeLinks = new StringBuilder();
        foreach (var type in types)
        {
            typeLinks.AppendLine($"      <a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }
        var searchScript = WrapScript(LoadEmbeddedRaw("search.js"));
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
            ["CSS"] = cssBlock,
            ["HEADER"] = header,
            ["FOOTER"] = footer,
            ["TYPE_COUNT"] = types.Count.ToString(),
            ["TYPE_LINKS"] = typeLinks.ToString().TrimEnd(),
            ["SEARCH_SCRIPT"] = searchScript
        });

        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var memberHtml = new StringBuilder();
            AppendMembers(memberHtml, "Methods", type.Methods);
            AppendMembers(memberHtml, "Properties", type.Properties);
            AppendMembers(memberHtml, "Fields", type.Fields);
            AppendMembers(memberHtml, "Events", type.Events);

            var summaryHtml = string.IsNullOrWhiteSpace(type.Summary)
                ? string.Empty
                : $"    <p>{System.Web.HttpUtility.HtmlEncode(type.Summary)}</p>";
            var remarksHtml = string.IsNullOrWhiteSpace(type.Remarks)
                ? string.Empty
                : $"    <div class=\"pf-api-remarks\">{System.Web.HttpUtility.HtmlEncode(type.Remarks)}</div>";

            var typeTitle = $"{type.FullName} - {options.Title}";
            var typeTemplate = LoadEmbeddedRaw("type.html");
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TYPE_TITLE"] = System.Web.HttpUtility.HtmlEncode(typeTitle),
                ["TYPE_FULLNAME"] = System.Web.HttpUtility.HtmlEncode(type.FullName),
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["TYPE_SUMMARY"] = summaryHtml,
                ["TYPE_REMARKS"] = remarksHtml,
                ["MEMBERS"] = memberHtml.ToString().TrimEnd()
            });

            File.WriteAllText(Path.Combine(typesDir, $"{type.Slug}.html"), typeHtml, Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateApiSitemap(sitemapPath, options.BaseUrl, types);
    }

    private static void GenerateDocsHtml(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types)
    {
        var header = LoadOptionalHtml(options.HeaderHtmlPath);
        var footer = LoadOptionalHtml(options.FooterHtmlPath);
        ApplyNavTokens(options, ref header, ref footer);
        var cssLink = string.IsNullOrWhiteSpace(options.CssHref) ? string.Empty : $"<link rel=\"stylesheet\" href=\"{options.CssHref}\" />";
        var fallbackCss = LoadEmbeddedRaw("fallback.css");
        var cssBlock = string.IsNullOrWhiteSpace(cssLink)
            ? WrapStyle(fallbackCss)
            : cssLink;

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "/api" : options.BaseUrl.TrimEnd('/');
        var docsScript = WrapScript(LoadEmbeddedRaw("docs.js"));
        var sidebarHtml = BuildDocsSidebar(types, baseUrl, string.Empty);
        var overviewHtml = BuildDocsOverview(types, baseUrl);

        var indexTemplate = LoadEmbeddedRaw("docs-index.html");
        var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
        {
            ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
            ["CSS"] = cssBlock,
            ["HEADER"] = header,
            ["FOOTER"] = footer,
            ["SIDEBAR"] = sidebarHtml,
            ["MAIN"] = overviewHtml,
            ["DOCS_SCRIPT"] = docsScript
        });
        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        foreach (var type in types)
        {
            var sidebar = BuildDocsSidebar(types, baseUrl, type.Slug);
            var typeMain = BuildDocsTypeDetail(type, baseUrl);
            var typeTemplate = LoadEmbeddedRaw("docs-type.html");
            var pageTitle = $"{type.Name} - {options.Title}";
            var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
            {
                ["TITLE"] = System.Web.HttpUtility.HtmlEncode(pageTitle),
                ["CSS"] = cssBlock,
                ["HEADER"] = header,
                ["FOOTER"] = footer,
                ["SIDEBAR"] = sidebar,
                ["MAIN"] = typeMain,
                ["DOCS_SCRIPT"] = docsScript
            });

            var htmlPath = Path.Combine(outputPath, $"{type.Slug}.html");
            File.WriteAllText(htmlPath, typeHtml, Encoding.UTF8);

            var flatPath = Path.Combine(outputPath, type.Slug);
            File.WriteAllText(flatPath, typeHtml, Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateDocsSitemap(sitemapPath, baseUrl, types);
    }

    private static void AppendMembers(StringBuilder sb, string label, List<ApiMemberModel> members)
    {
        if (members.Count == 0) return;
        sb.AppendLine($"    <section class=\"pf-api-section\">");
        sb.AppendLine($"      <h2>{label}</h2>");
        sb.AppendLine("      <ul>");
        foreach (var member in members)
        {
            var summary = string.IsNullOrWhiteSpace(member.Summary)
                ? string.Empty
                : $" - {System.Web.HttpUtility.HtmlEncode(member.Summary)}";
            sb.AppendLine("        <li>");
            sb.AppendLine($"          <strong>{System.Web.HttpUtility.HtmlEncode(member.Name)}</strong>{summary}");
            if (member.Parameters.Count > 0)
            {
                sb.AppendLine("          <div class=\"pf-api-params\">");
                sb.AppendLine("            <ul>");
                foreach (var param in member.Parameters)
                {
                    var type = string.IsNullOrWhiteSpace(param.Type) ? string.Empty : $" ({System.Web.HttpUtility.HtmlEncode(param.Type)})";
                    var psummary = string.IsNullOrWhiteSpace(param.Summary) ? string.Empty : $": {System.Web.HttpUtility.HtmlEncode(param.Summary)}";
                    sb.AppendLine($"              <li><code>{System.Web.HttpUtility.HtmlEncode(param.Name)}</code>{type}{psummary}</li>");
                }
                sb.AppendLine("            </ul>");
                sb.AppendLine("          </div>");
            }
            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                sb.AppendLine($"          <div class=\"pf-api-returns\">Returns: {System.Web.HttpUtility.HtmlEncode(member.Returns)}</div>");
            }
            sb.AppendLine("        </li>");
        }
        sb.AppendLine("      </ul>");
        sb.AppendLine("    </section>");
    }

    private static readonly string[] MainTypeOrder =
    {
        "QR",
        "Barcode",
        "QrEasy",
        "BarcodeEasy",
        "QrImageDecoder",
        "DataMatrixCode",
        "Pdf417Code",
        "AztecCode"
    };

    private static string BuildDocsSidebar(IReadOnlyList<ApiTypeModel> types, string baseUrl, string activeSlug)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"sidebar-header\">");
        var active = string.IsNullOrWhiteSpace(activeSlug) ? " active" : string.Empty;
        sb.AppendLine($"      <a href=\"{baseUrl}\" class=\"sidebar-title{active}\">");
        sb.AppendLine("        <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"18\" height=\"18\">");
        sb.AppendLine("          <path d=\"M4 19.5A2.5 2.5 0 0 1 6.5 17H20\"/>");
        sb.AppendLine("          <path d=\"M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z\"/>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("        <span>API Reference</span>");
        sb.AppendLine("      </a>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"sidebar-search\">");
        sb.AppendLine("      <svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
        sb.AppendLine("        <circle cx=\"11\" cy=\"11\" r=\"8\"/>");
        sb.AppendLine("        <path d=\"M21 21l-4.35-4.35\"/>");
        sb.AppendLine("      </svg>");
        sb.AppendLine("      <input id=\"api-filter\" type=\"text\" placeholder=\"Filter types...\" />");
        sb.AppendLine("      <button class=\"clear-search\" type=\"button\" aria-label=\"Clear search\">");
        sb.AppendLine("        <svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
        sb.AppendLine("          <path d=\"M18 6L6 18M6 6l12 12\"/>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("      </button>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <nav class=\"sidebar-nav\">");

        var mainTypes = GetMainTypes(types);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <div class=\"nav-section\">");
            sb.AppendLine("        <div class=\"nav-section-header main-api\">");
            sb.AppendLine("          <svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
            sb.AppendLine("            <path d=\"M9 18l6-6-6-6\"/>");
            sb.AppendLine("          </svg>");
            sb.AppendLine("          <span>Main API</span>");
            sb.AppendLine($"          <span class=\"type-count\">{mainTypes.Count}</span>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"nav-section-content\">");
            foreach (var type in mainTypes)
            {
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug));
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");
        }

        var grouped = types
            .Where(t => !IsMainType(t.Name))
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in grouped)
        {
            sb.AppendLine("      <div class=\"nav-section\">");
            sb.AppendLine("        <div class=\"nav-section-header\">");
            sb.AppendLine("          <svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
            sb.AppendLine("            <path d=\"M9 18l6-6-6-6\"/>");
            sb.AppendLine("          </svg>");
            sb.AppendLine($"          <span>{System.Web.HttpUtility.HtmlEncode(GetShortNamespace(group.Key))}</span>");
            sb.AppendLine($"          <span class=\"type-count\">{group.Count()}</span>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"nav-section-content\">");
            foreach (var type in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug));
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");
        }

        sb.AppendLine("    </nav>");
        sb.AppendLine("    <div class=\"sidebar-footer\">");
        sb.AppendLine("      <a href=\"/docs\" class=\"back-link\">");
        sb.AppendLine("        <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"14\" height=\"14\">");
        sb.AppendLine("          <path d=\"M19 12H5M12 19l-7-7 7-7\"/>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("        Back to Docs");
        sb.AppendLine("      </a>");
        sb.AppendLine("    </div>");
        return sb.ToString().TrimEnd();
    }

    private static string BuildSidebarTypeItem(ApiTypeModel type, string baseUrl, string activeSlug)
    {
        var active = string.Equals(activeSlug, type.Slug, StringComparison.OrdinalIgnoreCase) ? " active" : string.Empty;
        var search = $"{type.Name} {type.FullName} {type.Summary}".Trim();
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var name = System.Web.HttpUtility.HtmlEncode(type.Name);
        var kind = NormalizeKind(type.Kind);
        var icon = GetTypeIcon(type.Kind);
        return $"          <a href=\"{baseUrl}/{type.Slug}\" class=\"type-item{active}\" data-search=\"{searchAttr}\">" +
               $"<span class=\"type-icon {kind}\">{icon}</span><span class=\"type-name\">{name}</span></a>";
    }

    private static string BuildDocsOverview(IReadOnlyList<ApiTypeModel> types, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"api-overview\">");
        sb.AppendLine("      <h1>API Reference</h1>");
        sb.AppendLine("      <p class=\"lead\">Complete API documentation for CodeGlyphX, auto-generated from XML documentation.</p>");

        var mainTypes = GetMainTypes(types);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <section class=\"quick-start\">");
            sb.AppendLine("        <h2>Quick Start</h2>");
            sb.AppendLine("        <p class=\"section-desc\">The most commonly used classes for generating QR codes and barcodes.</p>");
            sb.AppendLine("        <div class=\"quick-grid\">");
            foreach (var type in mainTypes.Take(6))
            {
                var summary = Truncate(type.Summary, 100);
                sb.AppendLine($"          <a href=\"{baseUrl}/{type.Slug}\" class=\"quick-card\">");
                sb.AppendLine("            <div class=\"quick-card-header\">");
                sb.AppendLine($"              <span class=\"type-icon large {NormalizeKind(type.Kind)}\">{GetTypeIcon(type.Kind)}</span>");
                sb.AppendLine($"              <strong>{System.Web.HttpUtility.HtmlEncode(type.Name)}</strong>");
                sb.AppendLine("            </div>");
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    sb.AppendLine($"            <p>{System.Web.HttpUtility.HtmlEncode(summary)}</p>");
                }
                sb.AppendLine("          </a>");
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </section>");
        }

        sb.AppendLine("      <section class=\"all-namespaces\">");
        sb.AppendLine("        <h2>All Namespaces</h2>");
        sb.AppendLine($"        <p class=\"section-desc\">Browse all {types.Count} types organized by namespace.</p>");
        foreach (var group in types.GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine("        <div class=\"namespace-group\">");
            var nsLabel = System.Web.HttpUtility.HtmlEncode(group.Key);
            sb.AppendLine($"          <h3>{nsLabel} <span class=\"count\">({group.Count()})</span></h3>");
            sb.AppendLine("          <div class=\"type-chips\">");
            foreach (var type in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var search = $"{type.Name} {type.FullName} {type.Summary}".Trim();
                var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
                sb.AppendLine($"            <a href=\"{baseUrl}/{type.Slug}\" class=\"type-chip {NormalizeKind(type.Kind)}\" data-search=\"{searchAttr}\">");
                sb.AppendLine($"              <span class=\"chip-icon\">{GetTypeIcon(type.Kind)}</span>");
                sb.AppendLine($"              {System.Web.HttpUtility.HtmlEncode(type.Name)}");
                sb.AppendLine("            </a>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </section>");
        sb.AppendLine("    </div>");
        return sb.ToString().TrimEnd();
    }

    private static string BuildDocsTypeDetail(ApiTypeModel type, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <article class=\"type-detail\">");
        sb.AppendLine("      <nav class=\"breadcrumb\">");
        sb.AppendLine($"        <a href=\"{baseUrl}\">API Reference</a>");
        sb.AppendLine("        <span class=\"sep\">/</span>");
        sb.AppendLine($"        <span class=\"current\">{System.Web.HttpUtility.HtmlEncode(type.Name)}</span>");
        sb.AppendLine("      </nav>");

        sb.AppendLine("      <header class=\"type-header\">");
        var kindLabel = string.IsNullOrWhiteSpace(type.Kind) ? "Type" : type.Kind;
        sb.AppendLine("        <div class=\"type-title-row\">");
        sb.AppendLine($"          <span class=\"type-badge {NormalizeKind(type.Kind)}\">{System.Web.HttpUtility.HtmlEncode(kindLabel)}</span>");
        sb.AppendLine($"          <h1>{System.Web.HttpUtility.HtmlEncode(type.Name)}</h1>");
        sb.AppendLine("        </div>");
        sb.AppendLine($"        <code class=\"namespace\">namespace {System.Web.HttpUtility.HtmlEncode(type.Namespace)}</code>");
        sb.AppendLine("      </header>");

        if (!string.IsNullOrWhiteSpace(type.Summary))
            sb.AppendLine($"      <p class=\"type-summary\">{System.Web.HttpUtility.HtmlEncode(type.Summary)}</p>");
        if (!string.IsNullOrWhiteSpace(type.Remarks))
        {
            sb.AppendLine("      <section class=\"remarks\">");
            sb.AppendLine("        <h2>Remarks</h2>");
            sb.AppendLine($"        <p>{System.Web.HttpUtility.HtmlEncode(type.Remarks)}</p>");
            sb.AppendLine("      </section>");
        }

        AppendMemberCards(sb, "Methods", type.Methods);
        AppendMemberCards(sb, "Properties", type.Properties);
        AppendMemberCards(sb, "Fields", type.Fields);
        AppendMemberCards(sb, "Events", type.Events);

        sb.AppendLine("    </article>");
        return sb.ToString().TrimEnd();
    }

    private static void AppendMemberCards(StringBuilder sb, string label, List<ApiMemberModel> members)
    {
        if (members.Count == 0) return;
        sb.AppendLine("      <section class=\"member-section\">");
        sb.AppendLine($"        <h2>{label}</h2>");
        foreach (var member in members)
        {
            sb.AppendLine("        <div class=\"member-card\">");
            sb.AppendLine("          <div class=\"member-header\">");
            sb.AppendLine($"            <code class=\"member-signature\">{System.Web.HttpUtility.HtmlEncode(BuildSignature(member, label))}</code>");
            sb.AppendLine("          </div>");
            if (!string.IsNullOrWhiteSpace(member.Summary))
                sb.AppendLine($"          <p class=\"member-summary\">{System.Web.HttpUtility.HtmlEncode(member.Summary)}</p>");
            if (member.Parameters.Count > 0)
            {
                sb.AppendLine("          <h4>Parameters</h4>");
                sb.AppendLine("          <dl class=\"param-list\">");
                foreach (var param in member.Parameters)
                {
                    sb.AppendLine($"            <dt>{System.Web.HttpUtility.HtmlEncode(param.Name)} <span class=\"param-type\">{System.Web.HttpUtility.HtmlEncode(param.Type)}</span></dt>");
                    if (!string.IsNullOrWhiteSpace(param.Summary))
                        sb.AppendLine($"            <dd>{System.Web.HttpUtility.HtmlEncode(param.Summary)}</dd>");
                }
                sb.AppendLine("          </dl>");
            }
            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                sb.AppendLine("          <h4>Returns</h4>");
                sb.AppendLine($"          <p>{System.Web.HttpUtility.HtmlEncode(member.Returns)}</p>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </section>");
    }

    private static string BuildSignature(ApiMemberModel member, string section)
    {
        if (section != "Methods" || member.Parameters.Count == 0)
            return member.Name;
        var args = member.Parameters
            .Select(p =>
            {
                var type = string.IsNullOrWhiteSpace(p.Type) ? string.Empty : p.Type;
                var name = string.IsNullOrWhiteSpace(p.Name) ? string.Empty : p.Name;
                return string.IsNullOrWhiteSpace(type) ? name : $"{type} {name}".Trim();
            })
            .ToList();
        return $"{member.Name}({string.Join(", ", args)})";
    }

    private static IReadOnlyList<ApiTypeModel> GetMainTypes(IReadOnlyList<ApiTypeModel> types)
    {
        var results = new List<ApiTypeModel>();
        foreach (var name in MainTypeOrder)
        {
            var type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type != null)
                results.Add(type);
        }
        return results;
    }

    private static bool IsMainType(string name)
        => MainTypeOrder.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string GetShortNamespace(string ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return "(global)";
        var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? ns : parts[^1];
    }

    private static string GetTypeIcon(string? kind)
        => kind switch
        {
            "Class" => "C",
            "Struct" => "S",
            "Interface" => "I",
            "Enum" => "E",
            "Delegate" => "D",
            _ => "T"
        };

    private static string NormalizeKind(string? kind)
        => string.IsNullOrWhiteSpace(kind) ? "class" : kind.ToLowerInvariant();

    private static string Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= length ? value : value.Substring(0, length).Trim() + "...";
    }

    private static void GenerateApiSitemap(string outputPath, string baseUrl, IReadOnlyList<ApiTypeModel> types)
    {
        var sb = new StringBuilder();
        var baseTrim = baseUrl.TrimEnd('/');
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var type in types)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseTrim}/types/{type.Slug}.html</loc>");
            sb.AppendLine($"    <lastmod>{today}</lastmod>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("    <priority>0.5</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateDocsSitemap(string outputPath, string baseUrl, IReadOnlyList<ApiTypeModel> types)
    {
        var sb = new StringBuilder();
        var baseTrim = baseUrl.TrimEnd('/');
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var type in types)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseTrim}/{type.Slug}</loc>");
            sb.AppendLine($"    <lastmod>{today}</lastmod>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("    <priority>0.5</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static string LoadOptionalHtml(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        return File.ReadAllText(full);
    }

    private static void ApplyNavTokens(WebApiDocsOptions options, ref string header, ref string footer)
    {
        if (string.IsNullOrWhiteSpace(options.NavJsonPath)) return;
        var nav = LoadNavConfig(options);
        if (nav is null) return;

        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SITE_NAME"] = nav.SiteName,
            ["BRAND_NAME"] = nav.SiteName,
            ["BRAND_URL"] = nav.BrandUrl,
            ["BRAND_ICON"] = nav.BrandIcon,
            ["NAV_LINKS"] = BuildLinkHtml(nav.Primary),
            ["NAV_ACTIONS"] = BuildActionHtml(nav.Actions),
            ["FOOTER_PRODUCT"] = BuildLinkHtml(nav.FooterProduct),
            ["FOOTER_RESOURCES"] = BuildLinkHtml(nav.FooterResources),
            ["FOOTER_COMPANY"] = BuildLinkHtml(nav.FooterCompany),
            ["YEAR"] = DateTime.UtcNow.Year.ToString()
        };

        if (!string.IsNullOrWhiteSpace(header))
            header = ApplyTemplate(header, tokens);
        if (!string.IsNullOrWhiteSpace(footer))
            footer = ApplyTemplate(footer, tokens);
    }

    private static string BuildLinkHtml(IReadOnlyList<NavItem> items)
    {
        if (items.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Href) || string.IsNullOrWhiteSpace(item.Text))
                continue;
            var href = System.Web.HttpUtility.HtmlEncode(item.Href);
            var text = System.Web.HttpUtility.HtmlEncode(item.Text);
            var target = item.Target;
            var rel = item.Rel;
            if (string.IsNullOrWhiteSpace(target) && item.External)
                target = "_blank";
            if (string.IsNullOrWhiteSpace(rel) && item.External)
                rel = "noopener";

            sb.Append("<a href=\"").Append(href).Append("\"");
            if (!string.IsNullOrWhiteSpace(target))
                sb.Append(" target=\"").Append(System.Web.HttpUtility.HtmlEncode(target)).Append("\"");
            if (!string.IsNullOrWhiteSpace(rel))
                sb.Append(" rel=\"").Append(System.Web.HttpUtility.HtmlEncode(rel)).Append("\"");
            sb.Append(">").Append(text).Append("</a>");
        }
        return sb.ToString();
    }

    private static string BuildActionHtml(IReadOnlyList<NavAction> actions)
    {
        if (actions.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var action in actions)
        {
            var isButton = string.Equals(action.Kind, "button", StringComparison.OrdinalIgnoreCase);
            if (!isButton && string.IsNullOrWhiteSpace(action.Href))
                continue;

            var title = action.Title;
            var ariaLabel = string.IsNullOrWhiteSpace(action.AriaLabel) ? title : action.AriaLabel;
            var iconHtml = string.IsNullOrWhiteSpace(action.IconHtml) ? null : action.IconHtml;
            var text = string.IsNullOrWhiteSpace(action.Text) ? null : action.Text;
            var hasIcon = !string.IsNullOrWhiteSpace(iconHtml);
            if (text is null && !hasIcon && !string.IsNullOrWhiteSpace(title))
                text = title;

            if (isButton)
            {
                sb.Append("<button type=\"button\"");
                if (!string.IsNullOrWhiteSpace(action.CssClass))
                    sb.Append(" class=\"").Append(System.Web.HttpUtility.HtmlEncode(action.CssClass)).Append("\"");
                if (!string.IsNullOrWhiteSpace(title))
                    sb.Append(" title=\"").Append(System.Web.HttpUtility.HtmlEncode(title)).Append("\"");
                if (!string.IsNullOrWhiteSpace(ariaLabel))
                    sb.Append(" aria-label=\"").Append(System.Web.HttpUtility.HtmlEncode(ariaLabel)).Append("\"");
                sb.Append(">");
                if (hasIcon)
                    sb.Append(iconHtml);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (hasIcon) sb.Append(" ");
                    sb.Append(System.Web.HttpUtility.HtmlEncode(text));
                }
                sb.Append("</button>");
                continue;
            }

            var href = System.Web.HttpUtility.HtmlEncode(action.Href ?? string.Empty);
            var external = action.External || IsExternal(action.Href ?? string.Empty);
            var target = action.Target;
            var rel = action.Rel;
            if (external && string.IsNullOrWhiteSpace(target))
                target = "_blank";
            if (external && string.IsNullOrWhiteSpace(rel))
                rel = "noopener";

            sb.Append("<a href=\"").Append(href).Append("\"");
            if (!string.IsNullOrWhiteSpace(action.CssClass))
                sb.Append(" class=\"").Append(System.Web.HttpUtility.HtmlEncode(action.CssClass)).Append("\"");
            if (!string.IsNullOrWhiteSpace(target))
                sb.Append(" target=\"").Append(System.Web.HttpUtility.HtmlEncode(target)).Append("\"");
            if (!string.IsNullOrWhiteSpace(rel))
                sb.Append(" rel=\"").Append(System.Web.HttpUtility.HtmlEncode(rel)).Append("\"");
            if (!string.IsNullOrWhiteSpace(title))
                sb.Append(" title=\"").Append(System.Web.HttpUtility.HtmlEncode(title)).Append("\"");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                sb.Append(" aria-label=\"").Append(System.Web.HttpUtility.HtmlEncode(ariaLabel)).Append("\"");
            sb.Append(">");
            if (hasIcon)
                sb.Append(iconHtml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (hasIcon) sb.Append(" ");
                sb.Append(System.Web.HttpUtility.HtmlEncode(text));
            }
            sb.Append("</a>");
        }
        return sb.ToString();
    }

    private static string LoadEmbeddedRaw(string fileName)
    {
        var assembly = typeof(WebApiDocsGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Assets.ApiDocs.{fileName}", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(resourceName)) return string.Empty;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static NavConfig? LoadNavConfig(WebApiDocsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.NavJsonPath)) return null;
        var path = Path.GetFullPath(options.NavJsonPath);
        if (!File.Exists(path)) return null;

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var nav = new NavConfig
        {
            SiteName = options.SiteName ?? string.Empty,
            BrandUrl = string.IsNullOrWhiteSpace(options.BrandUrl) ? "/" : options.BrandUrl,
            BrandIcon = string.IsNullOrWhiteSpace(options.BrandIcon) ? "/codeglyphx-qr-icon.png" : options.BrandIcon
        };

        if (root.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            nav.SiteName = nameProp.GetString() ?? nav.SiteName;
        if (root.TryGetProperty("siteName", out var siteProp) && siteProp.ValueKind == JsonValueKind.String)
            nav.SiteName = siteProp.GetString() ?? nav.SiteName;

        if (root.TryGetProperty("Head", out var headProp) && headProp.ValueKind == JsonValueKind.Object)
        {
            if (headProp.TryGetProperty("Links", out var linksProp) && linksProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in linksProp.EnumerateArray())
                {
                    if (!link.TryGetProperty("Rel", out var relProp) || relProp.ValueKind != JsonValueKind.String)
                        continue;
                    var rel = relProp.GetString() ?? string.Empty;
                    if (!rel.Equals("icon", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!link.TryGetProperty("Href", out var hrefProp) || hrefProp.ValueKind != JsonValueKind.String)
                        continue;
                    var href = hrefProp.GetString();
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        nav.BrandIcon = href;
                        break;
                    }
                }
            }
        }

        if (root.TryGetProperty("Navigation", out var navProp) && navProp.ValueKind == JsonValueKind.Object)
        {
            ParseSiteNavigation(navProp, nav);
            return nav;
        }

        if (root.TryGetProperty("primary", out var primaryProp) && primaryProp.ValueKind == JsonValueKind.Array)
        {
            nav.Primary = ParseNavItems(primaryProp);
        }

        if (root.TryGetProperty("footer", out var footerProp) && footerProp.ValueKind == JsonValueKind.Object)
        {
            if (footerProp.TryGetProperty("product", out var productProp) && productProp.ValueKind == JsonValueKind.Array)
                nav.FooterProduct = ParseNavItems(productProp);
            if (footerProp.TryGetProperty("resources", out var resourcesProp) && resourcesProp.ValueKind == JsonValueKind.Array)
                nav.FooterResources = ParseNavItems(resourcesProp);
            if (footerProp.TryGetProperty("company", out var companyProp) && companyProp.ValueKind == JsonValueKind.Array)
                nav.FooterCompany = ParseNavItems(companyProp);
        }

        if (root.TryGetProperty("actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
            nav.Actions = ParseSiteNavActions(actionsProp);

        return nav;
    }

    private static void ParseSiteNavigation(JsonElement navElement, NavConfig nav)
    {
        if (navElement.TryGetProperty("Menus", out var menusProp) && menusProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var menu in menusProp.EnumerateArray())
            {
                if (!menu.TryGetProperty("Name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameProp.GetString() ?? string.Empty;
                if (!menu.TryGetProperty("Items", out var itemsProp) || itemsProp.ValueKind != JsonValueKind.Array)
                    continue;

                var items = ParseSiteNavItems(itemsProp);
                if (name.Equals("main", StringComparison.OrdinalIgnoreCase))
                    nav.Primary = items;
                else if (name.Equals("footer-product", StringComparison.OrdinalIgnoreCase))
                    nav.FooterProduct = items;
                else if (name.Equals("footer-resources", StringComparison.OrdinalIgnoreCase))
                    nav.FooterResources = items;
                else if (name.Equals("footer-company", StringComparison.OrdinalIgnoreCase))
                    nav.FooterCompany = items;
            }
        }

        if (navElement.TryGetProperty("Actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
            nav.Actions = ParseSiteNavActions(actionsProp);
    }

    private static List<NavItem> ParseNavItems(JsonElement itemsProp)
    {
        var list = new List<NavItem>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            var href = item.TryGetProperty("href", out var hrefProp) && hrefProp.ValueKind == JsonValueKind.String
                ? hrefProp.GetString()
                : null;
            var text = item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String
                ? textProp.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                continue;
            list.Add(new NavItem(href!, text!, IsExternal(href!)));
        }
        return list;
    }

    private static List<NavItem> ParseSiteNavItems(JsonElement itemsProp)
    {
        var list = new List<NavItem>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            var href = item.TryGetProperty("Url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                ? urlProp.GetString()
                : null;
            var text = item.TryGetProperty("Title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                ? titleProp.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                continue;

            var target = item.TryGetProperty("Target", out var targetProp) && targetProp.ValueKind == JsonValueKind.String
                ? targetProp.GetString()
                : null;
            var rel = item.TryGetProperty("Rel", out var relProp) && relProp.ValueKind == JsonValueKind.String
                ? relProp.GetString()
                : null;
            var external = item.TryGetProperty("External", out var extProp) && extProp.ValueKind == JsonValueKind.True;
            external |= IsExternal(href!);
            list.Add(new NavItem(href!, text!, external, target, rel));
        }
        return list;
    }

    private static List<NavAction> ParseSiteNavActions(JsonElement itemsProp)
    {
        var list = new List<NavAction>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            var href = ReadString(item, "Url", "href");
            var title = ReadString(item, "Title", "title");
            var text = ReadString(item, "Text", "text");
            var iconHtml = ReadString(item, "IconHtml", "iconHtml", "Icon", "icon");
            var cssClass = ReadString(item, "CssClass", "class");
            var kind = ReadString(item, "Kind", "kind");
            var ariaLabel = ReadString(item, "AriaLabel", "ariaLabel", "aria");
            var target = ReadString(item, "Target", "target");
            var rel = ReadString(item, "Rel", "rel");
            var external = ReadBool(item, "External", "external");
            if (!string.IsNullOrWhiteSpace(href))
                external |= IsExternal(href);

            list.Add(new NavAction(href, text, title, ariaLabel, iconHtml, cssClass, kind, external, target, rel));
        }
        return list;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static bool ReadBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True)
                return true;
        }
        return false;
    }

    private static bool IsExternal(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        return Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string WrapStyle(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<style>{content}</style>";

    private static string WrapScript(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<script>{content}</script>";

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string?> replacements)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        var result = template;
        foreach (var kvp in replacements)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value ?? string.Empty);
        }
        return result;
    }

    private sealed class ApiDocModel
    {
        public string? AssemblyName { get; set; }
        public string? AssemblyVersion { get; set; }
        public Dictionary<string, ApiTypeModel> Types { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ApiTypeModel
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? Remarks { get; set; }
        public string Kind { get; set; } = "Class";
        public string Slug { get; set; } = string.Empty;
        public List<ApiMemberModel> Methods { get; } = new();
        public List<ApiMemberModel> Properties { get; } = new();
        public List<ApiMemberModel> Fields { get; } = new();
        public List<ApiMemberModel> Events { get; } = new();
    }

    private sealed class ApiMemberModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public List<ApiParameterModel> Parameters { get; set; } = new();
        public string? Returns { get; set; }
    }

    private sealed class ApiParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Summary { get; set; }
    }

    private sealed class NavConfig
    {
        public string SiteName { get; set; } = "Site";
        public string BrandUrl { get; set; } = "/";
        public string BrandIcon { get; set; } = "/codeglyphx-qr-icon.png";
        public List<NavItem> Primary { get; set; } = new();
        public List<NavAction> Actions { get; set; } = new();
        public List<NavItem> FooterProduct { get; set; } = new();
        public List<NavItem> FooterResources { get; set; } = new();
        public List<NavItem> FooterCompany { get; set; } = new();
    }

    private sealed class NavAction
    {
        public NavAction(
            string? href,
            string? text,
            string? title,
            string? ariaLabel,
            string? iconHtml,
            string? cssClass,
            string? kind,
            bool external,
            string? target,
            string? rel)
        {
            Href = href;
            Text = text;
            Title = title;
            AriaLabel = ariaLabel;
            IconHtml = iconHtml;
            CssClass = cssClass;
            Kind = kind;
            External = external;
            Target = target;
            Rel = rel;
        }

        public string? Href { get; }
        public string? Text { get; }
        public string? Title { get; }
        public string? AriaLabel { get; }
        public string? IconHtml { get; }
        public string? CssClass { get; }
        public string? Kind { get; }
        public bool External { get; }
        public string? Target { get; }
        public string? Rel { get; }
    }

    private sealed class NavItem
    {
        public NavItem(string href, string text, bool external, string? target = null, string? rel = null)
        {
            Href = href;
            Text = text;
            External = external;
            Target = target;
            Rel = rel;
        }

        public string Href { get; }
        public string Text { get; }
        public bool External { get; }
        public string? Target { get; }
        public string? Rel { get; }
    }
}
