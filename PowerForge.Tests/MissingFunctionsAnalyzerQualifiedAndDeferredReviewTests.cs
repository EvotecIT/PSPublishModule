using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerQualifiedAndDeferredReviewTests
{
    [Fact]
    public void Analyze_RequiresEveryQualifiedConsumerInvocationToFollowInstaller()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeRepeatedGlobal {
                function global:Invoke-PowerForgeRepeatedGlobal { 'local' }
            }
            function Use-PowerForgeRepeatedGlobal {
                Invoke-PowerForgeRepeatedGlobal
            }
            Use-PowerForgeRepeatedGlobal
            Install-PowerForgeRepeatedGlobal
            Use-PowerForgeRepeatedGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeRepeatedGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AccountsForConditionalQualifiedConsumerBeforeInstaller()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeConditionalConsumerGlobal {
                function global:Invoke-PowerForgeConditionalConsumerGlobal { 'local' }
            }
            function Use-PowerForgeConditionalConsumerGlobal {
                Invoke-PowerForgeConditionalConsumerGlobal
            }
            if ($Enable) { Use-PowerForgeConditionalConsumerGlobal }
            Install-PowerForgeConditionalConsumerGlobal
            Use-PowerForgeConditionalConsumerGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalConsumerGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RequiresGuaranteedPathToQualifiedInstaller()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeConditionalGlobal {
                function global:Invoke-PowerForgeConditionalGlobal { 'local' }
            }
            function Start-PowerForgeConditionalGlobal {
                if ($Enable) { Install-PowerForgeConditionalGlobal }
            }
            Start-PowerForgeConditionalGlobal
            Invoke-PowerForgeConditionalGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RequiresQualifiedInstallerCallToDominateItsFunctionBody()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeLateGlobal {
                function global:Invoke-PowerForgeLateGlobal { 'local' }
            }
            function Start-PowerForgeLateGlobal {
                throw 'before installer'
                Install-PowerForgeLateGlobal
            }
            Start-PowerForgeLateGlobal
            Invoke-PowerForgeLateGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLateGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_InvalidatesQualifiedDeclarationRemovedAfterInstaller()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeRemovedGlobal {
                function global:Invoke-PowerForgeRemovedGlobal { 'local' }
            }
            Install-PowerForgeRemovedGlobal
            Remove-Item function:Invoke-PowerForgeRemovedGlobal
            Invoke-PowerForgeRemovedGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeRemovedGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RecognizesQualifiedDeclarationReinstalledAfterRemoval()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeReinstalledGlobal {
                function global:Invoke-PowerForgeReinstalledGlobal { 'local' }
            }
            Remove-Item function:Invoke-PowerForgeReinstalledGlobal -ErrorAction SilentlyContinue
            Install-PowerForgeReinstalledGlobal
            Invoke-PowerForgeReinstalledGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeReinstalledGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Remove-Item")]
    [InlineData("Clear-Item")]
    public void Analyze_IgnoresShadowedFunctionProviderRemoval(string removalCommand)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeShadowRemovalLocal { 'local' }
                function {{removalCommand}} { param($Path) }
                {{removalCommand}} function:Invoke-PowerForgeShadowRemovalLocal
                Invoke-PowerForgeShadowRemovalLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeShadowRemovalLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsDynamicAliasTargetAsDynamicInvocation()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDynamicAliasTargetCaller { Invoke-PowerForgeDynamicAliasTargetLate }
                $target = 'Invoke-PowerForgeDynamicAliasTargetCaller'
                Set-Alias sort $target
                sort
                function Invoke-PowerForgeDynamicAliasTargetLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDynamicAliasTargetLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_SkipsBoundParameterDefaultInDeferredGraph()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDefaultCaller { Invoke-PowerForgeDefaultLate }
                function Invoke-PowerForgeWithDefault {
                    param($Value = $(Invoke-PowerForgeDefaultCaller))
                }
                Invoke-PowerForgeWithDefault -Value 1
                function Invoke-PowerForgeDefaultLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDefaultLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_SkipsPositionallyBoundParameterDefaultInDeferredGraph()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgePositionalDefaultCaller { Invoke-PowerForgePositionalDefaultLate }
                function Invoke-PowerForgeWithPositionalDefault {
                    param($Value = $(Invoke-PowerForgePositionalDefaultCaller))
                }
                Invoke-PowerForgeWithPositionalDefault 1
                function Invoke-PowerForgePositionalDefaultLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePositionalDefaultLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_FollowsUnboundParameterDefaultInDeferredGraph()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeUnboundDefaultCaller { Invoke-PowerForgeUnboundDefaultLate }
                function Invoke-PowerForgeWithUnboundDefault {
                    param($Value = $(Invoke-PowerForgeUnboundDefaultCaller))
                }
                Invoke-PowerForgeWithUnboundDefault
                function Invoke-PowerForgeUnboundDefaultLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeUnboundDefaultLate", StringComparison.OrdinalIgnoreCase));
    }
}
