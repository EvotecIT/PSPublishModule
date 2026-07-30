namespace PowerForge.Tests;

public sealed class DocumentationMetadataNormalizerTests
{
    [Fact]
    public void DefaultValueFormatter_FormatsTypedValuesAsPowerShellExpressions()
    {
        Assert.Equal("$null", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Null"
        }));
        Assert.Equal("$true", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Boolean",
            Text = "True"
        }));
        Assert.Equal("[Example.Mode]::Advanced", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Enum",
            CanonicalTypeName = "Example.Mode",
            Name = "Advanced",
            Text = "2"
        }));
        Assert.Equal("[System.Enum]::ToObject([Example.Mode], 3)", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Enum",
            CanonicalTypeName = "Example.Mode",
            Text = "3"
        }));
        Assert.Equal("[System.String]", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Type",
            CanonicalTypeName = "System.String"
        }));
        Assert.Equal("@('one', $false)", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Collection",
            Items =
            [
                new DocumentationRuntimeValue { Kind = "String", Text = "one" },
                new DocumentationRuntimeValue { Kind = "Boolean", Text = "False" }
            ]
        }));
    }

    [Fact]
    public void DefaultValueFormatter_EncodesXmlInvalidAndMultilineStrings()
    {
        var formatted = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "String",
            Text = "A\0B\r\n"
        });

        Assert.Equal("(-join @('A', ([char]0), 'B', ([char]13), ([char]10)))", formatted);
        Assert.DoesNotContain('\0', formatted);
        Assert.DoesNotContain('\r', formatted);
        Assert.DoesNotContain('\n', formatted);
    }

    [Fact]
    public void Normalize_UsesMetadataDefaultAndNormalizesAliasesAndPossibleValues()
    {
        var parameter = new DocumentationParameterHelp
        {
            DefaultValue = "stale",
            HasMetadataDefault = true,
            MetadataDefaultValue = new DocumentationRuntimeValue { Kind = "String", Text = string.Empty },
            Aliases = ["", " short ", "SHORT", "s"],
            PossibleValues = ["", " Basic ", "BASIC", "Advanced"]
        };
        var payload = PayloadWith(new DocumentationCommandHelp
        {
            Name = "Get-Sample",
            Parameters = [parameter]
        });

        DocumentationMetadataNormalizer.Normalize(payload);
        DocumentationMetadataNormalizer.Normalize(payload);

        Assert.Equal("''", parameter.DefaultValue);
        Assert.Equal(["short", "s"], parameter.Aliases);
        Assert.Equal(["Basic", "Advanced"], parameter.PossibleValues);
        Assert.False(parameter.HasMetadataDefault);
        Assert.Null(parameter.MetadataDefaultHelp);
        Assert.Null(parameter.MetadataDefaultValue);
    }

    [Fact]
    public void Normalize_MatchesOutputDescriptionsOnlyThroughUnambiguousTypeIdentity()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-Sample",
            CommandType = "Cmdlet",
            AuthoredOutputs =
            [
                Type("Result", "A.Result", "A result."),
                Type("Result", "B.Result", "B result.")
            ],
            RuntimeOutputs =
            [
                Type("Result", "A.Result"),
                Type("Result", "B.Result")
            ]
        };

        DocumentationMetadataNormalizer.Normalize(PayloadWith(command));

        Assert.Collection(
            command.Outputs,
            output =>
            {
                Assert.Equal("A.Result", output.CanonicalTypeName);
                Assert.Equal("A result.", output.Description);
            },
            output =>
            {
                Assert.Equal("B.Result", output.CanonicalTypeName);
                Assert.Equal("B result.", output.Description);
            });
        Assert.Empty(command.AuthoredOutputs);
        Assert.Empty(command.RuntimeOutputs);
    }

    [Fact]
    public void Normalize_PreservesAuthoredFallbackButSuppressesSyntheticObjectOutput()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Set-Sample",
            CommandType = "Cmdlet",
            AuthoredOutputs =
            [
                Type("System.Object", "System.Object"),
                Type("Sample.Result", "Sample.Result", "The authored result.")
            ]
        };

        DocumentationMetadataNormalizer.Normalize(PayloadWith(command));

        var output = Assert.Single(command.Outputs);
        Assert.Equal("Sample.Result", output.CanonicalTypeName);
        Assert.Equal("The authored result.", output.Description);
    }

    [Fact]
    public void Normalize_PreservesAlreadyNormalizedOutputsAndIsIdempotent()
    {
        var existingOutput = Type("Sample.Result", "Sample.Result", "Existing output.");
        var command = new DocumentationCommandHelp
        {
            Name = "Get-Sample",
            CommandType = "Cmdlet",
            Outputs = [existingOutput]
        };
        var payload = PayloadWith(command);

        DocumentationMetadataNormalizer.Normalize(payload);
        DocumentationMetadataNormalizer.Normalize(payload);

        var output = Assert.Single(command.Outputs);
        Assert.Same(existingOutput, output);
        Assert.Equal("Existing output.", output.Description);
    }

    private static DocumentationExtractionPayload PayloadWith(DocumentationCommandHelp command)
        => new()
        {
            ModuleName = "TestModule",
            Commands = [command]
        };

    private static DocumentationTypeHelp Type(string name, string canonicalTypeName, string description = "")
        => new()
        {
            Name = name,
            ClrTypeName = canonicalTypeName,
            CanonicalTypeName = canonicalTypeName,
            Description = description
        };
}
