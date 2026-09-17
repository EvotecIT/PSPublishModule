using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerTests
{
    [Fact]
    public void Analyze_RecognizesNestedFunctionAndFilterDeclarations()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Convert-Value {
                function Get-LocalValue { 'local' }
                filter IsNumeric { $_ -is [int] }

                Get-LocalValue
                1 | IsNumeric
            }

            Convert-Value
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Get-LocalValue", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Name, "IsNumeric", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionCalledFromModuleScope()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeLocalOnly { 'local' }
            }

            Invoke-PowerForgeLocalOnly
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLocalOnly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionCalledFromSiblingScope()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function First {
                function Invoke-PowerForgeSiblingLocal { 'local' }
                Invoke-PowerForgeSiblingLocal
            }

            function Second {
                Invoke-PowerForgeSiblingLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeSiblingLocal", StringComparison.OrdinalIgnoreCase));
    }
}
