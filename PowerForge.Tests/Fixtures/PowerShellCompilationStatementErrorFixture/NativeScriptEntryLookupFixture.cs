using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PowerForge.Generated.Runtime;

namespace Generic.Compiler.StatementErrors;

// Exercises the shipped invocation owner, including actual lazy compiled callbacks.
public static class NativeScriptEntryLookupFixture
{
    public static NativeScriptEntryLookupObservation Run(string directory, string kind)
    {
        var path = Path.Combine(directory, kind + ".ps1");
        var source = "param([Alias('v')][string] $Value='default',[switch] $Flag) $global:AuthoredRuns++; 'authored'";
        if (kind == "binding") source = "[CmdletBinding()] param([int] $Value) $global:AuthoredRuns++; 'authored'";
        if (kind == "mandatory") source = "[CmdletBinding()] param([Parameter(Mandatory)][int] $Value) $global:AuthoredRuns++; 'authored'";
        File.WriteAllText(path, source, new UTF8Encoding(false));
        var otherPath = Path.Combine(directory, "other.ps1");
        File.WriteAllText(otherPath, "$global:AuthoredRuns++; 'other'", new UTF8Encoding(false));
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source)))
            .Replace("-", string.Empty).ToLowerInvariant();

        NativeScriptEntryAuthorization.Checks = 0;
        NativeScriptEntryAuthorization.Reject = kind == "authorization";
        var state = InitialSessionState.CreateDefault2();
        // The PowerShell 5 reference package omits this public runtime property.
        var policy = typeof(InitialSessionState).GetProperty("ExecutionPolicy", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new NotSupportedException("The native-entry fixture requires the public runspace policy API.");
        if (policy.GetSetMethod(nonPublic: false) is null || policy.PropertyType != typeof(Microsoft.PowerShell.ExecutionPolicy))
            throw new NotSupportedException("The native-entry fixture requires the public runspace policy setter.");
        policy.SetValue(state, Microsoft.PowerShell.ExecutionPolicy.Bypass, null);
        if (!Equals(policy.GetValue(state, null), Microsoft.PowerShell.ExecutionPolicy.Bypass))
            throw new InvalidOperationException("The owned runspace policy was not applied.");
        state.AuthorizationManager = new NativeScriptEntryAuthorization();
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        using var ps = PowerShell.Create();
        ps.Runspace = runspace;
        runspace.SessionStateProxy.SetVariable("AuthoredRuns", 0);
        var commands = runspace.SessionStateProxy.InvokeCommand;
        var callbacks = 0;
        EventHandler<CommandLookupEventArgs> replacement = (sender, lookup) => { };
        var entry = PowerShellNativeFunctionHost.CreateScriptEntry(runspace, path, hash, context =>
        {
            callbacks++;
            if (kind == "body-throw") throw new InvalidOperationException("compiled callback threw");
            if (kind == "replacement") commands.PostCommandLookupAction = replacement;
            context.WriteValue("compiled");
            if (kind != "arguments") return;
            context.WriteValue("value=" + context.GetVariable("Value"));
            context.WriteValue("flag=" + context.GetVariable("Flag"));
            foreach (var value in (object[])context.GetVariable("args")!) context.WriteValue("[" + value + "]");
        });
        var other = commands.GetCommand(otherPath, CommandTypes.ExternalScript);
        var alternateCommand = commands.GetCommand("Microsoft.PowerShell.Utility\\Write-Output", CommandTypes.Cmdlet);
        var lookups = 0;
        EventHandler<CommandLookupEventArgs> previous = (sender, lookup) =>
        {
            if (!lookup.CommandName.Equals(path, StringComparison.Ordinal)) return;
            lookups++;
            if (kind == "predecessor-other") lookup.Command = other;
            if (kind == "predecessor-command") lookup.Command = alternateCommand;
            if (kind == "predecessor-null") lookup.Command = null!;
            if (kind is "predecessor-error" or "exception-priority") throw new InvalidOperationException("predecessor lookup failed");
        };
        commands.PostCommandLookupAction = previous;
        var arguments = new List<CommandParameter>();
        if (kind == "arguments")
        {
            arguments.Add(new CommandParameter("v", "owned"));
            arguments.Add(new CommandParameter("Flag", false));
            arguments.Add(new CommandParameter(null, "head"));
            arguments.Add(new CommandParameter("Unknown", "a'b;$(throw 'argument executed')"));
            arguments.Add(new CommandParameter("Unknown", "second"));
            arguments.Add(new CommandParameter(null, "tail"));
        }
        if (kind == "binding") arguments.Add(new CommandParameter("Value", "invalid integer"));
        if (kind == "predecessor-command") arguments.Add(new CommandParameter(null, "deliberate command"));
        if (kind == "identity") File.WriteAllText(path, "$global:AuthoredRuns++; 'changed after preparation'", new UTF8Encoding(false));

        var records = new List<string>();
        Exception? failure = null;
        try
        {
            using var lookup = PowerShellNativeFunctionHost.AddScriptEntryCommand(ps, entry, arguments);
            lookup.Invoke(() =>
            {
                try { foreach (var value in ps.Invoke()) records.Add(value.ToString()); }
                catch (CommandNotFoundException) when (kind == "exception-priority")
                {
                    throw new InvalidOperationException("invocation boundary failed");
                }
            });
        }
        catch (Exception error) { failure = error; }
        return new NativeScriptEntryLookupObservation
        {
            Kind = kind, Records = records.ToArray(), FailureType = failure?.GetType().FullName ?? string.Empty,
            FailureMessage = failure?.Message ?? string.Empty, Callbacks = callbacks, Lookups = lookups,
            AuthoredRuns = (int)runspace.SessionStateProxy.GetVariable("AuthoredRuns"),
            AuthorizationChecks = NativeScriptEntryAuthorization.Checks,
            HookRestored = ReferenceEquals(commands.PostCommandLookupAction, kind == "replacement" ? replacement : previous),
            State = ps.InvocationStateInfo.State.ToString()
        };
    }
}

public sealed class NativeScriptEntryLookupObservation
{
    public string Kind { get; set; } = string.Empty;
    public string[] Records { get; set; } = Array.Empty<string>();
    public string FailureType { get; set; } = string.Empty;
    public string FailureMessage { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int Callbacks { get; set; }
    public int Lookups { get; set; }
    public int AuthoredRuns { get; set; }
    public int AuthorizationChecks { get; set; }
    public bool HookRestored { get; set; }
}
