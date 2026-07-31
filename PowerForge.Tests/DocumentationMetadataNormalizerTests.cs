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
        Assert.Equal(
            "[System.Management.Automation.SwitchParameter]::new($true)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "SwitchParameter",
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
        const string unsafeTypeName = "Example.A-B";
        const string unsafeAssemblyName = "Example.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        string ExactTypeExpression(string typeName)
            => "& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.FullName -eq '" +
               unsafeAssemblyName + "' }; $matches = [System.Collections.Generic.List[type]]::new(); foreach ($candidateAssembly in @($assembly)) { " +
               "$type = $candidateAssembly.GetType('" + typeName + "', $false, $false); if ($null -eq $type) { try { $type = $candidateAssembly.GetTypes() | " +
               "Where-Object { $_.FullName -ceq '" + typeName + "' } | Select-Object -First 1 } catch { $type = $null } }; " +
               "if ($null -ne $type) { $matches.Add($type) } }; if ($matches.Count -ne 1) { " +
               "throw 'Type identity is unavailable or ambiguous across loaded assemblies.' }; return $matches[0] }";
        Assert.Equal(
            ExactTypeExpression(unsafeTypeName),
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Type",
                CanonicalTypeName = "Example.A-B",
                Text = string.Join(",", unsafeTypeName.Select(character => (int)character)),
                AssemblyNameCodeUnits = string.Join(",", unsafeAssemblyName.Select(character => (int)character))
            }));
        const string whitespaceTypeName = " Example.Type ";
        Assert.Equal(
            ExactTypeExpression(whitespaceTypeName),
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Type",
                CanonicalTypeName = whitespaceTypeName,
                Text = string.Join(",", whitespaceTypeName.Select(character => (int)character)),
                AssemblyNameCodeUnits = string.Join(",", unsafeAssemblyName.Select(character => (int)character))
            }));
        Assert.Equal(
            "[System.Enum]::ToObject((" + ExactTypeExpression(unsafeTypeName) + "), ([System.Int32]1))",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Enum",
                CanonicalTypeName = "Example.A-B",
                Name = "X",
                Text = "1",
                UnderlyingTypeName = "System.Int32",
                RuntimeTypeNameCodeUnits = string.Join(",", unsafeTypeName.Select(character => (int)character)),
                AssemblyNameCodeUnits = string.Join(",", unsafeAssemblyName.Select(character => (int)character))
            }));
        Assert.Equal(
            "[System.Int32].MakePointerType()",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Type",
                CanonicalTypeName = "System.Int32*"
            }));
        Assert.Equal(
            "(" + ExactTypeExpression(unsafeTypeName) + ").MakePointerType()",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Type",
                CanonicalTypeName = "Example.A-B*",
                Text = string.Join(",", unsafeTypeName.Select(character => (int)character)),
                AssemblyNameCodeUnits = string.Join(",", unsafeAssemblyName.Select(character => (int)character))
            }));
        Assert.Equal(
            "[System.Int32].MakeByRefType()",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Type",
                CanonicalTypeName = "System.Int32&"
            }));
        Assert.Equal(
            "[System.Int32].MakeArrayType(1)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Type",
                CanonicalTypeName = "System.Int32[*]"
            }));
        Assert.Equal(
            "[System.Enum]::ToObject([Example.CaseMode], ([System.Int32]1))",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Enum",
                CanonicalTypeName = "Example.CaseMode",
                UnderlyingTypeName = "System.Int32",
                Text = "1"
            }));
        Assert.Equal("([double]-0.0)", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Double",
            Text = "-0"
        }));
        Assert.Equal("([double]1)", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Double",
            Text = "1"
        }));
        Assert.Equal("([single]-0.0)", PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Kind = "Single",
            Text = "-0"
        }));
        Assert.Equal(
            "[System.BitConverter]::Int64BitsToDouble(([long]9221120237041095220))",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "DoubleBits",
                Text = "9221120237041095220"
            }));
        Assert.Equal(
            "[System.BitConverter]::ToSingle([System.BitConverter]::GetBytes(([int]2143294004)), 0)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "SingleBits",
                Text = "2143294004"
            }));
        Assert.Equal(
            "[System.Decimal]::new(([int]0), ([int]0), ([int]0), $true, ([byte]4))",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "DecimalBits",
                Text = "0,0,0,-2147221504"
            }));
        Assert.Equal(
            "[System.Decimal]::Parse('79228162514264337593543950335', [System.Globalization.CultureInfo]::InvariantCulture)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "Decimal",
                Text = "79228162514264337593543950335"
            }));
        Assert.Equal(
            "[System.Numerics.BigInteger]::Parse('1234567890123456789012345678901234567890', [System.Globalization.CultureInfo]::InvariantCulture)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "BigInteger",
                Text = "1234567890123456789012345678901234567890"
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
            "[System.Uri]::new('https://example.com/a''b?x=1', [System.UriKind]::Absolute)",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "UriCodeUnits",
                Name = "Absolute",
                Text = string.Join(",", "https://example.com/a'b?x=1".Select(character => (int)character))
            }));
        Assert.Equal(
            "[System.DateOnly]::FromDayNumber(([int]739827))",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "DateOnly",
                Text = "739827"
            }));
        Assert.Equal(
            "[System.TimeOnly]::new(([long]452961234567))",
            PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Kind = "TimeOnly",
                Text = "452961234567"
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
    public void DefaultValueFormatter_PreservesValidAuthoredHelpText()
    {
        Assert.Equal("first\nsecond 😀", PowerShellDefaultValueFormatter.FormatDisplayText("first\nsecond 😀"));
        Assert.Equal("([char]55296)", PowerShellDefaultValueFormatter.FormatDisplayText(new string('\uD800', 1)));
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
            tokens.Add(new DocumentationRuntimeValue
            {
                Kind = "CollectionStart",
                CanonicalTypeName = "System.Object[]",
                Name = "Array"
            });
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
    public void DefaultValueFormatter_DecodesContainerTokens()
    {
        var formatted = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Tokens =
            [
                new DocumentationRuntimeValue
                {
                    Kind = "DictionaryStart",
                    CanonicalTypeName = "System.Collections.Generic.Dictionary[System.String,System.Object]",
                    Name = "Ordinal"
                },
                new DocumentationRuntimeValue { Kind = "DictionaryEntryStart" },
                new DocumentationRuntimeValue { Kind = "StringCodeUnits", Text = "65" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "1", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "DictionaryEntryEnd" },
                new DocumentationRuntimeValue { Kind = "DictionaryEntryStart" },
                new DocumentationRuntimeValue { Kind = "StringCodeUnits", Text = "97" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "2", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "DictionaryEntryEnd" },
                new DocumentationRuntimeValue { Kind = "DictionaryEntryStart" },
                new DocumentationRuntimeValue { Kind = "StringCodeUnits", Text = "101,110,100,112,111,105,110,116" },
                new DocumentationRuntimeValue
                {
                    Kind = "UriCodeUnits",
                    Name = "Relative",
                    Text = "114,101,108,97,116,105,118,101,47,112,97,116,104"
                },
                new DocumentationRuntimeValue { Kind = "DictionaryEntryEnd" },
                new DocumentationRuntimeValue { Kind = "DictionaryEnd" }
            ]
        });

        Assert.Equal(
            "& { $dictionary = [System.Collections.Generic.Dictionary[System.String,System.Object]]::new([System.StringComparer]::Ordinal); ([System.Collections.IDictionary]$dictionary).Add(('A'), (1)); ([System.Collections.IDictionary]$dictionary).Add(('a'), (2)); ([System.Collections.IDictionary]$dictionary).Add(('endpoint'), ([System.Uri]::new('relative/path', [System.UriKind]::Relative))); return ,$dictionary }",
            formatted);

        var array = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Tokens =
            [
                new DocumentationRuntimeValue
                {
                    Kind = "ArrayStart",
                    CanonicalTypeName = "System.Int32",
                    Text = "2,2",
                    Name = "0,0"
                },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "1", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "2", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "3", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "4", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "ArrayEnd" }
            ]
        });
        Assert.Equal(
            "& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2, 2), [int[]]@(0, 0)); $array.SetValue((1), [int[]]@(0, 0)); $array.SetValue((2), [int[]]@(0, 1)); $array.SetValue((3), [int[]]@(1, 0)); $array.SetValue((4), [int[]]@(1, 1)); return ,$array }",
            array);

        var boundedArray = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Tokens =
            [
                new DocumentationRuntimeValue
                {
                    Kind = "ArrayStart",
                    CanonicalTypeName = "System.Int32",
                    Text = "2",
                    Name = "2147483646"
                },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "7", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "8", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "ArrayEnd" }
            ]
        });
        Assert.Equal(
            "& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(2), [int[]]@(2147483646)); $array.SetValue((7), [int[]]@(2147483646)); $array.SetValue((8), [int[]]@(2147483647)); return ,$array }",
            boundedArray);

        var nestedCollection = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Tokens =
            [
                new DocumentationRuntimeValue { Kind = "CollectionStart", CanonicalTypeName = "System.Object[]", Name = "Array" },
                new DocumentationRuntimeValue { Kind = "CollectionStart", CanonicalTypeName = "System.Int32[]", Name = "Array" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "1", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "2", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "CollectionEnd" },
                new DocumentationRuntimeValue { Kind = "CollectionEnd" }
            ]
        });
        Assert.Equal(
            "& { $collection = [System.Object[]]::new(1); $collection.SetValue((& { $collection = [System.Int32[]]::new(2); $collection.SetValue((1), 0); $collection.SetValue((2), 1); return ,$collection }), 0); return ,$collection }",
            nestedCollection);

        var nestedArrayCollection = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Tokens =
            [
                new DocumentationRuntimeValue { Kind = "CollectionStart", CanonicalTypeName = "System.Object[]", Name = "Array" },
                new DocumentationRuntimeValue
                {
                    Kind = "ArrayStart",
                    CanonicalTypeName = "System.Int32",
                    Text = "1,1",
                    Name = "0,0"
                },
                new DocumentationRuntimeValue { Kind = "Formattable", Text = "9", CanonicalTypeName = "System.Int32" },
                new DocumentationRuntimeValue { Kind = "ArrayEnd" },
                new DocumentationRuntimeValue { Kind = "CollectionEnd" }
            ]
        });
        Assert.Equal(
            "& { $collection = [System.Object[]]::new(1); $collection.SetValue((& { $array = [System.Array]::CreateInstance([System.Int32], [int[]]@(1, 1), [int[]]@(0, 0)); $array.SetValue((9), [int[]]@(0, 0)); return ,$array }), 0); return ,$collection }",
            nestedArrayCollection);

        var unsafeArray = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
        {
            Tokens =
            [
                new DocumentationRuntimeValue
                {
                    Kind = "CollectionStart",
                    CanonicalTypeName = "Example.A-B[]",
                    ElementTypeName = "Example.A-B",
                    RuntimeTypeNameCodeUnits = string.Join(",", "Example.A-B".Select(character => (int)character)),
                    AssemblyNameCodeUnits = string.Join(",", "Example.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null".Select(character => (int)character)),
                    Name = "Array"
                },
                new DocumentationRuntimeValue { Kind = "Null" },
                new DocumentationRuntimeValue { Kind = "CollectionEnd" }
            ]
        });
        Assert.StartsWith(
            "& { $collection = [System.Array]::CreateInstance((& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies()",
            unsafeArray,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultValueFormatter_FormatsArrayCoordinatesInvariantly()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        var customCulture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        customCulture.NumberFormat.NegativeSign = "\u2212";
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = customCulture;
            var formatted = PowerShellDefaultValueFormatter.Format(new DocumentationRuntimeValue
            {
                Tokens =
                [
                    new DocumentationRuntimeValue
                    {
                        Kind = "ArrayStart",
                        CanonicalTypeName = "System.Int32",
                        Text = "1",
                        Name = "-2"
                    },
                    new DocumentationRuntimeValue { Kind = "Formattable", Text = "7", CanonicalTypeName = "System.Int32" },
                    new DocumentationRuntimeValue { Kind = "ArrayEnd" }
                ]
            });

            Assert.Contains("[int[]]@(-2)", formatted, StringComparison.Ordinal);
            Assert.DoesNotContain('\u2212', formatted);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
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
    public void Normalize_PreservesResolvedClrOutputIdentitiesThatDifferByWhitespace()
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-WhitespaceVariants",
            CommandType = "Cmdlet",
            RuntimeOutputs =
            [
                Type("Foo Bar", "Demo.Foo Bar"),
                Type("FooBar", "Demo.FooBar")
            ],
            AuthoredOutputs = [Type("FooBar", "FooBar", "Compact output description.")]
        };

        DocumentationMetadataNormalizer.Normalize(PayloadWith(command));

        Assert.Collection(
            command.Outputs,
            output =>
            {
                Assert.Equal("Demo.Foo Bar", output.CanonicalTypeName);
                Assert.True(string.IsNullOrEmpty(output.Description));
            },
            output =>
            {
                Assert.Equal("Demo.FooBar", output.CanonicalTypeName);
                Assert.Equal("Compact output description.", output.Description);
            });
    }

    [Theory]
    [InlineData("Widget", "Demo.Widget", "widget")]
    [InlineData("Box[System.String]", "Demo.Box[System.String]", "box[System.String]")]
    public void Normalize_MatchesUniqueUnqualifiedOutputNamesCaseInsensitively(
        string runtimeName,
        string runtimeIdentity,
        string authoredName)
    {
        var command = new DocumentationCommandHelp
        {
            Name = "Get-Widget",
            CommandType = "Cmdlet",
            RuntimeOutputs = [Type(runtimeName, runtimeIdentity)],
            AuthoredOutputs = [Type(authoredName, authoredName, "Unique widget description.")]
        };

        DocumentationMetadataNormalizer.Normalize(PayloadWith(command));

        var output = Assert.Single(command.Outputs);
        Assert.Equal(runtimeIdentity, output.CanonicalTypeName);
        Assert.Equal("Unique widget description.", output.Description);
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
        if (depth <= 0) return value;
        var result = value;
        for (var index = 0; index < depth; index++)
            result = "& { $collection = [System.Object[]]::new(1); $collection.SetValue((" + result +
                     "), 0); return ,$collection }";
        return result;
    }
}
