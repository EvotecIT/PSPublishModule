using PowerForge.Web;

namespace PowerForge.Tests;

public class WebVisualStorySvgCssAnimationTests
{
    [Fact]
    public void Stage_AcceptsEffectiveAnimationStyleOnSvgRoot()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" style=\"animation: fade 1s infinite\"><style>@keyframes fade{from{opacity:0}to{opacity:1}}</style><rect width=\"1\" height=\"1\"/></svg>");
    }

    [Fact]
    public void Stage_AcceptsEffectiveAnimationLonghands()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{from{opacity:0}to{opacity:1}}rect{animation-name:fade;animation-duration:1s}</style><rect width=\"1\" height=\"1\"/></svg>");
    }

    [Fact]
    public void Stage_AcceptsEffectiveAnimationWithCommentsAndImportantPriority()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{from{opacity:0}to{opacity:1}}rect{/* keep */animation:fade 1s infinite!important}</style><rect width=\"1\" height=\"1\"/></svg>");
    }

    [Theory]
    [InlineData("animation:none")]
    [InlineData("animation:fade 0s")]
    [InlineData("animation:fade 1s paused")]
    [InlineData("animation:fade 1s;animation:none")]
    [InlineData("animation:fade 1s;animation-play-state:paused")]
    [InlineData("animation:fade 1s 0")]
    [InlineData("animation-name:fade;animation-duration:1s;animation-iteration-count:0")]
    [InlineData("animation-name:fade")]
    [InlineData("animation-name:fade;animation-duration:0s")]
    public void Stage_RejectsCssThatDoesNotProduceMotion(string declarations)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{{from{{opacity:0}}to{{opacity:1}}}}rect{{{declarations}}}</style><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_RejectsCommentedOutKeyframes()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>/* @keyframes fade{from{opacity:0}to{opacity:1}} */rect{animation:fade 1s}</style><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_RejectsKeyframeTextInsideCssValues()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>rect{--note:@keyframes fade{from{opacity:0}to{opacity:1}};animation:fade 1s}</style><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_RejectsAnimationNameWithoutMatchingKeyframes()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{from{opacity:0}to{opacity:1}}rect{animation:spin 1s}</style><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_RejectsAnimationAppliedOnlyToMissingElements()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{from{opacity:0}to{opacity:1}}.missing{animation:fade 1s}</style><rect class=\"present\" width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("rect")]
    [InlineData(".present")]
    [InlineData("#target")]
    [InlineData("rect.present#target")]
    public void Stage_AcceptsAnimationAppliedToExistingElements(string selector)
    {
        StageSvg(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{{from{{opacity:0}}to{{opacity:1}}}}{selector}{{animation:fade 1s}}</style><rect id=\"target\" class=\"present\" width=\"1\" height=\"1\"/></svg>");
    }

    [Fact]
    public void Stage_CombinesAdjacentStyleTextAndCdataNodes()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{from{opacity:0}to{opacity:1}}<![CDATA[rect{animation:fade 1s}]]></style><rect width=\"1\" height=\"1\"/></svg>");
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("0ms")]
    [InlineData("00:00:00")]
    [InlineData("indefinite")]
    public void Stage_RejectsSmilAnimationWithoutPositiveDuration(string duration)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"><animate attributeName=\"opacity\" dur=\"{duration}\" values=\"0;1\"/></rect></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("250ms")]
    [InlineData("1s")]
    [InlineData("00:00:01")]
    public void Stage_AcceptsSmilAnimationWithPositiveDuration(string duration)
    {
        StageSvg(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"><animate attributeName=\"opacity\" dur=\"{duration}\" values=\"0;1\"/></rect></svg>");
    }

    [Theory]
    [InlineData("begin", "indefinite")]
    [InlineData("begin", "click")]
    [InlineData("repeatCount", "0")]
    [InlineData("repeatDur", "0s")]
    public void Stage_RejectsSmilAnimationThatCannotRunAutomatically(string attribute, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"><animate attributeName=\"opacity\" dur=\"1s\" {attribute}=\"{value}\" values=\"0;1\"/></rect></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_AcceptsSmilAnimationWithAutomaticClockBegin()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"><animate attributeName=\"opacity\" dur=\"1s\" begin=\"250ms\" repeatCount=\"2\" values=\"0;1\"/></rect></svg>");
    }

    [Fact]
    public void Stage_RejectsUntimedSetAsStaticPresentation()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"><set attributeName=\"opacity\" to=\"1\"/></rect></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_AcceptsTimedSetTransition()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1\" height=\"1\"><set attributeName=\"opacity\" to=\"1\" dur=\"1s\"/></rect></svg>");
    }

    [Theory]
    [InlineData("<image href=\"https://example.test/frame.png\"/>")]
    [InlineData("<image href=\"../frame.png\"/>")]
    [InlineData("<rect style=\"fill:url(https://example.test/fill.svg)\"/>")]
    [InlineData("<style>rect{fill:url('../fill.svg')}</style>")]
    [InlineData("<rect filter=\"url(https://example.test/filter.svg#blur)\"/>")]
    [InlineData("<rect fill=\"url(../paint.svg#gradient)\"/>")]
    [InlineData("<style>@import 'https://example.test/story.css';</style>")]
    [InlineData("<style>@import \"../story.css\";</style>")]
    public void Stage_RejectsExternalSvgResources(string content)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{{from{{opacity:0}}to{{opacity:1}}}}rect{{animation:fade 1s}}</style>{content}<rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("self-contained", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_AcceptsFragmentOnlySvgReferences()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"fill\"><stop offset=\"0\"/></linearGradient></defs><style>@keyframes fade{from{opacity:0}to{opacity:1}}rect{animation:fade 1s;fill:url(#fill)}</style><rect fill=\"url(#fill)\" width=\"1\" height=\"1\"/></svg>");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<foreignObject><div xmlns=\"http://www.w3.org/1999/xhtml\">active</div></foreignObject>")]
    [InlineData("<rect onload=\"alert(1)\" width=\"1\" height=\"1\"/>")]
    [InlineData("<a href=\"#target\"><set attributeName=\"href\" to=\"javascript:alert(1)\" dur=\"1s\"/></a>")]
    public void Stage_RejectsActiveSvgContent(string content)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{{from{{opacity:0}}to{{opacity:1}}}}rect{{animation:fade 1s}}</style>{content}<rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("Visual-story SVG artifacts cannot", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("@media print")]
    [InlineData("@supports (display:grid)")]
    [InlineData("@container (min-width:1px)")]
    public void Stage_RejectsAnimationAvailableOnlyInsideConditionalRules(string conditionalRule)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{{from{{opacity:0}}to{{opacity:1}}}}{conditionalRule}{{rect{{animation:fade 1s}}}}</style><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_RejectsKeyframesAvailableOnlyInsideConditionalRules()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            StageSvg(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@media print{@keyframes fade{from{opacity:0}to{opacity:1}}}rect{animation:fade 1s}</style><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Contains("supported animation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_AcceptsAnimationInsideNonConditionalLayer()
    {
        StageSvg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes fade{from{opacity:0}to{opacity:1}}@layer story{rect{animation:fade 1s}}</style><rect width=\"1\" height=\"1\"/></svg>");
    }

    private static void StageSvg(string svg)
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            File.WriteAllText(Path.Combine(root, "source", "demo.svg"), svg);
            _ = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = Path.Combine(root, "published")
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
