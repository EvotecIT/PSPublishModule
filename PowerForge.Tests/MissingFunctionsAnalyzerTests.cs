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
    [InlineData("Microsoft.PowerShell.Core\\Start-Job { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("sajb { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("Invoke-Command -ComputerName server -ScriptBlock { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("Invoke-Command server { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("icm server { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("Invoke-Command -Verbose server { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("icm -Session $session -ScriptBlock { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("1 | ForEach-Object -Parallel { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("1 | % -Parallel { Invoke-PowerForgeIsolatedLocal }")]
    [InlineData("Register-ObjectEvent -InputObject $timer -EventName Elapsed -Action { Invoke-PowerForgeIsolatedLocal }")]
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

    [Theory]
    [InlineData("1 | ForEach-Object { Invoke-PowerForgePipelineLocal }")]
    [InlineData("Invoke-Command -ScriptBlock { Invoke-PowerForgePipelineLocal }")]
    [InlineData("Invoke-Command { Invoke-PowerForgePipelineLocal }")]
    [InlineData("icm -NoNewScope -ScriptBlock { Invoke-PowerForgePipelineLocal }")]
    [InlineData("Invoke-Command -ArgumentList value -ScriptBlock { Invoke-PowerForgePipelineLocal }")]
    [InlineData("& { Invoke-PowerForgePipelineLocal }")]
    public void Analyze_RecognizesNestedFunctionInSameRunspaceScriptBlock(string invocation)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgePipelineLocal { 'local' }
                {{invocation}}
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePipelineLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("$callback = { Invoke-PowerForgeEscapingLocal }")]
    [InlineData("return { Invoke-PowerForgeEscapingLocal }")]
    [InlineData("{ Invoke-PowerForgeEscapingLocal }")]
    public void Analyze_ReportsNestedFunctionReferencedFromEscapingScriptBlock(string expression)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeEscapingLocal { 'local' }
                {{expression}}
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeEscapingLocal", StringComparison.OrdinalIgnoreCase));
    }

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

    [Fact]
    public void Analyze_RecognizesHelperDeclaredAfterDeferredCaller()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDeferredCaller {
                    Invoke-PowerForgeDeferredHelper
                }

                function Invoke-PowerForgeDeferredHelper { 'local' }
                Invoke-PowerForgeDeferredCaller
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDeferredHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsHelperWhenDeferredCallerRunsBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeEarlyCaller {
                    Invoke-PowerForgeLateHelper
                }

                Invoke-PowerForgeEarlyCaller
                function Invoke-PowerForgeLateHelper { 'local' }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLateHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_FollowsTransitiveDeferredCallersBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeTransitiveA {
                    Invoke-PowerForgeTransitiveHelper
                }

                function Invoke-PowerForgeTransitiveB {
                    Invoke-PowerForgeTransitiveA
                }

                Invoke-PowerForgeTransitiveB
                function Invoke-PowerForgeTransitiveHelper { 'local' }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeTransitiveHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesTransitiveDeferredCallersAfterDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeTransitiveA {
                    Invoke-PowerForgeTransitiveHelper
                }

                function Invoke-PowerForgeTransitiveB {
                    Invoke-PowerForgeTransitiveA
                }

                function Invoke-PowerForgeTransitiveHelper { 'local' }
                Invoke-PowerForgeTransitiveB
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeTransitiveHelper", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesDotSourcedNestedDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                . { function Invoke-PowerForgeDotSourcedLocal { 'local' } }
                Invoke-PowerForgeDotSourcedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDotSourcedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsProcessOnlyDotSourcedDeclarationWithoutPipelineInput()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                @() | . {
                    process {
                        function Invoke-PowerForgeProcessDotLocal { 'local' }
                    }
                }

                Invoke-PowerForgeProcessDotLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeProcessDotLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsCallOperatorScopedDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                & { function Invoke-PowerForgeChildScopedLocal { 'local' } }
                Invoke-PowerForgeChildScopedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeChildScopedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsConditionallyDotSourcedDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                if ($Enable) {
                    . { function Invoke-PowerForgeConditionalDotLocal { 'local' } }
                }

                Invoke-PowerForgeConditionalDotLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalDotLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ReportsProcessBlockDeclarationUsedFromEndBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                process {
                    function Invoke-PowerForgeProcessLocal { 'local' }
                }

                end {
                    Invoke-PowerForgeProcessLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeProcessLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesBeginBlockDeclarationUsedFromEndBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                begin {
                    function Invoke-PowerForgeBeginLocal { 'local' }
                }

                end {
                    Invoke-PowerForgeBeginLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeBeginLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesBeginBlockDeclarationUsedFromCleanBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                begin {
                    function Invoke-PowerForgeBeginCleanLocal { 'local' }
                }

                clean {
                    Invoke-PowerForgeBeginCleanLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeBeginCleanLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesProcessBlockDeclarationUsedWithinProcessBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                process {
                    function Invoke-PowerForgeProcessIterationLocal { 'local' }
                    Invoke-PowerForgeProcessIterationLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeProcessIterationLocal", StringComparison.OrdinalIgnoreCase));
    }
}
