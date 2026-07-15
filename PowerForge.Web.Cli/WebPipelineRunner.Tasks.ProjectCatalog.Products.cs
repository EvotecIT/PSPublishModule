using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly string[] AllowedProjectKinds = { "project", "product" };
    private static readonly string[] AllowedProductAvailability = { "available", "beta", "coming-soon", "private", "discontinued" };
    private static readonly string[] AllowedProductMediaRoles = { "hero", "gallery" };
    private static readonly string[] AllowedProductMediaFrames = { "auto", "phone", "tablet", "desktop", "square", "wide" };
    private static readonly string[] AllowedProductMediaFits = { "contain", "cover" };

    private static string NormalizeProjectKind(string? value, string fallback)
    {
        value = NormalizeOptionalString(value);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.ToLowerInvariant();
    }

    private static bool IsProductProject(ProjectCatalogEntry project)
    {
        return NormalizeProjectKind(project.Kind, project.Product is null ? "project" : "product")
            .Equals("product", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveProjectPageLayout(ProjectCatalogEntry project)
    {
        if (!IsProductProject(project))
            return "project";

        return NormalizeOptionalString(project.Product?.Layout) ?? "project";
    }

    private static void NormalizeProductCatalogContract(
        ProjectCatalogEntry project,
        Dictionary<string, string?> links,
        string slug)
    {
        if (!IsProductProject(project))
            return;

        project.Kind = "product";
        project.Brand ??= new ProjectBrandData();
        project.Brand.Accent = NormalizeOptionalString(project.Brand.Accent);
        project.Brand.Icon = NormalizeOptionalString(project.Brand.Icon);
        project.Brand.SocialImage = NormalizeOptionalString(project.Brand.SocialImage);

        project.Product ??= new ProductPresentationData();
        var product = project.Product;
        product.Layout = NormalizeOptionalString(product.Layout);
        product.Category = NormalizeOptionalString(product.Category);
        product.Tagline = NormalizeOptionalString(product.Tagline);
        product.ApplicationCategory = NormalizeOptionalString(product.ApplicationCategory) ?? "UtilitiesApplication";
        product.Availability = (NormalizeOptionalString(product.Availability) ?? "available").ToLowerInvariant();
        product.AvailabilityLabel = NormalizeOptionalString(product.AvailabilityLabel);
        product.Platforms = NormalizeProductStringArray(product.Platforms);

        product.Highlights = (product.Highlights ?? new List<ProductHighlightData>())
            .Where(static highlight => highlight is not null)
            .Select(static highlight =>
            {
                highlight.Title = NormalizeOptionalString(highlight.Title);
                highlight.Text = NormalizeOptionalString(highlight.Text);
                return highlight;
            })
            .Where(static highlight => !string.IsNullOrWhiteSpace(highlight.Title) || !string.IsNullOrWhiteSpace(highlight.Text))
            .ToList();

        product.Media = (product.Media ?? new List<ProductMediaData>())
            .Where(static media => media is not null)
            .Select(static media =>
            {
                media.Src = NormalizeOptionalString(media.Src);
                media.Alt = NormalizeOptionalString(media.Alt);
                media.Caption = NormalizeOptionalString(media.Caption);
                media.Role = NormalizeOptionalString(media.Role)?.ToLowerInvariant();
                media.Frame = (NormalizeOptionalString(media.Frame) ?? "auto").ToLowerInvariant();
                media.Fit = (NormalizeOptionalString(media.Fit) ?? "contain").ToLowerInvariant();
                media.Position = NormalizeOptionalString(media.Position);
                return media;
            })
            .Where(static media => !string.IsNullOrWhiteSpace(media.Src))
            .GroupBy(static media => media.Src!, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        if (product.Media.Count > 0 && !product.Media.Any(static media => media.Role?.Equals("hero", StringComparison.OrdinalIgnoreCase) == true))
        {
            var implicitHero = product.Media.FirstOrDefault(static media => string.IsNullOrWhiteSpace(media.Role));
            if (implicitHero is not null)
                implicitHero.Role = "hero";
        }

        foreach (var media in product.Media.Where(static media => string.IsNullOrWhiteSpace(media.Role)))
            media.Role = "gallery";

        product.PrimaryAction = NormalizeProductAction(product.PrimaryAction);
        product.SecondaryAction = NormalizeProductAction(product.SecondaryAction);

        var externalUrl = NormalizeOptionalString(project.ExternalUrl);
        var websiteUrl = TryGetDictionaryValue(links, "website");
        var appStoreUrl = TryGetDictionaryValue(links, "appStore");
        var sourceUrl = TryGetDictionaryValue(links, "source");
        if (product.PrimaryAction is null)
        {
            if (!string.IsNullOrWhiteSpace(externalUrl))
                product.PrimaryAction = new ProductActionData { Label = "Visit product website", Url = externalUrl };
            else if (!string.IsNullOrWhiteSpace(websiteUrl))
                product.PrimaryAction = new ProductActionData { Label = "Visit product website", Url = websiteUrl };
            else if (!string.IsNullOrWhiteSpace(appStoreUrl))
                product.PrimaryAction = new ProductActionData { Label = "Get the app", Url = appStoreUrl };
            else if (!string.IsNullOrWhiteSpace(sourceUrl))
                product.PrimaryAction = new ProductActionData { Label = "View source", Url = sourceUrl };
        }

        if (product.SecondaryAction is null &&
            !string.IsNullOrWhiteSpace(appStoreUrl) &&
            !string.Equals(product.PrimaryAction?.Url, appStoreUrl, StringComparison.OrdinalIgnoreCase))
        {
            product.SecondaryAction = new ProductActionData { Label = "View on the App Store", Url = appStoreUrl };
        }

        if (string.IsNullOrWhiteSpace(product.AvailabilityLabel))
        {
            product.AvailabilityLabel = product.Availability switch
            {
                "coming-soon" => "Coming soon",
                "beta" => "Beta",
                "private" => "Private release",
                "discontinued" => "Discontinued",
                _ => "Available now"
            };
        }

        project.HubPath = string.IsNullOrWhiteSpace(project.HubPath) ? $"/projects/{slug}/" : project.HubPath;
    }

    private static ProductActionData? NormalizeProductAction(ProductActionData? action)
    {
        if (action is null)
            return null;

        action.Label = NormalizeOptionalString(action.Label);
        action.Url = NormalizeOptionalString(action.Url);
        return string.IsNullOrWhiteSpace(action.Label) && string.IsNullOrWhiteSpace(action.Url) ? null : action;
    }

    private static string[] NormalizeProductStringArray(string[]? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateProductCatalogContract(
        List<ProjectCatalogFinding> findings,
        ProjectCatalogEntry project,
        string slug,
        string mode,
        string contentMode)
    {
        if (!IsProductProject(project))
            return;

        if (project.Product is null)
        {
            findings.Add(ProjectCatalogFinding.Error("missing-product-presentation", slug, "Product projects must define a product presentation object."));
            return;
        }

        var product = project.Product;
        if (!string.IsNullOrWhiteSpace(product.Layout) &&
            !Regex.IsMatch(product.Layout, "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant))
        {
            findings.Add(ProjectCatalogFinding.Error(
                "invalid-product-layout",
                slug,
                $"Product layout '{product.Layout}' must be a theme layout name containing only letters, digits, dots, underscores, or hyphens."));
        }
        RequireProductValue(findings, slug, "missing-product-category", product.Category, "Product projects must define product.category.");
        RequireProductValue(findings, slug, "missing-product-tagline", product.Tagline, "Product projects must define product.tagline.");
        if (product.Platforms is not { Length: > 0 })
            findings.Add(ProjectCatalogFinding.Error("missing-product-platforms", slug, "Product projects must define at least one product.platforms value."));

        if (!AllowedProductAvailability.Contains(product.Availability ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(ProjectCatalogFinding.Error(
                "invalid-product-availability",
                slug,
                $"Product availability '{product.Availability}' is not supported. Allowed: {string.Join(", ", AllowedProductAvailability)}."));
        }

        if (project.Brand is null)
        {
            findings.Add(ProjectCatalogFinding.Error("missing-product-brand", slug, "Product projects must define brand metadata."));
        }
        else
        {
            RequireProductValue(findings, slug, "missing-product-accent", project.Brand.Accent, "Product projects must define brand.accent.");
            RequireProductValue(findings, slug, "missing-product-icon", project.Brand.Icon, "Product projects must define brand.icon.");
            if (!string.IsNullOrWhiteSpace(project.Brand.Accent) &&
                !Regex.IsMatch(project.Brand.Accent, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant))
            {
                findings.Add(ProjectCatalogFinding.Error("invalid-product-accent", slug, $"brand.accent '{project.Brand.Accent}' must be a six-digit hexadecimal color such as #3366CC."));
            }

            ValidateProductImageDimensions(findings, slug, "brand.icon", project.Brand.Icon, project.Brand.IconWidth, project.Brand.IconHeight);
            ValidateProductImageDimensions(findings, slug, "brand.socialImage", project.Brand.SocialImage, project.Brand.SocialImageWidth, project.Brand.SocialImageHeight);
        }

        if (product.Media is not { Count: > 0 })
        {
            findings.Add(ProjectCatalogFinding.Error("missing-product-media", slug, "Product projects must define at least one product.media image."));
        }
        else
        {
            var heroCount = 0;
            foreach (var media in product.Media)
            {
                if (string.IsNullOrWhiteSpace(media.Src))
                    continue;

                if (media.Role?.Equals("hero", StringComparison.OrdinalIgnoreCase) == true)
                    heroCount++;
                RequireProductValue(findings, slug, "missing-product-media-alt", media.Alt, $"Product media '{media.Src}' must define meaningful alt text.");
                ValidateProductImageDimensions(findings, slug, $"product.media '{media.Src}'", media.Src, media.Width, media.Height);
                ValidateProductMediaToken(findings, slug, "role", media.Role, AllowedProductMediaRoles, media.Src);
                ValidateProductMediaToken(findings, slug, "frame", media.Frame, AllowedProductMediaFrames, media.Src);
                ValidateProductMediaToken(findings, slug, "fit", media.Fit, AllowedProductMediaFits, media.Src);
            }

            if (heroCount != 1)
                findings.Add(ProjectCatalogFinding.Error("invalid-product-hero-count", slug, $"Product projects must define exactly one hero image; found {heroCount}."));
        }

        ValidateProductAction(findings, slug, "primary", product.PrimaryAction, required: true);
        ValidateProductAction(findings, slug, "secondary", product.SecondaryAction, required: false);

        foreach (var highlight in product.Highlights ?? new List<ProductHighlightData>())
        {
            RequireProductValue(findings, slug, "missing-product-highlight-title", highlight.Title, "Each product highlight must define a title.");
            RequireProductValue(findings, slug, "missing-product-highlight-text", highlight.Text, $"Product highlight '{highlight.Title}' must define text.");
        }

        var support = TryGetProjectDictionaryValue(project.Links, "support");
        var privacy = TryGetProjectDictionaryValue(project.Links, "privacy");
        if (string.IsNullOrWhiteSpace(support))
            findings.Add(ProjectCatalogFinding.Error("missing-product-support-link", slug, "Product projects must define links.support."));
        if (string.IsNullOrWhiteSpace(privacy))
            findings.Add(ProjectCatalogFinding.Error("missing-product-privacy-link", slug, "Product projects must define links.privacy."));

        if ((mode.Equals("dedicated-external", StringComparison.OrdinalIgnoreCase) || contentMode.Equals("external", StringComparison.OrdinalIgnoreCase)) &&
            string.IsNullOrWhiteSpace(project.ExternalUrl))
        {
            findings.Add(ProjectCatalogFinding.Error("missing-product-website", slug, "Dedicated product projects must define externalUrl."));
        }
    }

    private static void ValidateProductImageDimensions(
        List<ProjectCatalogFinding> findings,
        string slug,
        string field,
        string? source,
        int width,
        int height)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        if (!IsValidProjectLinkTarget(source))
            findings.Add(ProjectCatalogFinding.Error("invalid-product-image-source", slug, $"{field} source '{source}' must be an absolute URL or root-relative route."));
        if (width <= 0 || height <= 0)
            findings.Add(ProjectCatalogFinding.Error("missing-product-image-dimensions", slug, $"{field} must define positive width and height values so layouts can preserve its aspect ratio."));
    }

    private static void ValidateProductMediaToken(
        List<ProjectCatalogFinding> findings,
        string slug,
        string field,
        string? value,
        IReadOnlyCollection<string> allowed,
        string? source)
    {
        if (allowed.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            return;

        findings.Add(ProjectCatalogFinding.Error(
            $"invalid-product-media-{field}",
            slug,
            $"Product media '{source}' has unsupported {field} '{value}'. Allowed: {string.Join(", ", allowed)}."));
    }

    private static void ValidateProductAction(
        List<ProjectCatalogFinding> findings,
        string slug,
        string name,
        ProductActionData? action,
        bool required)
    {
        if (action is null)
        {
            if (required)
                findings.Add(ProjectCatalogFinding.Error($"missing-product-{name}-action", slug, $"Product projects must define or resolve a {name} action."));
            return;
        }

        RequireProductValue(findings, slug, $"missing-product-{name}-action-label", action.Label, $"Product {name} action must define a label.");
        RequireProductValue(findings, slug, $"missing-product-{name}-action-url", action.Url, $"Product {name} action must define a URL.");
        if (!string.IsNullOrWhiteSpace(action.Url) && !IsValidProjectLinkTarget(action.Url))
            findings.Add(ProjectCatalogFinding.Error($"invalid-product-{name}-action-url", slug, $"Product {name} action URL '{action.Url}' must be absolute or root-relative."));
    }

    private static void RequireProductValue(
        List<ProjectCatalogFinding> findings,
        string slug,
        string code,
        string? value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            findings.Add(ProjectCatalogFinding.Error(code, slug, message));
    }

    private static void AppendProductFrontMatterExtensions(List<string> lines, ProjectCatalogEntry project)
    {
        if (!IsProductProject(project) || project.Product is null)
            return;

        var product = project.Product;
        lines.Add("meta.product:");
        AddNestedString(lines, 2, "category", product.Category);
        AddNestedString(lines, 2, "tagline", product.Tagline);
        AddNestedString(lines, 2, "application_category", product.ApplicationCategory);
        AddNestedString(lines, 2, "availability", product.Availability);
        AddNestedString(lines, 2, "availability_label", product.AvailabilityLabel);
        AddNestedStringArray(lines, 2, "platforms", product.Platforms);
        AddNestedAction(lines, 2, "primary_action", product.PrimaryAction);
        AddNestedAction(lines, 2, "secondary_action", product.SecondaryAction);

        if (product.Highlights is { Count: > 0 })
        {
            lines.Add("  highlights:");
            foreach (var highlight in product.Highlights)
            {
                lines.Add($"    - title: {YamlQuote(highlight.Title)}");
                AddNestedString(lines, 6, "text", highlight.Text);
            }
        }

        if (product.Media is { Count: > 0 })
        {
            lines.Add("  media:");
            foreach (var media in product.Media)
            {
                lines.Add($"    - src: {YamlQuote(media.Src)}");
                AddNestedString(lines, 6, "alt", media.Alt);
                AddNestedString(lines, 6, "caption", media.Caption);
                lines.Add($"      width: {media.Width}");
                lines.Add($"      height: {media.Height}");
                AddNestedString(lines, 6, "role", media.Role);
                AddNestedString(lines, 6, "frame", media.Frame);
                AddNestedString(lines, 6, "fit", media.Fit);
                AddNestedString(lines, 6, "position", media.Position);
            }
        }

        if (project.Brand is not null)
        {
            lines.Add("meta.brand:");
            AddNestedString(lines, 2, "accent", project.Brand.Accent);
            AddNestedString(lines, 2, "icon", project.Brand.Icon);
            if (!string.IsNullOrWhiteSpace(project.Brand.Icon))
            {
                lines.Add($"  icon_width: {project.Brand.IconWidth}");
                lines.Add($"  icon_height: {project.Brand.IconHeight}");
            }
            AddNestedString(lines, 2, "social_image", project.Brand.SocialImage);
            if (!string.IsNullOrWhiteSpace(project.Brand.SocialImage))
            {
                lines.Add($"  social_image_width: {project.Brand.SocialImageWidth}");
                lines.Add($"  social_image_height: {project.Brand.SocialImageHeight}");
            }
        }

        var hero = product.Media?.FirstOrDefault(static media => media.Role?.Equals("hero", StringComparison.OrdinalIgnoreCase) == true)
                   ?? product.Media?.FirstOrDefault();
        WriteMetaString(lines, "meta.software.name", project.Name);
        WriteMetaString(lines, "meta.software.description", project.Description);
        WriteMetaString(lines, "meta.software.application_category", product.ApplicationCategory);
        WriteMetaString(lines, "meta.software.operating_system", product.Platforms is { Length: > 0 } ? string.Join(", ", product.Platforms) : null);
        WriteMetaString(lines, "meta.software.version", project.Version);
        WriteMetaString(lines, "meta.software.download_url", TryGetProjectDictionaryValue(project.Links, "appStore") ?? TryGetProjectDictionaryValue(project.Links, "downloads"));
        WriteMetaString(lines, "meta.software.image", hero?.Src);
        WriteMetaString(lines, "meta.social_image", project.Brand?.SocialImage ?? hero?.Src);
        var socialImageWidth = project.Brand is { SocialImageWidth: > 0 } ? project.Brand.SocialImageWidth : hero?.Width ?? 0;
        var socialImageHeight = project.Brand is { SocialImageHeight: > 0 } ? project.Brand.SocialImageHeight : hero?.Height ?? 0;
        WriteMetaInteger(lines, "meta.social_image_width", socialImageWidth);
        WriteMetaInteger(lines, "meta.social_image_height", socialImageHeight);
        WriteMetaString(lines, "meta.social_card_image", project.Brand?.SocialImage ?? hero?.Src);
        WriteMetaString(lines, "meta.social_card_logo", project.Brand?.Icon);
        WriteMetaString(lines, "meta.social_card_badge", product.Category);
    }

    private static void AddNestedAction(List<string> lines, int indent, string key, ProductActionData? action)
    {
        if (action is null)
            return;

        lines.Add($"{new string(' ', indent)}{key}:");
        AddNestedString(lines, indent + 2, "label", action.Label);
        AddNestedString(lines, indent + 2, "url", action.Url);
    }

    private static void AddNestedStringArray(List<string> lines, int indent, string key, string[]? values)
    {
        if (values is not { Length: > 0 })
            return;

        lines.Add($"{new string(' ', indent)}{key}:");
        foreach (var value in values)
            lines.Add($"{new string(' ', indent + 2)}- {YamlQuote(value)}");
    }

    private static void AddNestedString(List<string> lines, int indent, string key, string? value)
    {
        value = NormalizeOptionalString(value);
        if (string.IsNullOrWhiteSpace(value))
            return;
        lines.Add($"{new string(' ', indent)}{key}: {YamlQuote(value)}");
    }

    private sealed class ProjectBrandData
    {
        [JsonPropertyName("accent")]
        public string? Accent { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }

        [JsonPropertyName("iconWidth")]
        public int IconWidth { get; set; }

        [JsonPropertyName("iconHeight")]
        public int IconHeight { get; set; }

        [JsonPropertyName("socialImage")]
        public string? SocialImage { get; set; }

        [JsonPropertyName("socialImageWidth")]
        public int SocialImageWidth { get; set; }

        [JsonPropertyName("socialImageHeight")]
        public int SocialImageHeight { get; set; }
    }

    private sealed class ProductPresentationData
    {
        [JsonPropertyName("layout")]
        public string? Layout { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("tagline")]
        public string? Tagline { get; set; }

        [JsonPropertyName("applicationCategory")]
        public string? ApplicationCategory { get; set; }

        [JsonPropertyName("platforms")]
        public string[]? Platforms { get; set; }

        [JsonPropertyName("availability")]
        public string? Availability { get; set; }

        [JsonPropertyName("availabilityLabel")]
        public string? AvailabilityLabel { get; set; }

        [JsonPropertyName("primaryAction")]
        public ProductActionData? PrimaryAction { get; set; }

        [JsonPropertyName("secondaryAction")]
        public ProductActionData? SecondaryAction { get; set; }

        [JsonPropertyName("highlights")]
        public List<ProductHighlightData>? Highlights { get; set; }

        [JsonPropertyName("media")]
        public List<ProductMediaData>? Media { get; set; }
    }

    private sealed class ProductActionData
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed class ProductHighlightData
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class ProductMediaData
    {
        [JsonPropertyName("src")]
        public string? Src { get; set; }

        [JsonPropertyName("alt")]
        public string? Alt { get; set; }

        [JsonPropertyName("caption")]
        public string? Caption { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("frame")]
        public string? Frame { get; set; }

        [JsonPropertyName("fit")]
        public string? Fit { get; set; }

        [JsonPropertyName("position")]
        public string? Position { get; set; }
    }
}
