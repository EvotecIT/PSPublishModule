using PowerForge.Web;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Tests;

public sealed partial class WebApiSocialCardThemeTests
{
    [Theory]
    [InlineData(typeof(ISocialCardContract), "public interface ISocialCardContract")]
    [InlineData(typeof(SocialCardHandler), "public delegate int SocialCardHandler(string value);")]
    [InlineData(typeof(SocialCardRefHandler), "public delegate void SocialCardRefHandler(ref int value);")]
    [InlineData(typeof(SocialCardTransform<,>), "public delegate TResult? SocialCardTransform<in T, out TResult>(T? value);")]
    [InlineData(typeof(SocialCardChoice), "public enum SocialCardChoice")]
    [InlineData(typeof(SocialCardValue), "public struct SocialCardValue")]
    [InlineData(typeof(SocialCardBase), "public abstract class SocialCardBase")]
    [InlineData(typeof(SocialCardLeaf), "public sealed class SocialCardLeaf")]
    public void ApiCards_RenderKindSpecificDeclarations(Type type, string declaration)
    {
        if (!WebSocialCardGenerator.IsPngRenderingAvailable()) return;
        var spec = new SiteSpec
        {
            Name = "Example", BaseUrl = "https://example.test", Social = new SocialSpec
            {
                AutoGenerateCards = true, GeneratedCardTitle = "page", GeneratedCardTextFit = true,
                GeneratedCardTheme = "code", GeneratedCardsPath = "/assets/social/generated/api",
                GeneratedCardThemes = new() { ["code"] = new SocialCardThemeSpec { Style = "api", Variant = "code-led" } }
            }
        };
        var config = Path.Combine(_root, "site.json");
        File.WriteAllText(config, JsonSerializer.Serialize(spec));
        var xml = Path.Combine(_root, "api.xml");
        File.WriteAllText(xml, $"<doc><assembly><name>Fixtures</name></assembly><members><member name=\"T:{type.FullName}\"><summary>Example output.</summary></member></members></doc>");
        var output = Path.Combine(_root, "_site", "api");
        var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
        {
            XmlPath = xml, AssemblyPath = type.Assembly.Location, OutputPath = output, SiteConfigPath = config,
            BaseUrl = "/api", Title = "Example API reference", Template = "docs", Format = "both", IncludeUndocumentedTypes = false,
            AutoGenerateSocialCards = true, SocialCardTheme = "code", SocialCardTitle = "name", SocialCardsRequired = true
        });
        Assert.Equal(1, result.TypeCount);
        var page = Directory.EnumerateFiles(output, "index.html", SearchOption.AllDirectories)
            .Single(path => path != Path.Combine(output, "index.html"));
        var html = File.ReadAllText(page);
        string Meta(string name) => WebUtility.HtmlDecode(Regex.Match(html, "property=\"" + name + "\" content=\"([^\"]*)\"").Groups[1].Value);
        var route = new Uri(Meta("og:url")).AbsolutePath;
        var name = type.Name.Split('`')[0];
        var item = new ContentItem
        {
            Title = name, Description = "Example output.", Collection = "api", OutputPath = route,
            Meta = new() { ["social_title"] = name, ["social_card_badge"] = "API", ["social_card_code"] = declaration }
        };
        var expected = WebSiteBuilder.CreateSocialCardGenerationPlan(spec, item, Meta("og:title"), Meta("og:description"), "Example", _root)!;
        Assert.Equal("/" + expected.RelativePath, new Uri(Meta("og:image")).AbsolutePath);
    }
}

public interface ISocialCardContract { }
public delegate int SocialCardHandler(string value);
public delegate void SocialCardRefHandler(ref int value);
public delegate TResult? SocialCardTransform<in T, out TResult>(T? value);
public enum SocialCardChoice { First }
public struct SocialCardValue { }
public abstract class SocialCardBase { }
public sealed class SocialCardLeaf { }
