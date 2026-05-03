using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebContributionProcessorTests
{
    [Fact]
    public void Import_RewritesFrontMatterAndBodyImagesWithoutChangingFencedExamples()
    {
        var root = CreateTempRoot("pf-web-contributions-import-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            var siteRoot = Path.Combine(root, "site");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "sample-post",
                """
                ---
                title: "Sample Post"
                description: "A practical sample contribution."
                date: "2026-04-29"
                language: "en"
                authors:
                  - jane-doe
                image: "./cover.webp"
                image_alt: "Sample cover"
                draft: true
                author: "Old Name"
                author_names:
                  - "Old Name"
                author_urls:
                  - "https://example.com/old"
                social_twitter_creator: "@Old"
                metadata:
                  author: "Nested Name"
                  image: "./nested.webp"
                  draft: true
                ---

                Body image:

                ![Diagram](images/diagram.png)

                ```yaml
                image: "./cover.webp"
                draft: true
                ```
                """);
            Directory.CreateDirectory(siteRoot);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot,
                SiteRoot = siteRoot,
                Import = true,
                Publish = true,
                Force = true
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            var imported = File.ReadAllText(Path.Combine(siteRoot, "content", "blog", "en", "sample-post.md"));
            Assert.Contains("image: \"/assets/blog/2026/sample-post/cover.webp\"", imported);
            Assert.Contains("image_alt: \"Sample cover\"", imported);
            Assert.Contains("draft: false", imported);
            Assert.Contains("author: \"Jane Doe\"", imported);
            Assert.Contains("author_urls:\n  - \"https://www.linkedin.com/in/janedoe\"", imported);
            Assert.Contains("social_twitter_creator: \"@JaneDoe\"", imported);
            Assert.Contains("![Diagram](/assets/blog/2026/sample-post/images/diagram.png)", imported);
            Assert.Contains("```yaml\nimage: \"./cover.webp\"\ndraft: true\n```", imported);
            Assert.Equal(1, CountOccurrences(imported, "author: \"Jane Doe\""));
            Assert.Contains("metadata:\n  author: \"Nested Name\"", imported);
            Assert.Contains("  image: \"./nested.webp\"", imported);
            Assert.Contains("  draft: true", imported);
            Assert.DoesNotContain("Old Name", imported);
            Assert.DoesNotContain("draft: false---", imported);

            var catalog = File.ReadAllText(Path.Combine(siteRoot, "data", "authors", "catalog.json"));
            Assert.Contains("\"authors\"", catalog);
            Assert.Contains("\"name\": \"Jane Doe\"", catalog);
            Assert.Contains("\"slug\": \"jane-doe\"", catalog);
            Assert.Contains("\"linkedin\": \"https://www.linkedin.com/in/janedoe\"", catalog);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Import_ContinuesWithOtherPostsWhenOneTargetAlreadyExists()
    {
        var root = CreateTempRoot("pf-web-contributions-partial-import-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            var siteRoot = Path.Combine(root, "site");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "existing-post");
            WritePost(sourceRoot, "fresh-post");
            var existingTarget = Path.Combine(siteRoot, "content", "blog", "en", "existing-post.md");
            Directory.CreateDirectory(Path.GetDirectoryName(existingTarget)!);
            File.WriteAllText(existingTarget, "keep me");

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot,
                SiteRoot = siteRoot,
                Import = true,
                Force = false
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("existing-post.md", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("keep me", File.ReadAllText(existingTarget));
            Assert.True(File.Exists(Path.Combine(siteRoot, "content", "blog", "en", "fresh-post.md")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Import_PreservesDraftWhenPublishIsFalse()
    {
        var root = CreateTempRoot("pf-web-contributions-draft-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            var siteRoot = Path.Combine(root, "site");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "draft-post");
            Directory.CreateDirectory(siteRoot);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot,
                SiteRoot = siteRoot,
                Import = true,
                Publish = false,
                Force = true
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            var imported = File.ReadAllText(Path.Combine(siteRoot, "content", "blog", "en", "draft-post.md"));
            Assert.Contains("draft: true", imported);
            Assert.DoesNotContain("draft: false", imported);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_AcceptsXHandleWithUnderscore()
    {
        var root = CreateTempRoot("pf-web-contributions-x-handle-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe", x: "Jane_Doe");
            WritePost(sourceRoot, "sample-post");

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Import_CopiesLocalAuthorAvatarIntoWebsiteAssets()
    {
        var root = CreateTempRoot("pf-web-contributions-author-avatar-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            var siteRoot = Path.Combine(root, "site");
            WriteAuthor(sourceRoot, "jane-doe", avatar: "./images/jane-doe.webp");
            var authorImagesRoot = Path.Combine(sourceRoot, "authors", "images");
            Directory.CreateDirectory(authorImagesRoot);
            File.WriteAllBytes(Path.Combine(authorImagesRoot, "jane-doe.webp"), [0, 1, 2, 3]);
            WritePost(sourceRoot, "sample-post");
            Directory.CreateDirectory(siteRoot);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot,
                SiteRoot = siteRoot,
                Import = true,
                Force = true
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.True(File.Exists(Path.Combine(siteRoot, "static", "assets", "authors", "jane-doe", "jane-doe.webp")));
            var catalog = File.ReadAllText(Path.Combine(siteRoot, "data", "authors", "catalog.json"));
            Assert.Contains("\"avatar\": \"/assets/authors/jane-doe/jane-doe.webp\"", catalog);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_RejectsImageReferencesThatEscapePostBundle()
    {
        var root = CreateTempRoot("pf-web-contributions-traversal-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "sample-post",
                """
                ---
                title: "Sample Post"
                description: "A practical sample contribution."
                date: "2026-04-29"
                language: "en"
                authors:
                  - jane-doe
                image: "./cover.webp"
                image_alt: "Sample cover"
                draft: true
                ---

                ![Bad](../secret.png)
                """);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("markdown image target '../secret.png' must stay inside the post bundle", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_RejectsAuthorAvatarThatEscapesAuthorsFolder()
    {
        var root = CreateTempRoot("pf-web-contributions-avatar-traversal-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe", avatar: "../jane-doe.webp");
            WritePost(sourceRoot, "sample-post");

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("avatar '../jane-doe.webp' must stay inside authors", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_RejectsOutOfRangeDate()
    {
        var root = CreateTempRoot("pf-web-contributions-date-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "old-post",
                """
                ---
                title: "Old Post"
                description: "A practical sample contribution."
                date: "1999-12-31"
                language: "en"
                authors:
                  - jane-doe
                image: "./cover.webp"
                image_alt: "Sample cover"
                draft: true
                ---

                Old post.
                """);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("date year must be between 2000 and 2100", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_RejectsAssetsOverConfiguredSizeLimit()
    {
        var root = CreateTempRoot("pf-web-contributions-asset-size-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "large-asset-post");

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot,
                MaxAssetBytes = 2
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("is larger than 2 bytes", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_ReportsInvalidAuthorProfiles()
    {
        var root = CreateTempRoot("pf-web-contributions-author-profile-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe", name: "", linkedin: "https://example.com/janedoe");
            WritePost(sourceRoot, "sample-post");

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("author 'jane-doe': missing name", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Errors, error => error.Contains("author 'jane-doe': linkedin must be a valid linkedin.com URL", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_ReportsEscapingContributionPathsAsErrors()
    {
        var root = CreateTempRoot("pf-web-contributions-paths-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            Directory.CreateDirectory(sourceRoot);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot,
                PostsPath = "../posts"
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("PostsPath must stay inside the configured root", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_ReportsPublishReadinessWarnings()
    {
        var root = CreateTempRoot("pf-web-contributions-editorial-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "editorial-post",
                """
                ---
                title: "Editorial Post"
                description: "A practical sample contribution."
                date: "2026-04-29"
                language: "en"
                authors:
                  - jane-doe
                image: "./cover.webp"
                image_alt: "Screenshot showing a configured workflow"
                draft: true
                ---

                PowerApps and PowerAutomate save files into Sharepoint.

                yaml
                ```text
                sample: true
                ```

                • File Name
                ________________________________________
                """);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.Contains(result.Warnings, warning => warning.Contains("decorative separator lines", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("standalone 'yaml'", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("bullet characters", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Warnings, warning => warning.Contains("PowerApps", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, warning => warning.Contains("PowerAutomate", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, warning => warning.Contains("Sharepoint", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_AllowsSetextHeadings()
    {
        var root = CreateTempRoot("pf-web-contributions-setext-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "setext-post",
                """
                ---
                title: "Setext Post"
                description: "A practical sample contribution."
                date: "2026-04-29"
                language: "en"
                authors:
                  - jane-doe
                image: "./cover.webp"
                image_alt: "Screenshot showing a configured workflow"
                draft: true
                ---

                Main Section
                ============

                Secondary Section
                -----------------
                """);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("decorative separator lines", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Validate_RejectsSlugLikeFeaturedImageAlt()
    {
        var root = CreateTempRoot("pf-web-contributions-alt-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "bad-alt-post",
                """
                ---
                title: "Bad Alt Post"
                description: "A practical sample contribution."
                date: "2026-04-29"
                language: "en"
                authors:
                  - jane-doe
                image: "./cover.webp"
                image_alt: "powerplatform-save-files-issue"
                draft: true
                ---

                Body.
                """);

            var result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("image_alt looks like a slug", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void ContributionsCli_FailOnWarningsReturnsNonZeroForEditorialWarnings()
    {
        var root = CreateTempRoot("pf-web-contributions-cli-warnings-");

        try
        {
            var sourceRoot = Path.Combine(root, "contributions");
            WriteAuthor(sourceRoot, "jane-doe");
            WritePost(sourceRoot, "warning-post",
                """
                ---
                title: "Warning Post"
                description: "A practical sample contribution."
                date: "2026-04-29"
                language: "en"
                authors:
                  - jane-doe
                image: "./cover.webp"
                image_alt: "Screenshot showing a workflow"
                draft: true
                ---

                ________________________________________
                """);

            var relaxed = WebCliCommandHandlers.HandleSubCommand(
                "contributions",
                new[] { "validate", "--root", sourceRoot },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);
            var strict = WebCliCommandHandlers.HandleSubCommand(
                "contributions",
                new[] { "validate", "--root", sourceRoot, "--fail-on-warnings" },
                outputJson: true,
                logger: new WebConsoleLogger(),
                outputSchemaVersion: 1);

            Assert.Equal(0, relaxed);
            Assert.Equal(1, strict);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void WriteAuthor(
        string sourceRoot,
        string slug,
        string x = "JaneDoe",
        string name = "Jane Doe",
        string linkedin = "https://www.linkedin.com/in/janedoe",
        string? avatar = null)
    {
        var authorsRoot = Path.Combine(sourceRoot, "authors");
        Directory.CreateDirectory(authorsRoot);
        var avatarLine = string.IsNullOrWhiteSpace(avatar) ? string.Empty : $"avatar: {avatar}\n";
        File.WriteAllText(Path.Combine(authorsRoot, slug + ".yml"),
            $$"""
            name: {{name}}
            slug: {{slug}}
            {{avatarLine}}linkedin: {{linkedin}}
            x: {{x}}
            """);
    }

    private static void WritePost(string sourceRoot, string slug, string? markdown = null)
    {
        var postRoot = Path.Combine(sourceRoot, "posts", "en", slug);
        Directory.CreateDirectory(Path.Combine(postRoot, "images"));
        File.WriteAllText(Path.Combine(postRoot, "index.md"), markdown ??
            $$"""
            ---
            title: "{{slug}}"
            description: "A practical sample contribution."
            date: "2026-04-29"
            language: "en"
            authors:
              - jane-doe
            image: "./cover.webp"
            image_alt: "Sample cover"
            draft: true
            ---

            Hello from {{slug}}.
            """);
        File.WriteAllBytes(Path.Combine(postRoot, "cover.webp"), [0, 1, 2, 3]);
        File.WriteAllBytes(Path.Combine(postRoot, "images", "diagram.png"), [0, 1, 2, 3]);
    }

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }
}
