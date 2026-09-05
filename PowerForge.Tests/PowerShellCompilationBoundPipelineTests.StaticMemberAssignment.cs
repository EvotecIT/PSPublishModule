namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationBoundPipelineTests
{
    [Fact]
    public void StaticClrPropertyAssignmentFlowsThroughTheSharedMemberMutationIr()
    {
        var document = PowerShellSourceParser.Parse(
            "function Set-Culture { [System.Globalization.CultureInfo]::DefaultThreadCurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('en-US'); return [System.Globalization.CultureInfo]::DefaultThreadCurrentCulture.Name }",
            TestPath("static-member-mutation.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net10.0");

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var assignment = Assert.IsType<PowerShellBoundClrMemberAssignmentStatement>(
            Assert.Single(result.Analyzed.Functions).Body.Statements[0]);
        Assert.Null(assignment.Receiver);
        Assert.Equal(typeof(System.Globalization.CultureInfo), assignment.DeclaringType);
        Assert.Equal(nameof(System.Globalization.CultureInfo.DefaultThreadCurrentCulture), assignment.MemberName);
        var source = Assert.Single(result.Emitted.Methods).Source;
        Assert.Contains(
            "global::System.Globalization.CultureInfo.DefaultThreadCurrentCulture = global::System.Globalization.CultureInfo.GetCultureInfo(\"en-US\");",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[System.Environment]::ExitCode += 1")]
    [InlineData("[System.DateTime]::MinValue = [System.DateTime]::UtcNow")]
    public void StaticClrMemberAssignmentRejectsWiderMutationContracts(string statement)
    {
        var document = PowerShellSourceParser.Parse(
            $"function Set-StaticValue {{ {statement} }}",
            TestPath("static-member-mutation-rejected.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Set_StaticValue");
        Assert.NotEmpty(result.Emitted.Diagnostics);
    }

    [Fact]
    public void StaticClrPropertyAssignmentRejectsRuntimeExceptionObservableSetterFailure()
    {
        var document = PowerShellSourceParser.Parse(
            "function Set-StaticValue { try { [System.Environment]::ExitCode = 1 } catch [System.Management.Automation.RuntimeException] { return 0 } }",
            TestPath("static-member-mutation-runtime-exception.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.DoesNotContain(result.Emitted.Methods, static method => method.GeneratedName == "Set_StaticValue");
        Assert.Contains(result.Bound.Diagnostics, static diagnostic => diagnostic.Code == "PSB2617");
    }
}
