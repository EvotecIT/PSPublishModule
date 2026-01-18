using System.Net.Http;
using System.Xml.Linq;

namespace PowerForge.Blazor;

/// <summary>
/// Documentation source that loads XML documentation via HTTP.
/// Designed for Blazor WebAssembly where file system access is not available.
/// </summary>
public class HttpXmlDocumentationSource : IDocumentationSource
{
    private readonly HttpClient _httpClient;
    private readonly string _xmlUrl;
    private readonly XmlDocSourceOptions _options;
    private ApiDoc? _apiDoc;
    private List<DocPage>? _pages;
    private bool _loaded;

    public string Id { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public int Order { get; }

    public HttpXmlDocumentationSource(HttpClient httpClient, string xmlUrl, XmlDocSourceOptions? options = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _xmlUrl = xmlUrl ?? throw new ArgumentNullException(nameof(xmlUrl));
        _options = options ?? new XmlDocSourceOptions();
        Id = _options.Id ?? "api";
        DisplayName = _options.DisplayName ?? "API Reference";
        Description = _options.Description;
        Order = _options.Order;
    }

    public async Task<IReadOnlyList<DocPage>> LoadPagesAsync(CancellationToken cancellationToken = default)
    {
        if (_pages != null) return _pages;

        await LoadApiDocAsync(cancellationToken);
        _pages = GeneratePages();
        return _pages;
    }

    public async Task<DocPage?> GetPageAsync(string slug, CancellationToken cancellationToken = default)
    {
        var pages = await LoadPagesAsync(cancellationToken);
        return pages.FirstOrDefault(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<DocNavigation> GetNavigationAsync(CancellationToken cancellationToken = default)
    {
        await LoadApiDocAsync(cancellationToken);
        return GenerateNavigation();
    }

    /// <summary>
    /// Gets the loaded API documentation model.
    /// </summary>
    public ApiDoc? GetApiDoc() => _apiDoc;

    private async Task LoadApiDocAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var response = await _httpClient.GetAsync(_xmlUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _apiDoc = new ApiDoc();
                return;
            }

            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xmlContent);
            _apiDoc = ParseXmlDoc(doc);
        }
        catch
        {
            _apiDoc = new ApiDoc();
        }
    }

    private ApiDoc ParseXmlDoc(XDocument doc)
    {
        var apiDoc = new ApiDoc();
        var docElement = doc.Element("doc");
        if (docElement == null) return apiDoc;

        // Assembly info
        var assembly = docElement.Element("assembly");
        if (assembly != null)
        {
            apiDoc.AssemblyName = assembly.Element("name")?.Value ?? string.Empty;
        }

        // Members
        var members = docElement.Element("members");
        if (members == null) return apiDoc;

        var typeDict = new Dictionary<string, ApiType>(StringComparer.Ordinal);
        var namespaceDict = new Dictionary<string, ApiNamespace>(StringComparer.Ordinal);

        foreach (var member in members.Elements("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name) || name.Length < 2) continue;

            var prefix = name[0];
            var fullName = name.Substring(2);

            switch (prefix)
            {
                case 'T': // Type
                    var type = ParseType(member, fullName);
                    typeDict[fullName] = type;
                    apiDoc.Types.Add(type);

                    // Add to namespace
                    if (!namespaceDict.TryGetValue(type.Namespace, out var ns))
                    {
                        ns = new ApiNamespace { Name = type.Namespace };
                        namespaceDict[type.Namespace] = ns;
                        apiDoc.Namespaces.Add(ns);
                    }
                    ns.Types.Add(type);
                    break;

                case 'M': // Method
                    AddMethodToType(typeDict, member, fullName);
                    break;

                case 'P': // Property
                    AddPropertyToType(typeDict, member, fullName);
                    break;

                case 'F': // Field
                    AddFieldToType(typeDict, member, fullName);
                    break;

                case 'E': // Event
                    AddEventToType(typeDict, member, fullName);
                    break;
            }
        }

        // Sort namespaces
        apiDoc.Namespaces = apiDoc.Namespaces.OrderBy(n => n.Name).ToList();
        foreach (var ns in apiDoc.Namespaces)
        {
            ns.Types = ns.Types.OrderBy(t => t.Name).ToList();
        }

        return apiDoc;
    }

    private ApiType ParseType(XElement member, string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        var (ns, name) = lastDot > 0
            ? (fullName.Substring(0, lastDot), fullName.Substring(lastDot + 1))
            : (string.Empty, fullName);

        return new ApiType
        {
            Name = name,
            FullName = fullName,
            Namespace = ns,
            Summary = GetSummary(member),
            Remarks = GetElement(member, "remarks"),
            Example = GetElement(member, "example"),
            SeeAlso = GetSeeAlso(member),
            Kind = InferTypeKind(name)
        };
    }

    private static ApiTypeKind InferTypeKind(string name)
    {
        if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
            return ApiTypeKind.Interface;
        if (name.EndsWith("Exception"))
            return ApiTypeKind.Class;
        return ApiTypeKind.Class;
    }

    private void AddMethodToType(Dictionary<string, ApiType> types, XElement member, string fullName)
    {
        var parenIdx = fullName.IndexOf('(');
        var nameWithoutParams = parenIdx > 0 ? fullName.Substring(0, parenIdx) : fullName;
        var lastDot = nameWithoutParams.LastIndexOf('.');
        if (lastDot <= 0) return;

        var typeName = nameWithoutParams.Substring(0, lastDot);
        var methodName = nameWithoutParams.Substring(lastDot + 1);

        if (!types.TryGetValue(typeName, out var type)) return;

        var method = new ApiMethod
        {
            Name = methodName,
            Summary = GetSummary(member),
            Remarks = GetElement(member, "remarks"),
            Returns = GetElement(member, "returns"),
            Example = GetElement(member, "example"),
            Parameters = GetParameters(member),
            TypeParameters = GetTypeParameters(member),
            Exceptions = GetExceptions(member),
            IsConstructor = methodName == ".ctor" || methodName == "#ctor"
        };

        if (method.IsConstructor)
        {
            method.Name = type.Name;
            type.Constructors.Add(method);
        }
        else
        {
            type.Methods.Add(method);
        }
    }

    private void AddPropertyToType(Dictionary<string, ApiType> types, XElement member, string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot <= 0) return;

        var typeName = fullName.Substring(0, lastDot);
        var propName = fullName.Substring(lastDot + 1);

        if (!types.TryGetValue(typeName, out var type)) return;

        type.Properties.Add(new ApiProperty
        {
            Name = propName,
            Summary = GetSummary(member),
            Remarks = GetElement(member, "remarks"),
            Value = GetElement(member, "value")
        });
    }

    private void AddFieldToType(Dictionary<string, ApiType> types, XElement member, string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot <= 0) return;

        var typeName = fullName.Substring(0, lastDot);
        var fieldName = fullName.Substring(lastDot + 1);

        if (!types.TryGetValue(typeName, out var type)) return;

        type.Fields.Add(new ApiField
        {
            Name = fieldName,
            Summary = GetSummary(member)
        });
    }

    private void AddEventToType(Dictionary<string, ApiType> types, XElement member, string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot <= 0) return;

        var typeName = fullName.Substring(0, lastDot);
        var eventName = fullName.Substring(lastDot + 1);

        if (!types.TryGetValue(typeName, out var type)) return;

        type.Events.Add(new ApiEvent
        {
            Name = eventName,
            Summary = GetSummary(member)
        });
    }

    private static string? GetSummary(XElement member)
    {
        var summary = member.Element("summary");
        return summary != null ? NormalizeXmlText(summary) : null;
    }

    private static string? GetElement(XElement member, string name)
    {
        var element = member.Element(name);
        return element != null ? NormalizeXmlText(element) : null;
    }

    private static string NormalizeXmlText(XElement element)
    {
        var text = string.Concat(element.Nodes().Select(n =>
        {
            if (n is XText txt) return txt.Value;
            if (n is XElement el)
            {
                return el.Name.LocalName switch
                {
                    "see" => $"`{el.Attribute("cref")?.Value?.Substring(2) ?? el.Value}`",
                    "seealso" => $"`{el.Attribute("cref")?.Value?.Substring(2) ?? el.Value}`",
                    "c" => $"`{el.Value}`",
                    "code" => $"\n```\n{el.Value}\n```\n",
                    "para" => $"\n\n{el.Value}\n\n",
                    "paramref" => $"*{el.Attribute("name")?.Value}*",
                    "typeparamref" => $"*{el.Attribute("name")?.Value}*",
                    _ => el.Value
                };
            }
            return string.Empty;
        }));

        return string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<ApiParameter> GetParameters(XElement member)
    {
        return member.Elements("param")
            .Select(p => new ApiParameter
            {
                Name = p.Attribute("name")?.Value ?? string.Empty,
                Description = NormalizeXmlText(p)
            })
            .ToList();
    }

    private static List<ApiTypeParam> GetTypeParameters(XElement member)
    {
        return member.Elements("typeparam")
            .Select(p => new ApiTypeParam
            {
                Name = p.Attribute("name")?.Value ?? string.Empty,
                Description = NormalizeXmlText(p)
            })
            .ToList();
    }

    private static List<ApiException> GetExceptions(XElement member)
    {
        return member.Elements("exception")
            .Select(e => new ApiException
            {
                Type = e.Attribute("cref")?.Value?.Substring(2) ?? string.Empty,
                Description = NormalizeXmlText(e)
            })
            .ToList();
    }

    private static List<string> GetSeeAlso(XElement member)
    {
        return member.Elements("seealso")
            .Select(s => s.Attribute("cref")?.Value?.Substring(2) ?? s.Value)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private List<DocPage> GeneratePages()
    {
        var pages = new List<DocPage>();
        if (_apiDoc == null) return pages;

        // Overview page
        pages.Add(new DocPage
        {
            Slug = $"{Id}/overview",
            Title = $"{_apiDoc.AssemblyName} API",
            Description = $"API documentation for {_apiDoc.AssemblyName}",
            ContentType = "api-overview",
            Order = 0,
            Metadata = { ["apiDoc"] = _apiDoc }
        });

        // Namespace pages
        int nsOrder = 1;
        foreach (var ns in _apiDoc.Namespaces)
        {
            pages.Add(new DocPage
            {
                Slug = $"{Id}/{ns.Name.Replace(".", "-").ToLowerInvariant()}",
                Title = ns.Name,
                Description = $"Types in the {ns.Name} namespace",
                ContentType = "api-namespace",
                Order = nsOrder++,
                Metadata = { ["namespace"] = ns }
            });

            // Type pages
            foreach (var type in ns.Types)
            {
                pages.Add(new DocPage
                {
                    Slug = $"{Id}/{type.FullName.Replace(".", "-").ToLowerInvariant()}",
                    Title = type.Name,
                    Description = type.Summary,
                    ContentType = "api-type",
                    ParentSlug = $"{Id}/{ns.Name.Replace(".", "-").ToLowerInvariant()}",
                    Metadata = { ["type"] = type }
                });
            }
        }

        return pages;
    }

    private DocNavigation GenerateNavigation()
    {
        var nav = new DocNavigation { SourceId = Id };
        if (_apiDoc == null) return nav;

        // Overview
        nav.Items.Add(new DocNavItem
        {
            Title = "Overview",
            Slug = $"{Id}/overview",
            Order = 0
        });

        // Namespaces
        int order = 1;
        foreach (var ns in _apiDoc.Namespaces)
        {
            var nsItem = new DocNavItem
            {
                Title = ns.Name,
                Slug = $"{Id}/{ns.Name.Replace(".", "-").ToLowerInvariant()}",
                Order = order++
            };

            // Types within namespace
            foreach (var type in ns.Types)
            {
                nsItem.Children.Add(new DocNavItem
                {
                    Title = type.Name,
                    Slug = $"{Id}/{type.FullName.Replace(".", "-").ToLowerInvariant()}",
                    Icon = GetTypeIcon(type.Kind)
                });
            }

            nav.Items.Add(nsItem);
        }

        return nav;
    }

    private static string GetTypeIcon(ApiTypeKind kind) => kind switch
    {
        ApiTypeKind.Interface => "I",
        ApiTypeKind.Enum => "E",
        ApiTypeKind.Struct => "S",
        ApiTypeKind.Delegate => "D",
        ApiTypeKind.Record => "R",
        _ => "C"
    };
}
