using System.Text.Json;
using PowerForge;
using PowerForge.Web;

public sealed class WebApiSourceRevisionTests
{
    [Fact]
    public void Generate_UsesGitRevisionAndLabelsTrackedChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-revision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var git = new GitClient(defaultTimeout: TimeSpan.FromSeconds(10));
            string Run(params string[] args)
            {
                var result = git.RunRawAsync(root, args).GetAwaiter().GetResult();
                Assert.True(result.Succeeded, result.StdErr);
                return result.StdOut.Trim();
            }
            Run("init");
            Directory.CreateDirectory(Path.Combine(root, "Fixtures"));
            var sourceFile = Path.Combine(root, "Fixtures", "DocsFidelityFixture.cs");
            File.WriteAllText(sourceFile, "fixture source");
            Run("add", ".");
            Run("-c", "user.email=fixture@example.invalid", "-c", "user.name=Fixture", "commit", "-m", "Fixture");
            var revision = Run("rev-parse", "HEAD");
            var xml = Path.Combine(root, "fixture.xml");
            File.WriteAllText(xml, """<doc><members><member name="T:PowerForge.Tests.DocsFidelity.Fixture"><summary>Fixture.</summary></member><member name="T:PowerForge.Tests.DocsFidelity.FixtureExtensions"><summary>Extensions.</summary></member></members></doc>""");
            var options = new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = typeof(PowerForge.Tests.DocsFidelity.Fixture).Assembly.Location,
                SourceRootPath = root, SourceUrlPattern = "https://example.invalid/blob/{revision}/{path}#L{line}",
                OutputPath = Path.Combine(root, "api"), Format = "json", IncludeUndocumentedTypes = false, GenerateGitFreshness = true
            };
            WebApiDocsGenerator.Generate(options);
            using (var json = ReadSource())
            {
                var source = json.RootElement.GetProperty("source");
                Assert.Equal(revision, source.GetProperty("revision").GetString());
                Assert.False(source.GetProperty("workingTreeChanged").GetBoolean());
                Assert.Equal(revision, json.RootElement.GetProperty("freshness").GetProperty("commitSha").GetString());
                Assert.Contains("/blob/" + revision + "/Fixtures/DocsFidelityFixture.cs", source.GetProperty("url").GetString());
                Assert.Equal(revision, json.RootElement.GetProperty("extensionMethods")[0].GetProperty("source").GetProperty("revision").GetString());
            }
            File.AppendAllText(sourceFile, " changed");
            WebApiDocsGenerator.Generate(options);
            using (var json = ReadSource())
            {
                Assert.True(json.RootElement.GetProperty("source").GetProperty("workingTreeChanged").GetBoolean());
                Assert.True(json.RootElement.GetProperty("extensionMethods")[0].GetProperty("source").GetProperty("workingTreeChanged").GetBoolean());
            }
            Run("add", "Fixtures/DocsFidelityFixture.cs");
            Run("-c", "user.email=fixture@example.invalid", "-c", "user.name=Fixture", "commit", "-m", "Update");
            WebApiDocsGenerator.Generate(options);
            using (var json = ReadSource())
            {
                var source = json.RootElement.GetProperty("source");
                Assert.Equal(Run("rev-parse", "HEAD"), source.GetProperty("revision").GetString());
                Assert.False(source.GetProperty("workingTreeChanged").GetBoolean());
            }
            JsonDocument ReadSource() => JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputPath, "types", "powerforge-tests-docsfidelity-fixture.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
