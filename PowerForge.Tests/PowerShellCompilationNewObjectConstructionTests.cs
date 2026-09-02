using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    private const string BoundedNewObjectFunctions = """
function Test-NewObjectConstruction {
    $version = Microsoft.PowerShell.Utility\New-Object -TypeName System.Version -ArgumentList (1, 2, 3, 4)
    $builder = New-Object System.Text.StringBuilder
    return ($version.ToString() -eq '1.2.3.4') -and ($builder.Length -eq 0)
}
""";

    [Fact]
    public void BoundedNewObjectUsesCanonicalClrConstructorIr()
    {
        var document = PowerShellSourceParser.Parse(BoundedNewObjectFunctions, "new-object-clr-construction.ps1");
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var function = Assert.Single(result.Analyzed.Functions);
        Assert.Equal(2, function.Body.Statements.OfType<PowerShellBoundAssignmentStatement>().Count());
        Assert.All(
            function.Body.Statements.OfType<PowerShellBoundAssignmentStatement>(),
            static statement => Assert.IsType<PowerShellBoundClrInvocationExpression>(statement.Value));
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("new global::System.Version(1, 2, 3, 4)", source, StringComparison.Ordinal);
        Assert.Contains("new global::System.Text.StringBuilder()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("New-Object", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("New-Object -TypeName System.Version -Args (1, 2, 3, 4)")]
    [InlineData("New-Object System.Version (1, 2, 3, 4)")]
    public void BoundedNewObjectAcceptsDocumentedAndPositionalArgumentListShapes(string construction)
    {
        var source = $"function Test-NewObjectShape {{ $value = {construction}; return $value.ToString() }}";
        var document = PowerShellSourceParser.Parse(source, "new-object-shape.ps1");
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var assignment = Assert.Single(Assert.Single(result.Analyzed.Functions).Body.Statements
            .OfType<PowerShellBoundAssignmentStatement>());
        Assert.IsType<PowerShellBoundClrInvocationExpression>(assignment.Value);
        var generatedSource = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains("new global::System.Version(1, 2, 3, 4)", generatedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("New-Object", generatedSource, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("net472")]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void BoundedNewObjectExecutesAcrossTargets(string targetFramework)
    {
        using var fixture = OracleFixture.Create(BoundedNewObjectFunctions);
        var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            Path.Combine(fixture.RootPath, targetFramework),
            "BoundedNewObject" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
            SingleFile = false
        });

        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        Assert.False(build.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(build.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods())
            .Single(static candidate => candidate.Name == "Test_NewObjectConstruction");
        Assert.True(
            method.GetParameters().Length == 0,
            "Unexpected generated parameters: " + string.Join(", ", method.GetParameters().Select(static parameter => parameter.ParameterType.FullName + " " + parameter.Name)));
        Assert.Equal(true, method.Invoke(null, null));
    }

    [Theory]
    [InlineData("param([string] $TypeName) $value = New-Object -TypeName $TypeName")]
    [InlineData("$value = New-Object -ComObject 'Scripting.FileSystemObject'")]
    [InlineData("$value = New-Object -TypeName PSObject -Property @{ Name = 'value' }")]
    [InlineData("$value = New-Object -TypeName System.Version -ArgumentList 1 2")]
    [InlineData("[char[]] $chars = 'a', 'b'; $value = New-Object System.String -ArgumentList $chars")]
    [InlineData("[char[]] $chars = 'a', 'b'; $value = New-Object System.String -ArgumentList @chars")]
    [InlineData("$value = New-Object System.String -ArgumentList @()")]
    [InlineData("$value = New-Object System.String -ArgumentList (,('a', 'b'))")]
    public void WiderNewObjectShapesRemainRejected(string body)
    {
        using var fixture = OracleFixture.Create($"function Test-NewObject {{ {body}; return $value }}");
        var unit = Assert.Single(Assert.Single(new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net10.0")).Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic => diagnostic.FeatureId == "command.new-object");
    }

    [Fact]
    public void UnrelatedCommandsCannotBorrowNewObjectTypeInference()
    {
        const string source = "function Test-UnrelatedCommand { $value = Get-RandomObject System.Version; return $value.Major }";
        var document = PowerShellSourceParser.Parse(source, "unrelated-command.ps1");
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Contains(result.Emitted.Diagnostics, static diagnostic =>
            diagnostic.Code == "command.get-randomobject");
        Assert.DoesNotContain(result.Emitted.Diagnostics, static diagnostic =>
            diagnostic.Code == "PSB2604" && diagnostic.Message.Contains("System.Version.Major", StringComparison.Ordinal));
        Assert.Empty(result.Emitted.Methods);
    }
}
