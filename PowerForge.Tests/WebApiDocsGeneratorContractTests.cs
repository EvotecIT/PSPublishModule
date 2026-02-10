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
}
