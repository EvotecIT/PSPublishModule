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

    [Fact]
    public void Analyze_IgnoresConditionalFunctionProviderRemoval()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeConditionallyRemovedLocal { 'local' }
                if ($false) { Remove-Item function:Invoke-PowerForgeConditionallyRemovedLocal }
                Invoke-PowerForgeConditionallyRemovedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionallyRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AppliesFunctionProviderRemovalWithinSameGuardedBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeGuardRemovedLocal { 'local' }
                if ($Enable) {
                    Remove-Item function:Invoke-PowerForgeGuardRemovedLocal
                    Invoke-PowerForgeGuardRemovedLocal
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeGuardRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AppliesGuardedRemovalAtDeferredInvocationOffset()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeGuardDeferredRemovedLocal { 'local' }
                function Invoke-PowerForgeGuardRemovedCaller { Invoke-PowerForgeGuardDeferredRemovedLocal }
                if ($Enable) {
                    Remove-Item function:Invoke-PowerForgeGuardDeferredRemovedLocal
                    Invoke-PowerForgeGuardRemovedCaller
                }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeGuardDeferredRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_IgnoresFunctionProviderVariableReadBeforeDeclaration()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                $saved = ${function:Invoke-PowerForgeVariableSavedCallback}
                function Invoke-PowerForgeVariableSavedCallback { Invoke-PowerForgeVariableSavedLocal }
                function Invoke-PowerForgeVariableSavedLocal { 'local' }
                Invoke-PowerForgeVariableSavedCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeVariableSavedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Script")]
    [InlineData("Global")]
    public void Analyze_FollowsExplicitlyScopedAliasInstalledBeforeInvocation(string scope)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Invoke-PowerForgeScopedAliasCaller { Invoke-PowerForgeScopedAliasLate }
            function Install-PowerForgeScopedAlias {
                Set-Alias go Invoke-PowerForgeScopedAliasCaller -Scope {{scope}}
            }
            Install-PowerForgeScopedAlias
            go
            function Invoke-PowerForgeScopedAliasLate { 'late' }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeScopedAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotApplyExplicitlyScopedAliasWhenInstallerDidNotRun()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Invoke-PowerForgeInactiveAliasCaller { Invoke-PowerForgeInactiveAliasLate }
            function Install-PowerForgeInactiveAlias {
                Set-Alias go Invoke-PowerForgeInactiveAliasCaller -Scope Script
            }
            go
            function Invoke-PowerForgeInactiveAliasLate { 'late' }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeInactiveAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Set-Alias")]
    [InlineData("New-Alias")]
    public void Analyze_IgnoresAliasMutationCommandShadowedByFunction(string aliasCommand)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeShadowedAliasCaller { Invoke-PowerForgeShadowedAliasLate }
                function {{aliasCommand}} { param($Name, $Value) }
                {{aliasCommand}} sort Invoke-PowerForgeShadowedAliasCaller
                sort
                function Invoke-PowerForgeShadowedAliasLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeShadowedAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ProjectsDotSourcedAliasIntoCallerScope()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDotAliasCaller { Invoke-PowerForgeDotAliasLate }
                . { Set-Alias go Invoke-PowerForgeDotAliasCaller }
                go
                function Invoke-PowerForgeDotAliasLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDotAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_ProjectsDotSourcedRemovalIntoCallerScope()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDotRemovedLocal { 'local' }
                . { Remove-Item function:Invoke-PowerForgeDotRemovedLocal }
                Invoke-PowerForgeDotRemovedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDotRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("script")]
    public void Analyze_NormalizesQualifiedFunctionNameForEscapeLookup(string qualifier)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function {{qualifier}}:Invoke-PowerForgeQualifiedEscapeCallback { Invoke-PowerForgeQualifiedEscapeLocal }
                function Invoke-PowerForgeQualifiedEscapeLocal { 'local' }
                Get-Command Invoke-PowerForgeQualifiedEscapeCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeQualifiedEscapeLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_IgnoresFunctionLookupCommandShadowedByFunction()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Get-Command { param($Name) }
                function Invoke-PowerForgeShadowLookupCallback { Invoke-PowerForgeShadowLookupLocal }
                function Invoke-PowerForgeShadowLookupLocal { 'local' }
                Get-Command Invoke-PowerForgeShadowLookupCallback
                Invoke-PowerForgeShadowLookupCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeShadowLookupLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Remove-Alias go")]
    [InlineData("Remove-Item alias:go")]
    public void Analyze_StopsApplyingAliasAfterGuaranteedRemoval(string removal)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeRemovedAliasCaller { Invoke-PowerForgeRemovedAliasLate }
                Set-Alias go Invoke-PowerForgeRemovedAliasCaller
                {{removal}}
                go
                function Invoke-PowerForgeRemovedAliasLate { 'late' }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeRemovedAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsLookupPipedToShadowedOutNullAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Out-Null { process { $script:SavedPowerForgeCallback = $_.ScriptBlock } }
                function Invoke-PowerForgeOutNullCallback { Invoke-PowerForgeOutNullLocal }
                function Invoke-PowerForgeOutNullLocal { 'local' }
                Get-Command Invoke-PowerForgeOutNullCallback | Out-Null
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeOutNullLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_CarriesFinallyDeclarationToFollowingCall()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                try {} finally {
                    function Invoke-PowerForgeFinallyFollowingLocal { 'local' }
                }
                Invoke-PowerForgeFinallyFollowingLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeFinallyFollowingLocal", StringComparison.OrdinalIgnoreCase));
    }
}
