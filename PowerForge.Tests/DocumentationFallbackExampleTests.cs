using PowerForge;

public class DocumentationFallbackExampleTests
{
    [Theory]
    [InlineData("Invoke-Thing [-OutputDirectory] <String> [-Optional <String[]>] [<CommonParameters>]", "String", "Invoke-Thing -OutputDirectory 'Value'")]
    [InlineData("Invoke-Thing -OutputDirectory <String> [[-Optional] <String[]>] [<CommonParameters>]", "String", "Invoke-Thing -OutputDirectory 'Value'")]
    [InlineData("Invoke-Thing [-OutputDirectory] <String[]> [-Optional <String>] [<CommonParameters>]", "String[]", "Invoke-Thing -OutputDirectory @('Value')")]
    [InlineData("Invoke-Thing -OutputDirectory [-Optional <String>] [<CommonParameters>]", "SwitchParameter", "Invoke-Thing -OutputDirectory")]
    public void FallbackIncludesRequiredNamedAndPositionalArguments(string syntax, string type, string expected)
    {
        var command = CreateCommand(syntax);
        command.Parameters.Add(new() { Name = "Optional", Type = "String" });
        command.Parameters.Add(new() { Name = "OutputDirectory", Type = type, Required = true });

        Enrich(command);

        Assert.Equal(expected, Assert.Single(command.Examples).Code);
    }

    [Fact]
    public void FallbackIncludesEveryRequiredArgumentInSyntaxOrder()
    {
        var names = new[] { "First", "Second", "Third", "Fourth", "Fifth", "Sixth" };
        var command = CreateCommand("Invoke-Thing " + string.Join(" ", names.Select(name => $"-{name} <String>")));
        foreach (var name in names.Reverse())
            command.Parameters.Add(new() { Name = name, Type = "String", Required = true });

        Enrich(command);

        Assert.Equal("Invoke-Thing " + string.Join(" ", names.Select(name => $"-{name} 'Value'")),
            Assert.Single(command.Examples).Code);
    }

    [Fact]
    public void FallbackOptionalArgumentsBelongToTheirParameterSet()
    {
        var command = CreateCommand("Invoke-Thing [[-ByName] <String>] [<CommonParameters>]");
        command.Syntax[0].Name = "ByName";
        command.Syntax.Add(new() { Name = "ById", Text = "Invoke-Thing [-ById <Int32>] [<CommonParameters>]" });
        command.Parameters.Add(new() { Name = "ByName", Type = "String", ParameterSets = new() { "ByName" } });
        command.Parameters.Add(new() { Name = "ById", Type = "Int32", ParameterSets = new() { "ById" } });

        Enrich(command);

        Assert.Equal(new[] { "Invoke-Thing -ByName 'Name'", "Invoke-Thing -ById 1" },
            command.Examples.Select(example => example.Code));
    }

    [Fact]
    public void FallbackDoesNotBorrowAnArgumentForAParameterlessSet()
    {
        var command = CreateCommand("Invoke-Thing [<CommonParameters>]");
        command.Syntax[0].Name = "Default";
        command.Syntax.Add(new() { Name = "ByName", Text = "Invoke-Thing -Name <String> [<CommonParameters>]" });
        command.Parameters.Add(new() { Name = "Name", Type = "String", ParameterSets = new() { "ByName" } });

        Enrich(command);

        Assert.Equal(new[] { "Invoke-Thing", "Invoke-Thing -Name 'Name'" },
            command.Examples.Select(example => example.Code));
    }

    [Fact]
    public void FallbackPreservesAuthoredExamples()
    {
        var command = CreateCommand("Invoke-Thing [-TargetPath] <String> [<CommonParameters>]");
        var authored = new DocumentationExampleHelp
        {
            Code = "Invoke-Thing -TargetPath './existing'",
            Remarks = "Use the existing directory."
        };
        command.Examples.Add(authored);

        Enrich(command);

        Assert.Same(authored, Assert.Single(command.Examples));
        Assert.Equal("Invoke-Thing -TargetPath './existing'", authored.Code);
        Assert.Equal("Use the existing directory.", authored.Remarks);
    }

    private static DocumentationCommandHelp CreateCommand(string syntax) => new()
    {
        Name = "Invoke-Thing",
        CommandType = "Cmdlet",
        Syntax = new() { new() { Name = "Default", IsDefault = true, Text = syntax } }
    };

    private static void Enrich(DocumentationCommandHelp command) => DocumentationFallbackEnricher.Enrich(
        new DocumentationExtractionPayload { Commands = new() { command } }, new NullLogger());
}
