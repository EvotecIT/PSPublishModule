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
                  <member name="T:Fixture.Input"><summary>Current input help.</summary></member>
                  <member name="T:Fixture.Output">
                    <summary>Output summary.</summary>
                    <remarks>Current output help.</remarks>
                  </member>
                  <member name="T:Fixture.Empty"><summary> </summary></member>
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
                },
                Inputs = new()
                {
                    new() { Name = "Fixture.Input", Description = "Old input help." },
                    new() { Name = "Fixture.Undocumented", Description = "External input help only." },
                    new() { Name = "Fixture.Empty", Description = "External empty-type help." }
                },
                Outputs = new()
                {
                    new() { Name = "Fixture.Output", Description = "Old output help." },
                    new() { Name = "Fixture.Undocumented", Description = "External output help only." }
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
            Assert.Equal("Current input help.", command.Inputs[0].Description);
            Assert.Equal("External input help only.", command.Inputs[1].Description);
            Assert.Equal("External empty-type help.", command.Inputs[2].Description);
            Assert.Equal("Current output help.", command.Outputs[0].Description);
            Assert.Equal("External output help only.", command.Outputs[1].Description);
        }
        finally { root.Delete(recursive: true); }
    }
}
