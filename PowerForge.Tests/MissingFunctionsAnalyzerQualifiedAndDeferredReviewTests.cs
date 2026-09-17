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

    [Fact]
    public void Analyze_DoesNotCarryDoLoopDeclarationPastGuaranteedRemoval()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                do {
                    function Invoke-PowerForgeDoRemovedLocal { 'local' }
                    Remove-Item function:Invoke-PowerForgeDoRemovedLocal
                } while ($false)
                Invoke-PowerForgeDoRemovedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDoRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotCarryTryDeclarationPastCatchRemoval()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                try {
                    function Invoke-PowerForgeCatchRemovedLocal { 'local' }
                    throw 'handled'
                } catch {
                    Remove-Item function:Invoke-PowerForgeCatchRemovedLocal
                }
                Invoke-PowerForgeCatchRemovedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeCatchRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_CarriesTryDeclarationWhenCatchCannotRun()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                try {
                    function Invoke-PowerForgeUnthrownCatchLocal { 'local' }
                } catch {
                    Remove-Item function:Invoke-PowerForgeUnthrownCatchLocal
                }
                Invoke-PowerForgeUnthrownCatchLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeUnthrownCatchLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Script")]
    [InlineData("Global")]
    public void Analyze_HonorsExplicitScopeOnAliasRemoval(string scope)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Invoke-PowerForgeExplicitRemovalCaller { Invoke-PowerForgeExplicitRemovalLate }
            function Install-PowerForgeRemovedAlias {
                Set-Alias go Invoke-PowerForgeExplicitRemovalCaller -Scope {{scope}}
                Remove-Alias go -Scope {{scope}}
            }
            Install-PowerForgeRemovedAlias
            go
            function Invoke-PowerForgeExplicitRemovalLate { 'late' }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeExplicitRemovalLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_IgnoresDiscardedFunctionProviderVariableRead()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDiscardedVariableCallback { Invoke-PowerForgeDiscardedVariableLocal }
                function Invoke-PowerForgeDiscardedVariableLocal { 'local' }
                $null = ${function:Invoke-PowerForgeDiscardedVariableCallback}
                Invoke-PowerForgeDiscardedVariableCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDiscardedVariableLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsModuleQualifiedFunctionLookupAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeModuleLookupCallback { Invoke-PowerForgeModuleLookupLocal }
                function Invoke-PowerForgeModuleLookupLocal { 'local' }
                Microsoft.PowerShell.Core\Get-Command Invoke-PowerForgeModuleLookupCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeModuleLookupLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsWildcardFunctionLookupAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeWildcardLookupCallback { Invoke-PowerForgeWildcardLookupLocal }
                function Invoke-PowerForgeWildcardLookupLocal { 'local' }
                Get-Command Invoke-PowerForgeWildcardLookup*
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeWildcardLookupLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotInheritPrivateConsumerShadowIntoNestedFunction()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function private:Where-Object { param([scriptblock] $FilterScript) }
                function Inner {
                    function Invoke-PowerForgePrivateConsumerLocal { 'local' }
                    Where-Object { Invoke-PowerForgePrivateConsumerLocal }
                }
                Inner
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePrivateConsumerLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Set-Alias Where-Object Save-PowerForgeWhatIfCallback -WhatIf")]
    [InlineData("Set-Item alias:Where-Object Save-PowerForgeWhatIfCallback -WhatIf")]
    public void Analyze_IgnoresAliasMutationWithWhatIf(string mutation)
    {
        var analyzer = new MissingFunctionsAnalyzer();
        var code = $$"""
            function Outer {
                function Invoke-PowerForgeWhatIfAliasLocal { 'local' }
                function Save-PowerForgeWhatIfCallback { param([scriptblock] $Callback) }
                {{mutation}}
                Where-Object { Invoke-PowerForgeWhatIfAliasLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeWhatIfAliasLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsDynamicFunctionLookupAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDynamicLookupCallback { Invoke-PowerForgeDynamicLookupLocal }
                function Invoke-PowerForgeDynamicLookupLocal { 'local' }
                $name = 'Invoke-PowerForgeDynamicLookupCallback'
                $script:SavedPowerForgeCallback = Get-Command $name
            }
            Outer
            & $SavedPowerForgeCallback.ScriptBlock
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDynamicLookupLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsDynamicFunctionProviderLookupAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDynamicProviderCallback { Invoke-PowerForgeDynamicProviderLocal }
                function Invoke-PowerForgeDynamicProviderLocal { 'local' }
                $path = 'function:Invoke-PowerForgeDynamicProviderCallback'
                $script:SavedPowerForgeCallback = Get-Item $path
            }
            Outer
            & $SavedPowerForgeCallback.ScriptBlock
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDynamicProviderLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AcceptsQualifiedDeclarationFromGuaranteedFinallyBlock()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeFinallyGlobal {
                try {} finally {
                    function global:Invoke-PowerForgeFinallyGlobal { 'installed' }
                }
            }
            Install-PowerForgeFinallyGlobal
            Invoke-PowerForgeFinallyGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeFinallyGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotAcceptQualifiedFinallyDeclarationFromConditionalTry()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeConditionalFinallyGlobal {
                if ($Enable) {
                    try {} finally {
                        function global:Invoke-PowerForgeConditionalFinallyGlobal { 'installed' }
                    }
                }
            }
            Install-PowerForgeConditionalFinallyGlobal
            Invoke-PowerForgeConditionalFinallyGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalFinallyGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TracksEscapedInnerFunctionAsDeferredEntry()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeOuterCallback {
                    function Invoke-PowerForgeInnerCallback { Invoke-PowerForgeNestedEscapeLocal }
                    ${function:Invoke-PowerForgeInnerCallback}
                }
                function Invoke-PowerForgeNestedEscapeLocal { 'local' }
                Invoke-PowerForgeOuterCallback
            }
            $callback = Outer
            & $callback
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeNestedEscapeLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TracksDynamicFunctionProviderRemoval()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeDynamicRemovedLocal { 'local' }
                $path = 'function:Invoke-PowerForgeDynamicRemovedLocal'
                Remove-Item $path
                Invoke-PowerForgeDynamicRemovedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDynamicRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotTreatAliasRemovalOptionValuesAsNames()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Invoke-PowerForgeLocalAliasCaller { Invoke-PowerForgeLocalAliasLate }
            Set-Alias Local Invoke-PowerForgeLocalAliasCaller
            Remove-Alias missing -Scope Local -ErrorAction SilentlyContinue
            Local
            function Invoke-PowerForgeLocalAliasLate { 'late' }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLocalAliasLate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_CarriesDeclarationFromGuaranteedCatch()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                try { throw 'handled' } catch {
                    function Invoke-PowerForgeGuaranteedCatchLocal { 'local' }
                }
                Invoke-PowerForgeGuaranteedCatchLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeGuaranteedCatchLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotCarryCatchDeclarationAfterConditionalThrow()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                try { if ($Enable) { throw 'handled' } } catch {
                    function Invoke-PowerForgeConditionalCatchLocal { 'local' }
                }
                Invoke-PowerForgeConditionalCatchLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeConditionalCatchLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AcceptsQualifiedDeclarationFromGuaranteedDoLoop()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeDoGlobal {
                do {
                    function global:Invoke-PowerForgeDoGlobal { 'installed' }
                } while ($false)
            }
            Install-PowerForgeDoGlobal
            Invoke-PowerForgeDoGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeDoGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotAcceptQualifiedDeclarationFromWhileLoop()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Install-PowerForgeWhileGlobal {
                while ($false) {
                    function global:Invoke-PowerForgeWhileGlobal { 'installed' }
                }
            }
            Install-PowerForgeWhileGlobal
            Invoke-PowerForgeWhileGlobal
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeWhileGlobal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_UsesBuiltInLookupAfterShadowFunctionRemoval()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Get-Command { param([string] $Name) }
                Remove-Item function:Get-Command
                function Invoke-PowerForgeRemovedShadowCallback { Invoke-PowerForgeRemovedShadowLocal }
                function Invoke-PowerForgeRemovedShadowLocal { 'local' }
                $script:SavedPowerForgeCallback = Get-Command Invoke-PowerForgeRemovedShadowCallback
            }
            Outer
            & $SavedPowerForgeCallback.ScriptBlock
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeRemovedShadowLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotDiscardLookupConsumedByIntermediatePipelineCommand()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgePipelineSavedCallback { Invoke-PowerForgePipelineSavedLocal }
                function Invoke-PowerForgePipelineSavedLocal { 'local' }
                $null = Get-Command Invoke-PowerForgePipelineSavedCallback |
                    ForEach-Object { $script:SavedPowerForgeCallback = $_.ScriptBlock }
            }
            Outer
            & $SavedPowerForgeCallback
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePipelineSavedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsFunctionProviderEnumerationAsEscaping()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeEnumeratedCallback { Invoke-PowerForgeEnumeratedLocal }
                function Invoke-PowerForgeEnumeratedLocal { 'local' }
                $script:SavedPowerForgeCallbacks = Get-ChildItem Function:
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeEnumeratedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DoesNotInheritPrivateAliasIntoChildFunction()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Save-PowerForgePrivateAliasCallback { param([scriptblock] $Callback) }
            Set-Alias Where-Object Save-PowerForgePrivateAliasCallback -Option Private
            function Outer {
                function Invoke-PowerForgePrivateAliasLocal { 'local' }
                Where-Object { Invoke-PowerForgePrivateAliasLocal }
            }
            Outer
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgePrivateAliasLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_AppliesPrivateAliasWithinItsOwnScope()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Save-PowerForgePrivateAliasCallback { param([scriptblock] $Callback) }
                function Invoke-PowerForgeSameScopePrivateAliasLocal { 'local' }
                Set-Alias Where-Object Save-PowerForgePrivateAliasCallback -Option Private
                Where-Object { Invoke-PowerForgeSameScopePrivateAliasLocal }
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeSameScopePrivateAliasLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_IgnoresDynamicRemovalFromKnownNonFunctionProvider()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeEnvironmentRemovalLocal { 'local' }
                $name = 'PowerForgeValue'
                Remove-Item "env:$name"
                Invoke-PowerForgeEnvironmentRemovalLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeEnvironmentRemovalLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TracksDynamicRemovalFromKnownFunctionProvider()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeExpandedRemovalLocal { 'local' }
                $name = 'Invoke-PowerForgeExpandedRemovalLocal'
                Remove-Item "function:$name"
                Invoke-PowerForgeExpandedRemovalLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeExpandedRemovalLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_MatchesWildcardFunctionRemovalPath()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeWildcardRemovedLocal { 'local' }
                Remove-Item function:Invoke-PowerForgeWildcard*
                Invoke-PowerForgeWildcardRemovedLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeWildcardRemovedLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_TreatsLiteralPathWildcardAsLiteralFunctionName()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeLiteralWildcardLocal { 'local' }
                Remove-Item -LiteralPath function:Invoke-PowerForgeLiteral*
                Invoke-PowerForgeLiteralWildcardLocal
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeLiteralWildcardLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_HonorsGetCommandTypeFilterThatExcludesFunctions()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeCmdletFilteredCallback { Invoke-PowerForgeCmdletFilteredLocal }
                function Invoke-PowerForgeCmdletFilteredLocal { 'local' }
                Get-Command Invoke-PowerForgeCmdletFilteredCallback -CommandType Cmdlet -ErrorAction Ignore
                Invoke-PowerForgeCmdletFilteredCallback
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.DoesNotContain(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeCmdletFilteredLocal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_HonorsGetCommandTypeFilterThatIncludesFunctions()
    {
        var analyzer = new MissingFunctionsAnalyzer();
        const string code = """
            function Outer {
                function Invoke-PowerForgeFunctionFilteredCallback { Invoke-PowerForgeFunctionFilteredLocal }
                function Invoke-PowerForgeFunctionFilteredLocal { 'local' }
                Get-Command Invoke-PowerForgeFunctionFilteredCallback -CommandType Function
            }
            """;

        var report = analyzer.Analyze(filePath: null, code: code);

        Assert.Contains(report.Summary, item =>
            string.Equals(item.Name, "Invoke-PowerForgeFunctionFilteredLocal", StringComparison.OrdinalIgnoreCase));
    }
}
