using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PSPublishModule.Services;

internal sealed class AutoLatestCompleter : IArgumentCompleter
{
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters)
    {
        var candidates = new[] { "Auto", "Latest" };
        var prefix = wordToComplete ?? string.Empty;

        foreach (var c in candidates)
        {
            if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return new CompletionResult(c);
        }
    }
}
