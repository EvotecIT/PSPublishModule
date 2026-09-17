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
    public void Analyze_DoesNotInheritPrivateFunctionIntoChildFunctionScope()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function private:Invoke-PowerForgePrivateLocal { 'local' }
                function Invoke-PowerForgePrivateCaller { Invoke-PowerForgePrivateLocal }
                Invoke-PowerForgePrivateCaller
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePrivateLocal", StringComparison.OrdinalIgnoreCase));
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
    [InlineData("Invoke-Command -NoNewScope:$true")]
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
    public void Analyze_DoesNotPromoteInvokeCommandDeclarationForDynamicNoNewScope()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                $sameScope = $false
                Invoke-Command -NoNewScope:$sameScope { function Invoke-PowerForgeDynamicChildLocal { 'local' } }
                Invoke-PowerForgeDynamicChildLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDynamicChildLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotPromoteDeclarationThroughShadowedInvokeCommand()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-Command { param([switch] $NoNewScope, [scriptblock] $ScriptBlock) }
                Invoke-Command -NoNewScope { function Invoke-PowerForgeShadowedChildLocal { 'local' } }
                Invoke-PowerForgeShadowedChildLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeShadowedChildLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotPromoteDeclarationThroughAliasedInvokeCommand()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Save-PowerForgeScriptBlock { param([switch] $NoNewScope, [scriptblock] $ScriptBlock) }
                Set-Alias Invoke-Command Save-PowerForgeScriptBlock
                Invoke-Command -NoNewScope { function Invoke-PowerForgeAliasedChildLocal { 'local' } }
                Invoke-PowerForgeAliasedChildLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeAliasedChildLocal", StringComparison.OrdinalIgnoreCase));
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
    public void Analyze_DoesNotPromoteDeclarationAfterEarlyExitInDotSourcedBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                . {
                    return
                    function Invoke-PowerForgeUnreachedDotLocal { 'local' }
                }
                Invoke-PowerForgeUnreachedDotLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeUnreachedDotLocal", StringComparison.OrdinalIgnoreCase));
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
    public void Analyze_ResolvesDeferredAliasAtCallerExecutionOffset()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeAliasEntry { go }
                function Invoke-PowerForgeAliasTarget { Invoke-PowerForgeDeferredAliasLate }
                Set-Alias go Invoke-PowerForgeAliasTarget
                Invoke-PowerForgeAliasEntry
                function Invoke-PowerForgeDeferredAliasLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDeferredAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_KeepsPrecedingAliasTargetWhenReplacementIsConditional()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeBadAliasTarget { Invoke-PowerForgeConditionalAliasLate }
                function Invoke-PowerForgeSafeAliasTarget { 'safe' }
                function Invoke-PowerForgeConditionalAliasCaller { sort }
                Set-Alias sort Invoke-PowerForgeBadAliasTarget
                if ($false) { Set-Alias sort Invoke-PowerForgeSafeAliasTarget }
                Invoke-PowerForgeConditionalAliasCaller
                function Invoke-PowerForgeConditionalAliasLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AccountsForEarlyBeginTransferToCleanThroughDeferredFunction()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                begin {
                    function Invoke-PowerForgeCleanCaller { Invoke-PowerForgeCleanDeferredLate }
                    throw 'stop'
                    function Invoke-PowerForgeCleanDeferredLate { 'late' }
                }
                clean {
                    Invoke-PowerForgeCleanCaller
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeCleanDeferredLate", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void Analyze_DoesNotTreatDiscardedFunctionLookupAsEscape()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeInspectedCallback { Invoke-PowerForgeInspectedLocal }
                function Invoke-PowerForgeInspectedLocal { 'local' }
                Get-Command Invoke-PowerForgeInspectedCallback | Out-Null
                Invoke-PowerForgeInspectedCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeInspectedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Remove-Item")]
    [InlineData("Clear-Item")]
    [InlineData("ri")]
    [InlineData("cli")]
    public void Analyze_InvalidatesFunctionRemovedThroughProvider(string removalCommand)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeRemovedLocal { 'local' }
                {{removalCommand}} function:Invoke-PowerForgeRemovedLocal
                Invoke-PowerForgeRemovedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Invoke-Expression")]
    [InlineData("iex")]
    public void Analyze_FollowsLiteralInvokeExpressionCall(string invocation)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeExpressionCaller { Invoke-PowerForgeExpressionLate }
                {{invocation}} 'Invoke-PowerForgeExpressionCaller'
                function Invoke-PowerForgeExpressionLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeExpressionLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesForEachObjectBeginDeclarationAfterEmptyInput()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                @() | ForEach-Object -Begin {
                    function Invoke-PowerForgeBeginLocal { 'local' }
                } -Process {}
                Invoke-PowerForgeBeginLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeBeginLocal", StringComparison.OrdinalIgnoreCase));
    }
}
