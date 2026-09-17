using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerReviewClosureTests
{
    [Fact]
    public void Analyze_ReportsNestedFunctionFromScriptBlockBoundAsInputObject()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function New-PowerForgeCallback {
                function Invoke-PowerForgeInputCallback { Invoke-PowerForgeInputLocal }
                function Invoke-PowerForgeInputLocal { 'local' }
                Sort-Object -InputObject { Invoke-PowerForgeInputCallback }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeInputCallback", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Where-Object")]
    [InlineData("Sort-Object")]
    [InlineData("Group-Object")]
    [InlineData("Measure-Object")]
    [InlineData("Select-Object")]
    public void Analyze_ReportsNestedFunctionFromEveryKnownConsumerDataParameter(string consumer)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeConsumerDataLocal { 'local' }
                {{consumer}} -InputObject { Invoke-PowerForgeConsumerDataLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConsumerDataLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Set-Alias")]
    [InlineData("New-Alias")]
    [InlineData("sal")]
    [InlineData("nal")]
    public void Analyze_ReportsNestedFunctionFromAuthoredAliasConsumer(string aliasCommand)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeAliasedConsumerLocal { 'local' }
                function Save-PowerForgeCallback { param([scriptblock] $Callback) }
                {{aliasCommand}} Where-Object Save-PowerForgeCallback
                Where-Object { Invoke-PowerForgeAliasedConsumerLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeAliasedConsumerLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("script")]
    [InlineData("global")]
    [InlineData("private")]
    public void Analyze_RecognizesScopeQualifiedFunctionDeclaration(string qualifier)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function {{qualifier}}:Invoke-PowerForgeScopedLocal { 'local' }
                Invoke-PowerForgeScopedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeScopedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesDeclarationThatDominatesCallInsideGuardedBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                if ($Enable) {
                    function Invoke-PowerForgeGuardedLocal { 'local' }
                    Invoke-PowerForgeGuardedLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeGuardedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Invoke-Command -NoNewScope")]
    [InlineData("icm -NoNewScope")]
    [InlineData("Microsoft.PowerShell.Core\\Invoke-Command -NoNewScope")]
    public void Analyze_RecognizesDeclarationPromotedByInvokeCommandNoNewScope(string invocation)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                {{invocation}} {
                    function Invoke-PowerForgePromotedLocal { 'local' }
                }
                Invoke-PowerForgePromotedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePromotedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsBeginDeclarationThatDoesNotDominateCleanTransfer()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                begin {
                    throw 'stop'
                    function Invoke-PowerForgeLateCleanLocal { 'local' }
                }
                clean {
                    Invoke-PowerForgeLateCleanLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLateCleanLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotTreatInertFunctionProviderTextAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeInertCallback { Invoke-PowerForgeInertLocal }
                function Invoke-PowerForgeInertLocal { 'local' }
                Write-Output 'function:Invoke-PowerForgeInertCallback'
                Invoke-PowerForgeInertCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeInertLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsFunctionProviderLookupAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeProviderCallback { Invoke-PowerForgeProviderLocal }
                function Invoke-PowerForgeProviderLocal { 'local' }
                Get-Item 'function:Invoke-PowerForgeProviderCallback'
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeProviderLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotPromoteInvokeCommandDeclarationWhenNoNewScopeIsFalse()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                Invoke-Command -NoNewScope:$false { function Invoke-PowerForgeChildLocal { 'local' } }
                Invoke-PowerForgeChildLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeChildLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesDotSourcedDeclarationThatDominatesGuardedCall()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                if ($Enable) {
                    . { function Invoke-PowerForgeGuardedDotLocal { 'local' } }
                    Invoke-PowerForgeGuardedDotLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeGuardedDotLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesTryDeclarationThatDominatesFinallyCall()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                try {
                    function Invoke-PowerForgeFinallyLocal { 'local' }
                } finally {
                    Invoke-PowerForgeFinallyLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeFinallyLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotCarryTryDeclarationIntoIsolatedFinallyScriptBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                try {
                    function Invoke-PowerForgeFinallyIsolatedLocal { 'local' }
                } finally {
                    Start-Job { Invoke-PowerForgeFinallyIsolatedLocal }
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeFinallyIsolatedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_FollowsAuthoredAliasTargetInDeferredCallGraph()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeAliasedCaller { Invoke-PowerForgeAliasedLate }
                Set-Alias sort Invoke-PowerForgeAliasedCaller
                sort
                function Invoke-PowerForgeAliasedLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeAliasedLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_UsesLatestUnconditionalFunctionDefinitionAtCallSite()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeRedefinedCaller { Invoke-PowerForgeRedefinedLate }
                function Invoke-PowerForgeRedefinedCaller { 'safe' }
                Invoke-PowerForgeRedefinedCaller
                function Invoke-PowerForgeRedefinedLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeRedefinedLate", StringComparison.OrdinalIgnoreCase));
    }
}
