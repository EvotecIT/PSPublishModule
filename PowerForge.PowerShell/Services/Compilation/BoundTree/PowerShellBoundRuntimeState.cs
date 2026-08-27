namespace PowerForge;

internal sealed class PowerShellBoundRuntimeStateExpression : PowerShellBoundExpression
{
    internal PowerShellBoundRuntimeStateExpression(
        SourceSpan span,
        PowerShellRuntimeStateIntrinsicKind kind,
        string targetFramework,
        PowerShellBoundExpression[] arguments)
        : base(
            span,
            new PowerShellTypeFact(
                PowerShellRuntimeStateIntrinsicPolicy.GetType(kind),
                PowerShellTypeFactProvenance.Inferred,
                "The bounded runtime-state intrinsic defines its CLR result type."),
            PowerShellValueState.Known,
            arguments.Aggregate(
                KindRequiresHostBinding(kind) ? PowerShellSemanticEffect.Host : PowerShellSemanticEffect.None,
                static (effects, argument) => effects | argument.Effects),
            arguments.Aggregate(GetRequiredCapabilities(kind), static (capabilities, argument) => capabilities | argument.Capabilities))
    {
        Kind = kind;
        TargetFramework = targetFramework;
        Arguments = arguments ?? Array.Empty<PowerShellBoundExpression>();
    }

    internal PowerShellRuntimeStateIntrinsicKind Kind { get; }
    internal string TargetFramework { get; }
    internal PowerShellBoundExpression[] Arguments { get; }
    internal bool RequiresHostBinding => KindRequiresHostBinding(Kind);

    private static bool KindRequiresHostBinding(PowerShellRuntimeStateIntrinsicKind kind)
        => kind is PowerShellRuntimeStateIntrinsicKind.PSVersion or
            PowerShellRuntimeStateIntrinsicKind.WhatIfPreference or
            PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or
            PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction;

    private static PowerShellRequiredCapability GetRequiredCapabilities(PowerShellRuntimeStateIntrinsicKind kind)
        => PowerShellRequiredCapability.RuntimeStateIntrinsics |
           (kind == PowerShellRuntimeStateIntrinsicKind.PSVersion ? PowerShellRequiredCapability.PowerShellHostTypes : PowerShellRequiredCapability.None) |
           (kind is PowerShellRuntimeStateIntrinsicKind.WhatIfPreference or
               PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget or
               PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction
               ? PowerShellRequiredCapability.PowerShellStreams
               : PowerShellRequiredCapability.None);
}
