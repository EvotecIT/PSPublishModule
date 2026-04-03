using System.Text.Json;
using PowerForge.Web;

public class WebSiteScaffolderTests
{
    [Fact]
    public void Scaffold_CreatesBlogAndTaxonomyStarterContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-scaffold-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = WebSiteScaffolder.Scaffold(root, "Starter", "https://example.test", "simple");
            Assert.True(Directory.Exists(result.OutputPath));
            Assert.Equal("balanced", result.MaintenanceProfile);

            Assert.True(File.Exists(Path.Combine(root, "content", "blog", "_index.md")));
            Assert.True(File.Exists(Path.Combine(root, "content", "blog", "hello-world.md")));
            Assert.True(File.Exists(Path.Combine(root, "content", "news", "_index.md")));
            Assert.True(File.Exists(Path.Combine(root, "content", "news", "release-1-0.md")));
            Assert.True(File.Exists(Path.Combine(root, "config", "presets", "pipeline.web-quality.json")));
            Assert.True(File.Exists(Path.Combine(root, "config", "presets", "pipeline.web-maintenance.json")));
            Assert.True(File.Exists(Path.Combine(root, ".github", "workflows", "website-ci.yml")));
            Assert.True(File.Exists(Path.Combine(root, ".github", "workflows", "website-maintenance.yml")));
            Assert.True(File.Exists(Path.Combine(root, ".powerforge", "engine-lock.json")));
            Assert.True(File.Exists(Path.Combine(root, "pipeline.maintenance.json")));

            using var specDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "site.json")));
            var spec = specDoc.RootElement;

            Assert.True(spec.TryGetProperty("features", out var features));
            Assert.Equal(JsonValueKind.Array, features.ValueKind);
            var featureList = features.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
            Assert.Contains("docs", featureList, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("blog", featureList, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("news", featureList, StringComparer.OrdinalIgnoreCase);

            var collections = spec.GetProperty("collections").EnumerateArray()
                .Select(element => new
                {
                    Name = element.GetProperty("name").GetString() ?? string.Empty,
                    Element = element
                })
                .ToArray();
            Assert.Contains(collections, collection => collection.Name.Equals("blog", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(collections, collection => collection.Name.Equals("news", StringComparison.OrdinalIgnoreCase));

            var blogCollection = collections.First(collection => collection.Name.Equals("blog", StringComparison.OrdinalIgnoreCase)).Element;
            Assert.Equal("hero", blogCollection.GetProperty("editorialCards").GetProperty("variant").GetString());
            Assert.Equal("16/9", blogCollection.GetProperty("editorialCards").GetProperty("imageAspect").GetString());

            var newsCollection = collections.First(collection => collection.Name.Equals("news", StringComparison.OrdinalIgnoreCase)).Element;
            Assert.Equal("compact", newsCollection.GetProperty("editorialCards").GetProperty("variant").GetString());
            Assert.Equal("16/9", newsCollection.GetProperty("editorialCards").GetProperty("imageAspect").GetString());

            var taxonomies = spec.GetProperty("taxonomies").EnumerateArray()
                .Select(element => element.GetProperty("name").GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains("tags", taxonomies, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("categories", taxonomies, StringComparer.OrdinalIgnoreCase);

            var navigationItems = spec.GetProperty("navigation")
                .GetProperty("menus")[0]
                .GetProperty("items")
                .EnumerateArray()
                .Select(element => element.GetProperty("title").GetString() ?? string.Empty)
                .ToArray();
            Assert.Contains("Blog", navigationItems, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("News", navigationItems, StringComparer.OrdinalIgnoreCase);

            using var pipelineDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "pipeline.json")));
            var pipeline = pipelineDoc.RootElement;
            Assert.Equal("./config/presets/pipeline.web-quality.json", pipeline.GetProperty("extends").GetString());
            Assert.False(pipeline.TryGetProperty("steps", out _));

            using var maintenancePipelineDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "pipeline.maintenance.json")));
            var maintenancePipeline = maintenancePipelineDoc.RootElement;
            Assert.Equal("./config/presets/pipeline.web-maintenance.json", maintenancePipeline.GetProperty("extends").GetString());

            var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "website-ci.yml"));
            Assert.Contains("POWERFORGE_LOCK_PATH: ./.powerforge/engine-lock.json", workflow, StringComparison.Ordinal);
            Assert.Contains("uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-website-ci.yml@main", workflow, StringComparison.Ordinal);
            Assert.Contains("website_root: .", workflow, StringComparison.Ordinal);
            Assert.Contains("pipeline_config: pipeline.json", workflow, StringComparison.Ordinal);
            Assert.Contains("powerforge_lock_path: ./.powerforge/engine-lock.json", workflow, StringComparison.Ordinal);
            Assert.Contains("powerforge_repository_override: ${{ vars.POWERFORGE_REPOSITORY }}", workflow, StringComparison.Ordinal);
            Assert.Contains("powerforge_ref_override: ${{ vars.POWERFORGE_REF }}", workflow, StringComparison.Ordinal);
            Assert.Contains("secrets: inherit", workflow, StringComparison.Ordinal);
            Assert.Contains("concurrency:", workflow, StringComparison.Ordinal);

            var maintenanceWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "website-maintenance.yml"));
            Assert.Contains("name: Website Maintenance", maintenanceWorkflow, StringComparison.Ordinal);
            Assert.Contains("schedule:", maintenanceWorkflow, StringComparison.Ordinal);
            Assert.Contains("actions: write", maintenanceWorkflow, StringComparison.Ordinal);
            Assert.Contains("uses: EvotecIT/PSPublishModule/.github/workflows/powerforge-website-maintenance.yml@main", maintenanceWorkflow, StringComparison.Ordinal);
            Assert.Contains("pipeline_config: pipeline.maintenance.json", maintenanceWorkflow, StringComparison.Ordinal);
            Assert.Contains("powerforge_lock_path: ./.powerforge/engine-lock.json", maintenanceWorkflow, StringComparison.Ordinal);
            Assert.Contains("cancel-in-progress: false", maintenanceWorkflow, StringComparison.Ordinal);

            using var presetDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "presets", "pipeline.web-quality.json")));
            var presetSteps = presetDoc.RootElement.GetProperty("steps").EnumerateArray().ToArray();
            var engineLockStep = presetSteps.First(step =>
                string.Equals(step.GetProperty("task").GetString(), "engine-lock", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("verify", engineLockStep.GetProperty("operation").GetString());
            Assert.Equal("./.powerforge/engine-lock.json", engineLockStep.GetProperty("path").GetString());
            Assert.True(engineLockStep.GetProperty("failOnDrift").GetBoolean());
            Assert.True(engineLockStep.GetProperty("requireImmutableRef").GetBoolean());
            Assert.Contains(engineLockStep.GetProperty("modes").EnumerateArray().Select(e => e.GetString() ?? string.Empty), m => string.Equals(m, "ci", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(presetSteps, step => string.Equals(step.GetProperty("task").GetString(), "sitemap", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(presetSteps, step => string.Equals(step.GetProperty("task").GetString(), "indexnow", StringComparison.OrdinalIgnoreCase));
            var artifactsStep = presetSteps.First(step =>
                string.Equals(step.GetProperty("task").GetString(), "github-artifacts-prune", StringComparison.OrdinalIgnoreCase));
            Assert.True(artifactsStep.GetProperty("optional").GetBoolean());
            Assert.True(artifactsStep.GetProperty("dryRun").GetBoolean());

            using var maintenancePresetDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "presets", "pipeline.web-maintenance.json")));
            var maintenanceSteps = maintenancePresetDoc.RootElement.GetProperty("steps").EnumerateArray().ToArray();
            var maintenanceArtifactsStep = maintenanceSteps.First(step =>
                string.Equals(step.GetProperty("task").GetString(), "github-artifacts-prune", StringComparison.OrdinalIgnoreCase));
            Assert.True(maintenanceArtifactsStep.GetProperty("optional").GetBoolean());
            Assert.True(maintenanceArtifactsStep.GetProperty("apply").GetBoolean());
            Assert.True(maintenanceArtifactsStep.GetProperty("continueOnError").GetBoolean());
            Assert.Equal(7, maintenanceArtifactsStep.GetProperty("keep").GetInt32());
            Assert.Equal(14, maintenanceArtifactsStep.GetProperty("maxAgeDays").GetInt32());
            Assert.Equal(100, maintenanceArtifactsStep.GetProperty("maxDelete").GetInt32());
            var readme = File.ReadAllText(Path.Combine(root, "README.md"));
            Assert.Contains("profile: `balanced`", readme, StringComparison.Ordinal);

            var auditStep = presetSteps.First(step =>
                string.Equals(step.GetProperty("task").GetString(), "audit", StringComparison.OrdinalIgnoreCase));

            Assert.True(auditStep.GetProperty("requireExplicitChecks").GetBoolean());
            Assert.False(auditStep.GetProperty("checkSeoMeta").GetBoolean());
            Assert.True(auditStep.GetProperty("checkNetworkHints").GetBoolean());
            Assert.True(auditStep.GetProperty("checkRenderBlockingResources").GetBoolean());
            Assert.True(auditStep.GetProperty("checkHeadingOrder").GetBoolean());
            Assert.True(auditStep.GetProperty("checkLinkPurposeConsistency").GetBoolean());
            Assert.True(auditStep.GetProperty("checkMediaEmbeds").GetBoolean());

            using var engineLockDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".powerforge", "engine-lock.json")));
            var engineLock = engineLockDoc.RootElement;
            Assert.Equal("EvotecIT/PSPublishModule", engineLock.GetProperty("repository").GetString());
            Assert.False(string.IsNullOrWhiteSpace(engineLock.GetProperty("ref").GetString()));
            Assert.Equal("stable", engineLock.GetProperty("channel").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scaffold_MaintenanceProfile_OverridesMaintenanceBudgets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-scaffold-maintenance-profile-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = WebSiteScaffolder.Scaffold(root, "Starter", "https://example.test", "simple", "conservative");
            Assert.Equal("conservative", result.MaintenanceProfile);

            using var maintenancePresetDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "presets", "pipeline.web-maintenance.json")));
            var maintenanceSteps = maintenancePresetDoc.RootElement.GetProperty("steps").EnumerateArray().ToArray();
            var maintenanceArtifactsStep = maintenanceSteps.First(step =>
                string.Equals(step.GetProperty("task").GetString(), "github-artifacts-prune", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(14, maintenanceArtifactsStep.GetProperty("keep").GetInt32());
            Assert.Equal(30, maintenanceArtifactsStep.GetProperty("maxAgeDays").GetInt32());
            Assert.Equal(50, maintenanceArtifactsStep.GetProperty("maxDelete").GetInt32());
            var readme = File.ReadAllText(Path.Combine(root, "README.md"));
            Assert.Contains("profile: `conservative`", readme, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scaffold_MaintenanceProfile_Invalid_ThrowsArgumentException()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-scaffold-maintenance-profile-invalid-" + Guid.NewGuid().ToString("N"));

        try
        {
            var error = Assert.Throws<ArgumentException>(() =>
                WebSiteScaffolder.Scaffold(root, "Starter", "https://example.test", "simple", "unsafe"));
            Assert.Contains("maintenance profile", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scaffold_MultiProjectApiSuiteStarter_CreatesApiSuiteStarterFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-scaffold-api-suite-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = WebSiteScaffolder.Scaffold(root, "Starter", "https://example.test", "scriban", starterProfileName: "multi-project-api-suite");

            Assert.Equal("multi-project-api-suite", result.StarterProfile);
            Assert.True(File.Exists(Path.Combine(root, "content", "pages", "projects", "api-suite.md")));
            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "api-suite.md")));
            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "projects", "api-guide-template.md")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "catalog.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "catalog.project-template.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "api-suite-narrative.json")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "sample-project-api-guides.json")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "README.md")));
            Assert.True(File.Exists(Path.Combine(root, "themes", "nova", "partials", "api-header.html")));
            Assert.True(File.Exists(Path.Combine(root, "themes", "nova", "partials", "api-footer.html")));
            Assert.True(File.Exists(Path.Combine(root, "themes", "nova", "assets", "api.css")));

            using var siteDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "site.json")));
            var site = siteDoc.RootElement;
            var features = site.GetProperty("features").EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray();
            Assert.Contains("apiDocs", features, StringComparer.OrdinalIgnoreCase);

            var navItems = site.GetProperty("navigation")
                .GetProperty("menus")[0]
                .GetProperty("items")
                .EnumerateArray()
                .Select(element => new
                {
                    Title = element.GetProperty("title").GetString() ?? string.Empty,
                    Url = element.GetProperty("url").GetString() ?? string.Empty
                })
                .ToArray();
            Assert.Contains(navItems, item =>
                item.Title.Equals("API", StringComparison.OrdinalIgnoreCase) &&
                item.Url.Equals("/projects/api-suite/", StringComparison.OrdinalIgnoreCase));

            using var manifestDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "themes", "nova", "theme.manifest.json")));
            var apiContract = manifestDoc.RootElement.GetProperty("featureContracts").GetProperty("apiDocs");
            Assert.Contains(apiContract.GetProperty("requiredPartials").EnumerateArray().Select(e => e.GetString() ?? string.Empty), value => value.Equals("api-header", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(apiContract.GetProperty("cssHrefs").EnumerateArray().Select(e => e.GetString() ?? string.Empty), value => value.Equals("/themes/nova/assets/api.css", StringComparison.OrdinalIgnoreCase));

            using var presetDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "presets", "pipeline.web-quality.json")));
            var presetSteps = presetDoc.RootElement.GetProperty("steps").EnumerateArray().ToArray();
            var apiStep = presetSteps.First(step =>
                string.Equals(step.GetProperty("task").GetString(), "project-apidocs", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("./data/projects/catalog.json", apiStep.GetProperty("catalog").GetString());
            Assert.Equal("./data/projects/api-suite-narrative.json", apiStep.GetProperty("suiteNarrativeManifest").GetString());
            Assert.Equal("/themes/nova/assets/app.css,/themes/nova/assets/api.css", apiStep.GetProperty("css").GetString());

            var readme = File.ReadAllText(Path.Combine(root, "README.md"));
            Assert.Contains("Starter profile:", readme, StringComparison.Ordinal);
            Assert.Contains("multi-project-api-suite", readme, StringComparison.Ordinal);
            Assert.Contains("/projects/api-suite/", readme, StringComparison.Ordinal);

            using var projectTemplateDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "projects", "catalog.project-template.json")));
            Assert.Equal("sample-project", projectTemplateDoc.RootElement.GetProperty("slug").GetString());
            Assert.True(projectTemplateDoc.RootElement.GetProperty("surfaces").GetProperty("apiPowerShell").GetBoolean());

            using var guidesDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "projects", "sample-project-api-guides.json")));
            var guideEntry = guidesDoc.RootElement.GetProperty("entries")[0];
            Assert.Equal("guide", guideEntry.GetProperty("kind").GetString());
            Assert.Contains("ps:Invoke-SampleProjectAction", guideEntry.GetProperty("targets")[0].GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scaffold_MultiProjectApiSuiteStarter_CanSeedFirstProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-scaffold-api-suite-first-project-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = WebSiteScaffolder.Scaffold(
                root,
                "Starter",
                "https://example.test",
                "scriban",
                starterProfileName: "multi-project-api-suite",
                suiteProjectSlug: "testimox",
                suiteProjectName: "TestimoX",
                suiteProjectSurface: "powershell");

            Assert.Equal("testimox", result.FirstSuiteProjectSlug);
            Assert.Equal("powershell", result.FirstSuiteProjectSurface);

            using var catalogDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "projects", "catalog.json")));
            var project = catalogDoc.RootElement.GetProperty("projects")[0];
            Assert.Equal("testimox", project.GetProperty("slug").GetString());
            Assert.Equal("TestimoX", project.GetProperty("name").GetString());
            Assert.True(project.GetProperty("surfaces").GetProperty("apiPowerShell").GetBoolean());
            Assert.Equal("Invoke-TestimoXAction", project.GetProperty("apiDocs").GetProperty("quickStartTypes").GetString());
            Assert.Equal("./data/projects/testimox-api-guides.json", project.GetProperty("apiDocs").GetProperty("relatedContentManifest").GetString());

            Assert.True(File.Exists(Path.Combine(root, "content", "docs", "projects", "testimox-quick-start.md")));
            Assert.True(File.Exists(Path.Combine(root, "data", "projects", "testimox-api-guides.json")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "README.md")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "powershell", "README.md")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "powershell", "examples", ".gitkeep")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "powershell", "templates", "Invoke-TestimoXAction.ps1.template")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "powershell", "templates", "testimox-help.xml.template")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "powershell", "templates", "testimox.psd1.template")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "powershell", "templates", "command-metadata.json.template")));
            Assert.True(File.Exists(Path.Combine(root, "projects-sources", "testimox", "powershell", "promote-from-templates.ps1")));

            using var narrativeDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "projects", "api-suite-narrative.json")));
            var narrativeItem = narrativeDoc.RootElement.GetProperty("sections")[0].GetProperty("items")[0];
            Assert.Equal("/docs/projects/testimox-quick-start/", narrativeItem.GetProperty("url").GetString());
            Assert.Contains("testimox", narrativeItem.GetProperty("projects")[0].GetString(), StringComparison.OrdinalIgnoreCase);

            using var guidesDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "projects", "testimox-api-guides.json")));
            var guideEntry = guidesDoc.RootElement.GetProperty("entries")[0];
            Assert.Equal("/docs/projects/testimox-quick-start/", guideEntry.GetProperty("url").GetString());
            Assert.Contains("ps:Invoke-TestimoXAction", guideEntry.GetProperty("targets")[0].GetString(), StringComparison.Ordinal);

            var sourceReadme = File.ReadAllText(Path.Combine(root, "projects-sources", "testimox", "powershell", "README.md"));
            Assert.Contains("templates/", sourceReadme, StringComparison.Ordinal);
            Assert.Contains("promote-from-templates.ps1", sourceReadme, StringComparison.Ordinal);

            var promoteScript = File.ReadAllText(Path.Combine(root, "projects-sources", "testimox", "powershell", "promote-from-templates.ps1"));
            Assert.Contains("Starter templates promoted into discoverable PowerShell API inputs.", promoteScript, StringComparison.Ordinal);
            Assert.Contains("command-metadata.json", promoteScript, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scaffold_Scriban_AddsEditorialLayouts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-scaffold-scriban-" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = WebSiteScaffolder.Scaffold(root, "Starter", "https://example.test", "scriban");
            Assert.True(Directory.Exists(result.OutputPath));

            using var siteDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "site.json")));
            var defaultTheme = siteDoc.RootElement.TryGetProperty("defaultTheme", out var themeProp)
                ? (themeProp.GetString() ?? string.Empty)
                : string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(defaultTheme));

            var layoutsRoot = Path.Combine(root, "themes", defaultTheme, "layouts");
            Assert.True(File.Exists(Path.Combine(layoutsRoot, "list.html")));
            Assert.True(File.Exists(Path.Combine(layoutsRoot, "post.html")));
            Assert.True(File.Exists(Path.Combine(layoutsRoot, "taxonomy.html")));
            Assert.True(File.Exists(Path.Combine(layoutsRoot, "term.html")));

            var listTemplate = File.ReadAllText(Path.Combine(layoutsRoot, "list.html"));
            Assert.Contains("pf.editorial_cards", listTemplate, StringComparison.Ordinal);
            Assert.Contains("pf.editorial_pager", listTemplate, StringComparison.Ordinal);
            Assert.DoesNotContain("\"hero\"", listTemplate, StringComparison.Ordinal);

            var postTemplate = File.ReadAllText(Path.Combine(layoutsRoot, "post.html"));
            Assert.Contains("pf.editorial_post_nav", postTemplate, StringComparison.Ordinal);

            var termTemplate = File.ReadAllText(Path.Combine(layoutsRoot, "term.html"));
            Assert.Contains("\"compact\"", termTemplate, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Build_ExportsNavigationTemplateMetadataToSiteNav()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-nav-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pagesPath = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(pagesPath);
            File.WriteAllText(Path.Combine(pagesPath, "index.md"),
                """
                ---
                title: Home
                ---

                Home
                """);

            var themeLayouts = Path.Combine(root, "themes", "base", "layouts");
            Directory.CreateDirectory(themeLayouts);
            File.WriteAllText(Path.Combine(themeLayouts, "page.html"),
                """
                <!doctype html><html><head><title>{{TITLE}}</title></head><body>{{CONTENT}}</body></html>
                """);

            var spec = new SiteSpec
            {
                Name = "Nav metadata test",
                BaseUrl = "https://example.test",
                DefaultTheme = "base",
                ContentRoot = "content",
                ThemesRoot = "themes",
                DataRoot = "data",
                Collections = new[]
                {
                    new CollectionSpec
                    {
                        Name = "pages",
                        Input = "content/pages",
                        Output = "/",
                        DefaultLayout = "page"
                    }
                },
                Navigation = new NavigationSpec
                {
                    AutoDefaults = false,
                    Menus = new[]
                    {
                        new MenuSpec
                        {
                            Name = "main",
                            Label = "Main",
                            Template = "menu-pill",
                            CssClass = "menu-main",
                            Meta = new Dictionary<string, object?> { ["variant"] = "primary" },
                            Items = new[]
                            {
                                new MenuItemSpec { Title = "Home", Url = "/" }
                            }
                        }
                    },
                    Regions = new[]
                    {
                        new NavigationRegionSpec
                        {
                            Name = "header.right",
                            Template = "cluster",
                            CssClass = "hdr-right",
                            Meta = new Dictionary<string, object?> { ["collapse"] = "mobile" },
                            Menus = new[] { "main" }
                        }
                    },
                    Footer = new NavigationFooterSpec
                    {
                        Label = "Footer",
                        Template = "footer-grid",
                        CssClass = "site-footer",
                        Meta = new Dictionary<string, object?> { ["columns"] = 3 },
                        Columns = new[]
                        {
                            new NavigationFooterColumnSpec
                            {
                                Name = "product",
                                Title = "Product",
                                Template = "footer-column",
                                CssClass = "footer-col",
                                Meta = new Dictionary<string, object?> { ["tone"] = "muted" },
                                Items = new[]
                                {
                                    new MenuItemSpec { Title = "Docs", Url = "/docs/" }
                                }
                            }
                        }
                    }
                }
            };

            var configPath = Path.Combine(root, "site.json");
            File.WriteAllText(configPath, "{}");
            var plan = WebSitePlanner.Plan(spec, configPath);
            var output = Path.Combine(root, "_site");
            WebSiteBuilder.Build(spec, plan, output);

            var navPath = Path.Combine(output, "data", "site-nav.json");
            Assert.True(File.Exists(navPath));

            using var navDoc = JsonDocument.Parse(File.ReadAllText(navPath));
            var rootElement = navDoc.RootElement;

            var menuModel = rootElement.GetProperty("menuModels")[0];
            Assert.Equal("menu-pill", menuModel.GetProperty("template").GetString());
            Assert.Equal("menu-main", menuModel.GetProperty("class").GetString());
            Assert.Equal("primary", menuModel.GetProperty("meta").GetProperty("variant").GetString());

            var region = rootElement.GetProperty("regions")[0];
            Assert.Equal("cluster", region.GetProperty("template").GetString());
            Assert.Equal("hdr-right", region.GetProperty("class").GetString());
            Assert.Equal("mobile", region.GetProperty("meta").GetProperty("collapse").GetString());

            var footer = rootElement.GetProperty("footerModel");
            Assert.Equal("footer-grid", footer.GetProperty("template").GetString());
            Assert.Equal("site-footer", footer.GetProperty("class").GetString());
            Assert.Equal(3, footer.GetProperty("meta").GetProperty("columns").GetInt32());

            var column = footer.GetProperty("columns")[0];
            Assert.Equal("footer-column", column.GetProperty("template").GetString());
            Assert.Equal("footer-col", column.GetProperty("class").GetString());
            Assert.Equal("muted", column.GetProperty("meta").GetProperty("tone").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
