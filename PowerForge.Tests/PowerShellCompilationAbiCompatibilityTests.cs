using System;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilerGate")]
public sealed class PowerShellCompilationAbiCompatibilityTests
{
    [Fact]
    public void Compare_AcceptsAdditiveMethods()
    {
        var baseline = Manifest(Method("Get_Value", "System.String"));
        var candidate = Manifest(Method("Get_Value", "System.String"), Method("Get_Count", "System.Int32"));

        var result = PowerShellCompilationAbiCompatibility.Compare(baseline, candidate);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.Issues);
        PowerShellCompilationAbiCompatibility.EnsureCompatible(baseline, candidate);
    }

    [Fact]
    public void Compare_RejectsRemovedMethod()
    {
        var result = PowerShellCompilationAbiCompatibility.Compare(
            Manifest(Method("Get_Value", "System.String")),
            Manifest(Method("Get_Count", "System.Int32")));

        var issue = Assert.Single(result.Issues);
        Assert.Equal(PowerShellCompilationAbiCompatibilityIssueKind.MethodRemoved, issue.Kind);
        Assert.Throws<InvalidOperationException>(() => PowerShellCompilationAbiCompatibility.EnsureCompatible(
            Manifest(Method("Get_Value", "System.String")),
            Manifest(Method("Get_Count", "System.Int32"))));
    }

    [Theory]
    [InlineData("System.Int32", false, false)]
    [InlineData("System.String", true, false)]
    [InlineData("System.String", false, true)]
    public void Compare_RejectsChangedParameterTypeNullabilityOrDefault(
        string typeName,
        bool nullable,
        bool hasDefault)
    {
        var baselineMethod = Method("Get_Value", "System.String");
        baselineMethod.Parameters = new[] { Parameter("value", "System.String") };
        var candidateMethod = Method("Get_Value", "System.String");
        candidateMethod.Parameters = new[] { Parameter("value", typeName, nullable, hasDefault) };

        var issue = Assert.Single(PowerShellCompilationAbiCompatibility.Compare(
            Manifest(baselineMethod), Manifest(candidateMethod)).Issues);

        Assert.Equal(PowerShellCompilationAbiCompatibilityIssueKind.MethodChanged, issue.Kind);
    }

    [Fact]
    public void Compare_IgnoresPrivateModuleSourceIdentityButRejectsPublicStateChange()
    {
        var baseline = Manifest(Method("Get_Value", "System.String"));
        baseline.ModuleLifetime = Lifetime("old.psm1", "value", "System.String");
        var candidate = Manifest(Method("Get_Value", "System.String"));
        candidate.ModuleLifetime = Lifetime("moved.psm1", "value", "System.String");
        Assert.True(PowerShellCompilationAbiCompatibility.Compare(baseline, candidate).IsCompatible);

        candidate.ModuleLifetime = Lifetime("moved.psm1", "value", "System.Int32");
        var issue = Assert.Single(PowerShellCompilationAbiCompatibility.Compare(baseline, candidate).Issues);
        Assert.Equal(PowerShellCompilationAbiCompatibilityIssueKind.ModuleLifetimeChanged, issue.Kind);
    }

    [Fact]
    public void Compare_RejectsUnsupportedFutureSchema()
    {
        var candidate = Manifest(Method("Get_Value", "System.String"));
        candidate.SchemaVersion = 6;

        var issue = Assert.Single(PowerShellCompilationAbiCompatibility.Compare(
            Manifest(Method("Get_Value", "System.String")), candidate).Issues);

        Assert.Equal(PowerShellCompilationAbiCompatibilityIssueKind.InvalidManifest, issue.Kind);
        Assert.Equal("candidate", issue.Path);
    }

    [Fact]
    public void Compare_RejectsNullNestedCollections()
    {
        var candidate = Manifest(Method("Get_Value", "System.String"));
        candidate.Methods[0].Parameters = null!;

        var issue = Assert.Single(PowerShellCompilationAbiCompatibility.Compare(
            Manifest(Method("Get_Value", "System.String")), candidate).Issues);

        Assert.Equal(PowerShellCompilationAbiCompatibilityIssueKind.InvalidManifest, issue.Kind);
        Assert.Equal("candidate", issue.Path);
    }

    [Fact]
    public void Compare_RejectsNullElementsInsideNestedCollections()
    {
        var candidate = Manifest(Method("Get_Value", "System.String"));
        candidate.Methods[0].OutputValueStates = new string[] { null! };

        var issue = Assert.Single(PowerShellCompilationAbiCompatibility.Compare(
            Manifest(Method("Get_Value", "System.String")), candidate).Issues);

        Assert.Equal(PowerShellCompilationAbiCompatibilityIssueKind.InvalidManifest, issue.Kind);
        Assert.Equal("candidate", issue.Path);
    }

    private static PowerShellCompilationAbiManifest Manifest(params PowerShellCompilationAbiMethod[] methods)
        => new()
        {
            NamespaceName = "Generated.Library",
            TypeName = "Commands",
            Methods = methods
        };

    private static PowerShellCompilationAbiMethod Method(string name, string returnType)
        => new()
        {
            PowerShellName = name.Replace('_', '-'),
            ClrName = name,
            ReturnType = returnType,
            OutputCardinality = "Scalar",
            OutputValueStates = new[] { "Known" },
            OutputScalarization = "PreserveScalar"
        };

    private static PowerShellCompilationAbiParameter Parameter(
        string name,
        string typeName,
        bool nullable = false,
        bool hasDefault = false)
        => new()
        {
            PowerShellName = name,
            ClrName = name,
            TypeName = typeName,
            Nullable = nullable,
            HasDefaultValue = hasDefault,
            DefaultValue = hasDefault
                ? new PowerShellCompilationLiteral(PowerShellCompilationLiteralKind.String, "System.String", "default")
                : null
        };

    private static PowerShellRuntimeFreeModuleContract Lifetime(string source, string field, string type)
        => new()
        {
            SourcePath = source,
            DocumentId = Guid.NewGuid().ToString("N"),
            InitializerName = "Initialize_" + Guid.NewGuid().ToString("N"),
            SupportedParameterCounts = new[] { 0 },
            Fields = new[] { new PowerShellRuntimeFreeModuleField { Name = field, TypeName = type } }
        };
}
