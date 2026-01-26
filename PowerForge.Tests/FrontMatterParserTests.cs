using PowerForge.Web;

public class FrontMatterParserTests
{
    [Fact]
    public void Parse_CapturesMetaWithDotNotationAndLists()
    {
        var markdown = @"---
title: Hello
hero.title: Welcome
hero.badges:
  - Alpha
  - New
meta.cta.primary: Get Started
tags: [one, two]
---

Body";

        var (matter, _) = FrontMatterParser.Parse(markdown);

        Assert.NotNull(matter);
        Assert.Equal("Hello", matter!.Title);
        Assert.Equal(new[] { "one", "two" }, matter.Tags);

        var hero = Assert.IsType<Dictionary<string, object?>>(matter.Meta["hero"]);
        Assert.Equal("Welcome", hero["title"]?.ToString());
        var badges = Assert.IsType<string[]>(hero["badges"]);
        Assert.Equal(new[] { "Alpha", "New" }, badges);

        var cta = Assert.IsType<Dictionary<string, object?>>(matter.Meta["cta"]);
        Assert.Equal("Get Started", cta["primary"]?.ToString());
    }
}
