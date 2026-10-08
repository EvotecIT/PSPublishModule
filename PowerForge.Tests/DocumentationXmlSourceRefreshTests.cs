namespace PowerForge.Tests;

public sealed class DocumentationXmlSourceRefreshTests
{
    [Fact]
    public void Enrich_RefreshesAuthoredMembersAndRetainsUndocumentedHelp()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var assemblyPath = Path.Combine(root.FullName, "Fixture.dll");
            File.WriteAllBytes(assemblyPath, Array.Empty<byte>());
            File.WriteAllText(Path.ChangeExtension(assemblyPath, ".xml"), """
                <doc><members>
                  <member name="T:Fixture.GetTest">
                    <summary>Current synopsis.</summary>
                    <remarks>Current description.</remarks>
                    <example><code>Get-Test -Path ./sample.txt</code></example>
                  </member>
                  <member name="P:Fixture.GetTest.Path"><summary>Current path help.</summary></member>
                </members></doc>
                """);
            var command = new DocumentationCommandHelp
            {
                Name = "Get-Test", CommandType = "Cmdlet", ImplementingType = "Fixture.GetTest",
                AssemblyPath = assemblyPath, Synopsis = "Old synopsis.", Description = "Old description.",
                Examples = new() { new() { Code = "Get-Test" } },
                Parameters = new()
                {
                    new() { Name = "Path", Description = "Old path help." },
                    new() { Name = "Other", Description = "External help only." }
                }
            };
            var payload = new DocumentationExtractionPayload { Commands = new() { command } };
            var enricher = new XmlDocCommentEnricher(new NullLogger());
            enricher.Enrich(payload);
            enricher.Enrich(payload); // A second refresh must not duplicate examples.

            Assert.Equal("Current synopsis.", command.Synopsis);
            Assert.Equal("Current description.", command.Description);
            Assert.Equal("Get-Test -Path ./sample.txt", Assert.Single(command.Examples).Code);
            Assert.Equal("Current path help.", command.Parameters[0].Description);
            Assert.Equal("External help only.", command.Parameters[1].Description);
        }
        finally { root.Delete(recursive: true); }
    }
}
