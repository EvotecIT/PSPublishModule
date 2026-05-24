using System;
using Xunit;

using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public class RepositoryContentNormalizerTests
{
    [Fact]
    public void RewriteRelativeUris_Rewrites_Markdown_And_RawHtml_Assets()
    {
        var content = """
        <img src='assets/ugit.svg' alt='ugit' />
        <a href="docs/Out-Git.md" target="_blank" rel="noopener noreferrer">Out-Git</a>
        [Use-Git](docs/Use-Git.md)
        """;

        var rewritten = RepositoryContentNormalizer.RewriteRelativeUris(
            content,
            "https://raw.githubusercontent.com/StartAutomating/ugit/main/",
            "https://github.com/StartAutomating/ugit/blob/main/");

        Assert.Contains("https://raw.githubusercontent.com/StartAutomating/ugit/main/assets/ugit.svg", rewritten);
        Assert.Contains("https://github.com/StartAutomating/ugit/blob/main/docs/Out-Git.md", rewritten);
        Assert.Contains("https://github.com/StartAutomating/ugit/blob/main/docs/Use-Git.md", rewritten);
        Assert.DoesNotContain("target=\"_blank\"", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"noopener noreferrer\"", rewritten, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RewriteRelativeUris_Preserves_Fenced_Code_And_RootRelative_Links()
    {
        var content = """
        <a href='/2022/03/20/'>permalink</a>
        ~~~html
        <img src='assets/keep.svg' alt='keep' />
        [Example](docs/Keep.md)
        ~~~
        """;

        var rewritten = RepositoryContentNormalizer.RewriteRelativeUris(
            content,
            "https://raw.githubusercontent.com/StartAutomating/ugit/main/");

        Assert.Contains("<a href='/2022/03/20/'>permalink</a>", rewritten);
        Assert.Contains("<img src='assets/keep.svg' alt='keep' />", rewritten);
        Assert.Contains("[Example](docs/Keep.md)", rewritten);
    }

    [Fact]
    public void IsLikelyTemplateSource_Detects_Jekyll_Liquid_Documents()
    {
        var content = """
        ---
        permalink: /2022/03/20/
        ---

        {% for post in site.posts %}
        * [{{ post.title }}]({{ post.url }})
        {% endfor %}
        """;

        Assert.True(RepositoryContentNormalizer.IsLikelyTemplateSource("2022-03-20.md", content));
        Assert.False(RepositoryContentNormalizer.IsLikelyTemplateSource("Use-Git.md", "## Use-Git\n\nRegular markdown."));
    }
}
