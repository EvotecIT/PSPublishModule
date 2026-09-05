namespace PowerForge;

/// <summary>
/// Adapts compiler help metadata to PowerForge's canonical documentation/MAML model.
/// </summary>
internal static class PowerShellCompiledHelpWriter
{
    internal static string? WriteExternalHelp(
        string moduleName,
        string moduleRoot,
        IReadOnlyCollection<PowerShellCompiledMethod> methods,
        string culture = "en-US")
    {
        var documented = methods.Where(static method => method.Help is not null)
            .OrderBy(static method => method.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (documented.Length == 0) return null;

        var payload = new DocumentationExtractionPayload { ModuleName = moduleName };
        foreach (var method in documented) payload.Commands.Add(CreateCommand(method));
        return new MamlHelpWriter().WriteExternalHelpFile(payload, moduleName, Path.Combine(moduleRoot, culture));
    }

    private static DocumentationCommandHelp CreateCommand(PowerShellCompiledMethod method)
    {
        var help = method.Help!;
        var command = new DocumentationCommandHelp
        {
            Name = method.SourceName,
            CommandType = "Cmdlet",
            DefaultParameterSet = method.CommandBinding.DefaultParameterSetName,
            Synopsis = help.Synopsis,
            Description = help.Description
        };
        foreach (var parameter in method.Parameters)
        {
            help.Parameters.TryGetValue(parameter.Name, out var description);
            var entry = new DocumentationParameterHelp
            {
                Name = parameter.Name,
                Type = parameter.TypeName,
                RuntimeTypeName = parameter.TypeName,
                RuntimeClrTypeName = parameter.TypeName,
                Description = description ?? string.Empty,
                Required = parameter.IsMandatory,
                Aliases = parameter.Aliases.ToList(),
                DontShow = parameter.Bindings.All(static binding => binding.DontShow),
                Position = GetPosition(parameter),
                PipelineInput = GetPipelineInput(parameter),
                AcceptWildcardCharacters = parameter.SupportsWildcards
            };
            foreach (var binding in parameter.Bindings)
            {
                var setName = string.IsNullOrWhiteSpace(binding.ParameterSetName) ? "__AllParameterSets" : binding.ParameterSetName;
                if (!entry.ParameterSets.Contains(setName, StringComparer.OrdinalIgnoreCase)) entry.ParameterSets.Add(setName);
                entry.ParameterSetRequired[setName] = binding.Mandatory;
            }
            command.Parameters.Add(entry);
        }
        for (var index = 0; index < help.Examples.Length; index++)
        {
            command.Examples.Add(new DocumentationExampleHelp
            {
                Title = $"Example {index + 1}",
                Code = help.Examples[index]
            });
        }
        if (!string.IsNullOrWhiteSpace(help.Notes))
            command.Notes.Add(new DocumentationNoteHelp { Text = help.Notes });
        foreach (var link in help.Links)
        {
            command.RelatedLinks.Add(Uri.TryCreate(link, UriKind.Absolute, out _)
                ? new DocumentationLinkHelp { Text = link, Uri = link }
                : new DocumentationLinkHelp { Text = link });
        }
        command.Inputs.AddRange(help.Inputs.Select(static value => new DocumentationTypeHelp { Name = value, Description = value }));
        command.Outputs.AddRange(help.Outputs.Select(static value => new DocumentationTypeHelp { Name = value, Description = value }));
        if (command.Outputs.Count == 0 && !method.ReturnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
            command.Outputs.Add(new DocumentationTypeHelp { Name = method.ReturnType, ClrTypeName = method.ReturnType });
        return command;
    }

    private static string GetPosition(PowerShellCompilationParameter parameter)
    {
        var positions = parameter.Bindings.Where(static binding => binding.Position.HasValue)
            .Select(static binding => binding.Position!.Value)
            .Distinct()
            .ToArray();
        return positions.Length == 1 ? positions[0].ToString(System.Globalization.CultureInfo.InvariantCulture) : "Named";
    }

    private static string GetPipelineInput(PowerShellCompilationParameter parameter)
    {
        var byValue = parameter.Bindings.Any(static binding => binding.ValueFromPipeline);
        var byProperty = parameter.Bindings.Any(static binding => binding.ValueFromPipelineByPropertyName);
        if (byValue && byProperty) return "True (ByValue, ByPropertyName)";
        if (byValue) return "True (ByValue)";
        if (byProperty) return "True (ByPropertyName)";
        return "False";
    }
}
