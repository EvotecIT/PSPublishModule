using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Loader;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Options for API documentation generation.</summary>
public sealed class WebApiDocsOptions
{
    /// <summary>Documentation source type.</summary>
    public ApiDocsType Type { get; set; } = ApiDocsType.CSharp;
    /// <summary>Path to the XML documentation file.</summary>
    public string XmlPath { get; set; } = string.Empty;
    /// <summary>Path to PowerShell help XML or folder.</summary>
    public string? HelpPath { get; set; }
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
    /// <summary>Optional root folder for API docs templates/assets overrides.</summary>
    public string? TemplateRootPath { get; set; }
    /// <summary>Optional override for index template.</summary>
    public string? IndexTemplatePath { get; set; }
    /// <summary>Optional override for type template.</summary>
    public string? TypeTemplatePath { get; set; }
    /// <summary>Optional override for docs index template.</summary>
    public string? DocsIndexTemplatePath { get; set; }
    /// <summary>Optional override for docs type template.</summary>
    public string? DocsTypeTemplatePath { get; set; }
    /// <summary>Optional override for docs script.</summary>
    public string? DocsScriptPath { get; set; }
    /// <summary>Optional override for search script.</summary>
    public string? SearchScriptPath { get; set; }
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
        if (options.Type == ApiDocsType.CSharp && string.IsNullOrWhiteSpace(options.XmlPath))
            throw new ArgumentException("XmlPath is required for CSharp API docs.", nameof(options));
        if (options.Type == ApiDocsType.PowerShell && string.IsNullOrWhiteSpace(options.HelpPath))
            throw new ArgumentException("HelpPath is required for PowerShell API docs.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var xmlPath = options.Type == ApiDocsType.CSharp
            ? Path.GetFullPath(options.XmlPath)
            : string.Empty;
        var helpPath = options.Type == ApiDocsType.PowerShell && !string.IsNullOrWhiteSpace(options.HelpPath)
            ? Path.GetFullPath(options.HelpPath)
            : string.Empty;
        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(outputPath);

        var warnings = new List<string>();
        if (options.Type == ApiDocsType.CSharp && !File.Exists(xmlPath))
            warnings.Add($"XML docs not found: {xmlPath}");
        if (options.Type == ApiDocsType.PowerShell && !File.Exists(helpPath) && !Directory.Exists(helpPath))
            warnings.Add($"PowerShell help not found: {helpPath}");

        Assembly? assembly = null;
        if (options.Type == ApiDocsType.CSharp && !string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
        {
            assembly = TryLoadAssembly(Path.GetFullPath(options.AssemblyPath), warnings);
        }
        else if (options.Type == ApiDocsType.CSharp && !string.IsNullOrWhiteSpace(options.AssemblyPath))
        {
            warnings.Add($"Assembly not found: {options.AssemblyPath}");
        }

        var apiDoc = options.Type == ApiDocsType.PowerShell
            ? ParsePowerShellHelp(helpPath, warnings)
            : ParseXml(xmlPath, assembly, options);
        var usedReflectionFallback = false;
        if (options.Type == ApiDocsType.CSharp && apiDoc.Types.Count == 0 && assembly is not null)
        {
            PopulateFromAssembly(apiDoc, assembly);
            usedReflectionFallback = apiDoc.Types.Count > 0;
            if (!usedReflectionFallback)
                warnings.Add("Reflection fallback produced 0 public types.");
        }
        else if (options.Type == ApiDocsType.CSharp && apiDoc.Types.Count == 0 && assembly is null && !string.IsNullOrWhiteSpace(options.AssemblyPath))
        {
            warnings.Add("Reflection fallback unavailable (assembly could not be loaded).");
        }

        if (options.Type == ApiDocsType.CSharp && assembly is not null)
        {
            EnrichFromAssembly(apiDoc, assembly);
        }
        var assemblyName = apiDoc.AssemblyName;
        var assemblyVersion = apiDoc.AssemblyVersion;

        if (options.Type == ApiDocsType.CSharp && !string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
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
                ["assembly"] = type.Assembly,
                ["baseType"] = type.BaseType,
                ["interfaces"] = type.Interfaces,
                ["attributes"] = type.Attributes,
                ["kind"] = type.Kind,
                ["slug"] = type.Slug,
                ["isStatic"] = type.IsStatic,
                ["isAbstract"] = type.IsAbstract,
                ["isSealed"] = type.IsSealed,
                ["summary"] = type.Summary,
                ["remarks"] = type.Remarks,
                ["methods"] = type.Methods.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["summary"] = m.Summary,
                    ["kind"] = m.Kind,
                    ["signature"] = m.Signature,
                    ["returnType"] = m.ReturnType,
                    ["declaringType"] = m.DeclaringType,
                    ["isInherited"] = m.IsInherited,
                    ["isStatic"] = m.IsStatic,
                    ["isExtension"] = m.IsExtension,
                    ["attributes"] = m.Attributes,
                    ["returns"] = m.Returns,
                    ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["summary"] = p.Summary,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue
                    }).ToList()
                }).ToList(),
                ["properties"] = type.Properties.Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["summary"] = p.Summary,
                    ["kind"] = p.Kind,
                    ["signature"] = p.Signature,
                    ["returnType"] = p.ReturnType,
                    ["declaringType"] = p.DeclaringType,
                    ["isInherited"] = p.IsInherited,
                    ["isStatic"] = p.IsStatic
                }).ToList(),
                ["fields"] = type.Fields.Select(f => new Dictionary<string, object?>
                {
                    ["name"] = f.Name,
                    ["summary"] = f.Summary,
                    ["kind"] = f.Kind,
                    ["signature"] = f.Signature,
                    ["returnType"] = f.ReturnType,
                    ["declaringType"] = f.DeclaringType,
                    ["isInherited"] = f.IsInherited,
                    ["isStatic"] = f.IsStatic,
                    ["value"] = f.Value
                }).ToList(),
                ["events"] = type.Events.Select(e => new Dictionary<string, object?>
                {
                    ["name"] = e.Name,
                    ["summary"] = e.Summary,
                    ["kind"] = e.Kind,
                    ["signature"] = e.Signature,
                    ["returnType"] = e.ReturnType,
                    ["declaringType"] = e.DeclaringType,
                    ["isInherited"] = e.IsInherited,
                    ["isStatic"] = e.IsStatic
                }).ToList(),
                ["extensionMethods"] = type.ExtensionMethods.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["summary"] = m.Summary,
                    ["kind"] = m.Kind,
                    ["signature"] = m.Signature,
                    ["returnType"] = m.ReturnType,
                    ["declaringType"] = m.DeclaringType,
                    ["isInherited"] = m.IsInherited,
                    ["isStatic"] = m.IsStatic,
                    ["isExtension"] = m.IsExtension,
                    ["attributes"] = m.Attributes,
                    ["returns"] = m.Returns,
                    ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["summary"] = p.Summary,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue
                    }).ToList()
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
            TypeCount = types.Count,
            UsedReflectionFallback = usedReflectionFallback,
            Warnings = warnings.ToArray()
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

    private static ApiDocModel ParsePowerShellHelp(string helpPath, List<string> warnings)
    {
        var apiDoc = new ApiDocModel();
        if (string.IsNullOrWhiteSpace(helpPath))
            return apiDoc;

        var resolved = ResolvePowerShellHelpFile(helpPath, warnings);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            return apiDoc;

        var moduleName = Path.GetFileNameWithoutExtension(resolved) ?? string.Empty;
        if (moduleName.EndsWith("-help", StringComparison.OrdinalIgnoreCase))
            moduleName = moduleName[..^5];
        apiDoc.AssemblyName = moduleName;

        XDocument doc;
        try
        {
            doc = XDocument.Load(resolved);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse PowerShell help: {Path.GetFileName(resolved)} ({ex.GetType().Name}: {ex.Message})");
            return apiDoc;
        }

        var commandNs = XNamespace.Get("http://schemas.microsoft.com/maml/dev/command/2004/10");
        var mamlNs = XNamespace.Get("http://schemas.microsoft.com/maml/2004/10");
        var devNs = XNamespace.Get("http://schemas.microsoft.com/maml/dev/2004/10");

        foreach (var command in doc.Descendants(commandNs + "command"))
        {
            var details = command.Element(commandNs + "details");
            var name = details?.Element(commandNs + "name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = command.Element(mamlNs + "name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var summary = GetFirstParagraph(details?.Element(mamlNs + "description"), mamlNs);
            var remarks = JoinParagraphs(command.Element(mamlNs + "description"), mamlNs);
            var returns = JoinReturnValues(command, commandNs, mamlNs);

            var type = new ApiTypeModel
            {
                Name = name!,
                FullName = name!,
                Namespace = moduleName ?? string.Empty,
                Kind = "Cmdlet",
                Slug = Slugify(name!),
                Summary = summary,
                Remarks = remarks
            };

            var syntax = command.Element(commandNs + "syntax");
            if (syntax is not null)
            {
                foreach (var syntaxItem in syntax.Elements(commandNs + "syntaxItem"))
                {
                    var member = new ApiMemberModel
                    {
                        Name = name!,
                        Returns = returns,
                        Kind = "Method"
                    };
                    foreach (var parameter in syntaxItem.Elements(commandNs + "parameter"))
                    {
                        var paramName = parameter.Element(mamlNs + "name")?.Value?.Trim() ?? string.Empty;
                        var paramSummary = JoinParagraphs(parameter.Element(mamlNs + "description"), mamlNs);
                        var paramType = parameter.Element(commandNs + "parameterValue")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(paramType))
                            paramType = parameter.Element(devNs + "type")?.Element(mamlNs + "name")?.Value?.Trim();

                        member.Parameters.Add(new ApiParameterModel
                        {
                            Name = paramName,
                            Type = paramType,
                            Summary = paramSummary
                        });
                    }
                    type.Methods.Add(member);
                }
            }

            apiDoc.Types[type.FullName] = type;
        }

        return apiDoc;
    }

    private static string? ResolvePowerShellHelpFile(string helpPath, List<string> warnings)
    {
        if (File.Exists(helpPath))
            return helpPath;

        if (!Directory.Exists(helpPath))
            return null;

        var primary = Directory.GetFiles(helpPath, "*-help.xml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var secondary = primary.Count == 0
            ? Directory.GetFiles(helpPath, "*help.xml", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        var candidates = primary.Count > 0 ? primary : secondary;
        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1)
            warnings.Add($"Multiple PowerShell help files found, using {Path.GetFileName(candidates[0])}");

        return candidates[0];
    }

    private static string? GetFirstParagraph(XElement? parent, XNamespace mamlNs)
    {
        if (parent is null) return null;
        return parent.Elements(mamlNs + "para")
            .Select(p => p.Value.Trim())
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
    }

    private static string? JoinParagraphs(XElement? parent, XNamespace mamlNs)
    {
        if (parent is null) return null;
        var parts = parent.Elements(mamlNs + "para")
            .Select(p => p.Value.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string? JoinReturnValues(XElement command, XNamespace commandNs, XNamespace mamlNs)
    {
        var values = command.Element(commandNs + "returnValues");
        if (values is null) return null;
        var parts = values.Elements(commandNs + "returnValue")
            .Select(rv => JoinParagraphs(rv.Element(mamlNs + "description"), mamlNs))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();
        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
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

    private static void EnrichFromAssembly(ApiDocModel doc, Assembly assembly)
    {
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
            model.BaseType = type.BaseType != null && type.BaseType != typeof(object)
                ? GetReadableTypeName(type.BaseType)
                : null;
            model.Interfaces.Clear();
            foreach (var iface in type.GetInterfaces())
            {
                model.Interfaces.Add(GetReadableTypeName(iface));
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
        name = Regex.Replace(name, "`{1,2}\\d+", string.Empty);
        return name.Replace(" ", string.Empty);
    }

    private static void FillMethodMember(ApiMemberModel member, MethodInfo method, Type declaring)
    {
        member.Kind = "Method";
        member.ReturnType = GetReadableTypeName(method.ReturnType);
        member.Signature = BuildMethodSignature(method);
        member.IsStatic = method.IsStatic;
        member.DeclaringType = method.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = method.DeclaringType != declaring;

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

    private static void FillPropertyMember(ApiMemberModel member, PropertyInfo property, Type declaring)
    {
        member.Kind = "Property";
        member.ReturnType = GetReadableTypeName(property.PropertyType);
        member.Signature = BuildPropertySignature(property);
        member.IsStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic == true;
        member.DeclaringType = property.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = property.DeclaringType != declaring;
    }

    private static void FillFieldMember(ApiMemberModel member, FieldInfo field, Type declaring)
    {
        member.Kind = "Field";
        member.ReturnType = GetReadableTypeName(field.FieldType);
        member.Signature = BuildFieldSignature(field);
        member.IsStatic = field.IsStatic;
        member.DeclaringType = field.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = field.DeclaringType != declaring;
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
    }

    private static bool IsExtensionMethod(MethodInfo method)
        => method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);

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
            Summary = source.Summary,
            Kind = source.Kind,
            Signature = source.Signature,
            ReturnType = source.ReturnType,
            DeclaringType = source.DeclaringType,
            IsInherited = source.IsInherited,
            IsStatic = source.IsStatic,
            IsExtension = isExtension,
            Returns = source.Returns,
            Value = source.Value
        };
        foreach (var attr in source.Attributes)
            clone.Attributes.Add(attr);
        clone.Parameters = source.Parameters
            .Select(p => new ApiParameterModel
            {
                Name = p.Name,
                Type = p.Type,
                Summary = p.Summary,
                IsOptional = p.IsOptional,
                DefaultValue = p.DefaultValue
            }).ToList();
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
        if (parameter.HasDefaultValue)
            model.DefaultValue = FormatDefaultValue(parameter.DefaultValue);
    }

    private static string BuildMethodSignature(MethodInfo method)
    {
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
        return $"{returnType} {name}({string.Join(", ", parameters)})";
    }

    private static string BuildParameterSignature(ParameterInfo parameter)
    {
        var prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
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
        return $"{GetReadableTypeName(property.PropertyType)} {property.Name} {{ {string.Join(" ", accessors)} }}";
    }

    private static string BuildFieldSignature(FieldInfo field)
    {
        var prefix = field.IsLiteral ? "const " : field.IsStatic ? "static " : string.Empty;
        return $"{prefix}{GetReadableTypeName(field.FieldType)} {field.Name}".Trim();
    }

    private static string BuildEventSignature(EventInfo evt)
    {
        var handler = evt.EventHandlerType is null ? "event" : GetReadableTypeName(evt.EventHandlerType);
        return $"event {handler} {evt.Name}";
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
            Kind = "Method",
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
            Summary = GetSummary(member),
            Kind = "Property"
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
            Summary = GetSummary(member),
            Kind = "Field"
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
            Summary = GetSummary(member),
            Kind = "Event"
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
            return $"[[cref:{cleaned}]]";
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
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = string.IsNullOrWhiteSpace(cssLink)
            ? WrapStyle(fallbackCss)
            : cssLink;

        var indexTemplate = LoadTemplate(options, "index.html", options.IndexTemplatePath);
        var typeLinks = new StringBuilder();
        foreach (var type in types)
        {
            typeLinks.AppendLine($"      <a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }
        var searchScript = WrapScript(LoadAsset(options, "search.js", options.SearchScriptPath));
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
            var typeTemplate = LoadTemplate(options, "type.html", options.TypeTemplatePath);
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
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = string.IsNullOrWhiteSpace(cssLink)
            ? WrapStyle(fallbackCss)
            : cssLink;

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "/api" : options.BaseUrl.TrimEnd('/');
        var docsScript = WrapScript(LoadAsset(options, "docs.js", options.DocsScriptPath));
        var sidebarHtml = BuildDocsSidebar(types, baseUrl, string.Empty);
        var overviewHtml = BuildDocsOverview(types, baseUrl);
        var slugMap = BuildTypeSlugMap(types);

        var indexTemplate = LoadTemplate(options, "docs-index.html", options.DocsIndexTemplatePath);
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
            var typeMain = BuildDocsTypeDetail(type, baseUrl, slugMap);
            var typeTemplate = LoadTemplate(options, "docs-type.html", options.DocsTypeTemplatePath);
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

            var typeDir = Path.Combine(outputPath, type.Slug);
            Directory.CreateDirectory(typeDir);
            File.WriteAllText(Path.Combine(typeDir, "index.html"), typeHtml, Encoding.UTF8);
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
            var summaryText = StripCrefTokens(member.Summary);
            var summary = string.IsNullOrWhiteSpace(summaryText)
                ? string.Empty
                : $" - {System.Web.HttpUtility.HtmlEncode(summaryText)}";
            sb.AppendLine("        <li>");
            var signature = !string.IsNullOrWhiteSpace(member.Signature)
                ? member.Signature
                : BuildSignature(member, label);
            sb.AppendLine($"          <strong>{System.Web.HttpUtility.HtmlEncode(signature)}</strong>{summary}");
            if (member.Parameters.Count > 0)
            {
                sb.AppendLine("          <div class=\"pf-api-params\">");
                sb.AppendLine("            <ul>");
                foreach (var param in member.Parameters)
                {
                    var type = string.IsNullOrWhiteSpace(param.Type) ? string.Empty : $" ({System.Web.HttpUtility.HtmlEncode(param.Type)})";
                    var psummaryText = StripCrefTokens(param.Summary);
                    var psummary = string.IsNullOrWhiteSpace(psummaryText) ? string.Empty : $": {System.Web.HttpUtility.HtmlEncode(psummaryText)}";
                    sb.AppendLine($"              <li><code>{System.Web.HttpUtility.HtmlEncode(param.Name)}</code>{type}{psummary}</li>");
                }
                sb.AppendLine("            </ul>");
                sb.AppendLine("          </div>");
            }
            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                var returnsText = StripCrefTokens(member.Returns);
                sb.AppendLine($"          <div class=\"pf-api-returns\">Returns: {System.Web.HttpUtility.HtmlEncode(returnsText)}</div>");
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
        var indexUrl = EnsureTrailingSlash(baseUrl);
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"sidebar-header\">");
        var active = string.IsNullOrWhiteSpace(activeSlug) ? " active" : string.Empty;
        sb.AppendLine($"      <a href=\"{indexUrl}\" class=\"sidebar-title{active}\">");
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

        var kindFilters = BuildKindFilters(types);
        if (kindFilters.Count > 0)
        {
            sb.AppendLine("    <div class=\"sidebar-filters\">");
            sb.AppendLine("      <div class=\"filter-label\">Type filters</div>");
            sb.AppendLine("      <div class=\"filter-buttons\">");
            sb.AppendLine("        <button class=\"filter-button active\" type=\"button\" data-kind=\"\">All</button>");
            foreach (var kind in kindFilters)
            {
                sb.AppendLine($"        <button class=\"filter-button\" type=\"button\" data-kind=\"{kind}\">{GetKindLabel(kind)}</button>");
            }
            sb.AppendLine("      </div>");
            var namespaces = types
                .Select(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (namespaces.Count > 0)
            {
                sb.AppendLine("      <div class=\"filter-row\">");
                sb.AppendLine("        <label for=\"api-namespace\" class=\"filter-label\">Namespace</label>");
                sb.AppendLine("        <select id=\"api-namespace\" class=\"namespace-select\">");
                sb.AppendLine("          <option value=\"\">All namespaces</option>");
                foreach (var ns in namespaces)
                {
                    var encoded = System.Web.HttpUtility.HtmlEncode(ns);
                    sb.AppendLine($"          <option value=\"{encoded}\">{encoded}</option>");
                }
                sb.AppendLine("        </select>");
                sb.AppendLine("      </div>");
            }
            sb.AppendLine("    </div>");
        }
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
        var summary = StripCrefTokens(type.Summary);
        var search = $"{type.Name} {type.FullName} {summary}".Trim();
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var name = System.Web.HttpUtility.HtmlEncode(type.Name);
        var kind = NormalizeKind(type.Kind);
        var icon = GetTypeIcon(type.Kind);
        var ns = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
        var href = BuildDocsTypeUrl(baseUrl, type.Slug);
        return $"          <a href=\"{href}\" class=\"type-item{active}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{ns}\">" +
               $"<span class=\"type-icon {kind}\">{icon}</span><span class=\"type-name\">{name}</span></a>";
    }

    private static string BuildDocsOverview(IReadOnlyList<ApiTypeModel> types, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"api-overview\">");
        sb.AppendLine("      <h1>API Reference</h1>");
        sb.AppendLine("      <p class=\"lead\">Complete API documentation auto-generated from source documentation.</p>");

        var mainTypes = GetMainTypes(types);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <section class=\"quick-start\">");
            sb.AppendLine("        <h2>Quick Start</h2>");
            sb.AppendLine("        <p class=\"section-desc\">Frequently used types and entry points.</p>");
            sb.AppendLine("        <div class=\"quick-grid\">");
            foreach (var type in mainTypes.Take(6))
            {
                var summary = Truncate(StripCrefTokens(type.Summary), 100);
                var quickHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                sb.AppendLine($"          <a href=\"{quickHref}\" class=\"quick-card\">");
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
                var summary = StripCrefTokens(type.Summary);
                var search = $"{type.Name} {type.FullName} {summary}".Trim();
                var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
                var kind = NormalizeKind(type.Kind);
                var nsValue = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
                var chipHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                sb.AppendLine($"            <a href=\"{chipHref}\" class=\"type-chip {kind}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{nsValue}\">");
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

    private static string BuildDocsTypeDetail(ApiTypeModel type, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <article class=\"type-detail\">");
        var indexUrl = EnsureTrailingSlash(baseUrl);
        sb.AppendLine("      <nav class=\"breadcrumb\">");
        sb.AppendLine($"        <a href=\"{indexUrl}\">API Reference</a>");
        sb.AppendLine("        <span class=\"sep\">/</span>");
        sb.AppendLine($"        <span class=\"current\">{System.Web.HttpUtility.HtmlEncode(type.Name)}</span>");
        sb.AppendLine("      </nav>");

        sb.AppendLine("      <header class=\"type-header\">");
        var kindLabel = string.IsNullOrWhiteSpace(type.Kind) ? "Type" : type.Kind;
        sb.AppendLine("        <div class=\"type-title-row\">");
        sb.AppendLine($"          <span class=\"type-badge {NormalizeKind(type.Kind)}\">{System.Web.HttpUtility.HtmlEncode(kindLabel)}</span>");
        sb.AppendLine($"          <h1>{System.Web.HttpUtility.HtmlEncode(type.Name)}</h1>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </header>");

        var flags = new List<string>();
        if (type.IsStatic) flags.Add("static");
        else
        {
            if (type.IsAbstract) flags.Add("abstract");
            if (type.IsSealed) flags.Add("sealed");
        }

        sb.AppendLine("      <div class=\"type-meta\">");
        sb.AppendLine("        <div class=\"type-meta-row\">");
        sb.AppendLine("          <span class=\"type-meta-label\">Namespace</span>");
        sb.AppendLine($"          <code>{System.Web.HttpUtility.HtmlEncode(type.Namespace)}</code>");
        sb.AppendLine("        </div>");
        if (!string.IsNullOrWhiteSpace(type.Assembly))
        {
            sb.AppendLine("        <div class=\"type-meta-row\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Assembly</span>");
            sb.AppendLine($"          <code>{System.Web.HttpUtility.HtmlEncode(type.Assembly)}</code>");
            sb.AppendLine("        </div>");
        }
        if (!string.IsNullOrWhiteSpace(type.BaseType))
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-inheritance\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Base</span>");
            sb.AppendLine($"          <code>{LinkifyType(type.BaseType, baseUrl, slugMap)}</code>");
            sb.AppendLine("        </div>");
        }
        if (type.Interfaces.Count > 0)
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-interfaces\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Implements</span>");
            sb.AppendLine("          <div class=\"type-meta-list\">");
            foreach (var iface in type.Interfaces.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"            <code>{LinkifyType(iface, baseUrl, slugMap)}</code>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        if (flags.Count > 0)
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-flags\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Modifiers</span>");
            sb.AppendLine($"          <span class=\"type-meta-flags-list\">{System.Web.HttpUtility.HtmlEncode(string.Join(", ", flags))}</span>");
            sb.AppendLine("        </div>");
        }
        if (type.Attributes.Count > 0)
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-attributes\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Attributes</span>");
            sb.AppendLine("          <div class=\"type-meta-list\">");
            foreach (var attr in type.Attributes)
            {
                sb.AppendLine($"            <code>{System.Web.HttpUtility.HtmlEncode(attr)}</code>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </div>");

        if (!string.IsNullOrWhiteSpace(type.Summary))
            sb.AppendLine($"      <p class=\"type-summary\">{RenderLinkedText(type.Summary, baseUrl, slugMap)}</p>");
        if (!string.IsNullOrWhiteSpace(type.Remarks))
        {
            sb.AppendLine("      <section class=\"remarks\">");
            sb.AppendLine("        <h2>Remarks</h2>");
            sb.AppendLine($"        <p>{RenderLinkedText(type.Remarks, baseUrl, slugMap)}</p>");
            sb.AppendLine("      </section>");
        }

        sb.AppendLine("      <div class=\"member-toolbar\">");
        sb.AppendLine("        <div class=\"member-filter\">");
        sb.AppendLine("          <label for=\"api-member-filter\">Filter members</label>");
        sb.AppendLine("          <input id=\"api-member-filter\" type=\"text\" placeholder=\"Search members...\" />");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"member-kind-filter\">");
        sb.AppendLine("          <button class=\"member-kind active\" type=\"button\" data-member-kind=\"\">All</button>");
        sb.AppendLine("          <button class=\"member-kind\" type=\"button\" data-member-kind=\"method\">Methods</button>");
        sb.AppendLine("          <button class=\"member-kind\" type=\"button\" data-member-kind=\"property\">Properties</button>");
        sb.AppendLine("          <button class=\"member-kind\" type=\"button\" data-member-kind=\"field\">Fields</button>");
        sb.AppendLine("          <button class=\"member-kind\" type=\"button\" data-member-kind=\"event\">Events</button>");
        if (type.ExtensionMethods.Count > 0)
            sb.AppendLine("          <button class=\"member-kind\" type=\"button\" data-member-kind=\"extension\">Extensions</button>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <label class=\"member-toggle\">");
        sb.AppendLine("          <input type=\"checkbox\" id=\"api-show-inherited\" />");
        sb.AppendLine("          Show inherited");
        sb.AppendLine("        </label>");
        sb.AppendLine("      </div>");

        AppendMemberSections(sb, "Methods", "method", type.Methods, baseUrl, slugMap);
        AppendMemberSections(sb, "Properties", "property", type.Properties, baseUrl, slugMap);
        AppendMemberSections(sb, type.Kind == "Enum" ? "Values" : "Fields", "field", type.Fields, baseUrl, slugMap);
        AppendMemberSections(sb, "Events", "event", type.Events, baseUrl, slugMap);
        if (type.ExtensionMethods.Count > 0)
            AppendMemberSections(sb, "Extension Methods", "extension", type.ExtensionMethods, baseUrl, slugMap, treatAsInherited: false);

        sb.AppendLine("    </article>");
        return sb.ToString().TrimEnd();
    }

    private static void AppendMemberSections(
        StringBuilder sb,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        bool treatAsInherited = true)
    {
        if (members.Count == 0) return;
        var direct = members.Where(m => !m.IsInherited).ToList();
        var inherited = treatAsInherited ? members.Where(m => m.IsInherited).ToList() : new List<ApiMemberModel>();

        if (direct.Count > 0)
            AppendMemberCards(sb, label, memberKind, direct, baseUrl, slugMap, false);
        if (inherited.Count > 0)
            AppendMemberCards(sb, $"Inherited {label}", memberKind, inherited, baseUrl, slugMap, true);
    }

    private static void AppendMemberCards(
        StringBuilder sb,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        bool inheritedSection)
    {
        if (members.Count == 0) return;
        var collapsed = inheritedSection ? " collapsed" : string.Empty;
        sb.AppendLine($"      <section class=\"member-section{collapsed}\" data-kind=\"{memberKind}\">");
        sb.AppendLine("        <div class=\"member-section-header\">");
        sb.AppendLine($"          <h2>{label}</h2>");
        sb.AppendLine("          <button class=\"member-section-toggle\" type=\"button\" aria-label=\"Toggle section\">");
        sb.AppendLine("            <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
        sb.AppendLine("              <path d=\"M9 18l6-6-6-6\"/>");
        sb.AppendLine("            </svg>");
        sb.AppendLine("          </button>");
        sb.AppendLine("        </div>");
        var hidden = inheritedSection ? " hidden" : string.Empty;
        sb.AppendLine($"        <div class=\"member-section-body\"{hidden}>");
        foreach (var member in members)
        {
            var memberId = BuildMemberId(memberKind, member);
            var signature = !string.IsNullOrWhiteSpace(member.Signature)
                ? member.Signature
                : BuildSignature(member, label);
            var search = $"{member.Name} {signature} {member.Summary}".Trim();
            var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
            var inherited = member.IsInherited ? "true" : "false";
            var inheritedNote = member.IsInherited && !string.IsNullOrWhiteSpace(member.DeclaringType)
                ? $"Inherited from {member.DeclaringType}"
                : string.Empty;

            sb.AppendLine($"        <div class=\"member-card\" id=\"{memberId}\" data-kind=\"{memberKind}\" data-inherited=\"{inherited}\" data-search=\"{searchAttr}\">");
            sb.AppendLine("          <div class=\"member-header\">");
            sb.AppendLine($"            <code class=\"member-signature\">{System.Web.HttpUtility.HtmlEncode(signature)}</code>");
            sb.AppendLine($"            <a class=\"member-anchor\" href=\"#{memberId}\" aria-label=\"Link to {System.Web.HttpUtility.HtmlEncode(member.Name)}\">#</a>");
            sb.AppendLine("          </div>");
            if (!string.IsNullOrWhiteSpace(member.ReturnType) && (label.Contains("Method") || memberKind == "extension"))
                sb.AppendLine($"          <div class=\"member-return\">Returns: <code>{System.Web.HttpUtility.HtmlEncode(member.ReturnType)}</code></div>");
            if (!string.IsNullOrWhiteSpace(inheritedNote))
            {
                var declaring = LinkifyType(member.DeclaringType, baseUrl, slugMap);
                sb.AppendLine($"          <div class=\"member-inherited\">Inherited from {declaring}</div>");
            }
            if (member.Attributes.Count > 0)
            {
                sb.AppendLine("          <div class=\"member-attributes\">");
                foreach (var attr in member.Attributes)
                {
                    sb.AppendLine($"            <code>{System.Web.HttpUtility.HtmlEncode(attr)}</code>");
                }
                sb.AppendLine("          </div>");
            }
            if (!string.IsNullOrWhiteSpace(member.Summary))
                sb.AppendLine($"          <p class=\"member-summary\">{RenderLinkedText(member.Summary, baseUrl, slugMap)}</p>");
            if (member.Parameters.Count > 0)
            {
                sb.AppendLine("          <h4>Parameters</h4>");
                sb.AppendLine("          <dl class=\"param-list\">");
                foreach (var param in member.Parameters)
                {
                    var optional = param.IsOptional ? " optional" : string.Empty;
                    var defaultValue = param.DefaultValue;
                    var defaultText = string.IsNullOrWhiteSpace(defaultValue) ? string.Empty : $" = {defaultValue}";
                    sb.AppendLine($"            <dt>{System.Web.HttpUtility.HtmlEncode(param.Name)} <span class=\"param-type{optional}\">{System.Web.HttpUtility.HtmlEncode(param.Type)}</span><span class=\"param-default\">{System.Web.HttpUtility.HtmlEncode(defaultText)}</span></dt>");
                    if (!string.IsNullOrWhiteSpace(param.Summary))
                        sb.AppendLine($"            <dd>{RenderLinkedText(param.Summary, baseUrl, slugMap)}</dd>");
                }
                sb.AppendLine("          </dl>");
            }
            if (label == "Fields" || label == "Values")
            {
                if (!string.IsNullOrWhiteSpace(member.Value))
                    sb.AppendLine($"          <div class=\"member-value\">Value: <code>{System.Web.HttpUtility.HtmlEncode(member.Value)}</code></div>");
            }
            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                sb.AppendLine("          <h4>Returns</h4>");
                sb.AppendLine($"          <p>{RenderLinkedText(member.Returns, baseUrl, slugMap)}</p>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </section>");
    }

    private static string BuildSignature(ApiMemberModel member, string section)
    {
        if (section != "Methods")
            return member.Name;
        var args = member.Parameters
            .Select(p =>
            {
                var type = string.IsNullOrWhiteSpace(p.Type) ? string.Empty : p.Type;
                var name = string.IsNullOrWhiteSpace(p.Name) ? string.Empty : p.Name;
                return string.IsNullOrWhiteSpace(type) ? name : $"{type} {name}".Trim();
            })
            .ToList();
        var returnType = string.IsNullOrWhiteSpace(member.ReturnType) ? string.Empty : $"{member.ReturnType} ";
        return $"{returnType}{member.Name}({string.Join(", ", args)})".Trim();
    }

    private static string BuildMemberId(string memberKind, ApiMemberModel member)
    {
        var baseName = $"{memberKind}-{member.Name}";
        if (member.Parameters.Count > 0)
        {
            var suffix = string.Join("-", member.Parameters.Select(p => NormalizeTypeName(p.Type)));
            if (!string.IsNullOrWhiteSpace(suffix))
                baseName = $"{baseName}-{suffix}";
        }
        return Slugify(baseName);
    }

    private static IReadOnlyDictionary<string, string> BuildTypeSlugMap(IReadOnlyList<ApiTypeModel> types)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shortNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (!string.IsNullOrWhiteSpace(type.FullName))
                map[type.FullName] = type.Slug;
            if (!string.IsNullOrWhiteSpace(type.Name))
            {
                shortNameCounts.TryGetValue(type.Name, out var count);
                shortNameCounts[type.Name] = count + 1;
            }
        }
        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type.Name)) continue;
            if (shortNameCounts.TryGetValue(type.Name, out var count) && count == 1)
                map[type.Name] = type.Slug;
        }
        return map;
    }

    private static string RenderLinkedText(string text, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var encoded = System.Web.HttpUtility.HtmlEncode(text);
        return Regex.Replace(encoded, "\\[\\[cref:(?<name>[^\\]]+)\\]\\]", match =>
        {
            var name = match.Groups["name"].Value;
            return LinkifyType(name, baseUrl, slugMap);
        });
    }

    private static string StripCrefTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return Regex.Replace(text, "\\[\\[cref:(?<name>[^\\]]+)\\]\\]", match =>
        {
            var name = match.Groups["name"].Value;
            return GetDisplayTypeName(name);
        });
    }

    private static string LinkifyType(string? name, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var cleaned = name.Replace("+", ".").Trim();
        var display = GetDisplayTypeName(cleaned);
        if (slugMap.TryGetValue(cleaned, out var slug))
        {
            var href = BuildDocsTypeUrl(baseUrl, slug);
            return $"<a href=\"{href}\">{System.Web.HttpUtility.HtmlEncode(display)}</a>";
        }
        if (slugMap.TryGetValue(display, out var shortSlug))
        {
            var href = BuildDocsTypeUrl(baseUrl, shortSlug);
            return $"<a href=\"{href}\">{System.Web.HttpUtility.HtmlEncode(display)}</a>";
        }
        return System.Web.HttpUtility.HtmlEncode(display);
    }

    private static string GetDisplayTypeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var normalized = name.Replace("{", "<").Replace("}", ">");
        normalized = Regex.Replace(normalized, "`{1,2}\\d+", string.Empty);
        var lastDot = normalized.LastIndexOf('.');
        return lastDot > 0 ? normalized.Substring(lastDot + 1) : normalized;
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

    private static readonly string[] KindOrder = { "class", "struct", "interface", "enum", "delegate" };

    private static List<string> BuildKindFilters(IReadOnlyList<ApiTypeModel> types)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
            available.Add(NormalizeKind(type.Kind));
        return KindOrder.Where(k => available.Contains(k)).ToList();
    }

    private static string GetKindLabel(string kind)
        => kind switch
        {
            "class" => "Classes",
            "struct" => "Structs",
            "interface" => "Interfaces",
            "enum" => "Enums",
            "delegate" => "Delegates",
            _ => "Types"
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
            sb.AppendLine($"    <loc>{baseTrim}/{type.Slug}/</loc>");
            sb.AppendLine($"    <lastmod>{today}</lastmod>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("    <priority>0.5</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static string BuildDocsTypeUrl(string baseUrl, string slug)
    {
        var baseTrim = baseUrl.TrimEnd('/');
        return EnsureTrailingSlash($"{baseTrim}/{slug}");
    }

    private static string EnsureTrailingSlash(string url)
        => url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";

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

    private static string LoadTemplate(WebApiDocsOptions options, string fileName, string? explicitPath)
    {
        var content = LoadFileText(explicitPath);
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            var candidate = Path.Combine(Path.GetFullPath(options.TemplateRootPath), fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        return LoadEmbeddedRaw(fileName);
    }

    private static string LoadAsset(WebApiDocsOptions options, string fileName, string? explicitPath)
    {
        var content = LoadFileText(explicitPath);
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            var candidate = Path.Combine(Path.GetFullPath(options.TemplateRootPath), fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        return LoadEmbeddedRaw(fileName);
    }

    private static string LoadFileText(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        return File.ReadAllText(full);
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
        public string? Assembly { get; set; }
        public string? BaseType { get; set; }
        public List<string> Interfaces { get; } = new();
        public List<string> Attributes { get; } = new();
        public string? Summary { get; set; }
        public string? Remarks { get; set; }
        public string Kind { get; set; } = "Class";
        public string Slug { get; set; } = string.Empty;
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public List<ApiMemberModel> Methods { get; } = new();
        public List<ApiMemberModel> Properties { get; } = new();
        public List<ApiMemberModel> Fields { get; } = new();
        public List<ApiMemberModel> Events { get; } = new();
        public List<ApiMemberModel> ExtensionMethods { get; } = new();
    }

    private sealed class ApiMemberModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? Kind { get; set; }
        public string? Signature { get; set; }
        public string? ReturnType { get; set; }
        public string? DeclaringType { get; set; }
        public bool IsInherited { get; set; }
        public bool IsStatic { get; set; }
        public string? Value { get; set; }
        public bool IsExtension { get; set; }
        public List<string> Attributes { get; } = new();
        public List<ApiParameterModel> Parameters { get; set; } = new();
        public string? Returns { get; set; }
    }

    private sealed class ApiParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Summary { get; set; }
        public bool IsOptional { get; set; }
        public string? DefaultValue { get; set; }
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
