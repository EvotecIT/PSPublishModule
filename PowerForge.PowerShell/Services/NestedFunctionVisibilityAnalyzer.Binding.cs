using System.Collections.Generic;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private readonly Dictionary<CommandAst, StaticBindingResult> _commandBindings = new();

    private StaticBindingResult BindCommandCached(CommandAst command)
    {
        if (_commandBindings.TryGetValue(command, out var binding))
            return binding;

        binding = StaticParameterBinder.BindCommand(command);
        // Incomplete metadata can become available through module discovery later in this analysis.
        // Only retain successful bindings, and never share them between analyzed syntax trees.
        if (binding.BindingExceptions.Count == 0)
            _commandBindings.Add(command, binding);

        return binding;
    }
}
