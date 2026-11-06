using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PowerForge.Tests;
using PowerForge.Web.Cli;
using Xunit;

[Collection(ProcessEnvironmentCollection.Name)]
public class WebPipelineRunnerSplitTests
{
    // overlay runs before the apidocs block in every shard; xref-merge runs after it and reads every shard's output.
    // "early" comes after the block but needs only "static", so it moves to shard 1's prepare run.
    private const string Pipeline =
        """
        {
          "maxParallelism": 2,
          "profile": true,
          "profilePath": "./_temp/profile.json",
          "steps": [
            { "task": "overlay", "id": "static", "source": "./static", "destination": "./_site" },
            { "task": "apidocs", "failOnWarnings": false, "id": "alpha", "type": "CSharp", "format": "both", "xml": "./alpha.xml", "out": "./_site/api/alpha", "baseUrl": "/api/alpha", "title": "Alpha API" },
            { "task": "apidocs", "failOnWarnings": false, "id": "beta", "type": "CSharp", "format": "both", "inputs": [
              { "id": "beta-one", "xml": "./beta-one.xml", "out": "./_site/api/beta-one", "baseUrl": "/api/beta-one", "title": "Beta One API" },
              { "id": "beta-two", "xml": "./beta-two.xml", "out": "./_site/api/beta-two", "baseUrl": "/api/beta-two", "title": "Beta Two API" },
              { "id": "beta-three", "xml": "./beta-three.xml", "out": "./_site/api/beta-three", "baseUrl": "/api/beta-three", "title": "Beta Three API" }
            ] },
            { "task": "apidocs", "failOnWarnings": false, "id": "gamma", "type": "CSharp", "format": "both", "xml": "./gamma.xml", "out": "./_site/api/gamma", "baseUrl": "/api/gamma", "title": "Gamma API" },
            { "task": "overlay", "id": "early", "splitPhase": "prepare", "dependsOn": "static", "source": "./static", "destination": "./_site/early" },
            { "task": "xref-merge", "id": "merge-xref", "dependsOn": ["alpha", "beta", "gamma"], "out": "./_site/xrefmap.json", "mapFiles": [
              "./_site/api/alpha/xrefmap.json", "./_site/api/beta-one/xrefmap.json", "./_site/api/beta-two/xrefmap.json",
              "./_site/api/beta-three/xrefmap.json", "./_site/api/gamma/xrefmap.json"
            ] }
          ]
        }
        """;

    [Fact]
    public void SplitRun_MergedShardsMatchSingleRun()
    {
        var root = CreateRoot();
        try
        {
            var single = CreateSite(root, "single");
            var main = CreateSite(root, "main");
            var helper = CreateSite(root, "helper");
            var archives = Path.Combine(root, "archives");

            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(single, "pipeline.json"), new WebConsoleLogger()));

            var mainPrepare = WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(1, 2, Path.Combine(archives, "main", "shard.tar")));
            var helperPrepare = WebPipelineRunner.RunPipeline(Path.Combine(helper, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(2, 2, Path.Combine(archives, "helper", "shard.tar")));
            AssertSucceeded(mainPrepare);
            AssertSucceeded(helperPrepare);

            // Each machine generated only part of the API docs and skipped the steps after the block.
            var mainApi = Directory.GetDirectories(Path.Combine(main, "_site", "api")).Select(Path.GetFileName).ToArray();
            var helperApi = Directory.GetDirectories(Path.Combine(helper, "_site", "api")).Select(Path.GetFileName).ToArray();
            Assert.NotEmpty(mainApi);
            Assert.NotEmpty(helperApi);
            Assert.Empty(mainApi.Intersect(helperApi));
            Assert.Equal(5, mainApi.Length + helperApi.Length);
            Assert.False(File.Exists(Path.Combine(main, "_site", "xrefmap.json")));
            Assert.Equal("skipped (split: runs in finish)", mainPrepare.Steps[^1].Message);

            // Only shard 1 runs the splitPhase step, after its archive holds the apidocs output.
            Assert.True(File.Exists(Path.Combine(main, "_site", "early", "index.html")));
            Assert.False(Directory.Exists(Path.Combine(helper, "_site", "early")));
            Assert.Equal("skipped (split: runs in shard 1)", helperPrepare.Steps[^2].Message);
            Assert.DoesNotContain(ReadArchiveEntries(Path.Combine(archives, "main", "shard.tar")),
                static name => name.StartsWith("files/_site/early/", StringComparison.Ordinal));

            // finish in shard 1's workspace must not run it again.
            var early = Path.Combine(main, "_site", "early", "index.html");
            var earlyContent = File.ReadAllText(early);
            File.WriteAllText(early, "marker");
            var finish = WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                split: WebPipelineSplitOptions.Parse("finish", null, null, archives));
            AssertSucceeded(finish);
            Assert.Equal("marker", File.ReadAllText(early));
            File.WriteAllText(early, earlyContent);

            Assert.Equal(ReadTree(single), ReadTree(main));

            // A workspace without shard 1's receipt runs the step itself.
            var helperFinish = WebPipelineRunner.RunPipeline(Path.Combine(helper, "pipeline.json"), new WebConsoleLogger(),
                split: WebPipelineSplitOptions.Parse("finish", null, null, archives));
            AssertSucceeded(helperFinish);
            Assert.Equal(ReadTree(single), ReadTree(helper));
            // The finish profile keeps the prepare run's results for the steps it skipped.
            Assert.DoesNotContain("split: ran in prepare", File.ReadAllText(Path.Combine(main, "_temp", "profile.json")), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void SplitFinish_RejectsMissingShardArchive()
    {
        var root = CreateRoot();
        try
        {
            var main = CreateSite(root, "main");
            var archives = Path.Combine(root, "archives");
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(1, 2, Path.Combine(archives, "shard-1.tar"))));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                    split: WebPipelineSplitOptions.Parse("finish", null, null, archives)));
            Assert.Contains("one split archive for each shard 1..2", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(main, "_site", "xrefmap.json")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void SplitFinish_RejectsAlteredPayloadAndForeignEngineBeforeChangingTheSite()
    {
        var root = CreateRoot();
        var previousEngine = Environment.GetEnvironmentVariable("POWERFORGE_ENGINE_IDENTITY");
        try
        {
            var main = CreateSite(root, "main");
            var helper = CreateSite(root, "helper");
            var archives = Path.Combine(root, "archives");
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(1, 2, Path.Combine(archives, "main", "shard.tar"))));
            var helperArchive = Path.Combine(archives, "helper", "shard.tar");
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(helper, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(2, 2, helperArchive)));
            var before = ReadTree(main);

            // One payload byte differs from the hash its manifest records.
            var original = File.ReadAllBytes(helperArchive);
            RewriteFirstPayload(helperArchive);
            var tampered = Assert.Throws<InvalidOperationException>(() =>
                WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                    split: WebPipelineSplitOptions.Parse("finish", null, null, archives)));
            Assert.Contains("different content", tampered.Message, StringComparison.Ordinal);
            Assert.Equal(before, ReadTree(main));

            // A shard built by another engine cannot be mixed in.
            File.WriteAllBytes(helperArchive, original);
            Environment.SetEnvironmentVariable("POWERFORGE_ENGINE_IDENTITY", "source:other/engine@0000000000000000000000000000000000000000");
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(helper, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(2, 2, helperArchive)));
            Environment.SetEnvironmentVariable("POWERFORGE_ENGINE_IDENTITY", previousEngine);
            var foreign = Assert.Throws<InvalidOperationException>(() =>
                WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                    split: WebPipelineSplitOptions.Parse("finish", null, null, archives)));
            Assert.Contains("other/engine", foreign.Message, StringComparison.Ordinal);
            Assert.Equal(before, ReadTree(main));
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_ENGINE_IDENTITY", previousEngine);
            TryDelete(root);
        }
    }

    [Fact]
    public void SplitFinish_RejectsOutputsLinkedOutsideTheRoot()
    {
        var root = CreateRoot();
        try
        {
            var main = CreateSite(root, "main");
            var helper = CreateSite(root, "helper");
            var archives = Path.Combine(root, "archives");
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(1, 2, Path.Combine(archives, "main", "shard.tar"))));
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(helper, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(2, 2, Path.Combine(archives, "helper", "shard.tar"))));

            // The helper's API folder in the main site now points outside the site.
            var helperApi = Directory.GetDirectories(Path.Combine(helper, "_site", "api")).Select(Path.GetFileName).First();
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            var linked = Path.Combine(main, "_site", "api", helperApi!);
            if (Directory.Exists(linked))
                Directory.Delete(linked, true);
            // A link to a folder inside the site whose own parent is a link to the outside folder.
            var alias = Path.Combine(main, "alias");
            Directory.CreateSymbolicLink(alias, outside);
            Directory.CreateSymbolicLink(linked, Path.Combine(alias, "sub"));
            Directory.CreateDirectory(Path.Combine(outside, "sub"));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                    split: WebPipelineSplitOptions.Parse("finish", null, null, archives)));
            // Planning sees the linked output folder; the archive targets are checked the same way.
            Assert.Contains("outside the pipeline root", error.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(outside, "sub")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void SplitRun_RejectsStepsBetweenApiDocsSteps()
    {
        var root = CreateRoot();
        try
        {
            var site = CreateSite(root, "site");
            var pipeline = Path.Combine(site, "pipeline.json");
            var gamma = "    { \"task\": \"apidocs\", \"failOnWarnings\": false, \"id\": \"gamma\"";
            File.WriteAllText(pipeline, Pipeline.Replace(
                gamma,
                "    { \"task\": \"overlay\", \"id\": \"late\", \"source\": \"./static\", \"destination\": \"./_late\" },\n" + gamma,
                StringComparison.Ordinal));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebPipelineRunner.RunPipeline(pipeline, new WebConsoleLogger(), split: Prepare(1, 2, Path.Combine(root, "shard.tar"))));
            Assert.Contains("consecutive apidocs steps", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void SplitFinish_RunsPrepareStepsThatShardOneSkipped()
    {
        var root = CreateRoot();
        try
        {
            var main = CreateSite(root, "main");
            var helper = CreateSite(root, "helper");
            var archives = Path.Combine(root, "archives");
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                skipTasks: new[] { "overlay" }, split: Prepare(1, 2, Path.Combine(archives, "main", "shard.tar"))));
            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(helper, "pipeline.json"), new WebConsoleLogger(),
                split: Prepare(2, 2, Path.Combine(archives, "helper", "shard.tar"))));
            Assert.False(Directory.Exists(Path.Combine(main, "_site", "early")));

            AssertSucceeded(WebPipelineRunner.RunPipeline(Path.Combine(main, "pipeline.json"), new WebConsoleLogger(),
                split: WebPipelineSplitOptions.Parse("finish", null, null, archives)));
            Assert.True(File.Exists(Path.Combine(main, "_site", "early", "index.html")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void SplitRun_RejectsPrepareStepsThatNeedTheFinishRun()
    {
        var root = CreateRoot();
        try
        {
            var site = CreateSite(root, "site");
            var pipeline = Path.Combine(site, "pipeline.json");
            File.WriteAllText(pipeline, Pipeline.Replace("\"dependsOn\": \"static\"", "\"dependsOn\": \"gamma\"", StringComparison.Ordinal));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebPipelineRunner.RunPipeline(pipeline, new WebConsoleLogger(), split: Prepare(1, 2, Path.Combine(root, "shard.tar"))));
            Assert.Contains("depends on step 'gamma'; it may depend only on steps before the apidocs block", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void AssignSplitShards_PutsHeaviestUnitsOnLightestShards()
    {
        var shards = WebPipelineRunner.AssignSplitShards(new long[] { 10, 1, 1, 1, 9, 8 }, 3);

        Assert.Equal(new[] { 1, 3, 2, 3, 2, 3 }, shards);
        var loads = new long[] { 10, 1, 1, 1, 9, 8 }
            .Select((weight, index) => (weight, shard: shards[index]))
            .GroupBy(static item => item.shard)
            .ToDictionary(static group => group.Key, static group => group.Sum(static item => item.weight));
        Assert.Equal(new long[] { 10, 10, 10 }, loads.OrderBy(static pair => pair.Key).Select(static pair => pair.Value));
    }

    [Theory]
    [InlineData("prepare", "3/2", "shard.tar", null)]
    [InlineData("prepare", "0/2", "shard.tar", null)]
    [InlineData("prepare", "1/2", null, null)]
    [InlineData("finish", null, null, null)]
    [InlineData("finish", "1/2", null, "archives")]
    [InlineData(null, "1/2", null, null)]
    [InlineData("merge", null, null, null)]
    public void SplitOptions_RejectInvalidCombinations(string? phase, string? shard, string? archive, string? archives)
    {
        Assert.Throws<ArgumentException>(() => WebPipelineSplitOptions.Parse(phase, shard, archive, archives));
    }

    [Fact]
    public void SplitOptions_AreOptional()
    {
        Assert.Null(WebPipelineSplitOptions.Parse(null, null, null, null));
    }

    private static WebPipelineSplitOptions Prepare(int index, int count, string archive)
        => WebPipelineSplitOptions.Parse("prepare", $"{index}/{count}", archive, null)!;

    private static void AssertSucceeded(PowerForge.Web.WebPipelineResult result)
        => Assert.True(result.Success, string.Join(Environment.NewLine, result.Steps.ConvertAll(step => $"{step.Task}: {step.Message}")));

    private static List<string> ReadArchiveEntries(string archive)
    {
        var names = new List<string>();
        using var reader = new TarReader(File.OpenRead(archive));
        while (reader.GetNextEntry() is { } entry)
            names.Add(entry.Name);
        return names;
    }

    private static void RewriteFirstPayload(string archive)
    {
        var entries = new List<(string Name, byte[] Data)>();
        using (var reader = new TarReader(File.OpenRead(archive)))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                using var buffer = new MemoryStream();
                entry.DataStream?.CopyTo(buffer);
                entries.Add((entry.Name, buffer.ToArray()));
            }
        }
        var payload = entries.FindIndex(static entry => entry.Name.StartsWith("files/", StringComparison.Ordinal) && entry.Data.Length > 0);
        entries[payload].Data[0] ^= 0x20;
        using var writer = new TarWriter(File.Create(archive), TarEntryFormat.Pax, leaveOpen: false);
        foreach (var (name, data) in entries)
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) });
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateSite(string root, string name)
    {
        var site = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(site, "static"));
        File.WriteAllText(Path.Combine(site, "static", "index.html"), "<html><body>home</body></html>");
        foreach (var assembly in new[] { "Alpha", "BetaOne", "BetaTwo", "BetaThree", "Gamma" })
        {
            var file = assembly switch
            {
                "BetaOne" => "beta-one.xml",
                "BetaTwo" => "beta-two.xml",
                "BetaThree" => "beta-three.xml",
                _ => assembly.ToLowerInvariant() + ".xml"
            };
            File.WriteAllText(Path.Combine(site, file), BuildMinimalXml(assembly, $"T:{assembly}.Widget"));
        }
        File.WriteAllText(Path.Combine(site, "pipeline.json"), Pipeline);
        return site;
    }

    private static SortedDictionary<string, string> ReadTree(string site)
    {
        var tree = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var output = Path.Combine(site, "_site");
        foreach (var file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            // Generators stamp their run time; everything else must match exactly.
            tree[Path.GetRelativePath(output, file).Replace('\\', '/')] =
                Regex.Replace(File.ReadAllText(file), @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?", "<timestamp>");
        return tree;
    }

    private static string BuildMinimalXml(string assemblyName, string memberName)
    {
        return
            $"""
            <doc>
              <assembly>
                <name>{assemblyName}</name>
              </assembly>
              <members>
                <member name="{memberName}">
                  <summary>{assemblyName} widget.</summary>
                </member>
              </members>
            </doc>
            """;
    }

    private static void TryDelete(string root)
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
