namespace PowerForge.Tests;

public sealed partial class DocumentationMetadataNormalizerTests
{
    [Fact]
    public void Normalize_PreservesCaseDistinctEnumMembersAfterMetadataValues()
    {
        var parameter = new DocumentationParameterHelp
        {
            PossibleValues = ["", " A ", "a", "Other", "OTHER"],
            EnumPossibleValues = ["A", "a", "A"]
        };
        var payload = PayloadWith(new DocumentationCommandHelp
        {
            Name = "Get-CaseMode",
            Parameters = [parameter]
        });

        DocumentationMetadataNormalizer.Normalize(payload);
        DocumentationMetadataNormalizer.Normalize(payload);

        Assert.Equal(["A", "Other", "a"], parameter.PossibleValues);
        Assert.Empty(parameter.EnumPossibleValues);
    }

    [Fact]
    public void Normalize_PreservesAssemblyDistinctRuntimeOutputsWithTheSameClrName()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-AssemblyDistinct",
            CommandType = "Cmdlet",
            RuntimeOutputs =
            [
                Type("SameResult", "Example.SameResult", runtimeIdentity: "AssemblyA#1::Example.SameResult"),
                Type("SameResult", "Example.SameResult", runtimeIdentity: "AssemblyB#2::Example.SameResult")
            ]
        };

        DocumentationMetadataNormalizer.Normalize(PayloadWith(command));

        Assert.Collection(
            command.Outputs,
            output =>
            {
                Assert.Equal("Example.SameResult", output.CanonicalTypeName);
                Assert.Empty(output.RuntimeIdentity);
            },
            output =>
            {
                Assert.Equal("Example.SameResult", output.CanonicalTypeName);
                Assert.Empty(output.RuntimeIdentity);
            });
    }
}
