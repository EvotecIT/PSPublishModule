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
        Assert.Equal("[System.Enum]::ToObject([Example.UnsignedMode], ([System.UInt64]18446744073709551614))", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Enum",
            CanonicalTypeName = "Example.UnsignedMode",
            UnderlyingTypeName = "System.UInt64",
            Text = "18446744073709551614"
        }));
        Assert.Equal("([char]120)", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Char",
            Text = "x"
        }));
        Assert.Equal("[System.String]", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Type",
            CanonicalTypeName = "System.String"
        }));
        Assert.Equal("-0.0", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Double",
            Text = "-0"
        }));
        Assert.Equal("([single]-0.0)", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Single",
            Text = "-0"
        }));
        Assert.Equal(
            "[System.Decimal]::Parse('79228162514264337593543950335', [System.Globalization.CultureInfo]::InvariantCulture)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Decimal",
                Text = "79228162514264337593543950335"
            }));
        Assert.Equal(
            "[System.Guid]::ParseExact('01234567-89ab-cdef-0123-456789abcdef', 'D')",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Guid",
                Text = "01234567-89ab-cdef-0123-456789abcdef"
            }));
        Assert.Equal(
            "[System.Version]::Parse('1.2.3.4')",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Version",
                Text = "1.2.3.4"
            }));
        Assert.Equal(
            "[System.DateTime]::new(([long]639210116961234567), [System.DateTimeKind]::Local)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "DateTime",
                Text = "639210116961234567",
                Name = "Local"
            }));
        Assert.Equal(
            "[System.DateTimeOffset]::ParseExact('2026-07-30T12:34:56.1234567+05:30', 'O', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "DateTimeOffset",
                Text = "2026-07-30T12:34:56.1234567+05:30"
            }));
        Assert.Equal(
            "[System.TimeSpan]::ParseExact('1.02:03:04.5678900', 'c', [System.Globalization.CultureInfo]::InvariantCulture)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "TimeSpan",
                Text = "1.02:03:04.5678900"
            }));
        Assert.Equal(
            "[scriptblock]::Create((-join @('param($Value)', ([char]10), '$Value')))",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "ScriptBlockCodeUnits",
                Text = string.Join(",", "param($Value)\n$Value".Select(character => (int)character))
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
    public void DefaultValueFormatter_DecodesFlatNestedTokensAndUnpairedSurrogates()
    {
        var tokens = new List<DocumentationRuntimeValue>();
        for (var index = 0; index < 120; index++)
            tokens.Add(new DocumentationRuntimeValue { Kind = "CollectionStart" });
        tokens.Add(new DocumentationRuntimeValue { Kind = "StringCodeUnits", Text = "55296" });
        for (var index = 0; index < 120; index++)
            tokens.Add(new DocumentationRuntimeValue { Kind = "CollectionEnd" });

        var formatted = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Tokens = tokens
        });

        Assert.Equal(NestedExpression(120, "(-join @(([char]55296)))"), formatted);
        Assert.DoesNotContain('\uFFFD', formatted);
    }

    [Fact]
    public void DefaultValueFormatter_XmlEncodesInvalidFallbackText()
    {
        var formatted = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "TextCodeUnits",
            Text = "55296"
        });

        Assert.Equal("(-join @(([char]55296)))", formatted);
        Assert.DoesNotContain('\uD800', formatted);
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
        Assert.Null(parameter.MetadataDefaultHelpCodeUnits);
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
    public void Normalize_PreservesClrOutputIdentitiesThatDifferOnlyByCase()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-CaseVariants",
            CommandType = "Cmdlet",
            RuntimeOutputs =
            [
                Type("Result", "Example.Result"),
                Type("RESULT", "Example.RESULT")
            ],
            AuthoredOutputs =
            [
                Type("Result", "Example.Result", "Mixed-case result."),
                Type("RESULT", "Example.RESULT", "Upper-case result.")
            ]
        };

        DocumentationMetadataNormalizer.Normalize(PayloadWith(command));

        Assert.Collection(
            command.Outputs,
            output =>
            {
                Assert.Equal("Example.Result", output.CanonicalTypeName);
                Assert.Equal("Mixed-case result.", output.Description);
            },
            output =>
            {
                Assert.Equal("Example.RESULT", output.CanonicalTypeName);
                Assert.Equal("Upper-case result.", output.Description);
            });
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

    private static string NestedExpression(int depth, string value)
    {
        var result = value;
        for (var index = 0; index < depth; index++)
            result = "@(" + result + ")";
        return result;
    }
}
