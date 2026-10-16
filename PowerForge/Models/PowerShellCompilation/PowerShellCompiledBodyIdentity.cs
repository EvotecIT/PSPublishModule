namespace PowerForge;

/// <summary>Retains an emitted private body's exact member and authored owner independently of the public method inventory.</summary>
internal sealed class PowerShellCompiledBodyIdentity
{
    internal PowerShellCompiledBodyIdentity(string generatedMemberName, string? declarationName,
        string documentId, string sourcePath, int startOffset, int endOffset,
        int sourceStartLine, int sourceStartColumn, int sourceEndLine, int sourceEndColumn,
        PowerShellCompilationSourceMapEntry[] sourceMap)
    {
        GeneratedMemberName = generatedMemberName;
        DeclarationName = declarationName;
        DocumentId = documentId;
        SourcePath = sourcePath;
        StartOffset = startOffset;
        EndOffset = endOffset;
        SourceStartLine = sourceStartLine;
        SourceStartColumn = sourceStartColumn;
        SourceEndLine = sourceEndLine;
        SourceEndColumn = sourceEndColumn;
        SourceMap = sourceMap;
    }

    internal string GeneratedMemberName { get; }
    internal string? GeneratedDeclaringTypeName { get; set; }
    internal string? DeclarationName { get; }
    internal string DocumentId { get; }
    internal string SourcePath { get; }
    internal int StartOffset { get; }
    internal int EndOffset { get; }
    internal int SourceStartLine { get; }
    internal int SourceStartColumn { get; }
    internal int SourceEndLine { get; }
    internal int SourceEndColumn { get; }

    /// <summary>Actual binding and boundary evidence from this emitted body, including its reachable child closure.</summary>
    internal PowerShellNativeFunctionBinding? NativeFunctionBinding { get; set; }
    internal PowerShellCompilationRegionGraph? RegionGraph { get; set; }
    internal bool RequiresPowerShellCommandRegions { get; set; }
    internal bool RequiresPowerShellRuntimeState { get; set; }
    internal bool RequiresPowerShellStatementErrors { get; set; }
    internal bool RequiresPowerShellStopping { get; set; }
    internal bool RequiresPowerShellModuleStateRead { get; set; }
    internal bool RequiresPowerShellModuleStateWrite { get; set; }
    internal bool RequiresPowerShellModuleState => RequiresPowerShellModuleStateRead || RequiresPowerShellModuleStateWrite;
    internal string[] RequiredPowerShellModuleVariables { get; set; } = System.Array.Empty<string>();
    internal string[] WrittenPowerShellModuleVariables { get; set; } = System.Array.Empty<string>();
    internal int HostedRegionSiteCount => RegionGraph?.HostedCommandBoundarySites ?? 0;
    internal int PowerShellModuleStateReadSiteCount => RegionGraph?.ModuleStateReadBoundarySites ?? 0;
    internal int PowerShellModuleStateWriteSiteCount => RegionGraph?.ModuleStateWriteBoundarySites ?? 0;

    /// <summary>Only this body's statement maps; descendants have separate identities. Entries are shared with the containing method's flattened map.</summary>
    internal PowerShellCompilationSourceMapEntry[] SourceMap { get; }

    /// <summary>Relocates statement ranges while preserving the emitted and authored identity.</summary>
    internal PowerShellCompiledBodyIdentity WithSourceMap(PowerShellCompilationSourceMapEntry[] sourceMap)
        => WithAuthoredSource(DocumentId, SourcePath, StartOffset, EndOffset,
            SourceStartLine, SourceStartColumn, SourceEndLine, SourceEndColumn, sourceMap);

    /// <summary>Projects authored positions without losing the actual emitted body's binding or boundary evidence.</summary>
    internal PowerShellCompiledBodyIdentity WithAuthoredSource(string documentId, string sourcePath,
        int startOffset, int endOffset, int sourceStartLine, int sourceStartColumn,
        int sourceEndLine, int sourceEndColumn, PowerShellCompilationSourceMapEntry[] sourceMap)
        => new(GeneratedMemberName, DeclarationName, documentId, sourcePath, startOffset, endOffset,
            sourceStartLine, sourceStartColumn, sourceEndLine, sourceEndColumn, sourceMap)
        {
            GeneratedDeclaringTypeName = GeneratedDeclaringTypeName,
            NativeFunctionBinding = NativeFunctionBinding,
            RegionGraph = RegionGraph,
            RequiresPowerShellCommandRegions = RequiresPowerShellCommandRegions,
            RequiresPowerShellRuntimeState = RequiresPowerShellRuntimeState,
            RequiresPowerShellStatementErrors = RequiresPowerShellStatementErrors,
            RequiresPowerShellStopping = RequiresPowerShellStopping,
            RequiresPowerShellModuleStateRead = RequiresPowerShellModuleStateRead,
            RequiresPowerShellModuleStateWrite = RequiresPowerShellModuleStateWrite,
            RequiredPowerShellModuleVariables = RequiredPowerShellModuleVariables,
            WrittenPowerShellModuleVariables = WrittenPowerShellModuleVariables
        };
}
