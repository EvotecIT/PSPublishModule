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
