using System;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

public sealed class NativeScriptEntryAuthorization : AuthorizationManager
{
    public static int Checks;
    public static bool Reject;
    public NativeScriptEntryAuthorization() : base("PFC.NativeScriptEntryQualification") { }
    protected override bool ShouldRun(CommandInfo commandInfo, CommandOrigin origin, PSHost host, out Exception reason)
    {
        if (commandInfo.CommandType != CommandTypes.ExternalScript) { reason = null!; return true; }
        Checks++;
        reason = Reject ? new SecurityException("Native script entry qualification denied this script.") : null!;
        return !Reject;
    }
}

public static class NativeScriptEntryPolicyFixture
{
    public static int CallbackInvocations;
    public static ExternalScriptInfo Create(SessionState session, PSHost host, string path, string hash)
    {
        CallbackInvocations = 0;
        return PowerShellNativeFunctionHost.CreateScriptEntry(session, path, hash,
            context => { CallbackInvocations++; context.WriteValue("ran"); });
    }
    public static ExternalScriptInfo CreateRestricted(SessionState session, PSHost host, string path, string hash)
    {
        var previous = session.LanguageMode;
        try { session.LanguageMode = PSLanguageMode.ConstrainedLanguage; return Create(session, host, path, hash); }
        finally { session.LanguageMode = previous; }
    }
}

public sealed class NativeScriptEntryBindingAttribute : ArgumentTransformationAttribute
{
    public static int Constructions;
    public NativeScriptEntryBindingAttribute() { Constructions++; }
    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData) => inputData;
}
