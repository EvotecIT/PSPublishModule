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

    [Fact]
    public void Parse_CapturesCategoriesAsTypedField_AndPreservesMetaCategories()
    {
        var markdown = @"---
title: Categories Demo
categories:
  - PowerShell
  - Active Directory
---

Body";

        var (matter, _) = FrontMatterParser.Parse(markdown);

        Assert.NotNull(matter);
        Assert.Equal(new[] { "PowerShell", "Active Directory" }, matter!.Categories);
        var categories = Assert.IsType<string[]>(matter.Meta["categories"]);
        Assert.Equal(new[] { "PowerShell", "Active Directory" }, categories);
    }

    [Fact]
    public void Parse_CapturesTypedLocalizationFields_AndLocalizedAliasMetaLists()
    {
        var markdown = @"---
title: Localized FAQ
language: pl
i18n.group: docs:faq-shared
i18n.aliases.pl:
  - /pl/stary-faq/
aliases.en:
  - /docs/old-faq/
---

Body";

        var (matter, _) = FrontMatterParser.Parse(markdown);

        Assert.NotNull(matter);
        Assert.Equal("pl", matter!.Language);
        Assert.Equal("docs:faq-shared", matter.TranslationKey);

        var i18n = Assert.IsType<Dictionary<string, object?>>(matter.Meta["i18n"]);
        Assert.Equal("docs:faq-shared", i18n["group"]?.ToString());
        var i18nAliases = Assert.IsType<Dictionary<string, object?>>(i18n["aliases"]);
        var plAliases = Assert.IsType<string[]>(i18nAliases["pl"]);
        Assert.Equal(new[] { "/pl/stary-faq/" }, plAliases);

        var aliases = Assert.IsType<Dictionary<string, object?>>(matter.Meta["aliases"]);
        var enAliases = Assert.IsType<string[]>(aliases["en"]);
        Assert.Equal(new[] { "/docs/old-faq/" }, enAliases);
    }

    [Fact]
    public void Parse_CapturesNestedLocalizationBlocks_ForNativeMultilingualAuthoring()
    {
        var markdown = @"---
title: Localized FAQ
i18n:
  language: pl
  group: docs:faq-shared
  aliases:
    pl:
      - /pl/stary-faq/
translations:
  en:
    route: /docs/faq-english/
    aliases:
      - /docs/old-faq/
  pl:
    route: /pl/docs/faq-polski/
---

Body";

        var (matter, _) = FrontMatterParser.Parse(markdown);

        Assert.NotNull(matter);
        Assert.Equal("pl", matter!.Language);
        Assert.Equal("docs:faq-shared", matter.TranslationKey);

        var i18n = Assert.IsType<Dictionary<string, object?>>(matter.Meta["i18n"]);
        Assert.Equal("docs:faq-shared", i18n["group"]?.ToString());

        var translations = Assert.IsType<Dictionary<string, object?>>(matter.Meta["translations"]);
        var en = Assert.IsType<Dictionary<string, object?>>(translations["en"]);
        Assert.Equal("/docs/faq-english/", en["route"]?.ToString());
        var enAliases = Assert.IsType<string[]>(en["aliases"]);
        Assert.Equal(new[] { "/docs/old-faq/" }, enAliases);
    }
}
