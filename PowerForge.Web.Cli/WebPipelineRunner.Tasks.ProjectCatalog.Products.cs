using System;
using System.Collections.Generic;
using System.IO;
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
    private static readonly string[] AllowedProductChannelStatuses = { "available", "beta", "coming-soon" };

    // Distribution channels a product page can present. The label is the default shown to visitors;
    // data can override it per channel.
    private static readonly Dictionary<string, string> ProductChannelLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["microsoftStore"] = "Microsoft Store",
        ["appStore"] = "App Store",
        ["macAppStore"] = "Mac App Store",
        ["googlePlay"] = "Google Play",
        ["winget"] = "WinGet",
        ["homebrew"] = "Homebrew",
        ["chocolatey"] = "Chocolatey",
        ["scoop"] = "Scoop",
        ["nuget"] = "NuGet",
        ["powershellGallery"] = "PowerShell Gallery",
        ["npm"] = "npm",
        ["docker"] = "Docker",
        ["githubReleases"] = "GitHub Releases",
        ["download"] = "Download",
        ["web"] = "Open in the browser"
    };

    // Channels whose URL is a download or store listing, in the order preferred for SoftwareApplication downloadUrl.
    private static readonly string[] ProductDownloadChannelKinds =
    {
        "microsoftStore", "appStore", "macAppStore", "googlePlay", "download", "githubReleases"
    };

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

        // With separate product pages the project page keeps the engineering layout (docs, API, examples, stats);
        // the product layout belongs to the product page.
        if (HasSeparateProductPage(project))
            return "project";

        return NormalizeOptionalString(project.Product?.Layout) ?? "project";
    }

    private static bool HasSeparateProductPage(ProjectCatalogEntry project) =>
        IsProductProject(project) && !string.IsNullOrWhiteSpace(project.Product?.Path);

    /// <summary>
    /// A product keeps a project page when it has something an engineer would look for: public source or a
    /// docs, API, or examples surface. Private products only get the product page.
    /// </summary>
    private static bool HasProjectPage(ProjectCatalogEntry project)
    {
        if (!HasSeparateProductPage(project))
            return true;
        if (project.Product?.ProjectPage is bool explicitValue)
            return explicitValue;
        if (!string.IsNullOrWhiteSpace(project.GitHubRepo) || !string.IsNullOrWhiteSpace(TryGetProjectDictionaryValue(project.Links, "source")))
            return true;
        return project.Surfaces is not null &&
               new[] { "docs", "apiDotNet", "apiPowerShell", "examples" }.Any(key => project.Surfaces.TryGetValue(key, out var enabled) && enabled);
    }

    private static string? NormalizeProductRoute(string? value)
    {
        value = NormalizeOptionalString(value);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = "/" + value.Replace('\\', '/').Trim('/') + "/";
        return value == "//" ? "/" : value;
    }

    private static void NormalizeProductCatalogContract(
        ProjectCatalogEntry project,
        Dictionary<string, string?> links,
        string slug,
        string? productRoute)
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
                media.Light = NormalizeOptionalString(media.Light);
                media.Dark = NormalizeOptionalString(media.Dark);
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

        product.Channels = (product.Channels ?? new List<ProductChannelData>())
            .Where(static channel => channel is not null)
            .Select(static channel =>
            {
                channel.Kind = NormalizeOptionalString(channel.Kind);
                if (channel.Kind is not null)
                {
                    var known = ProductChannelLabels.Keys.FirstOrDefault(key => key.Equals(channel.Kind, StringComparison.OrdinalIgnoreCase));
                    if (known is not null)
                        channel.Kind = known;
                }
                channel.Status = (NormalizeOptionalString(channel.Status) ?? "available").ToLowerInvariant();
                channel.Url = NormalizeOptionalString(channel.Url);
                channel.Command = NormalizeOptionalString(channel.Command);
                channel.Note = NormalizeOptionalString(channel.Note);
                channel.Platforms = NormalizeProductStringArray(channel.Platforms);
                channel.Label = NormalizeOptionalString(channel.Label) ??
                                (channel.Kind is not null && ProductChannelLabels.TryGetValue(channel.Kind, out var label) ? label : channel.Kind);
                return channel;
            })
            .Where(static channel => !string.IsNullOrWhiteSpace(channel.Kind))
            .ToList();

        // productRoute is set when the site generates separate product pages (project-catalog productContentRoot).
        product.Path = productRoute is null ? null : NormalizeProductRoute(product.Path) ?? $"{productRoute}{slug}/";
        // Lets themes link a product to its project page, or straight to the product page when there is none.
        product.ProjectPath = productRoute is not null && HasProjectPage(project) ? ResolveProjectHubPath(project, slug) : null;

        product.PrimaryAction = NormalizeProductAction(product.PrimaryAction);
        product.SecondaryAction = NormalizeProductAction(product.SecondaryAction);

        var externalUrl = NormalizeOptionalString(project.ExternalUrl);
        var websiteUrl = TryGetDictionaryValue(links, "website");
        var appStoreUrl = TryGetDictionaryValue(links, "appStore");
        var downloadsUrl = TryGetDictionaryValue(links, "downloads");
        var sourceUrl = TryGetDictionaryValue(links, "source");
        if (product.PrimaryAction is null)
        {
            if (!string.IsNullOrWhiteSpace(externalUrl))
                product.PrimaryAction = new ProductActionData { Label = "Visit product website", Url = externalUrl };
            else if (!string.IsNullOrWhiteSpace(websiteUrl))
                product.PrimaryAction = new ProductActionData { Label = "Visit product website", Url = websiteUrl };
            else if (!string.IsNullOrWhiteSpace(appStoreUrl))
                product.PrimaryAction = new ProductActionData { Label = "Get the app", Url = appStoreUrl };
            else if (!string.IsNullOrWhiteSpace(downloadsUrl))
                product.PrimaryAction = new ProductActionData { Label = "Download", Url = downloadsUrl };
            else if (!string.IsNullOrWhiteSpace(sourceUrl))
                product.PrimaryAction = new ProductActionData { Label = "View source", Url = sourceUrl };
        }

        var secondaryDownloadUrl = appStoreUrl ?? downloadsUrl;
        if (product.SecondaryAction is null &&
            !string.IsNullOrWhiteSpace(secondaryDownloadUrl) &&
            !string.Equals(product.PrimaryAction?.Url, secondaryDownloadUrl, StringComparison.OrdinalIgnoreCase))
        {
            product.SecondaryAction = new ProductActionData
            {
                Label = !string.IsNullOrWhiteSpace(appStoreUrl) ? "View on the App Store" : "Download",
                Url = secondaryDownloadUrl
            };
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

    private static void MergeManifestBrand(ProjectCatalogEntry project, ProjectBrandData manifestBrand)
    {
        project.Brand ??= new ProjectBrandData();
        var target = project.Brand;

        if (!string.IsNullOrWhiteSpace(manifestBrand.Accent))
            target.Accent = manifestBrand.Accent;
        if (!string.IsNullOrWhiteSpace(manifestBrand.Icon))
            target.Icon = manifestBrand.Icon;
        if (manifestBrand.IconWidth > 0)
            target.IconWidth = manifestBrand.IconWidth;
        if (manifestBrand.IconHeight > 0)
            target.IconHeight = manifestBrand.IconHeight;
        if (!string.IsNullOrWhiteSpace(manifestBrand.SocialImage))
            target.SocialImage = manifestBrand.SocialImage;
        if (manifestBrand.SocialImageWidth > 0)
            target.SocialImageWidth = manifestBrand.SocialImageWidth;
        if (manifestBrand.SocialImageHeight > 0)
            target.SocialImageHeight = manifestBrand.SocialImageHeight;
    }

    private static void MergeManifestProduct(ProjectCatalogEntry project, ProductPresentationData manifestProduct)
    {
        project.Product ??= new ProductPresentationData();
        var target = project.Product;

        if (!string.IsNullOrWhiteSpace(manifestProduct.Layout))
            target.Layout = manifestProduct.Layout;
        if (!string.IsNullOrWhiteSpace(manifestProduct.Category))
            target.Category = manifestProduct.Category;
        if (!string.IsNullOrWhiteSpace(manifestProduct.Tagline))
            target.Tagline = manifestProduct.Tagline;
        if (!string.IsNullOrWhiteSpace(manifestProduct.ApplicationCategory))
            target.ApplicationCategory = manifestProduct.ApplicationCategory;
        if (manifestProduct.Platforms is { Length: > 0 })
            target.Platforms = manifestProduct.Platforms;
        if (!string.IsNullOrWhiteSpace(manifestProduct.Availability))
            target.Availability = manifestProduct.Availability;
        if (!string.IsNullOrWhiteSpace(manifestProduct.AvailabilityLabel))
            target.AvailabilityLabel = manifestProduct.AvailabilityLabel;
        if (manifestProduct.PrimaryAction is not null)
            target.PrimaryAction = MergeManifestProductAction(target.PrimaryAction, manifestProduct.PrimaryAction);
        if (manifestProduct.SecondaryAction is not null)
            target.SecondaryAction = MergeManifestProductAction(target.SecondaryAction, manifestProduct.SecondaryAction);
        if (manifestProduct.Highlights is { Count: > 0 })
            target.Highlights = manifestProduct.Highlights;
        if (manifestProduct.Media is { Count: > 0 })
            target.Media = manifestProduct.Media;
        if (manifestProduct.Channels is not null)
            target.Channels = manifestProduct.Channels;
        if (manifestProduct.ProjectPage is not null)
            target.ProjectPage = manifestProduct.ProjectPage;
        if (!string.IsNullOrWhiteSpace(manifestProduct.Path))
            target.Path = manifestProduct.Path;
    }

    private static ProductActionData MergeManifestProductAction(ProductActionData? target, ProductActionData manifestAction)
    {
        target ??= new ProductActionData();
        if (!string.IsNullOrWhiteSpace(manifestAction.Label))
            target.Label = manifestAction.Label;
        if (!string.IsNullOrWhiteSpace(manifestAction.Url))
            target.Url = manifestAction.Url;
        return target;
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
                foreach (var variant in new[] { media.Light, media.Dark })
                {
                    if (!string.IsNullOrWhiteSpace(variant) && !IsValidProductViewerSource(variant))
                        findings.Add(ProjectCatalogFinding.Error("invalid-product-image-source", slug, $"Product media variant '{variant}' must be an HTTPS URL or root-relative route."));
                }
                ValidateProductMediaToken(findings, slug, "role", media.Role, AllowedProductMediaRoles, media.Src);
                ValidateProductMediaToken(findings, slug, "frame", media.Frame, AllowedProductMediaFrames, media.Src);
                ValidateProductMediaToken(findings, slug, "fit", media.Fit, AllowedProductMediaFits, media.Src);
            }

            if (heroCount != 1)
                findings.Add(ProjectCatalogFinding.Error("invalid-product-hero-count", slug, $"Product projects must define exactly one hero image; found {heroCount}."));
        }

        ValidateProductAction(findings, slug, "primary", product.PrimaryAction, required: true);
        ValidateProductAction(findings, slug, "secondary", product.SecondaryAction, required: false);

        if (!string.IsNullOrWhiteSpace(product.Path) &&
            !Regex.IsMatch(product.Path, "^/[A-Za-z0-9][A-Za-z0-9/_-]*/$", RegexOptions.CultureInvariant))
        {
            findings.Add(ProjectCatalogFinding.Error("invalid-product-path", slug, $"product.path '{product.Path}' must be a root-relative route such as /products/{slug}/."));
        }

        foreach (var channel in product.Channels ?? new List<ProductChannelData>())
        {
            if (!ProductChannelLabels.ContainsKey(channel.Kind ?? string.Empty))
            {
                findings.Add(ProjectCatalogFinding.Error(
                    "invalid-product-channel-kind",
                    slug,
                    $"Product channel '{channel.Kind}' is not supported. Allowed: {string.Join(", ", ProductChannelLabels.Keys)}."));
            }
            if (!AllowedProductChannelStatuses.Contains(channel.Status ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(ProjectCatalogFinding.Error(
                    "invalid-product-channel-status",
                    slug,
                    $"Product channel '{channel.Kind}' has unsupported status '{channel.Status}'. Allowed: {string.Join(", ", AllowedProductChannelStatuses)}."));
            }
            if (string.IsNullOrWhiteSpace(channel.Url) && string.IsNullOrWhiteSpace(channel.Command) &&
                !string.Equals(channel.Status, "coming-soon", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(ProjectCatalogFinding.Error("missing-product-channel-target", slug, $"Product channel '{channel.Kind}' must define a url or an install command unless it is coming-soon."));
            }
            if (!string.IsNullOrWhiteSpace(channel.Url) && !IsValidProductViewerSource(channel.Url))
                findings.Add(ProjectCatalogFinding.Error("invalid-product-channel-url", slug, $"Product channel '{channel.Kind}' URL '{channel.Url}' must be an HTTPS URL or root-relative route."));
        }

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

    private static bool IsValidProductViewerSource(string value) =>
        !value.Any(char.IsControl) && !value.Contains('\\') && IsValidProjectLinkTarget(value) &&
        (value.StartsWith('/') || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);

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
        lines.Add("meta.product_presentation:");
        AddNestedString(lines, 2, "category", product.Category);
        AddNestedString(lines, 2, "tagline", product.Tagline);
        AddNestedString(lines, 2, "application_category", product.ApplicationCategory);
        AddNestedString(lines, 2, "availability", product.Availability);
        AddNestedString(lines, 2, "availability_label", product.AvailabilityLabel);
        AddNestedStringArray(lines, 2, "platforms", product.Platforms);
        AddNestedAction(lines, 2, "primary_action", product.PrimaryAction);
        AddNestedAction(lines, 2, "secondary_action", product.SecondaryAction);
        AddNestedString(lines, 2, "path", product.Path);
        if (HasSeparateProductPage(project))
            lines.Add($"  project_page: {HasProjectPage(project).ToString().ToLowerInvariant()}");

        if (product.Channels is { Count: > 0 })
        {
            lines.Add("  channels:");
            foreach (var channel in product.Channels)
            {
                lines.Add($"    - kind: {YamlQuote(channel.Kind)}");
                AddNestedString(lines, 6, "label", channel.Label);
                AddNestedString(lines, 6, "status", channel.Status);
                AddNestedString(lines, 6, "url", channel.Url);
                AddNestedString(lines, 6, "command", channel.Command);
                AddNestedString(lines, 6, "note", channel.Note);
                AddNestedStringArray(lines, 6, "platforms", channel.Platforms);
            }
        }

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
                AddNestedString(lines, 6, "light", media.Light);
                AddNestedString(lines, 6, "dark", media.Dark);
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
        WriteMetaString(lines, "meta.software.download_url", ResolveProductDownloadUrl(project));
        WriteMetaString(lines, "meta.software.website_url", project.ExternalUrl ?? TryGetProjectDictionaryValue(project.Links, "website"));
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

    private static string? ResolveProductDownloadUrl(ProjectCatalogEntry project)
    {
        var channels = project.Product?.Channels;
        if (channels is { Count: > 0 })
        {
            foreach (var kind in ProductDownloadChannelKinds)
            {
                var match = channels.FirstOrDefault(channel =>
                    string.Equals(channel.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(channel.Status, "coming-soon", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(channel.Url) &&
                    IsValidProductViewerSource(channel.Url!));
                if (match is not null)
                    return match.Url;
            }
        }

        return TryGetProjectDictionaryValue(project.Links, "appStore") ?? TryGetProjectDictionaryValue(project.Links, "downloads");
    }

    /// <summary>
    /// Store, legacy app, and product routes belong to the product page; everything else stays with the project page.
    /// A product without a project page also takes over its old project route so existing links redirect.
    /// </summary>
    private static (string[] ProjectAliases, string[] ProductAliases) SplitProductAliases(ProjectCatalogEntry project, string slug)
    {
        var productPath = project.Product?.Path;
        var all = (project.Aliases ?? Array.Empty<string>())
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias.Trim())
            .Where(alias => !string.Equals(alias.TrimEnd('/') + "/", productPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!HasProjectPage(project))
        {
            all.Add(ResolveProjectHubPath(project, slug));
            return (Array.Empty<string>(), all.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        static bool IsProductRoute(string alias) =>
            alias.StartsWith("/products/", StringComparison.OrdinalIgnoreCase) ||
            alias.StartsWith("/apps/", StringComparison.OrdinalIgnoreCase);

        return (all.Where(alias => !IsProductRoute(alias)).ToArray(), all.Where(IsProductRoute).ToArray());
    }

    private static void GenerateProductPages(
        IReadOnlyList<ProjectCatalogEntry> projects,
        string productContentRoot,
        bool forceOverwriteExisting,
        out int written,
        out int skipped,
        out int deleted)
    {
        written = 0;
        skipped = 0;
        deleted = 0;
        if (string.IsNullOrWhiteSpace(productContentRoot))
            return;
        Directory.CreateDirectory(productContentRoot);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var slug = NormalizeSlug(project.Slug);
            if (string.IsNullOrWhiteSpace(slug) || !HasSeparateProductPage(project) || project.Product is null)
                continue;

            var outputPath = Path.Combine(productContentRoot, slug + ".md");
            expected.Add(Path.GetFullPath(outputPath));
            if (!CanOverwriteGenerated(outputPath, forceOverwriteExisting))
            {
                skipped++;
                continue;
            }

            var product = project.Product;
            var name = string.IsNullOrWhiteSpace(project.Name) ? slug : project.Name!;
            var description = string.IsNullOrWhiteSpace(project.Description) ? product.Tagline ?? name : project.Description!;
            var mode = NormalizeProjectMode(project.Mode, "hub-full");
            var status = NormalizeProjectStatus(project.Status, "active");
            var listed = project.Listed ?? !status.Equals("archived", StringComparison.OrdinalIgnoreCase);
            var hubPath = ResolveProjectHubPath(project, slug);
            var hasProjectPage = HasProjectPage(project);
            var (_, productAliases) = SplitProductAliases(project, slug);

            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(name)}",
                $"description: {YamlQuote(description)}",
                $"slug: {YamlQuote(slug)}",
                $"layout: {NormalizeOptionalString(product.Layout) ?? "product"}"
            };
            if (productAliases.Length > 0)
            {
                lines.Add("aliases:");
                foreach (var alias in productAliases)
                    lines.Add($"  - {YamlQuote(alias)}");
            }

            lines.Add("meta.product_page: true");
            lines.Add($"meta.product_path: {YamlQuote(product.Path)}");
            lines.Add($"meta.project_mode: {YamlQuote(mode)}");
            lines.Add("meta.project_kind: \"product\"");
            lines.Add($"meta.project_status: {YamlQuote(status)}");
            lines.Add($"meta.project_listed: {listed.ToString().ToLowerInvariant()}");
            lines.Add($"meta.project_base_slug: {YamlQuote(slug)}");
            if (hasProjectPage)
                lines.Add($"meta.product_project_path: {YamlQuote(hubPath)}");
            if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
                lines.Add($"meta.project_external_url: {YamlQuote(project.ExternalUrl)}");
            if (!string.IsNullOrWhiteSpace(project.GitHubRepo))
                lines.Add($"meta.project_github_repo: {YamlQuote(project.GitHubRepo)}");
            if (!string.IsNullOrWhiteSpace(project.Version))
                lines.Add($"meta.project_version: {YamlQuote(project.Version)}");
            AppendProjectFrontMatterExtensions(lines, project, includeProductPresentation: true);
            lines.Add("meta.generated_by: powerforge.project-catalog");
            lines.Add("---");
            lines.Add(string.Empty);
            lines.Add(description);
            lines.Add(string.Empty);
            if (!string.IsNullOrWhiteSpace(project.ExternalUrl))
            {
                lines.Add($"- Website: [{project.ExternalUrl}]({project.ExternalUrl})");
                lines.Add(string.Empty);
            }

            WriteMarkdown(outputPath, lines);
            written++;
        }

        foreach (var filePath in Directory.EnumerateFiles(productContentRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(filePath).StartsWith("_", StringComparison.Ordinal) || expected.Contains(Path.GetFullPath(filePath)))
                continue;
            if (!CanOverwriteGenerated(filePath, forceOverwriteExisting: false))
                continue;
            File.Delete(filePath);
            deleted++;
        }
    }

    private sealed class ProductChannelData
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("label")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Label { get; set; }

        [JsonPropertyName("status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Status { get; set; }

        [JsonPropertyName("url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Url { get; set; }

        [JsonPropertyName("command")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Command { get; set; }

        [JsonPropertyName("note")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Note { get; set; }

        [JsonPropertyName("platforms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Platforms { get; set; }
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

        /// <summary>Product page route; set by the catalog when the site generates separate product pages.</summary>
        [JsonPropertyName("path")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }

        /// <summary>Overrides whether the product also keeps a /projects/ page (defaults to having public source, docs, API, or examples).</summary>
        [JsonPropertyName("projectPage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ProjectPage { get; set; }

        /// <summary>The product's project page route, or null when it only has a product page. Computed by the catalog; not an input.</summary>
        [JsonPropertyName("projectPath")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProjectPath { get; set; }

        [JsonPropertyName("channels")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ProductChannelData>? Channels { get; set; }
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
        [JsonPropertyName("light")]
        public string? Light { get; set; }

        [JsonPropertyName("dark")]
        public string? Dark { get; set; }

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
