namespace PowerForge;

/// <summary>Controls where the module and its delivery files appear in an artefact.</summary>
public enum ArtefactLayout
{
    /// <summary>Preserves the standard module package layout.</summary>
    Module = 0,

    /// <summary>Places delivery files at the root and modules under the Modules directory.</summary>
    Delivery = 1
}
