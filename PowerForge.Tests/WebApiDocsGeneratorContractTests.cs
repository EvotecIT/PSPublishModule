using System;
using System.IO;
using System.Linq;
using Xunit;
using PowerForge.Web;

public class WebApiDocsGeneratorContractTests
{
    [Fact]
    public void GenerateDocsHtml_UsesEmbeddedHeaderFooter_WhenNavJsonProvidedAndFragmentsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var siteJsonPath = Path.Combine(root, "site.json");
        File.WriteAllText(siteJsonPath,
            """
            {
              "Name": "TestSite",
              "Navigation": {
                "Menus": [
                  { "Name": "main", "Items": [ { "Title": "Docs", "Url": "/docs/" } ] }
                ]
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = siteJsonPath
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, w => w.Contains("embedded header/footer", StringComparison.OrdinalIgnoreCase));

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);
            Assert.Contains("pf-api-header", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenNavJsonMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-nav-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = Path.Combine(root, "missing-site.json")
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, w => w.Contains("[PFWEB.APIDOCS.NAV]", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("nav json not found", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenCustomFragmentsDoNotContainNavPlaceholders()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-nav-placeholders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var siteJsonPath = Path.Combine(root, "site.json");
        File.WriteAllText(siteJsonPath,
            """
            {
              "Name": "TestSite",
              "Navigation": {
                "Menus": [
                  { "Name": "main", "Items": [ { "Title": "Docs", "Url": "/docs/" } ] }
                ]
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var headerPath = Path.Combine(root, "api-header.html");
        File.WriteAllText(headerPath, "<header><span>Header</span></header>");

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = siteJsonPath,
            HeaderHtmlPath = headerPath
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, w => w.Contains("[PFWEB.APIDOCS.NAV]", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("placeholders", StringComparison.OrdinalIgnoreCase));

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);
            Assert.Contains("<span>Header</span>", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenNavTokensPresentButNavJsonPathNotSet()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-nav-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var headerPath = Path.Combine(root, "api-header.html");
        File.WriteAllText(headerPath, "<header>{{NAV_LINKS}}</header>");

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            HeaderHtmlPath = headerPath
            // NavJsonPath intentionally not set.
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, w => w.Contains("[PFWEB.APIDOCS.NAV.REQUIRED]", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("NAV_*", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_UsesNavigationProfiles_WhenNavJsonIsSiteJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-navprofile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var siteJsonPath = Path.Combine(root, "site.json");
        File.WriteAllText(siteJsonPath,
            """
            {
              "Name": "TestSite",
              "Navigation": {
                "Menus": [
                  { "Name": "main", "Items": [ { "Title": "Home", "Url": "/" } ] }
                ],
                "Profiles": [
                  {
                    "Name": "api",
                    "Paths": [ "/api/**" ],
                    "InheritMenus": false,
                    "Menus": [
                      { "Name": "main", "Items": [ { "Title": "Docs", "Url": "/docs/" }, { "Title": "API", "Url": "/api/" } ] }
                    ]
                  }
                ]
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = siteJsonPath,
            NavContextPath = "/api/"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("href=\"/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/api/\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_RendersNestedNavigationAsDropdown_WhenMenuHasChildren()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-nav-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var siteJsonPath = Path.Combine(root, "site.json");
        File.WriteAllText(siteJsonPath,
            """
            {
              "Name": "TestSite",
              "Navigation": {
                "Menus": [
                  {
                    "Name": "main",
                    "Items": [
                      { "Title": "Home", "Url": "/" },
                      {
                        "Title": "Docs",
                        "Url": "/docs/",
                        "Items": [
                          { "Title": "Guide", "Url": "/docs/guide/" },
                          { "Title": "API", "Url": "/docs/api/" }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = siteJsonPath
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("class=\"nav-dropdown\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("class=\"nav-dropdown-trigger\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("class=\"nav-dropdown-menu\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/guide/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/api/\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_RendersNestedNavigationWhenParentHasNoHref()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-nav-parent-nohref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var siteJsonPath = Path.Combine(root, "site.json");
        File.WriteAllText(siteJsonPath,
            """
            {
              "Name": "TestSite",
              "Navigation": {
                "Menus": [
                  {
                    "Name": "main",
                    "Items": [
                      { "Title": "Home", "Url": "/" },
                      {
                        "Title": "Docs",
                        "Items": [
                          { "Title": "Guide", "Url": "/docs/guide/" },
                          { "Title": "API", "Url": "/docs/api/" }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = siteJsonPath
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("nav-dropdown-trigger-button", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/guide/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/api/\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_LoadsMainMenuFromLegacyMenusObject_WhenMenuModelsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-legacy-menus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var navJsonPath = Path.Combine(root, "site-nav.json");
        File.WriteAllText(navJsonPath,
            """
            {
              "generated": true,
              "menus": {
                "main": [
                  { "text": "Home", "href": "/" },
                  {
                    "text": "Docs",
                    "items": [
                      { "text": "Guide", "href": "/docs/guide/" }
                    ]
                  }
                ],
                "footer-product": [
                  { "text": "API", "href": "/api/" }
                ]
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = navJsonPath
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("href=\"/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/guide/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/api/\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_UsesApiDocsSurface_WhenSiteNavSurfacesPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-surfaces-apidocs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var navJsonPath = Path.Combine(root, "site-nav.json");
        File.WriteAllText(navJsonPath,
            """
            {
              "generated": true,
              "surfaces": {
                "main": {
                  "primary": [
                    { "text": "Home", "href": "/" }
                  ]
                },
                "apidocs": {
                  "primary": [
                    { "text": "API Home", "href": "/api/" },
                    { "text": "Docs", "href": "/docs/" }
                  ],
                  "actions": [
                    { "text": "Install", "href": "https://example.test/install", "external": true }
                  ]
                }
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = navJsonPath,
            NavContextPath = "/api/"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("href=\"/api/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://example.test/install\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(">Home<", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_UsesConfiguredNavSurfaceName_WhenProvided()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-surfaces-explicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var navJsonPath = Path.Combine(root, "site-nav.json");
        File.WriteAllText(navJsonPath,
            """
            {
              "generated": true,
              "surfaces": {
                "docs": {
                  "primary": [
                    { "text": "Docs Root", "href": "/docs/" }
                  ]
                },
                "apidocs": {
                  "primary": [
                    { "text": "API Root", "href": "/api/" }
                  ]
                }
              }
            }
            """);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            NavJsonPath = navJsonPath,
            NavContextPath = "/api/",
            NavSurfaceName = "docs"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("href=\"/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">Docs Root<", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(">API Root<", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenCssMissingExpectedSelectors()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-css-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var cssPath = Path.Combine(root, "css", "api.css");
        Directory.CreateDirectory(Path.GetDirectoryName(cssPath)!);
        File.WriteAllText(cssPath, ".not-api { color: red; }");

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            CssHref = "/css/api.css"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, w => w.Contains("API docs CSS contract:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenCssMissingScrollbarSelectors()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-css-scrollbars-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var cssPath = Path.Combine(root, "css", "api.css");
        Directory.CreateDirectory(Path.GetDirectoryName(cssPath)!);
        File.WriteAllText(cssPath,
            """
            .api-layout{}
            .api-sidebar{}
            .api-content{}
            .api-overview{}
            .type-chips{}
            .type-chip{}
            .chip-icon{}
            .sidebar-count{}
            .sidebar-toggle{}
            .type-item{}
            .filter-button{}
            .member-card{}
            .member-signature{}
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            CssHref = "/css/api.css"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, w =>
                w.Contains("API docs CSS contract:", StringComparison.OrdinalIgnoreCase) &&
                w.Contains(".member-card pre::-webkit-scrollbar", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_DoesNotWarnWhenCssContainsExpectedSelectors()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-css-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var cssPath = Path.Combine(root, "css", "api.css");
        Directory.CreateDirectory(Path.GetDirectoryName(cssPath)!);
        File.WriteAllText(cssPath,
            """
            .api-layout{}
            .api-sidebar{}
            .api-content{}
            .api-overview{}
            .type-chips{}
            .type-chip{}
            .chip-icon{}
            .sidebar-count{}
            .sidebar-toggle{}
            .type-item{}
            .filter-button{}
            .member-card{}
            .member-signature{}
            .member-card pre::-webkit-scrollbar{}
            .member-card pre::-webkit-scrollbar-track{}
            .member-card pre::-webkit-scrollbar-thumb{}
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            CssHref = "/css/api.css"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.DoesNotContain(result.Warnings, w => w.Contains("API docs CSS contract:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenQuickStartTypeNamesDoNotMatchTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-quickstart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };
        options.QuickStartTypeNames.Add("MissingType");

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.Contains(result.Warnings, w => w.Contains("[PFWEB.APIDOCS.QUICKSTART]", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, w => w.Contains("quickStartTypes", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_InferDocsHomeFromApiBaseUrl_WhenDocsHomeNotProvided()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-docshome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api/powershell"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("href=\"/docs/powershell/\" class=\"back-link\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_IncludesCriticalCssHtml_WhenProvided()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-criticalcss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            CriticalCssHtml = "<style>.pf-critical-test{color:red;}</style>"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);
            Assert.Contains(".pf-critical-test", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    [Fact]
    public void GenerateDocsHtml_UsesConsistentSidebarTitleState_OnIndexAndTypePages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-sidebar-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var indexHtml = File.ReadAllText(indexHtmlPath);
            Assert.Contains("class=\"sidebar-title active\"", indexHtml, StringComparison.OrdinalIgnoreCase);

            var typeIndexPath = Directory
                .GetFiles(outputPath, "index.html", SearchOption.AllDirectories)
                .FirstOrDefault(path => !string.Equals(path, indexHtmlPath, StringComparison.OrdinalIgnoreCase));
            Assert.False(string.IsNullOrWhiteSpace(typeIndexPath), "Expected at least one generated type index page.");

            var typeHtml = File.ReadAllText(typeIndexPath!);
            Assert.Contains("class=\"sidebar-title active\"", typeHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }
}
