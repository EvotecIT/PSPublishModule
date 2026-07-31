using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed class DocumentationBinaryFixtureTests
{
    [Fact]
    public void DocumentationEngine_GeneratesExpectedOutputs_ForBinaryFixture()
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PowerForge.Tests", "Fixtures", "BinaryDocFixture"));
        var outputDirectory = BuildFixtureProject(fixtureRoot);
        var tempRoot = Path.Combine(Path.GetTempPath(), "pf-binary-doc-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var moduleName = "BinaryDocFixture";
            var assemblyPath = Path.Combine(outputDirectory, moduleName + ".dll");
            var xmlPath = Path.Combine(outputDirectory, moduleName + ".xml");
            var manifestPath = Path.Combine(tempRoot, moduleName + ".psd1");
            var expectedRoot = Path.Combine(fixtureRoot, "Expected");

            Assert.True(File.Exists(assemblyPath), $"Expected built fixture assembly at '{assemblyPath}'.");
            Assert.True(File.Exists(xmlPath), $"Expected built fixture XML docs at '{xmlPath}'.");

            File.Copy(assemblyPath, Path.Combine(tempRoot, Path.GetFileName(assemblyPath)), overwrite: true);
            File.Copy(xmlPath, Path.Combine(tempRoot, Path.GetFileName(xmlPath)), overwrite: true);

            File.WriteAllText(manifestPath, """
@{
    RootModule = 'BinaryDocFixture.dll'
    ModuleVersion = '1.0.0'
    GUID = '66666666-6666-6666-6666-666666666666'
    Author = 'PowerForge.Tests'
    Description = 'Binary fixture module for documentation generation tests.'
    FunctionsToExport = @()
    CmdletsToExport = @('Get-BinaryDocSample')
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var staleHelpDirectory = Path.Combine(tempRoot, "en-US");
            Directory.CreateDirectory(staleHelpDirectory);
            var staleExternalHelp = File.ReadAllText(Path.Combine(expectedRoot, "BinaryDocFixture-help.xml"))
                .Replace("<dev:defaultValue>[BinaryDocFixture.BinaryDocMode]::Basic</dev:defaultValue>", "<dev:defaultValue>None</dev:defaultValue>", StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(staleHelpDirectory, "BinaryDocFixture-help.xml"),
                staleExternalHelp,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var extracted = engine.ExtractHelpPayload(tempRoot, manifestPath, TimeSpan.FromMinutes(1));
            var modeParameter = Assert.Single(
                Assert.Single(extracted.Commands).Parameters,
                parameter => string.Equals(parameter.Name, "Mode", StringComparison.Ordinal));
            Assert.Equal("[BinaryDocFixture.BinaryDocMode]::Basic", modeParameter.DefaultValue);

            var result = engine.Build(
                moduleName: moduleName,
                stagingPath: tempRoot,
                moduleManifestPath: manifestPath,
                documentation: new DocumentationConfiguration
                {
                    Path = "Docs",
                    PathReadme = Path.Combine("Docs", "Readme.md")
                },
                buildDocumentation: new BuildDocumentationConfiguration
                {
                    Enable = true,
                    GenerateExternalHelp = true,
                    IncludeAboutTopics = false,
                    GenerateFallbackExamples = true
                });

            Assert.True(result.Succeeded, result.ErrorMessage);

            var markdownPath = Path.Combine(tempRoot, "Docs", "Get-BinaryDocSample.md");
            var externalHelpPath = Path.Combine(tempRoot, "en-US", "BinaryDocFixture-help.xml");
            Assert.True(File.Exists(markdownPath), $"Expected generated markdown help at '{markdownPath}'.");
            Assert.True(File.Exists(externalHelpPath), $"Expected generated MAML help at '{externalHelpPath}'.");

            Assert.Equal(
                NormalizeText(File.ReadAllText(Path.Combine(expectedRoot, "Get-BinaryDocSample.md"))),
                NormalizeText(File.ReadAllText(markdownPath)));
            Assert.Equal(
                NormalizeText(File.ReadAllText(Path.Combine(expectedRoot, "BinaryDocFixture-help.xml"))),
                NormalizeText(File.ReadAllText(externalHelpPath)));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }

            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }

    [Fact]
    public void DocumentationEngine_PreservesExplicitlyEmptyMetadataDefaults()
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PowerForge.Tests", "Fixtures", "BinaryDocFixture"));
        var outputDirectory = BuildFixtureProject(fixtureRoot);
        var tempRoot = Path.Combine(Path.GetTempPath(), "pf-binary-doc-empty-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            const string moduleName = "BinaryDocFixture";
            var manifestPath = Path.Combine(tempRoot, moduleName + ".psd1");
            File.Copy(Path.Combine(outputDirectory, moduleName + ".dll"), Path.Combine(tempRoot, moduleName + ".dll"));
            File.Copy(Path.Combine(outputDirectory, moduleName + ".xml"), Path.Combine(tempRoot, moduleName + ".xml"));
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'BinaryDocFixture.dll'
    ModuleVersion = '1.0.0'
    GUID = '77777777-7777-7777-7777-777777777777'
    Author = 'PowerForge.Tests'
    Description = 'Binary fixture module for empty-default extraction tests.'
    FunctionsToExport = @()
    CmdletsToExport = @('Get-BinaryDocEmptyDefault', 'Get-BinaryDocAuthoredOutput', 'Get-BinaryDocConflictingOutput', 'Get-BinaryDocAmbiguousOutputs', 'Get-BinaryDocCaseInsensitiveOutput', 'Get-BinaryDocNestedOutput')
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var staleHelpDirectory = Path.Combine(tempRoot, "en-US");
            Directory.CreateDirectory(staleHelpDirectory);
            File.WriteAllText(Path.Combine(staleHelpDirectory, moduleName + "-help.xml"), """
<?xml version="1.0" encoding="utf-8"?>
<helpItems schema="maml" xmlns="http://msh">
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-BinaryDocEmptyDefault</command:name>
      <command:verb>Get</command:verb>
      <command:noun>BinaryDocEmptyDefault</command:noun>
      <maml:description>
        <maml:para>Returns parameters with explicitly empty defaults.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Returns parameters with explicitly empty defaults.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-BinaryDocEmptyDefault</maml:name>
        <command:parameter required="false" variableLength="false" globbing="false" pipelineInput="False" position="named" aliases="None">
          <maml:name>OptionalValue</maml:name>
          <command:parameterValue required="false" variableLength="false">String</command:parameterValue>
          <dev:type>
            <maml:name>String</maml:name>
          </dev:type>
          <dev:defaultValue>Stale value</dev:defaultValue>
        </command:parameter>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters>
      <command:parameter required="false" variableLength="false" globbing="false" pipelineInput="False" position="named" aliases="None">
        <maml:name>OptionalValue</maml:name>
        <command:parameterValue required="false" variableLength="false">String</command:parameterValue>
        <dev:type>
          <maml:name>String</maml:name>
        </dev:type>
        <dev:defaultValue>Stale value</dev:defaultValue>
      </command:parameter>
    </command:parameters>
    <command:inputTypes />
    <command:returnValues />
  </command:command>
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-BinaryDocAuthoredOutput</command:name>
      <command:verb>Get</command:verb>
      <command:noun>BinaryDocAuthoredOutput</command:noun>
      <maml:description>
        <maml:para>Uses authored external help as its output contract.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Uses authored external help as its output contract.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-BinaryDocAuthoredOutput</maml:name>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters />
    <command:inputTypes />
    <command:returnValues>
      <command:returnValue>
        <dev:type>
          <maml:name>BinaryDocFixture.BinaryDocOutput</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>An authored binary output description.</maml:para>
        </maml:description>
      </command:returnValue>
    </command:returnValues>
  </command:command>
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-BinaryDocConflictingOutput</command:name>
      <command:verb>Get</command:verb>
      <command:noun>BinaryDocConflictingOutput</command:noun>
      <maml:description>
        <maml:para>Returns one qualified output while stale help names another.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Returns one qualified output while stale help names another.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-BinaryDocConflictingOutput</maml:name>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters />
    <command:inputTypes />
    <command:returnValues>
      <command:returnValue>
        <dev:type>
          <maml:name>BinaryDocFixture.OutputB.Result</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>This stale OutputB description must not leak onto OutputA.</maml:para>
        </maml:description>
      </command:returnValue>
    </command:returnValues>
  </command:command>
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-BinaryDocAmbiguousOutputs</command:name>
      <command:verb>Get</command:verb>
      <command:noun>BinaryDocAmbiguousOutputs</command:noun>
      <maml:description>
        <maml:para>Returns two output types with the same short name.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Returns two output types with the same short name.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-BinaryDocAmbiguousOutputs</maml:name>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters />
    <command:inputTypes />
    <command:returnValues>
      <command:returnValue>
        <dev:type>
          <maml:name>BinaryDocFixture.OutputB.Result</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>Only the OutputB result has an authored description.</maml:para>
        </maml:description>
      </command:returnValue>
      <command:returnValue>
        <dev:type>
          <maml:name>BinaryDocFixture.OutputA.RESULT</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>The case-variant RESULT has its own authored description.</maml:para>
        </maml:description>
      </command:returnValue>
    </command:returnValues>
  </command:command>
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-BinaryDocCaseInsensitiveOutput</command:name>
      <command:verb>Get</command:verb>
      <command:noun>BinaryDocCaseInsensitiveOutput</command:noun>
      <maml:description>
        <maml:para>Matches an authored unique short output whose casing differs.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Matches an authored unique short output whose casing differs.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-BinaryDocCaseInsensitiveOutput</maml:name>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters />
    <command:inputTypes />
    <command:returnValues>
      <command:returnValue>
        <dev:type>
          <maml:name>result</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>The canonical qualified result description survives authored casing.</maml:para>
        </maml:description>
      </command:returnValue>
    </command:returnValues>
  </command:command>
  <command:command xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
    <command:details>
      <command:name>Get-BinaryDocNestedOutput</command:name>
      <command:verb>Get</command:verb>
      <command:noun>BinaryDocNestedOutput</command:noun>
      <maml:description>
        <maml:para>Matches a nested output authored with normal C# type spelling.</maml:para>
      </maml:description>
    </command:details>
    <maml:description>
      <maml:para>Matches a nested output authored with normal C# type spelling.</maml:para>
    </maml:description>
    <command:syntax>
      <command:syntaxItem>
        <maml:name>Get-BinaryDocNestedOutput</maml:name>
      </command:syntaxItem>
    </command:syntax>
    <command:parameters />
    <command:inputTypes />
    <command:returnValues>
      <command:returnValue>
        <dev:type>
          <maml:name>BinaryDocFixture.NestedOutputs.Outer.Result</maml:name>
        </dev:type>
        <maml:description>
          <maml:para>The nested result keeps its authored description.</maml:para>
        </maml:description>
      </command:returnValue>
    </command:returnValues>
  </command:command>
</helpItems>
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var payload = engine.ExtractHelpPayload(tempRoot, manifestPath, TimeSpan.FromMinutes(1));
            var command = Assert.Single(
                payload.Commands,
                item => string.Equals(item.Name, "Get-BinaryDocEmptyDefault", StringComparison.Ordinal));
            Assert.Empty(command.Outputs);

            Assert.Equal(
                "Empty string",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Label", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "''",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Separator", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "five seconds",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "DelaySeconds", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "', '",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Delimiter", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "& { $collection = [System.String[]]::new(2); $collection.SetValue(('a'), 0); $collection.SetValue(('b c'), 1); return ,$collection }",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Names", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "& { $collection = [System.Boolean[]]::new(2); $collection.SetValue(($true), 0); $collection.SetValue(($false), 1); return ,$collection }",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Switches", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "& { $collection = [BinaryDocFixture.BinaryDocMode[]]::new(2); $collection.SetValue(([BinaryDocFixture.BinaryDocMode]::Basic), 0); $collection.SetValue(([BinaryDocFixture.BinaryDocMode]::Advanced), 1); return ,$collection }",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Modes", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "[System.Enum]::ToObject([BinaryDocFixture.BinaryDocMode], ([System.Int32]3))",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "UnnamedMode", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "[System.Enum]::ToObject([BinaryDocFixture.BinaryDocUnsignedMode], ([System.UInt64]18446744073709551614))",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "UnnamedUnsignedMode", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "(-join @(([char]0)))",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "ControlText", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "([char]0)",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "ControlCharacter", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "([char]120)",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Character", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "[System.String]",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "ValueType", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "[System.Collections.Generic.List`1]",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "OpenGenericType", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "[BinaryDocFixture.BinaryDocOuter`1+BinaryDocInner`1[System.Int32,System.String]]",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "NestedGenericType", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "& { $collection = [System.Type[]]::new(2); $collection.SetValue(([System.String]), 0); $collection.SetValue(([System.Int32]), 1); return ,$collection }",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "ValueTypes", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "([char]0)",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "ControlHelp", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "first\nsecond 😀",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "MultilineHelp", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "(-join @('first', ([char]10), '```', ([char]10), 'last'))",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "FenceText", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "[double]::NaN",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "NotANumber", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "& { $collection = [System.Single[]]::new(2); $collection.SetValue(([single]::PositiveInfinity), 0); $collection.SetValue(([single]::NegativeInfinity), 1); return ,$collection }",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "Infinities", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "([double]0.84551240822557006)",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "PreciseDouble", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "([single]1.23456776)",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "PreciseSingle", StringComparison.Ordinal)).DefaultValue);
            Assert.Equal(
                "$null",
                Assert.Single(command.Parameters, parameter => string.Equals(parameter.Name, "OptionalValue", StringComparison.Ordinal)).DefaultValue);

            var authoredOutputCommand = Assert.Single(
                payload.Commands,
                item => string.Equals(item.Name, "Get-BinaryDocAuthoredOutput", StringComparison.Ordinal));
            var authoredOutput = Assert.Single(authoredOutputCommand.Outputs);
            Assert.Equal("BinaryDocFixture.BinaryDocOutput", authoredOutput.ClrTypeName);
            Assert.Contains("authored binary output description", authoredOutput.Description, StringComparison.Ordinal);

            var conflictingOutputCommand = Assert.Single(
                payload.Commands,
                item => string.Equals(item.Name, "Get-BinaryDocConflictingOutput", StringComparison.Ordinal));
            var conflictingOutput = Assert.Single(conflictingOutputCommand.Outputs);
            Assert.Equal("BinaryDocFixture.OutputA.Result", conflictingOutput.ClrTypeName);
            Assert.True(string.IsNullOrEmpty(conflictingOutput.Description));

            var ambiguousOutputCommand = Assert.Single(
                payload.Commands,
                item => string.Equals(item.Name, "Get-BinaryDocAmbiguousOutputs", StringComparison.Ordinal));
            Assert.Equal(3, ambiguousOutputCommand.Outputs.Count);
            var outputA = Assert.Single(
                ambiguousOutputCommand.Outputs,
                output => string.Equals(output.ClrTypeName, "BinaryDocFixture.OutputA.Result", StringComparison.Ordinal));
            var outputB = Assert.Single(
                ambiguousOutputCommand.Outputs,
                output => string.Equals(output.ClrTypeName, "BinaryDocFixture.OutputB.Result", StringComparison.Ordinal));
            var outputCaseVariant = Assert.Single(
                ambiguousOutputCommand.Outputs,
                output => string.Equals(output.ClrTypeName, "BinaryDocFixture.OutputA.RESULT", StringComparison.Ordinal));
            Assert.True(string.IsNullOrEmpty(outputA.Description));
            Assert.Contains("case-variant RESULT", outputCaseVariant.Description, StringComparison.Ordinal);
            Assert.Contains("Only the OutputB result", outputB.Description, StringComparison.Ordinal);

            var caseInsensitiveOutputCommand = Assert.Single(
                payload.Commands,
                item => string.Equals(item.Name, "Get-BinaryDocCaseInsensitiveOutput", StringComparison.Ordinal));
            var caseInsensitiveOutput = Assert.Single(caseInsensitiveOutputCommand.Outputs);
            Assert.Equal("BinaryDocFixture.CanonicalOutput.Result", caseInsensitiveOutput.ClrTypeName);
            Assert.Contains("survives authored casing", caseInsensitiveOutput.Description, StringComparison.Ordinal);

            var nestedOutputCommand = Assert.Single(
                payload.Commands,
                item => string.Equals(item.Name, "Get-BinaryDocNestedOutput", StringComparison.Ordinal));
            var nestedOutput = Assert.Single(nestedOutputCommand.Outputs);
            Assert.Equal("BinaryDocFixture.NestedOutputs.Outer+Result", nestedOutput.ClrTypeName);
            Assert.Contains("nested result keeps", nestedOutput.Description, StringComparison.OrdinalIgnoreCase);

            var markdownDirectory = Path.Combine(tempRoot, "GeneratedDocs");
            var mamlDirectory = Path.Combine(tempRoot, "GeneratedHelp");
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, moduleName, markdownDirectory);
            var generatedMamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, moduleName, mamlDirectory);
            var generatedMaml = File.ReadAllText(generatedMamlPath);
            Assert.DoesNotContain('\0', generatedMaml);
            Assert.Contains(
                "Default value: & { $collection = [System.String[]]::new(2); $collection.SetValue(('a'), 0); $collection.SetValue(('b c'), 1); return ,$collection }",
                File.ReadAllText(Path.Combine(markdownDirectory, "Get-BinaryDocEmptyDefault.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>&amp; { $collection = [System.String[]]::new(2); $collection.SetValue(('a'), 0); $collection.SetValue(('b c'), 1); return ,$collection }</dev:defaultValue>",
                File.ReadAllText(generatedMamlPath),
                StringComparison.Ordinal);
            Assert.Contains(
                "Default value: & { $collection = [System.Boolean[]]::new(2); $collection.SetValue(($true), 0); $collection.SetValue(($false), 1); return ,$collection }",
                File.ReadAllText(Path.Combine(markdownDirectory, "Get-BinaryDocEmptyDefault.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>&amp; { $collection = [BinaryDocFixture.BinaryDocMode[]]::new(2); $collection.SetValue(([BinaryDocFixture.BinaryDocMode]::Basic), 0); $collection.SetValue(([BinaryDocFixture.BinaryDocMode]::Advanced), 1); return ,$collection }</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>[System.Enum]::ToObject([BinaryDocFixture.BinaryDocMode], ([System.Int32]3))</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>[System.Enum]::ToObject([BinaryDocFixture.BinaryDocUnsignedMode], ([System.UInt64]18446744073709551614))</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>(-join @(([char]0)))</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>&amp; { $collection = [System.Type[]]::new(2); $collection.SetValue(([System.String]), 0); $collection.SetValue(([System.Int32]), 1); return ,$collection }</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>[System.Collections.Generic.List`1]</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>[BinaryDocFixture.BinaryDocOuter`1+BinaryDocInner`1[System.Int32,System.String]]</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>[double]::NaN</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>&amp; { $collection = [System.Single[]]::new(2); $collection.SetValue(([single]::PositiveInfinity), 0); $collection.SetValue(([single]::NegativeInfinity), 1); return ,$collection }</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "<dev:defaultValue>$null</dev:defaultValue>",
                generatedMaml,
                StringComparison.Ordinal);
            Assert.Contains(
                "Default value: (-join @('first', ([char]10), '```', ([char]10), 'last'))",
                File.ReadAllText(Path.Combine(markdownDirectory, "Get-BinaryDocEmptyDefault.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "An authored binary output description.",
                File.ReadAllText(Path.Combine(markdownDirectory, "Get-BinaryDocAuthoredOutput.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "Only the OutputB result has an authored description.",
                File.ReadAllText(Path.Combine(markdownDirectory, "Get-BinaryDocAmbiguousOutputs.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "BinaryDocFixture.OutputA.Result",
                File.ReadAllText(Path.Combine(markdownDirectory, "Get-BinaryDocAmbiguousOutputs.md")),
                StringComparison.Ordinal);
            Assert.Contains(
                "BinaryDocFixture.OutputB.Result",
                generatedMaml,
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }

            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }

    private static string BuildFixtureProject(string fixtureRoot)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "pf-binary-doc-fixture-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = fixtureRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add("BinaryDocFixture.csproj");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add("--verbosity");
        psi.ArgumentList.Add("minimal");
        psi.ArgumentList.Add("-p:OutputPath=" + outputDirectory + Path.DirectorySeparatorChar);

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdOut = process!.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Fixture build failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{stdErr}");

        return outputDirectory;
    }

    private static string NormalizeText(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Trim();
}
