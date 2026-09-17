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

    [Theory]
    [InlineData("Start-Job { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("Start-ThreadJob { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("Invoke-Command -ScriptBlock { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("1 | ForEach-Object -Parallel { Invoke-PowerForgeIsolatedLocal }")]
    public void Analyze_ReportsNestedFunctionCalledFromIsolatedInvocation(string invocation)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeIsolatedLocal { 'local' }
                {{invocation}}
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeIsolatedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesNestedFunctionInSameRunspaceScriptBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgePipelineLocal { 'local' }
                1 | ForEach-Object { Invoke-PowerForgePipelineLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePipelineLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsConditionalNestedFunctionDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                if ($Enable) {
                    function Invoke-PowerForgeOptionalLocal { 'local' }
                }

                Invoke-PowerForgeOptionalLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeOptionalLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionCalledBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                Invoke-PowerForgeLateLocal
                function Invoke-PowerForgeLateLocal { 'local' }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLateLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesRecursiveNestedFunctionCall()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeRecursiveLocal {
                    Invoke-PowerForgeRecursiveLocal
                }

                Invoke-PowerForgeRecursiveLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeRecursiveLocal", StringComparison.OrdinalIgnoreCase));
    }
}
