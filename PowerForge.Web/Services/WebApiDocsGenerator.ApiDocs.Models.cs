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
        public List<string> Aliases { get; } = new();
        public List<string> InputTypes { get; } = new();
        public List<string> OutputTypes { get; } = new();
        public string? Assembly { get; set; }
        public ApiSourceLink? Source { get; set; }
        public string? BaseType { get; set; }
        public List<string> Interfaces { get; } = new();
        public List<string> Attributes { get; } = new();
        public string? Summary { get; set; }
        public string? Remarks { get; set; }
        public List<ApiTypeParameterModel> TypeParameters { get; } = new();
        public List<ApiExampleModel> Examples { get; } = new();
        public List<string> SeeAlso { get; } = new();
        public string Kind { get; set; } = "Class";
        public string Slug { get; set; } = string.Empty;
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public List<ApiMemberModel> Methods { get; } = new();
        public List<ApiMemberModel> Constructors { get; } = new();
        public List<ApiMemberModel> Properties { get; } = new();
        public List<ApiMemberModel> Fields { get; } = new();
        public List<ApiMemberModel> Events { get; } = new();
        public List<ApiMemberModel> ExtensionMethods { get; } = new();
    }

    private sealed class ApiMemberModel
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Summary { get; set; }
        public string? Kind { get; set; }
        public string? ParameterSetName { get; set; }
        public bool IncludesCommonParameters { get; set; }
        public string? Signature { get; set; }
        public string? ReturnType { get; set; }
        public string? DeclaringType { get; set; }
        public bool IsInherited { get; set; }
        public bool IsStatic { get; set; }
        public string? Access { get; set; }
        public List<string> Modifiers { get; } = new();
        public string? Value { get; set; }
        public string? ValueSummary { get; set; }
        public bool IsConstructor { get; set; }
        public bool IsExtension { get; set; }
        public List<string> Attributes { get; } = new();
        public List<ApiTypeParameterModel> TypeParameters { get; } = new();
        public List<ApiExampleModel> Examples { get; } = new();
        public List<ApiExceptionModel> Exceptions { get; } = new();
        public List<string> SeeAlso { get; } = new();
        public List<ApiParameterModel> Parameters { get; set; } = new();
        public string? Returns { get; set; }
        public ApiSourceLink? Source { get; set; }
    }

    private sealed class ApiParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Summary { get; set; }
        public List<string> Aliases { get; } = new();
        public List<string> PossibleValues { get; } = new();
        public bool IsOptional { get; set; }
        public string? DefaultValue { get; set; }
        public string? Position { get; set; }
        public string? PipelineInput { get; set; }
    }

    private sealed class ApiTypeParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
    }

    private sealed class ApiExampleModel
    {
        public string Kind { get; set; } = "text";
        public string Text { get; set; } = string.Empty;
    }

    private sealed class ApiSourceLink
    {
        public string Path { get; set; } = string.Empty;
        public int Line { get; set; }
        public string? Url { get; set; }
    }

    private sealed class SourceLinkContext : IDisposable
    {
        private readonly MetadataReaderProvider _provider;
        private readonly Stream _stream;
        private readonly MetadataReader _reader;
        private readonly string? _sourceRoot;
        private readonly string? _sourcePathPrefix;
        private readonly string? _defaultPattern;
        private readonly IReadOnlyList<SourceUrlMappingRule> _sourceUrlMappings;
        private static readonly string[] SupportedSourceUrlTokens = { "path", "line", "root", "pathNoRoot", "pathNoPrefix" };

        private SourceLinkContext(
            MetadataReaderProvider provider,
            Stream stream,
            string? sourceRoot,
            string? sourcePathPrefix,
            string? defaultPattern,
            IReadOnlyList<SourceUrlMappingRule> sourceUrlMappings)
        {
            _provider = provider;
            _stream = stream;
            _reader = provider.GetMetadataReader();
            _sourceRoot = sourceRoot;
            _sourcePathPrefix = NormalizePathPrefix(sourcePathPrefix ?? string.Empty);
            _defaultPattern = defaultPattern;
            _sourceUrlMappings = sourceUrlMappings ?? Array.Empty<SourceUrlMappingRule>();
        }

        public static SourceLinkContext? Create(WebApiDocsOptions options, Assembly assembly, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(options.SourceUrlPattern) && string.IsNullOrWhiteSpace(options.SourceRootPath))
                return null;

            var assemblyPath = options.AssemblyPath;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                assemblyPath = assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                warnings.Add("Source links disabled: assembly path not available.");
                return null;
            }

            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (!File.Exists(pdbPath))
            {
                warnings.Add($"Source links disabled: PDB not found at {pdbPath}.");
                return null;
            }

            try
            {
                var stream = File.OpenRead(pdbPath);
                var provider = MetadataReaderProvider.FromPortablePdbStream(stream);

                string? root = null;
                if (!string.IsNullOrWhiteSpace(options.SourceRootPath))
                {
                    root = Path.GetFullPath(options.SourceRootPath);
                }
                else if (!string.IsNullOrWhiteSpace(options.SourceUrlPattern))
                {
                    // If the project lives in a subfolder of a repo, using the git root as SourceRootPath
                    // keeps generated URLs consistent (and avoids missing prefixes like "IntelligenceX/...").
                    root = TryFindGitRoot(assemblyPath);
                }

                var pattern = options.SourceUrlPattern;
                if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(pattern))
                {
                    warnings.Add("SourceUrlPattern set without SourceRootPath (and git root not found); source URLs will be omitted.");
                    pattern = null;
                }
                else if (!string.IsNullOrWhiteSpace(pattern))
                {
                    ValidateSourceUrlTemplatePattern(pattern, "sourceUrl", warnings);
                }

                var mappings = BuildSourceUrlMappings(options.SourceUrlMappings, warnings);
                return new SourceLinkContext(provider, stream, root, options.SourcePathPrefix, pattern, mappings);
            }
            catch (Exception ex)
            {
                warnings.Add($"Source links disabled: {ex.Message}");
                return null;
            }
        }

        public ApiSourceLink? TryGetSource(Type type)
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var link = TryGetSource(ctor);
                if (link is not null) return link;
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName) continue;
                var link = TryGetSource(method);
                if (link is not null) return link;
            }
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var link = TryGetSource(property);
                if (link is not null) return link;
            }
            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var link = TryGetSource(evt);
                if (link is not null) return link;
            }
            return null;
        }

        public ApiSourceLink? TryGetSource(MethodBase method)
        {
            if (method is null || method.MetadataToken == 0) return null;
            try
            {
                var handle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken);
                var debugInfo = _reader.GetMethodDebugInformation(handle);
                foreach (var sp in debugInfo.GetSequencePoints())
                {
                    if (sp.IsHidden) continue;
                    var document = _reader.GetDocument(sp.Document);
                    var path = _reader.GetString(document.Name);
                    return BuildSourceLink(path, sp.StartLine);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Source mapping failed for {method.DeclaringType?.FullName}.{method.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        public ApiSourceLink? TryGetSource(PropertyInfo property)
        {
            var accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
            return accessor is null ? null : TryGetSource(accessor);
        }

        public ApiSourceLink? TryGetSource(EventInfo evt)
        {
            var accessor = evt.GetAddMethod(true) ?? evt.GetRemoveMethod(true);
            return accessor is null ? null : TryGetSource(accessor);
        }

        public ApiSourceLink? TryGetSource(FieldInfo field)
        {
            return null;
        }

        private ApiSourceLink? BuildSourceLink(string path, int line)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var resolved = path;
            if (!string.IsNullOrWhiteSpace(_sourceRoot))
            {
                try
                {
                    resolved = Path.GetRelativePath(_sourceRoot, path);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Source path relativize failed: {ex.GetType().Name}: {ex.Message}");
                    resolved = path;
                }
            }
            resolved = resolved.Replace('\\', '/');
            var url = BuildSourceUrl(resolved, line);
            return new ApiSourceLink { Path = resolved, Line = line, Url = url };
        }

        private string? BuildSourceUrl(string resolvedPath, int line)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath))
                return null;

            var normalizedPath = NormalizeSourcePath(resolvedPath);
            var mapping = MatchSourceUrlMapping(normalizedPath);
            var pathNoPrefix = mapping is null
                ? normalizedPath
                : TrimMappedPrefix(normalizedPath, mapping.PathPrefix);

            var prefixedPath = ApplySourcePathPrefix(normalizedPath, _sourcePathPrefix);
            var prefixedPathNoPrefix = ApplySourcePathPrefix(pathNoPrefix, _sourcePathPrefix);
            var root = GetFirstPathSegment(prefixedPath);
            var pathNoRoot = RemoveFirstPathSegment(prefixedPath);
            var effectivePath = mapping is { StripPathPrefix: true } ? prefixedPathNoPrefix : prefixedPath;

            var pattern = mapping?.UrlPattern;
            if (string.IsNullOrWhiteSpace(pattern))
                pattern = _defaultPattern;
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            pattern = TryApplyGitHubRepoAutoFix(pattern, root, prefixedPath);

            return pattern
                .Replace("{path}", effectivePath, StringComparison.OrdinalIgnoreCase)
                .Replace("{line}", line.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{root}", root, StringComparison.OrdinalIgnoreCase)
                .Replace("{pathNoRoot}", pathNoRoot, StringComparison.OrdinalIgnoreCase)
                .Replace("{pathNoPrefix}", prefixedPathNoPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string ApplySourcePathPrefix(string path, string? prefix)
        {
            var normalizedPath = NormalizeSourcePath(path);
            var normalizedPrefix = NormalizePathPrefix(prefix ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedPrefix))
                return normalizedPath;
            if (PathMatchesPrefix(normalizedPath, normalizedPrefix))
                return normalizedPath;
            return $"{normalizedPrefix}/{normalizedPath}";
        }

        private SourceUrlMappingRule? MatchSourceUrlMapping(string normalizedPath)
        {
            if (_sourceUrlMappings.Count == 0 || string.IsNullOrWhiteSpace(normalizedPath))
                return null;

            foreach (var mapping in _sourceUrlMappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.PathPrefix))
                    continue;
                if (PathMatchesPrefix(normalizedPath, mapping.PathPrefix))
                    return mapping;
            }

            return null;
        }

        private static IReadOnlyList<SourceUrlMappingRule> BuildSourceUrlMappings(
            IReadOnlyList<WebApiDocsSourceUrlMapping> mappings,
            List<string> warnings)
        {
            if (mappings is null || mappings.Count == 0)
                return Array.Empty<SourceUrlMappingRule>();

            var rules = new List<SourceUrlMappingRule>();
            foreach (var mapping in mappings)
            {
                if (mapping is null)
                    continue;

                var pathPrefix = NormalizePathPrefix(mapping.PathPrefix);
                if (string.IsNullOrWhiteSpace(pathPrefix))
                {
                    warnings?.Add("API docs source: sourceUrlMappings entry ignored because pathPrefix is empty.");
                    continue;
                }

                var pattern = mapping.UrlPattern?.Trim();
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    warnings?.Add($"API docs source: sourceUrlMappings entry for '{pathPrefix}' ignored because urlPattern is empty.");
                    continue;
                }

                ValidateSourceUrlTemplatePattern(pattern, $"sourceUrlMappings entry for '{pathPrefix}'", warnings);
                rules.Add(new SourceUrlMappingRule(pathPrefix, pattern, mapping.StripPathPrefix));
            }

            if (rules.Count == 0)
                return Array.Empty<SourceUrlMappingRule>();

            return rules
                .OrderByDescending(static r => r.PathPrefix.Length)
                .ToArray();
        }

        private static string NormalizeSourcePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return WebApiDocsGenerator.TrimLeadingRelativeSegments(value.Replace('\\', '/').Trim().Trim('/'));
        }

        private static string NormalizePathPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return WebApiDocsGenerator.TrimLeadingRelativeSegments(value.Replace('\\', '/').Trim().Trim('/'));
        }

        private static void ValidateSourceUrlTemplatePattern(string pattern, string label, List<string>? warnings)
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(label))
                return;

            var tokens = ExtractSourceUrlTokens(pattern);
            var hasPathToken = tokens.Any(static token =>
                token.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("pathNoRoot", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("pathNoPrefix", StringComparison.OrdinalIgnoreCase));
            if (!hasPathToken)
            {
                warnings?.Add($"API docs source: {label} does not contain a path token (use {{path}}, {{pathNoRoot}}, or {{pathNoPrefix}}).");
            }

            var unknown = tokens
                .Where(token => !SupportedSourceUrlTokens.Any(s => s.Equals(token, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static token => token, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unknown.Length == 0)
                return;

            var preview = string.Join(", ", unknown.Select(static token => $"{{{token}}}"));
            warnings?.Add($"API docs source: {label} contains unsupported token(s): {preview}. Supported tokens: {{path}}, {{line}}, {{root}}, {{pathNoRoot}}, {{pathNoPrefix}}.");
        }

        private static string[] ExtractSourceUrlTokens(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return Array.Empty<string>();

            var tokens = new List<string>();
            for (var i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] != '{')
                    continue;

                var end = pattern.IndexOf('}', i + 1);
                if (end <= i + 1)
                    continue;

                var name = pattern.Substring(i + 1, end - i - 1).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    tokens.Add(name);
                i = end;
            }

            return tokens.ToArray();
        }

        private static bool PathMatchesPrefix(string path, string prefix)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(prefix))
                return false;
            if (string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            return path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimMappedPrefix(string path, string prefix)
        {
            if (!PathMatchesPrefix(path, prefix))
                return path;
            if (string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return path.Substring(prefix.Length + 1);
        }

        private static string GetFirstPathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            var slash = path.IndexOf('/', StringComparison.Ordinal);
            return slash < 0 ? path : path.Substring(0, slash);
        }

        private static string RemoveFirstPathSegment(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            var slash = path.IndexOf('/', StringComparison.Ordinal);
            return slash < 0 ? string.Empty : path.Substring(slash + 1);
        }

        private static string TryApplyGitHubRepoAutoFix(string pattern, string rootSegment, string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return pattern;
            if (string.IsNullOrWhiteSpace(rootSegment))
                return pattern;
            if (!HasDuplicatedRootSegment(normalizedPath))
                return pattern;
            if (!TryExtractGitHubRepoName(pattern, out var repoName))
                return pattern;
            if (string.Equals(repoName, rootSegment, StringComparison.OrdinalIgnoreCase))
                return pattern;
            if (pattern.IndexOf("{root}", StringComparison.OrdinalIgnoreCase) >= 0)
                return pattern;

            return TryReplaceGitHubRepoName(pattern, repoName, rootSegment, out var updatedPattern)
                ? updatedPattern
                : pattern;
        }

        private static bool HasDuplicatedRootSegment(string path)
        {
            var normalized = NormalizeSourcePath(path);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;
            var firstSlash = normalized.IndexOf('/', StringComparison.Ordinal);
            if (firstSlash <= 0)
                return false;
            var secondSlash = normalized.IndexOf('/', firstSlash + 1);
            if (secondSlash < 0)
                return false;

            var first = normalized.Substring(0, firstSlash);
            var second = normalized.Substring(firstSlash + 1, secondSlash - firstSlash - 1);
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReplaceGitHubRepoName(string pattern, string existingRepo, string newRepo, out string updated)
        {
            updated = pattern;
            if (string.IsNullOrWhiteSpace(pattern) ||
                string.IsNullOrWhiteSpace(existingRepo) ||
                string.IsNullOrWhiteSpace(newRepo))
                return false;

            var marker = "github.com/";
            var markerIndex = pattern.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                return false;

            var ownerStart = markerIndex + marker.Length;
            var ownerEnd = pattern.IndexOf('/', ownerStart);
            if (ownerEnd < 0)
                return false;

            var repoStart = ownerEnd + 1;
            var repoEnd = pattern.IndexOf('/', repoStart);
            if (repoEnd < 0)
                return false;

            var repoSegment = pattern.Substring(repoStart, repoEnd - repoStart);
            if (!string.Equals(repoSegment, existingRepo, StringComparison.OrdinalIgnoreCase))
                return false;

            updated = pattern.Substring(0, repoStart) + newRepo + pattern.Substring(repoEnd);
            return true;
        }

        private sealed class SourceUrlMappingRule
        {
            public SourceUrlMappingRule(string pathPrefix, string urlPattern, bool stripPathPrefix)
            {
                PathPrefix = pathPrefix;
                UrlPattern = urlPattern;
                StripPathPrefix = stripPathPrefix;
            }

            public string PathPrefix { get; }
            public string UrlPattern { get; }
            public bool StripPathPrefix { get; }
        }

        public void Dispose()
        {
            _provider.Dispose();
            _stream.Dispose();
        }

        private static string? TryFindGitRoot(string path)
        {
            try
            {
                var current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                while (!string.IsNullOrWhiteSpace(current))
                {
                    var git = Path.Combine(current, ".git");
                    if (Directory.Exists(git) || File.Exists(git))
                        return current;

                    var parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                        break;
                    current = parent;
                }
            }
            catch
            {
                // best-effort
            }
            return null;
        }
    }

    private sealed class ApiExceptionModel
    {
        public string Type { get; set; } = string.Empty;
        public string? Summary { get; set; }
    }

    private sealed class NavConfig
    {
        public string SiteName { get; set; } = "Site";
        public string SiteBaseUrl { get; set; } = string.Empty;
        public string SocialImage { get; set; } = string.Empty;
        public int? SocialImageWidth { get; set; }
        public int? SocialImageHeight { get; set; }
        public string SocialTwitterCard { get; set; } = "summary";
        public string SocialTwitterSite { get; set; } = string.Empty;
        public string SocialTwitterCreator { get; set; } = string.Empty;
        public string BrandUrl { get; set; } = "/";
        public string BrandIcon { get; set; } = "/codeglyphx-qr-icon.png";
        public List<NavItem> Primary { get; set; } = new();
        public List<NavAction> Actions { get; set; } = new();
        public List<NavItem> FooterProduct { get; set; } = new();
        public List<NavItem> FooterResources { get; set; } = new();
        public List<NavItem> FooterCompany { get; set; } = new();
    }

    private readonly record struct ApiSocialProfile(
        string SiteName,
        string SiteBaseUrl,
        string Image,
        int? ImageWidth,
        int? ImageHeight,
        string TwitterCard,
        string TwitterSite,
        string TwitterCreator);

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
        public NavItem(string? href, string text, bool external, string? target = null, string? rel = null, List<NavItem>? items = null)
        {
            Href = href;
            Text = text;
            External = external;
            Target = target;
            Rel = rel;
            Items = items ?? new List<NavItem>();
        }

        public string? Href { get; }
        public string Text { get; }
        public bool External { get; }
        public string? Target { get; }
        public string? Rel { get; }
        public List<NavItem> Items { get; }
    }
}
