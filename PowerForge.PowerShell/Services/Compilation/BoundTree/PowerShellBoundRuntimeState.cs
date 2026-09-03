namespace PowerForge;

internal sealed class PowerShellBoundRuntimeStateExpression : PowerShellBoundExpression
{
    internal PowerShellBoundRuntimeStateExpression(
        SourceSpan span,
        PowerShellRuntimeStateIntrinsicKind kind,
        string targetFramework,
        string semanticProfileId,
        PowerShellBoundExpression[] arguments,
        PowerShellCompilationCommandProviderContract? provider = null)
        : base(
            span,
            new PowerShellTypeFact(
                PowerShellRuntimeStateIntrinsicPolicy.GetType(kind),
                PowerShellTypeFactProvenance.Inferred,
                "The bounded runtime-state intrinsic defines its CLR result type."),
            kind is PowerShellRuntimeStateIntrinsicKind.EnvironmentVariable or PowerShellRuntimeStateIntrinsicKind.ModuleVariable
                ? PowerShellValueState.Unknown
                : PowerShellValueState.Known,
            arguments.Aggregate(
                KindRequiresHostBinding(kind) ? PowerShellSemanticEffect.Host : PowerShellSemanticEffect.None,
                static (effects, argument) => effects | argument.Effects),
            arguments.Aggregate(GetRequiredCapabilities(kind), static (capabilities, argument) => capabilities | argument.Capabilities))
    {
        Kind = kind;
        TargetFramework = targetFramework;
        SemanticProfileId = semanticProfileId;
        Arguments = arguments ?? Array.Empty<PowerShellBoundExpression>();
        Provider = provider;
    }

    internal PowerShellRuntimeStateIntrinsicKind Kind { get; }
    internal string TargetFramework { get; }
    internal string SemanticProfileId { get; }
    internal PowerShellImmutableArray<PowerShellBoundExpression> Arguments { get; }
    internal PowerShellCompilationCommandProviderContract? Provider { get; }
    internal bool RequiresHostBinding => KindRequiresHostBinding(Kind);

    private static bool KindRequiresHostBinding(PowerShellRuntimeStateIntrinsicKind kind)
        => kind is PowerShellRuntimeStateIntrinsicKind.PSVersion or
            PowerShellRuntimeStateIntrinsicKind.LanguageMode or
            PowerShellRuntimeStateIntrinsicKind.WhatIfPreference or
            PowerShellRuntimeStateIntrinsicKind.ActionPreference or
            PowerShellRuntimeStateIntrinsicKind.ConfirmPreference or
            PowerShellRuntimeStateIntrinsicKind.ErrorCollection or
            PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or
            PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction;

    private static PowerShellRequiredCapability GetRequiredCapabilities(PowerShellRuntimeStateIntrinsicKind kind)
        => PowerShellRequiredCapability.RuntimeStateIntrinsics |
           (kind is PowerShellRuntimeStateIntrinsicKind.PSVersion or PowerShellRuntimeStateIntrinsicKind.LanguageMode or PowerShellRuntimeStateIntrinsicKind.ActionPreference or PowerShellRuntimeStateIntrinsicKind.ConfirmPreference or PowerShellRuntimeStateIntrinsicKind.ErrorCollection
               ? PowerShellRequiredCapability.PowerShellHostTypes
               : PowerShellRequiredCapability.None) |
           (kind is PowerShellRuntimeStateIntrinsicKind.WhatIfPreference or
               PowerShellRuntimeStateIntrinsicKind.ActionPreference or
               PowerShellRuntimeStateIntrinsicKind.ConfirmPreference or
               PowerShellRuntimeStateIntrinsicKind.ErrorCollection or
               PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or
               PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction
               ? PowerShellRequiredCapability.PowerShellStreams
               : PowerShellRequiredCapability.None) |
           (kind == PowerShellRuntimeStateIntrinsicKind.ModuleVariable
               ? PowerShellRequiredCapability.PowerShellModuleState | PowerShellRequiredCapability.PowerShellHostTypes
               : PowerShellRequiredCapability.None);
}
