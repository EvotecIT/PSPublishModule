using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerBoundaryTests
{
    [Fact]
    public void Analyze_ReportsNestedFunctionPassedToUnknownScriptBlockConsumer()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeCallbackLocal { 'local' }
                Save-Callback { Invoke-PowerForgeCallbackLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeCallbackLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionReachedThroughDynamicInvocationBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDynamicCaller {
                    Invoke-PowerForgeDynamicHelper
                }

                $name = 'Invoke-PowerForgeDynamicCaller'
                & $name
                function Invoke-PowerForgeDynamicHelper { 'local' }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDynamicHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsHelperReachedThroughConditionalDynamicCallerBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                if ($true) {
                    function Invoke-PowerForgeConditionalDynamicCaller {
                        Invoke-PowerForgeConditionalDynamicHelper
                    }
                }

                $name = 'Invoke-PowerForgeConditionalDynamicCaller'
                & $name
                function Invoke-PowerForgeConditionalDynamicHelper { 'local' }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalDynamicHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionFromSplatPotentiallyUsedForRemoting()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeSplattedRemoteLocal { 'local' }
                $remote = @{ ComputerName = 'server' }
                Invoke-Command @remote -ScriptBlock { Invoke-PowerForgeSplattedRemoteLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeSplattedRemoteLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionFromSplatPotentiallyUsedForParallelExecution()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeSplattedParallelLocal { 'local' }
                $parallel = @{ Parallel = { Invoke-PowerForgeSplattedParallelLocal } }
                1 | ForEach-Object @parallel
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeSplattedParallelLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsHelperReachedFromTrapBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeTrapCaller {
                    Invoke-PowerForgeTrapHelper
                }

                throw 'before helper declaration'
                function Invoke-PowerForgeTrapHelper { 'local' }
                trap { Invoke-PowerForgeTrapCaller; continue }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeTrapHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesHelperDeclaredBeforeAnyTrapTrigger()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeTrapCaller {
                    Invoke-PowerForgeTrapHelper
                }

                function Invoke-PowerForgeTrapHelper { 'local' }
                trap { Invoke-PowerForgeTrapCaller; continue }
                throw 'after helper declaration'
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeTrapHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsDotSourcedHelperReachedFromTrapBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDotTrapCaller {
                    Invoke-PowerForgeDotTrapHelper
                }

                throw 'before helper declaration'
                . { function Invoke-PowerForgeDotTrapHelper { 'local' } }
                trap { Invoke-PowerForgeDotTrapCaller; continue }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDotTrapHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesDotSourcedHelperWhenNoStatementCanTriggerTrapBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeSafeDotTrapCaller {
                    Invoke-PowerForgeSafeDotTrapHelper
                }

                . { function Invoke-PowerForgeSafeDotTrapHelper { 'local' } }
                trap { Invoke-PowerForgeSafeDotTrapCaller; continue }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeSafeDotTrapHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsDotSourcedHelperWhenInnerStatementCanTriggerTrapBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeInnerDotTrapCaller {
                    Invoke-PowerForgeInnerDotTrapHelper
                }

                . {
                    throw 'before helper declaration'
                    function Invoke-PowerForgeInnerDotTrapHelper { 'local' }
                }
                trap { Invoke-PowerForgeInnerDotTrapCaller; continue }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeInnerDotTrapHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("& Start-Job { Invoke-PowerForgeOperatorArgumentLocal }")]
    [InlineData("& Save-Callback { Invoke-PowerForgeOperatorArgumentLocal }")]
    public void Analyze_ReportsNestedFunctionFromCallOperatorCommandArgument(string invocation)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeOperatorArgumentLocal { 'local' }
                {{invocation}}
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeOperatorArgumentLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Where-Object")]
    [InlineData("ForEach-Object")]
    [InlineData("Invoke-Command")]
    public void Analyze_ReportsNestedFunctionFromShadowedSynchronousConsumer(string consumerName)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeShadowedConsumerLocal { 'local' }
                function {{consumerName}} {
                    param([scriptblock] $FilterScript)
                    $script:SavedFilter = $FilterScript
                }

                {{consumerName}} { Invoke-PowerForgeShadowedConsumerLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeShadowedConsumerLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionFromUntrustedModuleQualifiedConsumer()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeUntrustedConsumerLocal { 'local' }
                CustomModule\Where-Object { Invoke-PowerForgeUntrustedConsumerLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeUntrustedConsumerLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesNestedFunctionFromTrustedModuleQualifiedConsumer()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeTrustedConsumerLocal { 'local' }
                Microsoft.PowerShell.Core\Where-Object { Invoke-PowerForgeTrustedConsumerLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeTrustedConsumerLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("group")]
    [InlineData("measure")]
    [InlineData("select")]
    public void Analyze_RecognizesNestedFunctionFromStandardSynchronousAlias(string alias)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeAliasLocal { 'local' }
                1 | {{alias}} { Invoke-PowerForgeAliasLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeAliasLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesBuiltinConsumerBeforeSameNamedLocalDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeEarlyBuiltinLocal { 'local' }
                1 | Where-Object { Invoke-PowerForgeEarlyBuiltinLocal }
                function Where-Object { param([scriptblock] $FilterScript) }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeEarlyBuiltinLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionFromConditionallyShadowedConsumer()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeConditionalShadowLocal { 'local' }
                if ($UseCustomConsumer) {
                    function Where-Object { param([scriptblock] $FilterScript) }
                }

                1 | Where-Object { Invoke-PowerForgeConditionalShadowLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalShadowLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionCalledFromReturnedFunctionBody()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function New-PowerForgeCallback {
                function Invoke-PowerForgeSavedCallback {
                    Invoke-PowerForgeReturnedLocal
                }

                function Invoke-PowerForgeReturnedLocal { 'local' }
                ${function:Invoke-PowerForgeSavedCallback}
            }

            $callback = New-PowerForgeCallback
            & $callback
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeReturnedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesNestedFunctionCalledFromImmediatelyInvokedFunctionBody()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeImmediateCallback {
                    Invoke-PowerForgeImmediateLocal
                }

                function Invoke-PowerForgeImmediateLocal { 'local' }
                & ${function:Invoke-PowerForgeImmediateCallback}
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeImmediateLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsNestedFunctionCalledFromLookedUpFunctionBody()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function New-PowerForgeLookedUpCallback {
                function Invoke-PowerForgeLookedUpCallback {
                    Invoke-PowerForgeLookedUpLocal
                }

                function Invoke-PowerForgeLookedUpLocal { 'local' }
                (Get-Command Invoke-PowerForgeLookedUpCallback).ScriptBlock
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLookedUpLocal", StringComparison.OrdinalIgnoreCase));
    }

}
